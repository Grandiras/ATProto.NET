# Custom Lexicon Records

The AT Protocol allows any application to define its own data schemas using [Lexicons](https://atproto.com/guides/lexicon). ATProto.NET provides a typed `RecordCollection<T>` API that makes working with custom records as easy as using a database collection.

## Key Concept

In AT Protocol:
- **One account** can be used across **many applications**
- Each app defines its own **Lexicon** (schema namespace, e.g., `com.example.todo`)
- Records are stored in the user's **PDS** (Personal Data Server) in named **collections**
- Each collection has an **NSID** (e.g., `com.example.todo.item`)

## Defining Record Types

### Using AtProtoRecord Base Class

The `AtProtoRecord` base class automatically handles the `$type` and `createdAt` fields:

```csharp
using System.Text.Json.Serialization;
using ATProtoNet;

public class TodoItem : AtProtoRecord
{
    // The Lexicon NSID for this record type
    public override string Type => "com.example.todo.item";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}
```

This serializes to:
```json
{
  "$type": "com.example.todo.item",
  "createdAt": "2024-01-15T10:30:00.000Z",
  "title": "Buy groceries",
  "completed": false,
  "priority": 2,
  "tags": ["shopping", "errands"]
}
```

### Using Plain C# Classes

You don't have to extend `AtProtoRecord`. Any serializable class works:

```csharp
public class Bookmark
{
    [JsonPropertyName("$type")]
    public string Type => "com.example.bookmarks.bookmark";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}
```

## Getting a Collection

```csharp
var todos = client.GetCollection<TodoItem>("com.example.todo.item");
```

The collection NSID should match your Lexicon definition. By convention, it follows reverse-domain notation: `com.yourcompany.appname.recordtype`.

## CRUD Operations

### Create

```csharp
var created = await todos.CreateAsync(new TodoItem
{
    Title = "Buy groceries",
    Priority = 2,
    Tags = ["shopping"],
});

Console.WriteLine($"URI: {created.Uri}");
Console.WriteLine($"CID: {created.Cid}");
Console.WriteLine($"Record Key: {created.RecordKey}");
```

The server generates a TID-based record key. You can also specify one:

```csharp
var created = await todos.CreateAsync(
    new TodoItem { Title = "Custom key" },
    rkey: "my-custom-key");
```

### Read

```csharp
var item = await todos.GetAsync(created.RecordKey);

Console.WriteLine($"Title: {item.Value.Title}");
Console.WriteLine($"URI: {item.Uri}");
Console.WriteLine($"CID: {item.Cid}");
Console.WriteLine($"Key: {item.RecordKey}");
```

### Update (Put)

`PutAsync` is an upsert — it creates the record if it doesn't exist, or replaces it if it does:

```csharp
await todos.PutAsync(created.RecordKey, new TodoItem
{
    Title = "Buy groceries",
    Completed = true,
    Priority = 2,
});
```

For optimistic concurrency, pass the expected CID:

```csharp
await todos.PutAsync(
    created.RecordKey,
    updatedItem,
    swapRecord: item.Cid);  // Fails if record was modified since read
```

### Delete

```csharp
await todos.DeleteAsync(created.RecordKey);
```

### Check Existence

```csharp
bool exists = await todos.ExistsAsync("some-record-key");
```

## Listing Records

### Paginated Listing

```csharp
var page = await todos.ListAsync(limit: 25);

foreach (var record in page.Records)
{
    Console.WriteLine($"[{record.RecordKey}] {record.Value.Title}");
}

// Check for more pages
if (page.HasMore)
{
    var nextPage = await todos.ListAsync(limit: 25, cursor: page.Cursor);
}
```

### Enumerate All Records

For iterating over all records with automatic pagination:

```csharp
await foreach (var record in todos.EnumerateAsync())
{
    Console.WriteLine($"{record.RecordKey}: {record.Value.Title}");
}

// With custom page size
await foreach (var record in todos.EnumerateAsync(pageSize: 50))
{
    // Process each record
}
```

### Reverse Order

```csharp
var page = await todos.ListAsync(limit: 25, reverse: true);
```

## Reading Other Users' Data

One of the key features of AT Protocol is that records are public by default. You can read records from any user's repository:

```csharp
// Read a specific record from another user
var item = await todos.GetFromAsync("did:plc:otherperson", "record-key");

// List records from another user
var page = await todos.ListFromAsync("did:plc:otherperson", limit: 50);

// Enumerate all of their records
await foreach (var record in todos.EnumerateFromAsync("did:plc:otherperson"))
{
    Console.WriteLine(record.Value.Title);
}
```

## Multiple Collections (Multi-App)

A single AT Protocol account supports data from many applications by using different collection NSIDs:

```csharp
await client.LoginAsync("alice.example.com", "app-password");

// Different apps, same account, different collections
var todos = client.GetCollection<TodoItem>("com.example.todo.item");
var bookmarks = client.GetCollection<Bookmark>("com.example.bookmarks.bookmark");
var notes = client.GetCollection<Note>("com.example.notes.note");
var recipes = client.GetCollection<Recipe>("com.example.recipes.recipe");

// Each collection is independent
await todos.CreateAsync(new TodoItem { Title = "Cook dinner" });
await recipes.CreateAsync(new Recipe { Name = "Pasta Carbonara" });
```

## Record Type Design Guidelines

### NSID Naming Convention

Follow reverse-domain notation for your Lexicon NSIDs:

```
com.yourcompany.appname.recordtype
```

Examples:
- `com.example.todo.item`
- `com.example.todo.project`
- `com.example.bookmarks.bookmark`
- `com.example.bookmarks.folder`
- `org.myorg.inventory.product`

### Field Naming

Use camelCase for JSON fields (AT Protocol convention):

```csharp
public class MyRecord : AtProtoRecord
{
    public override string Type => "com.example.myapp.record";

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = "";

    [JsonPropertyName("lastModified")]
    public string? LastModified { get; set; }

    [JsonPropertyName("itemCount")]
    public int ItemCount { get; set; }
}
```

### Timestamps

Use ISO 8601 format for date/time fields:

```csharp
[JsonPropertyName("dueDate")]
public string? DueDate { get; set; }

// Set like this:
record.DueDate = DateTime.UtcNow.ToString("o");
record.DueDate = "2024-06-15T14:30:00.000Z";
```

### References Between Records

Use AT URIs to reference other records:

```csharp
public class Comment : AtProtoRecord
{
    public override string Type => "com.example.todo.comment";

    [JsonPropertyName("todoUri")]
    public string TodoUri { get; set; } = "";  // AT URI to the todo item

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}
```

### Blobs (Images, Files)

First upload, then reference:

```csharp
// Upload a blob
var blobResponse = await client.Repo.UploadBlobAsync(
    filePath: "/path/to/image.jpg",
    mimeType: "image/jpeg");

// Reference it in your record
public class PhotoRecord : AtProtoRecord
{
    public override string Type => "com.example.photos.photo";

    [JsonPropertyName("image")]
    public BlobRef? Image { get; set; }

    [JsonPropertyName("caption")]
    public string Caption { get; set; } = "";
}
```

## Error Handling

```csharp
using ATProtoNet.Http;

try
{
    var item = await todos.GetAsync("nonexistent-key");
}
catch (AtProtoHttpException ex) when (ex.ErrorType == "RecordNotFound")
{
    Console.WriteLine("Record does not exist");
}
catch (AtProtoHttpException ex)
{
    Console.WriteLine($"XRPC Error: {ex.ErrorType} — {ex.ErrorMessage}");
    Console.WriteLine($"Status: {ex.StatusCode}");
}
```

## Next Steps

- [Custom XRPC Endpoints](custom-xrpc.md) — Define and call custom Lexicon methods
- [Batch Operations](batch-operations.md) — Atomic multi-record writes
- [Blob Upload](blob-upload.md) — Upload images and files
