# Custom XRPC Endpoints

Beyond record CRUD, the AT Protocol supports custom **query** (GET) and **procedure** (POST) methods defined by Lexicon schemas. ATProto.NET provides direct access to call these on any PDS or app service, and a transport that packages for other Lexicons build their own sub-clients on.

## Queries (HTTP GET)

Use `QueryAsync<TOut>` for Lexicon query methods. The method name is an `Nsid` (from
`ATProtoNet.Identity`; parse a literal with `Nsid.Parse`, or keep it in a `static readonly` field),
and the parameters an `XrpcParams` (from `ATProtoNet.Http`):

```csharp
// Define your output type
public class SearchResult
{
    [JsonPropertyName("items")]
    public List<SearchItem> Items { get; set; } = [];

    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

public class SearchItem
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("score")]
    public double Score { get; set; }
}

// Call it
var result = await client.QueryAsync<SearchResult>(
    Nsid.Parse("com.example.todo.search"),
    new XrpcParams { { "q", "groceries" }, { "limit", 10 } });

foreach (var item in result.Items)
    Console.WriteLine($"{item.Title} (score: {item.Score})");
```

### Query Parameters

`XrpcParams` collects the parameters in order. Build it with a collection initializer or fluently;
a `null` value is left out:

```csharp
var parameters = new XrpcParams
{
    { "limit", 25 },
    { "cursor", cursor },          // skipped while cursor is null
    { "includeArchived", true },
};

var fluent = new XrpcParams()
    .Add("actor", did)                     // identifier types pass as their string value
    .AddAll("tags", ["work", "home"]);     // an array parameter: tags=work&tags=home

var result = await client.QueryAsync<MyResult>(Nsid.Parse("com.example.mymethod"), parameters);

// No parameters
var stats = await client.QueryAsync<MyResult>(Nsid.Parse("com.example.getStats"));
```

Values are formatted for the wire: booleans as `true`/`false`, numbers in the invariant culture, `DateTimeOffset` as ISO 8601 UTC (`2026-09-24T12:00:00.000Z`), enums by their JSON names, identifier types (`Did`, `AtUri`, `Nsid`, …) and an `AtDatetime` as their text, and `AddAll` as a repeated key.

An anonymous object or a dictionary also works, as a convenience overload: each property is one
parameter, a sequence is a repeated key, and `DateTime` values go out as ISO 8601 UTC. It reads the
object's properties by reflection, so it is marked `[RequiresUnreferencedCode]`; prefer
`XrpcParams` in trimmed or AOT-compiled apps.

```csharp
var result = await client.QueryAsync<MyResult>(
    Nsid.Parse("com.example.mymethod"),
    new { limit = 25, cursor = "abc", includeArchived = true });
```

### Per-call options

Every custom call takes an optional `XrpcCallOptions`, which applies to that one call and overrides the client's defaults for it — safe on a client shared between concurrent callers:

```csharp
var labels = await client.QueryAsync<QueryLabelsResult>(
    Nsid.Parse("com.atproto.label.queryLabels"),
    new XrpcParams().AddAll("uriPatterns", ["at://did:plc:alice/*"]),
    new XrpcCallOptions
    {
        Proxy = "did:plc:labeler#atproto_labeler",        // atproto-proxy
        AcceptLabelers = ["did:plc:labeler;redact"],      // atproto-accept-labelers
        Headers = new Dictionary<string, string> { ["X-Trace-Id"] = traceId },
        Timeout = TimeSpan.FromSeconds(5),                // throws TimeoutException on expiry
    });
```

## Procedures (HTTP POST)

Use `ProcedureAsync<TIn, TOut>` for Lexicon procedure methods that take an input and return an output:

```csharp
public class MarkCompleteInput
{
    [JsonPropertyName("before")]
    public required AtDatetime Before { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }
}

public class BatchResult
{
    [JsonPropertyName("processed")]
    public int Processed { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = [];
}

var result = await client.ProcedureAsync<MarkCompleteInput, BatchResult>(
    Nsid.Parse("com.example.todo.markAllComplete"),
    new MarkCompleteInput { Before = AtDatetime.Parse("2024-01-01T00:00:00Z"), Category = "shopping" });

Console.WriteLine($"Processed {result.Processed} items");
```

The input is serialized as JSON by its runtime type; a `null` input sends no body. Query
parameters, when the procedure takes any, go in the `parameters` argument.

### Procedures without output

For procedures whose output you do not need, and for those without input:

```csharp
// Input, no output
await client.ProcedureAsync(
    Nsid.Parse("com.example.todo.cleanup"),
    new CleanupInput { DaysOld = 30 });

// Neither input nor output
await client.ProcedureAsync(Nsid.Parse("com.example.todo.resetAll"));

// No input, but query parameters
await client.ProcedureAsync(Nsid.Parse("com.example.todo.resetAll"), new XrpcParams().Add("dryRun", true));
```

## Building a sub-client for another Lexicon

The SDK ships sub-clients for `com.atproto.*`, `app.bsky.*`, `chat.bsky.*`, `tools.ozone.*` and
`site.standard.*`. A package for any other namespace builds its own on `client.Transport`, an
`IXrpcTransport`: the same pipeline the SDK's sub-clients use. Calls through it go to the client's
service with its installed session — refreshed when it expires — and follow a sign-in, a refresh,
a move to another PDS or a sign-out without any wiring. They get the same rate-limit retries, the
same `XrpcCallOptions`, and the same exceptions.

Here is a complete sub-client for a hypothetical `com.example.todo` Lexicon, as a NuGet package
would ship it:

