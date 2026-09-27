# Acting as the Signed-In User

A web application that signs users in with the [OAuth cookie login](oauth.md#hosted-login-aspnet-core)
keeps each user's OAuth session on the server and calls AT Protocol APIs as that user. This page
covers the two pieces that make it work, both in `ATProtoNet.Server`: `IAtProtoClientFactory`, which
builds an `AtProtoClient` for the user of a request, and the session store it reads. The rest of the
registration is on [ASP.NET Core Integration](aspnet-core.md).

## Quick Start

### 1. Register Services

```csharp
using ATProtoNet.Server;
using ATProtoNet.Server.Authentication;

builder.Services.AddAuthentication("Cookies").AddCookie();
builder.Services.AddAtProto()
    .WithOAuth()                   // the OAuth cookie login
    .WithClientFactory()           // backend AT Proto access as the signed-in user
    .WithFileSessionStore();       // where the sessions are kept (in memory by default)
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapAtProtoOAuth();
```

### 2. Use in API Endpoints

```csharp
using System.Security.Claims;
using ATProtoNet.Server.Services;

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
its widgets use and your components can too; see [Blazor: Widgets](blazor.md#widgets).

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

```csharp partial
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

The returned client is **disposable** — always use `await using`.

## Session stores

The factory reads each user's session from the registered `IAtProtoSessionStore`, the same
interface `AtProtoClient` persists its own session to (see
[Session Management: Persisting sessions](session-management.md#persisting-sessions) for the
interface and a custom implementation). The store is chosen on the builder, and the choice wins
whichever order the calls run in:

| Call | Store |
|------|-------|
| *(none)* | `InMemoryAtProtoSessionStore`, with a warning at startup |
| `WithInMemorySessionStore()` | `InMemoryAtProtoSessionStore`, on purpose: no warning |
| `WithFileSessionStore(o => o.Directory = …)` | `FileAtProtoSessionStore` |
| `WithEfCoreSessionStore<TContext>()` | `EfCoreAtProtoSessionStore<TContext>` (`ATProtoNet.Server.EntityFrameworkCore`) |
| `WithSessionStore<TStore>()` | Your own |

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

### Entity Framework Core

`ATProtoNet.Server.EntityFrameworkCore` has an EF Core implementation for deployments with more
than one instance:

```bash
dotnet add package ATProtoNet.Server.EntityFrameworkCore
```

Register it with your `DbContext`:

```csharp
using ATProtoNet.Server.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));

builder.Services.AddAtProto()
    .WithOAuth()
    .WithClientFactory()
    .WithEfCoreSessionStore<AppDbContext>();

public class AppDbContext : AtProtoTokenDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Your other DbSets...
}
```

Or add the entity to a context you already have:

```csharp
using ATProtoNet.Server.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public class ExistingDbContext : DbContext
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
`com.atproto.simplespace` member lists; see [Spaces: The stores](spaces.md#the-stores) —
`EfCoreJtiReplayStore<T>`, the single-use-token replay table that service auth and the space
server share (register it with `AddAtProtoEfCoreJtiReplayStore<T>()`, over `JtiReplayDbContext` or
any context calling `JtiReplayDbContext.ConfigureJtiReplayModel`), and the Sync 1.1 repository
state of a verifying firehose consumer (see [Firehose: Keeping state](firehose.md#keeping-state)).

### Your own store

A store over your own database is registered with `WithSessionStore<TStore>()`. It is a
**singleton** called concurrently, so it must not hold a scoped service such as a `DbContext`:
take an `IDbContextFactory` and open a context per call, as the EF Core store does. Encrypt what
you persist: an `OAuthSession` carries its DPoP private key (`DPoPKey`, unencrypted PKCS#8) and
every session its tokens.

## Sample

See [samples/ServerIntegrationSample/](../samples/ServerIntegrationSample/) for a complete working example with:
- the OAuth cookie login and `LoginForm`
- a profile page using `ProfileCard`
- a timeline page using `ComposePost` and `FeedView`, whose posts can be liked and reposted
- minimal API endpoints (`/api/profile`, `/api/timeline`) using `IAtProtoClientFactory`
