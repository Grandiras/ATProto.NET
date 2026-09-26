# Server-Side AT Protocol Integration

ATProtoNet.Server provides tools for integrating AT Protocol access into ASP.NET Core applications:
the OAuth cookie login, and authenticated backend API calls with the stored OAuth sessions. The
Blazor components in ATProtoNet.Blazor build on it. The package depends on nothing beyond the
ASP.NET Core shared framework; the EF Core stores are in `ATProtoNet.Server.EntityFrameworkCore`.

## Quick Start

### 1. Register Services

```csharp
// Program.cs
using ATProtoNet.Server;
using ATProtoNet.Server.Authentication;

builder.Services.AddAuthentication("Cookies").AddCookie();
builder.Services.AddAtProto()
    .WithOAuth()                   // OAuth cookie login
    .WithClientFactory()           // Backend AT Proto access as the signed-in user
    .WithFileSessionStore();       // Where the sessions are kept (in memory by default)
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAtProtoOAuth();
```

See [OAuth: Hosted Login](oauth.md#hosted-login-aspnet-core) for the login's endpoints, claims
(`AtProtoClaimTypes`) and production configuration, and [the builder](#the-atproto-builder) below
for everything `AddAtProto()` hangs off.

### 2. Use in API Endpoints

```csharp
app.MapGet("/api/profile", async (ClaimsPrincipal user, IAtProtoClientFactory factory) =>
{
    await using var client = await factory.CreateClientForUserAsync(user);
    if (client is null) return Results.Unauthorized();

    var profile = await client.Bsky.Actor.GetProfileAsync(client.Session!.Did);
    return Results.Ok(new { profile.DisplayName, profile.Handle, profile.Description });
}).RequireAuthorization();
```

### 3. Use in Blazor Components

`ATProtoNet.Blazor` registers a scoped client for the signed-in user (`AddAtProtoBlazor()`), which
its widgets use and your components can too:

```razor
@page "/profile"
@using ATProtoNet.Blazor
@attribute [Authorize]
@inject AtProtoUserClientAccessor UserClient

@code {
    protected override async Task OnInitializedAsync()
    {
        var client = await UserClient.GetClientAsync();   // shared by the circuit; do not dispose
        if (client is null) return;

        var profile = await client.Bsky.Actor.GetProfileAsync(client.Session!.Did);
        // Use profile data...
    }
}
```

## How It Works

```
┌───────────────────── Browser ─────────────────────┐
│  User clicks "Sign In" → <LoginForm> submits to   │
│  GET /atproto/login?handle=alice.bsky.social       │
└──────────────┬────────────────────────────────────┘
               │
┌──────────────▼────────────────────────────────────┐
│               ASP.NET Core Server                  │
│                                                    │
│  1. /atproto/login → AtProtoOAuthService           │
│     → Resolves PDS, starts OAuth, redirects        │
│                                                    │
│  2. /atproto/callback ← Authorization Server       │
│     → Exchanges code for DPoP-bound tokens         │
│     → Creates claims (DID, handle, PDS URL)        │
│     → Issues cookie via SignInAsync()               │
│     → Stores session in IAtProtoSessionStore ─┐    │
│                                               │    │
│  3. API endpoint or Blazor component           │    │
│     → IAtProtoClientFactory                    │    │
│       → Reads DID from cookie claims           │    │
│       → Restores the session ◄────────────────┘    │
│       → Creates authenticated AtProtoClient        │
│       → Calls AT Proto APIs on user's PDS          │
│       → Refreshes on demand, writes rotated        │
│         tokens back to the store                   │
│                                                    │
│  4. /atproto/logout                                │
│     → Revokes the session (RFC 7009), removes it   │
└────────────────────────────────────────────────────┘
```

Key security points:
- **No tokens in the browser.** OAuth tokens and DPoP private keys stay server-side.
- **Cookie is encrypted** by ASP.NET Core Data Protection.
- **DPoP-bound tokens** — even if intercepted, tokens can't be used without the private key.
- **Per-request clients** — `IAtProtoClientFactory` creates a new `AtProtoClient` per call, avoiding token leakage between requests.
- **One refresh at a time per user** — the per-request clients refresh under the account's lock, so a single-use refresh token is spent once.

## `IAtProtoClientFactory`

Creates authenticated `AtProtoClient` instances from stored sessions. Each client refreshes its
session on demand (before the access token expires, and after the PDS rejects it) and writes the
rotated tokens back to the store, so the next request's client starts from them.

```csharp
public interface IAtProtoClientFactory
{
    Task<AtProtoClient?> CreateClientForUserAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
}
```

Returns `null` when:
- The principal carries no user of the OAuth login: an authenticated identity the login issued
  (authentication type `ATProto`), or one with an `auth_method` claim of `oauth`, holding a `did`
  (or `ClaimTypes.NameIdentifier`) claim
- No session is stored for the user's DID (signed out, or expired)

Identities of other schemes are ignored. Service auth in particular issues the same `did` claim
for whoever holds a token naming that DID, and such a caller must not act through the account's
stored OAuth session; on an endpoint that accepts both schemes, the factory still returns the
cookie user's client, or none. A principal you build yourself counts when it carries
`auth_method` = `oauth` (`AtProtoClaimTypes.AuthMethod`).

The clients refresh under the `ISessionRefreshCoordinator` that `WithClientFactory()` registers:
when two requests for the same user both find the access token about to expire, one refreshes and
stores the new tokens, and the other reads them from the store instead of spending the refresh token
again (see [OAuth: Refreshing Across Requests](oauth.md#refreshing-across-requests)). The in-process
coordinator covers one process; instances sharing a store need a distributed lock behind the
interface. OAuth sessions are refreshed and revoked with the `OAuthClient` registered in dependency
injection, which `WithOAuth()` provides; the factory resolves it when a client first needs it, so
resolving the factory before the server has started (in a hosted service, say) is fine.

The clients send with the builder's `HttpClient` and take `UserAgent` and `RateLimit` from
`AddAtProto(o => …)`. The other `AtProtoClientOptions` describe the registered client's own session
and do not apply to them: each addresses its user's PDS and refreshes on demand.

The factory keeps the imported DPoP key of each account it has recently served (up to 1,024), so a
request does not pay for importing it again. A sign-out, a refused session, or a session found gone
from the store drops and releases the account's key, so no client of that session signs with it again.

The returned client is **disposable** — always use `await using`:

```csharp
await using var client = await factory.CreateClientForUserAsync(user);
```

## `IAtProtoSessionStore`

Server-side session storage, keyed by DID. It is the same interface `AtProtoClient` persists its own
session to (see [Session Management](session-management.md#persisting-sessions)).

```csharp
public interface IAtProtoSessionStore
{
    ValueTask<AtProtoSession?> GetAsync(Did did, CancellationToken ct = default);
    ValueTask SetAsync(AtProtoSession session, CancellationToken ct = default);
    ValueTask RemoveAsync(Did did, CancellationToken ct = default);
}
```

The store is chosen on the builder, and the choice wins whichever order the calls run in:

| Call | Store |
|------|-------|
| *(none)* | `InMemoryAtProtoSessionStore`, with a warning at startup |
| `WithInMemorySessionStore()` | `InMemoryAtProtoSessionStore`, on purpose: no warning |
| `WithFileSessionStore(o => o.Directory = …)` | `FileAtProtoSessionStore` |
| `WithEfCoreSessionStore<TContext>()` | `EfCoreAtProtoSessionStore<TContext>` (`ATProtoNet.Server.EntityFrameworkCore`) |
| `WithSessionStore<TStore>()` / `WithSessionStore(sp => …)` | Your own |

### Default: In Memory

`WithClientFactory()` keeps sessions in memory unless you choose a store, and says so in a warning
when the host starts: a restart loses every session (users have to sign in again), and a second
instance does not see them. Nothing is written to disk unless you ask for it. Keep the in-memory
store without the warning, for development or tests, with `WithInMemorySessionStore()`.

### `FileAtProtoSessionStore`

Stores each session as an encrypted file using ASP.NET Core Data Protection. Sessions persist across
app restarts. Suitable for single-server deployments. Files written by the 0.6
`FileAtProtoTokenStore` are read as they are. Reads take no lock (a write replaces the file in one
rename), and writes are serialized per account.

```csharp
// {LocalApplicationData}/ATProtoNet/tokens/ by default
builder.Services.AddAtProto().WithClientFactory().WithFileSessionStore();

// Custom directory
builder.Services.AddAtProto().WithClientFactory()
    .WithFileSessionStore(o => o.Directory = "/var/data/atproto-tokens");
```

Keep the Data Protection key ring somewhere that survives a restart (in a container, persist it with
`AddDataProtection().PersistKeysTo…`), or the stored sessions cannot be decrypted.

### Custom Implementation

For production, implement `IAtProtoSessionStore` with a durable, encrypted store. Sessions serialize
with `System.Text.Json` as `AtProtoSession`. The store is registered as a **singleton** and called
concurrently, so it must not hold a scoped service such as a `DbContext`: take an
`IDbContextFactory` and open a context per call, as the EF Core store does.

```csharp
public class DatabaseSessionStore(IDbContextFactory<MyDbContext> contexts, IDataProtectionProvider protection)
    : IAtProtoSessionStore
{
    private readonly IDataProtector _protector = protection.CreateProtector("MyApp.Sessions");

    public async ValueTask SetAsync(AtProtoSession session, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var entry = await db.Sessions.FindAsync([session.Did.Value], ct);
        if (entry is null)
            db.Sessions.Add(entry = new SessionEntry { Did = session.Did.Value });

        entry.Payload = _protector.Protect(JsonSerializer.Serialize(session));
        await db.SaveChangesAsync(ct);
    }

    // ... GetAsync, RemoveAsync
}

// Register:
builder.Services.AddDbContextFactory<MyDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddAtProto().WithClientFactory().WithSessionStore<DatabaseSessionStore>();
```

> **Security:** an `OAuthSession` carries its DPoP private key (`DPoPKey`, unencrypted PKCS#8) and
> every session its tokens. Always encrypt sessions before persisting them.

### Entity Framework Core Session Store

ATProtoNet provides a ready-made EF Core implementation in the `ATProtoNet.Server.EntityFrameworkCore`
package (namespace `ATProtoNet.Server.EntityFrameworkCore`):

```bash
dotnet add package ATProtoNet.Server.EntityFrameworkCore
```

Register with your `DbContext`:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));

// Add the EF Core session store
builder.Services.AddAtProto()
    .WithOAuth()
    .WithClientFactory()
    .WithEfCoreSessionStore<AppDbContext>();
```

Your `DbContext` must inherit from `AtProtoTokenDbContext` or include the `AtProtoTokenEntity` set:

```csharp
public class AppDbContext : AtProtoTokenDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Your other DbSets...
}
```

Or add the entity to an existing context:

```csharp
public class AppDbContext : DbContext
{
    public DbSet<AtProtoTokenEntity> AtProtoTokens => Set<AtProtoTokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        AtProtoTokenDbContext.ConfigureAtProtoTokenModel(modelBuilder);
    }
}
```

The `EfCoreAtProtoSessionStore` encrypts each session with ASP.NET Core Data Protection and keeps it
in the `EncryptedTokenData` column. The table (`AtProtoTokens`: `Did`, `EncryptedTokenData`,
`UpdatedAt`) is the one the 0.6 token store used, so upgrading needs no migration, and rows the 0.6
store wrote are read as they are.

The same package also carries EF Core stores for the space server — the writer set and the
`com.atproto.simplespace` member lists; see [Permissioned Data (Spaces)](spaces.md#the-stores) —
and `EfCoreJtiReplayStore<T>`, the single-use-token replay table that service auth and the space
server share (register it with `AddAtProtoEfCoreJtiReplayStore<T>()`, over `JtiReplayDbContext` or
any context calling `JtiReplayDbContext.ConfigureJtiReplayModel`).

## Standalone Client (Server-to-Server)

For bot or service scenarios where you authenticate with app passwords (not user OAuth),
`AddAtProto()` registers one `AtProtoClient`, a singleton by default:

```csharp
builder.Services.AddAtProto(options =>
{
    options.InstanceUrl = "https://bsky.social";
});

// In a controller or service:
public class MyService
{
    private readonly AtProtoClient _client;

    public MyService(AtProtoClient client) => _client = client;

    public async Task PostAsync(string text)
    {
        await _client.LoginAsync("my-bot.bsky.social", "app-password-here");
        await _client.Bsky.PostAsync(text);
    }
}
```

## The AtProto Builder

`AddAtProto()` registers the `AtProtoClient` and returns an `IAtProtoBuilder`, which the rest of the
registration hangs off:

| Method | Registers |
|--------|-----------|
| `AddAtProto(o => …)` | `AtProtoClient` (singleton), configured by `AtProtoClientOptions` |
| `.WithLifetime(ServiceLifetime.Scoped)` | the `AtProtoClient` with another lifetime (one per request) |
| `.WithOAuth(o => …)` | the hosted OAuth login: `AtProtoOAuthService` and its `OAuthClient` |
| `.WithClientFactory()` | `IAtProtoClientFactory`, the `ISessionRefreshCoordinator`, and an in-memory store by default |
| `.WithInMemorySessionStore()` / `.WithFileSessionStore(…)` / `.WithEfCoreSessionStore<T>()` / `.WithSessionStore<T>()` | the session store |
| `.WithHealthCheck()` | a health check calling `describeServer` through the `AtProtoClient` |
| `.Services` | the service collection |
| `.HttpClient` | the `IHttpClientBuilder` of the named client (`AtProtoServiceCollectionExtensions.HttpClientName`) every `AtProtoClient` of the registration sends with |

Every options class (`AtProtoClientOptions`, `AtProtoOAuthServerOptions`, `FileSessionStoreOptions`,
and those of `AddAtProtoIdentity`, `AddAtProtoSpaces` and `AddAtProtoPdsAdmin`) goes through
`IOptions<T>`: it binds from configuration, and a bad value stops the host when it starts with an
`OptionsValidationException` rather than failing the first request. The callbacks you pass
(`AddAtProto(o => …)`, `WithOAuth(o => …)`, …) run after the bound configuration, whichever order
the calls are made in, so code wins.

```csharp
builder.Services.AddAtProto()
    .WithOAuth()
    .WithClientFactory()
    .WithFileSessionStore();

builder.Services.Configure<AtProtoClientOptions>(builder.Configuration.GetSection("AtProto"));
builder.Services.Configure<AtProtoOAuthServerOptions>(builder.Configuration.GetSection("AtProto:OAuth"));
builder.Services.Configure<FileSessionStoreOptions>(builder.Configuration.GetSection("AtProto:Sessions"));
```

### HTTP handlers and resilience

Add logging, telemetry or proxy handlers through `IAtProtoBuilder.HttpClient`. Do **not** add a
handler that retries on its own. The SDK already retries a `429` (`AtProtoClientOptions.RateLimit`),
refreshes an expired session, and signs a fresh DPoP proof for every attempt; a retry below it
sends a non-idempotent `POST` twice (a `createRecord` becomes two records) and resends the request
with the DPoP proof it already carries. A proof is single-use: the server refuses one it has seen,
and one it did not track would be exactly the replay DPoP is meant to stop. The same goes for the
OAuth login's own client (`AtProtoOAuthExtensions.HttpClientName`), whose authorization codes and
refresh tokens are single-use.

The builder's client (`"ATProtoNet"`) is shared by the registered `AtProtoClient` and the client
factory's per-user clients, which sign every request of an OAuth session with a DPoP proof, so no
retry policy is safe on it, not even one limited to `GET`. For transient server errors, retry
above the SDK, where every attempt is a new request with a new proof (see
[Error Handling](error-handling.md#retry-pattern)).

Aspire service defaults add the standard resilience handler to every client through
`ConfigureHttpClientDefaults` (with retries, and a 10-second attempt timeout that also cuts off a
large `getRepo` or `getBlob` download). Take it off the SDK's clients with `RemoveAllResilienceHandlers()`
(from `Microsoft.Extensions.Http.Resilience`, which marks it experimental: `EXTEXP0001`):

```csharp
#pragma warning disable EXTEXP0001
var atproto = builder.Services.AddAtProto().WithOAuth().WithClientFactory();
atproto.HttpClient.RemoveAllResilienceHandlers();
builder.Services.AddHttpClient(AtProtoOAuthExtensions.HttpClientName).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
```

## Sample

See [samples/ServerIntegrationSample/](../samples/ServerIntegrationSample/) for a complete working example with:
- the OAuth cookie login and `LoginForm`
- a profile page using `ProfileCard`
- a timeline page using `ComposePost` and `FeedView`, whose posts can be liked and reposted
- minimal API endpoints (`/api/profile`, `/api/timeline`) using `IAtProtoClientFactory`

## XRPC Endpoint Handlers

ATProtoNet.Server supports defining server-side XRPC endpoint handlers using a DI-friendly interface pattern. This is useful for building AT Protocol services (PDS, appview, relay, etc.) that expose `/xrpc/{nsid}` routes.

### Defining a Query Endpoint

Each handler declares the NSID it serves once, as a static property:

```csharp
using ATProtoNet.Identity;
using ATProtoNet.Server.Xrpc;

public class GetStatusEndpoint : IXrpcQuery<StatusOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.getStatus");

    public Task<StatusOutput> HandleAsync(HttpContext context, CancellationToken ct)
    {
        return Task.FromResult(new StatusOutput { Status = "ok" });
    }
}
```

For queries with parameters:

```csharp
public class GetTimelineEndpoint : IXrpcQuery<TimelineParams, TimelineOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("app.bsky.feed.getTimeline");

    public Task<TimelineOutput> HandleAsync(TimelineParams parameters, HttpContext context, CancellationToken ct)
    {
        // parameters are bound from the query string by each property's type: a list takes
        // every value of a repeated key (or its single one), and a Did or AtUri is validated
        // by its parser
        // ...
    }
}
```

### Defining a Procedure Endpoint

```csharp
public class CreateRecordEndpoint : IXrpcProcedure<CreateRecordInput, CreateRecordOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.atproto.repo.createRecord");

    public Task<CreateRecordOutput> HandleAsync(CreateRecordInput input, HttpContext context, CancellationToken ct)
    {
        // input is deserialized from the JSON request body
        // ...
    }
}
```

For procedures that return no output:

```csharp
public class PingEndpoint : IXrpcProcedureVoid<PingInput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.ping");

    public Task HandleAsync(PingInput input, HttpContext context, CancellationToken ct)
    {
        // ...
        return Task.CompletedTask;
    }
}
```

Procedures with no input implement `IXrpcProcedure<TOutput>` or `IXrpcProcedureVoid`; a procedure
taking bytes (a blob upload) implements `IXrpcBlobProcedure<TOutput>`, and a query answering with
bytes implements `IXrpcBlobQuery<TParams>`.

### Registering Endpoints

Register individual endpoints or scan an entire assembly:

```csharp
// Individual registration
builder.Services.AddXrpcEndpoint<GetStatusEndpoint>();

