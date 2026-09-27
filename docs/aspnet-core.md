# ASP.NET Core Integration

`ATProtoNet.Server` wires the SDK into an ASP.NET Core application: an `AtProtoClient` from
dependency injection, the OAuth cookie login, per-user clients, service auth for calls from other
services, and XRPC endpoints of your own. This page covers the registration; each feature has its
own page:

| Feature | Page |
|---------|------|
| Sign users in with their AT Protocol account | [OAuth: Hosted Login](oauth.md#hosted-login-aspnet-core) |
| Call AT Protocol APIs as the signed-in user, and where their sessions are kept | [Acting as the Signed-In User](server.md) |
| Serve `/xrpc/{nsid}` endpoints, and accept service auth from other services | [XRPC Endpoint Handlers](xrpc-handlers.md) |
| Blazor components | [Blazor](blazor.md) |
| Aspire service defaults and health checks | [.NET Aspire](aspire.md) |

## Installation

```bash
dotnet add package ATProtoNet.Server
```

The package depends on nothing beyond the ASP.NET Core shared framework. The EF Core stores are in
`ATProtoNet.Server.EntityFrameworkCore`.

## The AtProto Builder

`AddAtProto()` registers an `AtProtoClient` and returns an `IAtProtoBuilder`, which the rest of the
registration hangs off:

```csharp
using ATProtoNet.Server;
using ATProtoNet.Server.Authentication;

builder.Services.AddAtProto(options => options.InstanceUrl = "https://your-pds.example.com")
    .WithOAuth()
    .WithClientFactory()
    .WithFileSessionStore()
    .WithHealthCheck();
```

| Method | Registers |
|--------|-----------|
| `AddAtProto(o => …)` | `AtProtoClient` (singleton), configured by `AtProtoClientOptions` |
| `.WithLifetime(ServiceLifetime.Scoped)` | the `AtProtoClient` with another lifetime (one per request) |
| `.WithOAuth(o => …)` | the hosted OAuth login: `AtProtoOAuthService` and its `OAuthClient` (`ATProtoNet.Server.Authentication`) |
| `.WithClientFactory()` | `IAtProtoClientFactory`, the `ISessionRefreshCoordinator`, and an in-memory store by default |
| `.WithInMemorySessionStore()` / `.WithFileSessionStore(…)` / `.WithEfCoreSessionStore<T>()` / `.WithSessionStore<T>()` | the session store (`WithEfCoreSessionStore` is in `ATProtoNet.Server.EntityFrameworkCore`); see [Session stores](server.md#session-stores) |
| `.WithHealthCheck()` | a health check calling `describeServer` through the `AtProtoClient` |
| `.Services` | the service collection |
| `.HttpClient` | the `IHttpClientBuilder` of the named client (`AtProtoServiceCollectionExtensions.HttpClientName`, `"ATProtoNet"`) every `AtProtoClient` of the registration sends with |

The Aspire `builder.AddAtProtoClient()` returns the same builder (see [.NET Aspire](aspire.md)).

### Options and configuration

Every options class (`AtProtoClientOptions`, `AtProtoOAuthServerOptions`, `FileSessionStoreOptions`,
and those of `AddAtProtoIdentity`, `AddAtProtoSpaces` and `AddAtProtoPdsAdmin`) goes through
`IOptions<T>`: it binds from configuration, and a bad value (an `InstanceUrl` that is not an
absolute http(s) URL, OAuth scopes without `atproto`, …) stops the host when it starts with an
`OptionsValidationException` rather than failing the first request. The callbacks you pass
(`AddAtProto(o => …)`, `WithOAuth(o => …)`, …) run after the bound configuration, whichever order
the calls are made in, so code wins.

```csharp
using ATProtoNet.Server.TokenStore;

builder.Services.AddAtProto()
    .WithOAuth()
    .WithClientFactory()
    .WithFileSessionStore();

builder.Services.Configure<AtProtoClientOptions>(builder.Configuration.GetSection("AtProto"));
builder.Services.Configure<AtProtoOAuthServerOptions>(builder.Configuration.GetSection("AtProto:OAuth"));
builder.Services.Configure<FileSessionStoreOptions>(builder.Configuration.GetSection("AtProto:Sessions"));
```

### The registered client

The `AtProtoClient` of `AddAtProto()` is one account's client: a bot, a service account, or a
back end that signs in with an app password. It is a singleton unless `WithLifetime` says
otherwise, and it writes its session to the registered store, so a restart can resume it:

```csharp
builder.Services.AddAtProto(options => options.InstanceUrl = "https://your-pds.example.com")
    .WithFileSessionStore();
```

For many users signed in with OAuth, use the client factory instead, which builds a client per
request from each user's stored session; see [Acting as the Signed-In User](server.md).

### HTTP handlers and resilience

Every client of the registration sends through one named `HttpClient`, whose builder is
`IAtProtoBuilder.HttpClient`. Add logging, telemetry or proxy handlers there. Do **not** add a
handler that retries on its own. The SDK already retries a `429` (`AtProtoClientOptions.RateLimit`),
refreshes an expired session, and signs a fresh DPoP proof for every attempt; a retry below it
sends a non-idempotent `POST` twice (a `createRecord` becomes two records) and resends the request
with the DPoP proof it already carries. A proof is single-use: the server refuses one it has seen,
and one it did not track would be exactly the replay DPoP is meant to stop. The same goes for the
OAuth login's own client (`AtProtoOAuthExtensions.HttpClientName`), whose authorization codes and
refresh tokens are single-use.

The builder's client is shared by the registered `AtProtoClient` and the client factory's per-user
clients, which sign every request of an OAuth session with a DPoP proof, so no retry policy is safe
on it, not even one limited to `GET`. For transient server errors, retry above the SDK, where every
attempt is a new request with a new proof (see [Error Handling](error-handling.md#retry-pattern)).
Aspire service defaults add a retrying handler to every client; [remove it](aspire.md#resilience)
from the SDK's clients.

## Authentication

`ATProtoNet.Server` authenticates two kinds of caller:

- **Users**, with the OAuth cookie login: `AddAtProto().WithOAuth()` and `MapAtProtoOAuth()`
  sign a user in with their AT Protocol account and an ordinary authentication cookie (see
  [OAuth: Hosted Login](oauth.md#hosted-login-aspnet-core)).
- **Other services and apps**, with service auth: `AddAuthentication().AddAtProtoServiceAuth(...)`
  verifies the service auth token a feed generator, labeler, AppView or PDS-proxied app sends, bound
  to the XRPC method it calls (see
  [Serving XRPC to other services](xrpc-handlers.md#serving-xrpc-to-other-services)).

```csharp
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Xrpc;

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

A service account's todo list, kept in its own repository. `TodoItem` is a custom record type (see
[Custom Lexicon Records](custom-records.md)):

```csharp
using Microsoft.AspNetCore.Mvc;

public class TodoItem : AtProtoRecord, IAtProtoRecord
{
    public static Nsid Collection { get; } = Nsid.Parse("com.example.todo.item");
    public override string Type => Collection;

    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("completed")] public bool Completed { get; set; }
}

[ApiController]
[Route("api/todos")]
public class TodoController(AtProtoClient client) : ControllerBase
{
    private readonly RecordCollection<TodoItem> _todos = client.GetCollection<TodoItem>();

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int limit = 50, [FromQuery] string? cursor = null)
    {
        var page = await _todos.ListAsync(limit: limit, cursor: cursor);

        return Ok(new
        {
            items = page.Records.Select(r => new
            {
                key = r.RecordKey.Value,
                uri = r.Uri.Value,
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
        var item = await _todos.FindAsync(RecordKey.Parse(key));
        return item is null ? NotFound() : Ok(item.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TodoItem item)
    {
        var created = await _todos.CreateAsync(item);

        return CreatedAtAction(nameof(Get),
            new { key = created.RecordKey.Value },
            new { uri = created.Uri.Value, key = created.RecordKey.Value });
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] TodoItem item)
    {
        var updated = await _todos.PutAsync(RecordKey.Parse(key), item);
        return Ok(new { uri = updated.Uri.Value, cid = updated.Cid.Value });
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key)
    {
        await _todos.DeleteAsync(RecordKey.Parse(key));
        return NoContent();
    }
}
```

## Minimal API Example

```csharp
using ATProtoNet.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAtProto(options => options.InstanceUrl = "https://your-pds.example.com");

var app = builder.Build();

// Sign the service account in on startup
var client = app.Services.GetRequiredService<AtProtoClient>();
await client.LoginAsync("service-account.example.com", "app-password");

var todos = client.GetCollection<TodoItem>();

app.MapGet("/todos", async (int? limit, string? cursor) =>
    Results.Ok(await todos.ListAsync(limit: limit ?? 50, cursor: cursor)));

app.MapPost("/todos", async (TodoItem item) =>
{
    var created = await todos.CreateAsync(item);
    return Results.Created(created.Uri.Value, created);
});

app.Run();
```

## Multi-User Pattern (App Password)

For apps where each user authenticates with an app password (not OAuth), register a client per
request and install the user's session on it:

```csharp
using ATProtoNet.Auth;
using ATProtoNet.Server;

builder.Services.AddAtProto(options => options.InstanceUrl = "https://your-pds.example.com")
    .WithLifetime(ServiceLifetime.Scoped);

var app = builder.Build();

// Install the AT Protocol session this request carries (kept server-side in a real app)
app.Use(async (context, next) =>
{
    var sessionJson = context.Request.Cookies["atproto_session"];
    if (sessionJson is not null && JsonSerializer.Deserialize<AtProtoSession>(sessionJson) is { } session)
    {
        var client = context.RequestServices.GetRequiredService<AtProtoClient>();
        await client.ApplySessionAsync(session);   // no request; refreshes on demand
    }
    await next();
});
```

For users signed in with OAuth (recommended), use `WithClientFactory()` and
`IAtProtoClientFactory` instead; see [Acting as the Signed-In User](server.md).
