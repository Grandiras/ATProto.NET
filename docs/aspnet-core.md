# ASP.NET Core Integration

ATProto.NET provides first-class ASP.NET Core integration through the `ATProtoNet.Server` package.

## Installation

```bash
dotnet add package ATProtoNet.Server
```

## Service Registration

`AddAtProto()` registers an `AtProtoClient` and returns an `IAtProtoBuilder`, which the rest of the
registration hangs off (see [Server Integration](server.md#the-atproto-builder)).

### Singleton Client

Register a single shared `AtProtoClient`:

```csharp
// Program.cs
builder.Services.AddAtProto(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});
```

The options go through `IOptions<AtProtoClientOptions>`, so they also bind from configuration, and
an `InstanceUrl` that is not an absolute http(s) URL stops the host at startup:

```csharp
builder.Services.AddAtProto();
builder.Services.Configure<AtProtoClientOptions>(builder.Configuration.GetSection("AtProto"));
```

### Custom Session Store

The client writes its session to the registered store, so it survives a restart. The store is a
singleton; one over a database takes an `IDbContextFactory` rather than a `DbContext` (see
[Custom Implementation](server.md#custom-implementation)):

```csharp
builder.Services.AddAtProto(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
})
.WithSessionStore<DatabaseSessionStore>();
```

### Scoped Client (Per-Request)

For multi-user scenarios where each request has its own session:

```csharp
builder.Services.AddAtProto(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
})
.WithLifetime(ServiceLifetime.Scoped);
```

### HTTP Handlers

Every client of the registration sends through one named `HttpClient`, whose builder is
`IAtProtoBuilder.HttpClient`. Add logging or telemetry handlers there, but not a handler that
retries on its own: see [HTTP handlers and resilience](server.md#http-handlers-and-resilience).

## Authentication

`ATProtoNet.Server` authenticates two kinds of caller:

- **Users**, with the OAuth cookie login: `AddAtProto().WithOAuth()` and `MapAtProtoOAuth()`
  sign a user in with their AT Protocol account and an ordinary authentication cookie (see
  [OAuth: Hosted Login](oauth.md#hosted-login-aspnet-core) and the
  [multi-user pattern](#multi-user-pattern-oauth) below).
- **Other services and apps**, with service auth: `AddAuthentication().AddAtProtoServiceAuth(...)`
  verifies the service auth token a feed generator, labeler, AppView or PDS-proxied app sends, bound
  to the XRPC method it calls (see
  [Serving XRPC to other services](xrpc-handlers.md#serving-xrpc-to-other-services)).

```csharp
using ATProtoNet.Server.Authentication;

builder.Services.AddAuthentication()
    .AddAtProtoServiceAuth(o => o.Audiences.Add("did:web:api.example.com#my_service"));
builder.Services.AddAuthorization();

app.MapXrpcEndpoints().RequireServiceAuth();
```

Both put the caller's DID in the `did` claim (`AtProtoClaimTypes.Did`), but only the OAuth login's
identity reaches the account's stored session through `IAtProtoClientFactory`: a service auth
token is good for the one method it names, not for acting as the account. A service does not
accept its users' PDS access tokens: they are meant for the PDS alone.

## Controller Example: Custom App

```csharp
[ApiController]
[Route("api/todos")]
public class TodoController : ControllerBase
{
    private readonly AtProtoClient _client;

    public TodoController(AtProtoClient client) => _client = client;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int limit = 50, [FromQuery] string? cursor = null)
    {
        var todos = _client.GetCollection<TodoItem>();
        var page = await todos.ListAsync(limit: limit, cursor: cursor);

        return Ok(new
        {
            items = page.Records.Select(r => new
            {
                key = r.RecordKey,
                uri = r.Uri,
                title = r.Value.Title,
                completed = r.Value.Completed,
            }),
            cursor = page.Cursor,
            hasMore = page.HasMore,
        });
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key)
    {
        var todos = _client.GetCollection<TodoItem>();

        var item = await todos.FindAsync(RecordKey.Parse(key));
        return item is null ? NotFound() : Ok(item.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TodoItem item)
    {
        var todos = _client.GetCollection<TodoItem>();
        var created = await todos.CreateAsync(item);

        return CreatedAtAction(nameof(Get),
            new { key = created.RecordKey },
            new { uri = created.Uri, key = created.RecordKey });
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] TodoItem item)
    {
        var todos = _client.GetCollection<TodoItem>();
        var updated = await todos.PutAsync(RecordKey.Parse(key), item);

        return Ok(new { uri = updated.Uri, cid = updated.Cid });
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key)
    {
        var todos = _client.GetCollection<TodoItem>();
        await todos.DeleteAsync(RecordKey.Parse(key));
        return NoContent();
    }
}
```

## Minimal API Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAtProto(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});

var app = builder.Build();

// Login on startup
var client = app.Services.GetRequiredService<AtProtoClient>();
await client.LoginAsync("service-account.example.com", "app-password");

var todos = client.GetCollection<TodoItem>();

app.MapGet("/todos", async (int? limit, string? cursor) =>
{
    var page = await todos.ListAsync(limit: limit ?? 50, cursor: cursor);
    return Results.Ok(page);
});

app.MapPost("/todos", async (TodoItem item) =>
{
    var created = await todos.CreateAsync(item);
    return Results.Created(created.Uri, created);
});

app.Run();
```

## Multi-User Pattern (App Password)

For apps where each user authenticates with app passwords (not OAuth):

```csharp
// Create a per-request client
builder.Services.AddAtProto(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
})
.WithLifetime(ServiceLifetime.Scoped);

// Middleware to authenticate the AT Protocol session from a cookie/header
app.Use(async (context, next) =>
{
    var sessionJson = context.Request.Cookies["atproto_session"];
    if (sessionJson is not null)
    {
        var session = JsonSerializer.Deserialize<AtProtoSession>(sessionJson);
        var client = context.RequestServices.GetRequiredService<AtProtoClient>();
        await client.ApplySessionAsync(session!); // no request; refreshes on demand
    }
    await next();
});
```

## Multi-User Pattern (OAuth)

For apps with OAuth-based user login (recommended), use `IAtProtoClientFactory`
from the Server package, which handles DPoP keys, token storage, refresh coordination and per-user
client creation:

```csharp
using ATProtoNet.Server;
using ATProtoNet.Server.Authentication;

builder.Services.AddAuthentication("Cookies").AddCookie();
builder.Services.AddAtProto()
    .WithOAuth()                // OAuth cookie login
    .WithClientFactory()        // Client factory + refresh coordinator
    .WithFileSessionStore();    // Session store (in memory, with a startup warning, by default)

app.MapAtProtoOAuth();

// In endpoints or services:
app.MapGet("/api/profile", async (ClaimsPrincipal user, IAtProtoClientFactory factory) =>
{
    await using var client = await factory.CreateClientForUserAsync(user);
    if (client is null) return Results.Unauthorized();

    var profile = await client.Bsky.Actor.GetProfileAsync(client.Session!.Did);
    return Results.Ok(profile);
}).RequireAuthorization();
```

See [Server Integration](server.md) for full documentation on `IAtProtoClientFactory`, `IAtProtoSessionStore`, and custom session store implementations.
