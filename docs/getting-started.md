# Installation & Setup

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- A PDS (Personal Data Server) account — either your own or a hosted one like [bsky.social](https://bsky.social)

## Install the Package

```bash
# Core SDK
dotnet add package ATProtoNet

# ASP.NET Core integration — DI, OAuth cookie login, service auth, EF Core token store,
# and .NET Aspire client integration (optional)
dotnet add package ATProtoNet.Server

# Blazor components (optional)
dotnet add package ATProtoNet.Blazor

# Aspire AppHost-side PDS container resource (optional)
dotnet add package ATProtoNet.Aspire.Hosting

# Lexicon code generator CLI tool (optional)
dotnet tool install -g ATProtoNet.LexiconGenerator
```

## Create a Client

There is one constructor, and every argument is optional:

```csharp
using ATProtoNet;

var client = new AtProtoClient(new AtProtoClientOptions { InstanceUrl = "https://your-pds.example.com" });
```

`new AtProtoClient()` alone addresses `https://bsky.social`.

### Options

```csharp
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

The core package ships `InMemoryAtProtoSessionStore`; `ATProtoNet.Server` adds encrypted file and EF Core
stores, and a custom `IAtProtoSessionStore` is a few lines, as shown in
[Session Management](session-management.md#persisting-sessions).

The client covers one account's session with its PDS. Firehose and Jetstream consumers are built
on their own, independent of it; see [Firehose Streaming](firehose.md).

## Authenticate

### Login with Handle & Password

```csharp
var session = await client.LoginAsync("alice.example.com", "app-password");

Console.WriteLine($"Logged in as: {session.Handle}");
Console.WriteLine($"DID: {session.Did}");
```

> **Tip:** Use [App Passwords](https://bsky.app/settings/app-passwords) instead of your main password.

### Check Authentication State

```csharp
if (client.IsAuthenticated)
{
    Console.WriteLine($"Authenticated as {client.Handle} ({client.Did})");
}
```

### Session Properties

After login, you can access:

```csharp
client.Did       // Did: did:plc:abc123...
client.Handle    // Handle: alice.example.com
client.Session   // AtProtoSession — here a PasswordSession, with tokens, email, etc.
```

## Bookmarks

Bluesky bookmarks are private: the appview stores them for their owner, outside the repository.
Bookmark a post by its URI and CID, and read them back newest first:

```csharp
await client.Bsky.Bookmark.CreateBookmarkAsync(post.Uri, post.Cid);

await foreach (var bookmark in client.Bsky.Bookmark.EnumerateBookmarksAsync())
{
    var text = bookmark.Item switch
    {
        PostView view => view.Record.GetProperty("text").GetString(),
        NotFoundPost => "(deleted)",
        BlockedPost => "(blocked)",
        _ => "(unsupported)",
    };
    Console.WriteLine(text);
}

await client.Bsky.Bookmark.DeleteBookmarkAsync(post.Uri);
```

`PostView.Viewer.Bookmarked` and `PostView.BookmarkCount` show a post's bookmark state in feeds.

## What's Next?

- [Custom Lexicon Records](custom-records.md) — Build your own AT Protocol app
- [OAuth Authentication](oauth.md) — Secure auth for web apps with DPoP, PAR, PKCE
- [Session Management](session-management.md) — Token refresh, persistence, resume
- [ASP.NET Core](aspnet-core.md) — Use in web applications
- [Blazor](blazor.md) — Components with OAuth login support
- [Firehose Streaming](firehose.md) — Real-time event streaming with verification
- [.NET Aspire](aspire.md) — Cloud-native integration
- [Managed PDS](managed-pds.md) — run the Bluesky PDS container and administer it
