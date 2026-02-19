# Custom XRPC Endpoints

Beyond record CRUD, the AT Protocol supports custom **query** (GET) and **procedure** (POST) methods defined by Lexicon schemas. ATProto.NET provides direct access to call these on any PDS or app service.

## Queries (HTTP GET)

Use `QueryAsync<T>` for Lexicon query methods:

```csharp
// Define your response type
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
    "com.example.todo.search",
    new { q = "groceries", limit = 10 });

foreach (var item in result.Items)
    Console.WriteLine($"{item.Title} (score: {item.Score})");
```

### Query Parameters

Parameters can be passed as:

**Anonymous objects** (most convenient):
```csharp
var result = await client.QueryAsync<MyResult>(
    "com.example.mymethod",
    new { limit = 25, cursor = "abc", includeArchived = true });
```

**Dictionaries**:
```csharp
var result = await client.QueryAsync<MyResult>(
    "com.example.mymethod",
    new Dictionary<string, string?> { ["limit"] = "25", ["cursor"] = "abc" });
```

**No parameters**:
```csharp
var result = await client.QueryAsync<MyResult>("com.example.mymethod");
```

## Procedures (HTTP POST)

Use `ProcedureAsync<T>` for Lexicon procedure methods that return a response:

```csharp
public class BatchResult
{
    [JsonPropertyName("processed")]
    public int Processed { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = [];
}

var result = await client.ProcedureAsync<BatchResult>(
    "com.example.todo.markAllComplete",
    new { before = "2024-01-01", category = "shopping" });

Console.WriteLine($"Processed {result.Processed} items");
```

### Fire-and-Forget Procedures

For procedures with no return value:

```csharp
await client.ProcedureAsync(
    "com.example.todo.cleanup",
    new { daysOld = 30 });

// No body needed
await client.ProcedureAsync("com.example.todo.resetAll");
```

## Combining with RecordCollection

A typical custom AT Protocol app uses both records and custom methods:

```csharp
// Record CRUD via collections
var todos = client.GetCollection<TodoItem>("com.example.todo.item");
var projects = client.GetCollection<Project>("com.example.todo.project");

// Custom methods for app-specific logic
var searchResults = await client.QueryAsync<SearchResult>(
    "com.example.todo.search", new { q = "urgent" });

var stats = await client.QueryAsync<TodoStats>(
    "com.example.todo.getStats");

await client.ProcedureAsync(
    "com.example.todo.archiveCompleted");
```

## Low-Level XRPC Client

For advanced scenarios, you can access the underlying `RepoClient` or `ServerClient` directly:

```csharp
// Direct repo operations
var response = await client.Repo.CreateRecordAsync(
    repo: client.Did!,
    collection: "com.example.myapp.record",
    record: new { foo = "bar", count = 42 });

// Direct server operations
var session = await client.Server.GetSessionAsync();
```

## Error Handling

```csharp
try
{
    var result = await client.QueryAsync<MyResult>(
        "com.example.mymethod", new { limit = 10 });
}
catch (AtProtoHttpException ex)
{
    // XRPC error response
    Console.WriteLine($"Error: {ex.ErrorType}");
    Console.WriteLine($"Message: {ex.ErrorMessage}");
    Console.WriteLine($"Status: {ex.StatusCode}");
}
```

## Next Steps

- [Custom Lexicon Records](custom-records.md) — Define and use typed record collections
- [Batch Operations](batch-operations.md) — Atomic multi-record writes
- [Error Handling](error-handling.md) — Complete error handling guide
