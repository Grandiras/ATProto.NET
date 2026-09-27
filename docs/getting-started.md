# Installation & Setup

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- A PDS (Personal Data Server) account — either your own or a hosted one like [bsky.social](https://bsky.social)

## Install the Package

```bash
# Core SDK
dotnet add package ATProtoNet

# ASP.NET Core integration — DI, OAuth cookie login, service auth, XRPC endpoints, the space
# server, and .NET Aspire client integration (optional)
dotnet add package ATProtoNet.Server

# EF Core stores for ATProtoNet.Server — sessions, replay table, space server, sync state (optional)
dotnet add package ATProtoNet.Server.EntityFrameworkCore

# Blazor components (optional)
dotnet add package ATProtoNet.Blazor

# Aspire AppHost-side PDS container resource (optional)
dotnet add package ATProtoNet.Aspire.Hosting

# Lexicon CLI: code generation, linting, diffing, publishing and resolving (optional)
dotnet tool install -g ATProtoNet.LexiconGenerator
```

| Package | Depends on | Use it for |
|---------|------------|------------|
| `ATProtoNet` | nothing ASP.NET | the client, identities, OAuth, streaming, repositories, Spaces |
| `ATProtoNet.Server` | `ATProtoNet`, the ASP.NET Core shared framework | an ASP.NET Core application or service |
| `ATProtoNet.Server.EntityFrameworkCore` | `ATProtoNet.Server`, EF Core | the Server package's state in a database |
| `ATProtoNet.Blazor` | `ATProtoNet.Server` | Blazor components acting as the signed-in user |
| `ATProtoNet.Aspire.Hosting` | Aspire hosting | a PDS container in an Aspire AppHost |

See [Architecture](architecture.md) for how they layer.

## Create a Client

There is one constructor, and every argument is optional:

```csharp
using ATProtoNet;

var client = new AtProtoClient(new AtProtoClientOptions { InstanceUrl = "https://your-pds.example.com" });
```

`new AtProtoClient()` alone addresses `https://bsky.social`.

### Options

```csharp
using ATProtoNet.Auth;

var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());

var client = new AtProtoClient(
    new AtProtoClientOptions
    {
        InstanceUrl = "https://your-pds.example.com", // PDS or entryway URL (default: https://bsky.social)
        AutoRefreshSession = true,                     // Refresh tokens on demand (default: true)
        BackgroundRefresh = false,                     // Also refresh on a timer while idle (default: false)
        UserAgent = "MyApp/1.0",                       // User-Agent header (default: ATProtoNet/<version>)
    },
    httpClient: httpClient,                                // Custom HttpClient (default: one the client owns)
    sessionStore: new InMemoryAtProtoSessionStore(),       // Session persistence (default: none)
    logger: loggerFactory.CreateLogger<AtProtoClient>());  // Logging (default: none)
```

The core package ships `InMemoryAtProtoSessionStore`; `ATProtoNet.Server` adds an encrypted file store
and `ATProtoNet.Server.EntityFrameworkCore` an EF Core one, and a custom `IAtProtoSessionStore` is a few lines, as shown in
[Session Management](session-management.md#persisting-sessions).

The client covers one account's session with its PDS. Firehose and Jetstream consumers are built
on their own, independent of it; see [Firehose Streaming](firehose.md).

## Authenticate

### Login with Handle & Password

```csharp
var session = await client.LoginAsync("alice.example.com", "app-password");

Console.WriteLine($"Logged in as: {session.Handle}");
Console.WriteLine($"DID: {session.Did}");

if (client.IsAuthenticated)
    Console.WriteLine($"Authenticated as {client.Handle} ({client.Did})");
```

> **Tip:** Use [App Passwords](https://bsky.app/settings/app-passwords) instead of your main password.

For a user-facing application, sign users in with [OAuth](oauth.md) instead. How sessions are
refreshed, persisted, resumed and ended is on [Session Management](session-management.md).

## What the client offers

`AtProtoClient` groups the Lexicon methods by namespace. IntelliSense and the XML documentation
of each client list its methods.

| Property | Lexicons | Guide |
|----------|----------|-------|
| `GetCollection<T>()` | your own records, as `RecordCollection<T>` | [Custom Lexicon Records](custom-records.md) |
| `QueryAsync` / `ProcedureAsync` / `Transport` | your own XRPC methods | [Custom XRPC Endpoints](custom-xrpc.md) |
| `Server` | `com.atproto.server.*`: sessions, accounts, app passwords, invite codes | [Session Management](session-management.md) |
| `Repo` | `com.atproto.repo.*`: records, blobs, batch writes | [Low-Level Repo API](low-level-repo.md) |
| `Identity` | `com.atproto.identity.*` | [Identity Resolution](did-resolution.md) |
| `Sync` | `com.atproto.sync.*`: repositories, blobs, verified record reads | [Low-Level Repo API](low-level-repo.md#verified-record-reads) |
| `Admin`, `Moderation`, `Label` | `com.atproto.admin.*`, `com.atproto.moderation.*`, `com.atproto.label.*` | [Managed PDS](managed-pds.md), [Labeler Services](labeler.md) |
| `Space`, `SimpleSpace` | `com.atproto.space.*`, `com.atproto.simplespace.*` | [Spaces](spaces.md) |
| `Temp`, `Lexicon` | `com.atproto.temp.*`, `com.atproto.lexicon.*` | [Identity Resolution: Resolving lexicons](did-resolution.md#resolving-lexicons) |
| `Bsky` | `app.bsky.*` | [Bluesky](bluesky.md) |
| `Chat` | `chat.bsky.*` | [Chat & Direct Messages](chat.md) |
| `Ozone` | `tools.ozone.*` | [Ozone Moderation](ozone.md) |
| `Site` | `site.standard.*` | [Standard.site](standard-site.md) |

### Pagination

Every cursored response implements `ICursorPage<T>` (`Items`, `Cursor`). `List*` / `Get*` /
`Search*` methods return one page and take `limit` then `cursor`; `Enumerate*` methods return an
`IAsyncEnumerable<T>` over every page, take `int? pageSize = null` (the server's default), and stop
when the server returns no cursor, an empty one, or one it already returned:

```csharp
var page = await client.Bsky.Graph.GetFollowersAsync(client.Did!, limit: 50);
Console.WriteLine($"{page.Followers.Count} followers, next cursor {page.Cursor}");

await foreach (var follower in client.Bsky.Graph.EnumerateFollowersAsync(client.Did!))
    Console.WriteLine(follower.Handle);
```

### Identifiers

Identifiers are typed: a DID is a `Did`, a record's address an `AtUri`, a collection an `Nsid`, and
so on (see [Identity Types](identity-types.md)). Values the API returns pass straight back in;
parse literals once, where they enter your code:

```csharp
var alice = AtIdentifier.Parse("alice.bsky.social");
var profile = await client.Bsky.Actor.GetProfileAsync(alice);
Console.WriteLine($"{profile.DisplayName} is {profile.Did}");
```

## What's Next?

- [Custom Lexicon Records](custom-records.md) — Build your own AT Protocol app
- [OAuth Authentication](oauth.md) — Secure auth for web apps with DPoP, PAR, PKCE
- [Session Management](session-management.md) — Token refresh, persistence, resume
- [Bluesky](bluesky.md) — posts, feeds, the social graph and notifications
- [ASP.NET Core](aspnet-core.md) — Use in web applications
- [Firehose Streaming](firehose.md) — Real-time event streaming with verification
- [Error Handling](error-handling.md) — the exceptions the SDK throws
- [Migrating to 0.7](migrating-to-0.7.md) — upgrading from 0.6