// Assembly scanning (finds every class implementing IXrpcEndpoint)
builder.Services.AddXrpcEndpointsFromAssembly(typeof(Program).Assembly);

var app = builder.Build();

// Map all registered endpoints as /xrpc/{nsid} routes. The result is the /xrpc route group,
// so conventions apply to every XRPC endpoint.
app.MapXrpcEndpoints()
    .RequireAuthorization();
```

Query endpoints map as `GET /xrpc/{nsid}`, procedures map as `POST /xrpc/{nsid}`. `[Authorize]`,
`[AllowAnonymous]` and `[EnableRateLimiting]` on a handler class apply to that endpoint. Every
failure is answered with an XRPC error body: an `XrpcException` with its own status and error
name, a request that does not bind with `400 InvalidRequest`, and any other exception with
`500 InternalServerError`, logged and without its message. An NSID no handler serves answers
`501 MethodNotImplemented`, and a registered one called with the wrong HTTP method answers `405`.

A service called by other AT Protocol services — a feed generator, labeler or AppView — authenticates
those calls with service auth: `AddAuthentication().AddAtProtoServiceAuth(...)` and
`app.MapXrpcEndpoints().RequireServiceAuth()`. See
[Serving XRPC to other services](xrpc-handlers.md#serving-xrpc-to-other-services).

For a comprehensive guide covering dependency injection, combining with PDS hosting, and more examples, see [XRPC Endpoint Handlers](xrpc-handlers.md).