```csharp
using System.Text.Json.Serialization;
using ATProtoNet;
using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace Example.Todo;

public sealed class ListItemsOutput
{
    [JsonPropertyName("items")]
    public IReadOnlyList<TodoView> Items { get; init; } = [];

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

public sealed class TodoView
{
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }
}

internal sealed class SetDoneInput
{
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    [JsonPropertyName("done")]
    public required bool Done { get; init; }
}

/// <summary>com.example.todo.* — the to-do app's queries and procedures.</summary>
public sealed class TodoClient(IXrpcTransport transport)
{
    private static readonly Nsid ListItems = Nsid.Parse("com.example.todo.listItems");
    private static readonly Nsid SetDone = Nsid.Parse("com.example.todo.setDone");
    private static readonly Nsid ExportAll = Nsid.Parse("com.example.todo.export");

    public Task<ListItemsOutput> ListItemsAsync(
        AtIdentifier actor, int? limit = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        transport.QueryAsync<ListItemsOutput>(
            ListItems,
            new XrpcParams().Add("actor", actor).Add("limit", limit).Add("cursor", cursor),
            cancellationToken: cancellationToken);

    public Task SetDoneAsync(AtUri item, bool done, CancellationToken cancellationToken = default) =>
        transport.ProcedureAsync(SetDone, new SetDoneInput { Uri = item, Done = done }, cancellationToken: cancellationToken);

    // A binary output: the caller disposes the stream.
    public Task<XrpcStreamResponse> ExportAsync(CancellationToken cancellationToken = default) =>
        transport.DownloadAsync(ExportAll, cancellationToken: cancellationToken);
}

/// <summary>Makes the sub-client read like one of the SDK's own: <c>client.Todo().ListItemsAsync(…)</c>.</summary>
public static class TodoClientExtensions
{
    public static TodoClient Todo(this AtProtoClient client) => new(client.Transport);
}
```

An app then uses it next to the built-in sub-clients:

```csharp
await using var client = new AtProtoClient(new AtProtoClientOptions { InstanceUrl = "https://pds.example.com" });
await client.LoginAsync("alice.example.com", "app-password");

var todo = client.Todo();
var page = await todo.ListItemsAsync(client.Did!, limit: 50);
await todo.SetDoneAsync(page.Items[0].Uri, done: true);
```

`IXrpcTransport` also has `UploadAsync<TOut>` for procedures whose input is binary. The sub-client
is as cheap as the transport it wraps, so create it where it is needed or keep one per client.
Because the transport is an interface, the sub-client's own tests can substitute it:

```csharp
var transport = Substitute.For<IXrpcTransport>();
transport.QueryAsync<ListItemsOutput>(Arg.Any<Nsid>(), Arg.Any<XrpcParams?>(), Arg.Any<XrpcCallOptions?>(), Arg.Any<CancellationToken>())
    .Returns(new ListItemsOutput());

var page = await new TodoClient(transport).ListItemsAsync(AtIdentifier.Parse("alice.example.com"));
```

Models a package defines for its Lexicon follow the SDK's conventions (`[JsonPropertyName]` on every
property, typed identifiers, `IReadOnlyList<T>`); deriving them from `LexObject` keeps fields a newer
revision of the Lexicon adds. See [Custom Lexicon Records](custom-records.md) for record types and
[Lexicon Code Generation](lexicon-codegen.md) for generating them from Lexicon JSON.

## Combining with RecordCollection

A typical custom AT Protocol app uses both records and custom methods:

```csharp
// Record CRUD via collections (TodoItem and Project implement IAtProtoRecord)
var todos = client.GetCollection<TodoItem>();
var projects = client.GetCollection<Project>();

// Custom methods for app-specific logic
var searchResults = await client.QueryAsync<SearchResult>(
    Nsid.Parse("com.example.todo.search"), new XrpcParams().Add("q", "urgent"));

var stats = await client.QueryAsync<TodoStats>(
    Nsid.Parse("com.example.todo.getStats"));

await client.ProcedureAsync(
    Nsid.Parse("com.example.todo.archiveCompleted"));
```

## Low-Level Repository Calls

For advanced scenarios, you can use the `RepoClient` or `ServerClient` directly:

```csharp
// Direct repo operations: returns a RecordRef (URI, CID, commit)
var written = await client.Repo.CreateRecordAsync(
    repo: client.Did!,
    collection: Nsid.Parse("com.example.myapp.record"),
    record: new { foo = "bar", count = 42 });

// Read it back, typed or as raw JSON: a RecordView<T>
var raw = await client.Repo.GetRecordAsync(written.Uri);
Console.WriteLine(raw.Value.GetProperty("count").GetInt32());

// Direct server operations
var session = await client.Server.GetSessionAsync();
```

## Error Handling

```csharp
try
{
    var result = await client.QueryAsync<MyResult>(
        Nsid.Parse("com.example.mymethod"), new XrpcParams().Add("limit", 10));
}
catch (XrpcException ex) when (ex.Is("TodoListFull"))
{
    // An error name your Lexicon declares
}
catch (XrpcException ex)
{
    // Any other XRPC error response
    Console.WriteLine($"Error: {ex.Error}");
    Console.WriteLine($"Message: {ex.ErrorMessage}");
    Console.WriteLine($"Status: {ex.StatusCode}");
}
catch (XrpcResponseFormatException ex)
{
    // The service answered 2xx with a body that is not a MyResult
    Console.WriteLine($"{ex.Nsid}: {ex.Message}");
}
```

## Next Steps

- [Custom Lexicon Records](custom-records.md) — Define and use typed record collections
- [Batch Operations](batch-operations.md) — Atomic multi-record writes
- [Error Handling](error-handling.md) — Complete error handling guide
