# ATProto.NET

[![CI](https://github.com/Grandiras/ATProto.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/Grandiras/ATProto.NET/actions/workflows/ci.yml)

A comprehensive .NET SDK for the [AT Protocol](https://atproto.com). Build custom AT Protocol applications with your own Lexicon schemas, or interact with Bluesky — all with clean, modern .NET 10 APIs.

**Source:** [Forgejo (canonical)](https://git.grandiras.net/Grandiras/ATProto.NET) · [GitHub (mirror — issues & PRs welcome here)](https://github.com/Grandiras/ATProto.NET)

⚠️ Disclosure: This repository was mainly created by a coding agent. Thorough testing has been conducted. The maintainer is a human though (me :), Grandiras)

## Why ATProto.NET?

The AT Protocol isn't just Bluesky — it's an open protocol where **one account works across many apps**. Each app defines its own [Lexicon schemas](https://atproto.com/guides/lexicon) and stores records in the user's Personal Data Server (PDS). ATProto.NET makes it easy to build these custom applications in .NET.

## Features

- **Custom Lexicon support** — `RecordCollection<T>` for typed CRUD on your own record schemas
- **Full AT Protocol** — authentication, repositories, identity, sync, admin, labels, moderation
- **Custom XRPC endpoints** — call your own query/procedure methods
- **Bluesky APIs** — actors, feeds, posts, social graph, notifications, rich text
- **ASP.NET Core integration** — dependency injection, JWT authentication handler
- **Blazor components** — login forms, profile cards, post cards, feed views, composers
- **Rich text builder** — fluent API with automatic UTF-8 byte offset calculation
- **Firehose client** — real-time WebSocket streaming
- **Type-safe identity** — `Did`, `Handle`, `AtUri`, `Nsid`, `Tid`, `RecordKey`, `Cid`
- **Automatic session management** — token refresh, persistence, resume

## Quick Start

### Install

```bash
dotnet add package ATProtoNet
```

### Connect & Authenticate

```csharp
using ATProtoNet;

var client = new AtProtoClientBuilder()
    .WithInstanceUrl("https://your-pds.example.com")
    .Build();

await client.LoginAsync("alice.example.com", "app-password");
```

## Building Custom AT Protocol Apps

The core value of ATProto.NET is enabling you to build **your own applications** on the AT Protocol. Define your Lexicon record types as C# classes, then use the typed `RecordCollection<T>` API for full CRUD.

### 1. Define Your Record Types

```csharp
using System.Text.Json.Serialization;
using ATProtoNet;

// A simple todo item stored in the user's PDS
public class TodoItem : AtProtoRecord
{
    public override string Type => "com.example.todo.item";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; }
}
```

`AtProtoRecord` provides `$type` and `createdAt` fields automatically. You can also use any plain C# class — the collection API works with any serializable type.

### 2. Get a Typed Collection

```csharp
// Get a strongly-typed collection for your record type
var todos = client.GetCollection<TodoItem>("com.example.todo.item");
```

### 3. CRUD Operations

```csharp
// Create
var created = await todos.CreateAsync(new TodoItem
{
    Title = "Buy groceries",
    Priority = 2,
});
Console.WriteLine($"Created: {created.Uri} (key: {created.RecordKey})");

// Read
var item = await todos.GetAsync(created.RecordKey);
Console.WriteLine($"Title: {item.Value.Title}");

// Update (upsert)
await todos.PutAsync(created.RecordKey, new TodoItem
{
    Title = "Buy groceries",
    Completed = true,
    Priority = 2,
});

// Delete
await todos.DeleteAsync(created.RecordKey);

// Check existence
bool exists = await todos.ExistsAsync(created.RecordKey);
```

### 4. List & Paginate

```csharp
// List with pagination
var page = await todos.ListAsync(limit: 25);
foreach (var record in page.Records)
    Console.WriteLine($"[{(record.Value.Completed ? "x" : " ")}] {record.Value.Title}");

if (page.HasMore)
{
    var nextPage = await todos.ListAsync(limit: 25, cursor: page.Cursor);
}

// Enumerate all records (automatic pagination)
await foreach (var record in todos.EnumerateAsync())
{
    Console.WriteLine($"{record.RecordKey}: {record.Value.Title}");
}
```

### 5. Read From Other Users

```csharp
// Read records from any user's repository
var theirTodos = await todos.ListFromAsync("did:plc:otherperson");

await foreach (var record in todos.EnumerateFromAsync("did:plc:otherperson"))
{
    Console.WriteLine(record.Value.Title);
}

var specificItem = await todos.GetFromAsync("did:plc:otherperson", "3abc");
```

### 6. Custom XRPC Endpoints

If your app defines custom query or procedure Lexicon methods (not just record types), call them directly:

```csharp
// Custom query (HTTP GET)
var result = await client.QueryAsync<SearchResult>(
    "com.example.todo.search",
    new { q = "groceries", limit = 10 });

// Custom procedure (HTTP POST)
var status = await client.ProcedureAsync<BatchResult>(
    "com.example.todo.markAllComplete",
    new { before = "2024-01-01" });

// Fire-and-forget procedure
await client.ProcedureAsync("com.example.todo.cleanup");
```

### Multi-App Example

One AT Protocol account can power many apps — each with its own Lexicons:

```csharp
await client.LoginAsync("alice.example.com", "app-password");

// Todo app
var todos = client.GetCollection<TodoItem>("com.example.todo.item");

// Bookmark manager
var bookmarks = client.GetCollection<Bookmark>("com.example.bookmarks.bookmark");

// Recipe collection
var recipes = client.GetCollection<Recipe>("com.example.recipes.recipe");

// All stored in the same user's PDS, in separate collections
await todos.CreateAsync(new TodoItem { Title = "Cook dinner" });
await bookmarks.CreateAsync(new Bookmark { Url = "https://example.com", Title = "Example" });
await recipes.CreateAsync(new Recipe { Name = "Pasta", Ingredients = ["pasta", "sauce"] });
```

## Bluesky Integration

ATProto.NET also provides full Bluesky application support:

### Create a Post

```csharp
await client.PostAsync("Hello from ATProto.NET!");
```

### Rich Text

```csharp
using ATProtoNet.Lexicon.App.Bsky.RichText;

var (text, facets) = new RichTextBuilder()
    .Text("Check out ")
    .Link("ATProto.NET", "https://github.com/example/ATProto.NET")
    .Text(" — built with ")
    .Tag("atproto")
    .Text("!")
    .Build();

await client.PostAsync(text, facets: facets);
```

### Profiles & Feeds

```csharp
var profile = await client.Bsky.Actor.GetProfileAsync("alice.bsky.social");
Console.WriteLine($"{profile.DisplayName} — {profile.Description}");

var timeline = await client.Bsky.Feed.GetTimelineAsync(limit: 25);
foreach (var item in timeline.Feed!)
    Console.WriteLine($"@{item.Post!.Author!.Handle}: {item.Post.Record}");
```

### Social Actions

```csharp
await client.FollowAsync("did:plc:abc123");
await client.LikeAsync("at://did:plc:abc/app.bsky.feed.post/3k2la", "bafyreib...");
await client.RepostAsync("at://did:plc:abc/app.bsky.feed.post/3k2la", "bafyreib...");
```

## ASP.NET Core Integration

### Register Services

```csharp
// Program.cs
builder.Services.AddAtProto(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});

// Or scoped (per-request) clients
builder.Services.AddAtProtoScoped(options =>
{
    options.InstanceUrl = "https://your-pds.example.com";
});
```

### AT Protocol Authentication

```csharp
builder.Services.AddAuthentication()
    .AddScheme<AtProtoAuthenticationOptions, AtProtoAuthenticationHandler>(
        "AtProto", options =>
        {
            options.PdsUrl = "https://your-pds.example.com";
        });
```

### Use in Controllers

```csharp
[ApiController]
[Route("api/todos")]
public class TodoController : ControllerBase
{
    private readonly AtProtoClient _client;

    public TodoController(AtProtoClient client) => _client = client;

    [HttpGet]
    public async Task<IActionResult> ListTodos()
    {
        var todos = _client.GetCollection<TodoItem>("com.example.todo.item");
        var page = await todos.ListAsync(limit: 50);
        return Ok(page.Records.Select(r => r.Value));
    }

    [HttpPost]
    public async Task<IActionResult> CreateTodo([FromBody] TodoItem item)
    {
        var todos = _client.GetCollection<TodoItem>("com.example.todo.item");
        var created = await todos.CreateAsync(item);
        return Created(created.Uri, item);
    }
}
```

## Blazor Integration

### Register Services

```csharp
builder.Services.AddAtProtoBlazor();
```

### Components

```razor
@using ATProtoNet.Blazor.Components

<AtProtoLoginForm OnLoginSuccess="HandleLogin" />
<AtProtoProfileCard Actor="@did" />
<AtProtoFeedView />
<AtProtoComposePost OnPostCreated="HandlePost" />
<AtProtoPostCard Post="@post" />
```

## Architecture

```
ATProto.NET/
├── src/
│   ├── ATProtoNet/                    # Core SDK
│   │   ├── Identity/                  # Did, Handle, AtUri, Nsid, Tid, etc.
│   │   ├── Auth/                      # Session, ISessionStore
│   │   ├── Http/                      # XrpcClient, AtProtoHttpException
│   │   ├── Models/                    # BlobRef, StrongRef, Label, etc.
│   │   ├── Serialization/             # JSON converters, defaults
│   │   ├── Streaming/                 # FirehoseClient
│   │   ├── RecordCollection.cs        # Typed collection CRUD for custom records
│   │   ├── AtProtoClient.cs           # Main client facade
│   │   └── Lexicon/
│   │       ├── Com/AtProto/           # Protocol-level APIs
│   │       │   ├── Server/            # Authentication, session management
│   │       │   ├── Repo/              # Record CRUD, blob upload
│   │       │   ├── Identity/          # Handle/DID resolution
│   │       │   ├── Sync/              # Repo sync, blob download
│   │       │   ├── Admin/             # Admin operations
│   │       │   ├── Label/             # Content labels
│   │       │   └── Moderation/        # Moderation reports
│   │       └── App/Bsky/              # Bluesky app APIs
│   │           ├── Actor/             # Profiles, preferences
│   │           ├── Feed/              # Posts, timeline, feeds
│   │           ├── Graph/             # Follows, blocks, mutes, lists
│   │           ├── Notification/      # Notifications
│   │           ├── RichText/          # Rich text builder, facets
│   │           └── Embed/             # Images, links, quotes, video
│   ├── ATProtoNet.Server/            # ASP.NET Core integration
│   │   ├── Extensions/               # DI registration
│   │   └── Authentication/           # JWT auth handler
│   └── ATProtoNet.Blazor/            # Blazor components
│       ├── Components/                # Razor components
│       ├── Services/                  # Auth state provider
│       └── Extensions/               # DI registration
└── tests/
    ├── ATProtoNet.Tests/              # Unit tests (218 tests)
    └── ATProtoNet.IntegrationTests/   # Integration tests (requires PDS)
```

## API Reference

### Client Namespaces

| Property | Namespace | Description |
|----------|-----------|-------------|
| `client.GetCollection<T>()` | — | **Typed CRUD for custom records** |
| `client.QueryAsync<T>()` | — | **Custom XRPC queries** |
| `client.ProcedureAsync<T>()` | — | **Custom XRPC procedures** |
| `client.Server` | `com.atproto.server.*` | Authentication, sessions, app passwords |
| `client.Repo` | `com.atproto.repo.*` | Low-level record CRUD, blob upload, batch writes |
| `client.Identity` | `com.atproto.identity.*` | Handle/DID resolution |
| `client.Sync` | `com.atproto.sync.*` | Repo sync, blob download |
| `client.Admin` | `com.atproto.admin.*` | Admin operations |
| `client.Label` | `com.atproto.label.*` | Content label queries |
| `client.Moderation` | `com.atproto.moderation.*` | Moderation reports |
| `client.Bsky.Actor` | `app.bsky.actor.*` | Profile read/write, search |
| `client.Bsky.Feed` | `app.bsky.feed.*` | Posts, timeline, feeds, likes |
| `client.Bsky.Graph` | `app.bsky.graph.*` | Follows, blocks, mutes, lists |
| `client.Bsky.Notification` | `app.bsky.notification.*` | Notification management |

### Identity Types

```csharp
var did = Did.Parse("did:plc:z72i7hdynmk6r22z27h6tvur");
var handle = Handle.Parse("alice.example.com");
var uri = AtUri.Parse("at://did:plc:abc/com.example.todo.item/3k2la");
var nsid = Nsid.Parse("com.example.todo.item");
var tid = Tid.Next();
var rkey = RecordKey.Parse("self");
var id = AtIdentifier.Parse("did:plc:abc"); // or "alice.example.com"
```

### Custom Session Persistence

```csharp
public class FileSessionStore : ISessionStore
{
    private readonly string _path;
    public FileSessionStore(string path) => _path = path;

    public async Task SaveAsync(Session session, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(session);
        await File.WriteAllTextAsync(_path, json, ct);
    }

    public async Task<Session?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return null;
        var json = await File.ReadAllTextAsync(_path, ct);
        return JsonSerializer.Deserialize<Session>(json);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }
}

var client = new AtProtoClientBuilder()
    .WithInstanceUrl("https://your-pds.example.com")
    .WithSessionStore(new FileSessionStore("session.json"))
    .Build();
```

### Firehose (Real-time Streaming)

```csharp
using ATProtoNet.Streaming;

var firehose = new FirehoseClient("wss://bsky.network");

await foreach (var message in firehose.SubscribeAsync())
{
    Console.WriteLine($"Seq: {message.Seq}, Repo: {message.Repo}");
}
```

## Running Tests

### Unit Tests

```bash
dotnet test tests/ATProtoNet.Tests
```

### Integration Tests

Integration tests require a running PDS. Set environment variables and run:

```bash
export ATPROTO_PDS_URL=http://localhost:2583
export ATPROTO_TEST_HANDLE=test.handle
export ATPROTO_TEST_PASSWORD=your-password

dotnet test tests/ATProtoNet.IntegrationTests
```

#### Local PDS with Podman/Docker

```bash
podman run -d \
  --name pds \
  -p 2583:3000 \
  -e PDS_HOSTNAME=localhost \
  -e PDS_JWT_SECRET=$(openssl rand -hex 16) \
  -e PDS_ADMIN_PASSWORD=admin \
  -e PDS_PLC_ROTATION_KEY_K256_PRIVATE_KEY_HEX=$(openssl rand -hex 32) \
  -e PDS_DATA_DIRECTORY=/pds \
  -e PDS_BLOBSTORE_DISK_LOCATION=/pds/blocks \
  -e PDS_DID_PLC_URL=https://plc.directory \
  ghcr.io/bluesky-social/pds:latest
```

## Requirements

- .NET 10.0 SDK or later
- For ASP.NET Core: `Microsoft.AspNetCore.App` framework reference
- For Blazor: `Microsoft.AspNetCore.Components.Web` 10.0+

## License

MIT
