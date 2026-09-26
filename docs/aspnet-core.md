# ASP.NET Core Integration

ATProto.NET provides first-class ASP.NET Core integration through the `ATProtoNet.Server` package.

## Installation

```bash
dotnet add package ATProtoNet.Server
```

## Service Registration

### Singleton Client

Register a single shared `AtProtoClient`:

```csharp
// Program.cs
builder.Services.AddAtProto(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});
```

### Custom Session Store

```csharp
builder.Services.AddAtProto<DatabaseSessionStore>(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});
```

### Scoped Client (Per-Request)

For multi-user scenarios where each request has its own session:

```csharp
builder.Services.AddAtProtoScoped(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});
```

## Authentication

`ATProtoNet.Server` authenticates two kinds of caller:

- **Users**, with the OAuth cookie login: `AddAtProtoAuthentication()` and `MapAtProtoOAuth()`
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

Both put the caller's DID in the `did` claim (`AtProtoClaimTypes.Did`). A service does not accept
its users' PDS access tokens: they are meant for the PDS alone.

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
builder.Services.AddAtProtoScoped(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});

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
builder.Services.AddAtProtoAuthentication(); // OAuth cookie login
builder.Services.AddAtProtoServer();          // Session store + client factory

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
