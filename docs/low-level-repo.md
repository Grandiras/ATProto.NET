# Low-Level Repo API

For advanced scenarios, you can use the `RepoClient` directly instead of the higher-level `RecordCollection<T>`.

## Direct Record Operations

### Create Record

```csharp
var response = await client.Repo.CreateRecordAsync(
    repo: client.Did!,
    collection: "com.example.myapp.record",
    record: new { 
        foo = "bar", 
        count = 42,
    },
    rkey: null,         // Server generates TID
    validate: true,      // Validate against Lexicon schema
    swapCommit: null     // Optional CAS
);

Console.WriteLine($"URI: {response.Uri}");
Console.WriteLine($"CID: {response.Cid}");
```

### Get Record (Untyped)

```csharp
var response = await client.Repo.GetRecordAsync(
    repo: "did:plc:abc123",
    collection: "com.example.myapp.record",
    rkey: "3k2la7rxjgs2t");

// response.Value is a JsonElement
Console.WriteLine(response.Value.GetProperty("foo").GetString());
```

### Get Record (Typed)

```csharp
var response = await client.Repo.GetRecordAsync<TodoItem>(
    repo: "did:plc:abc123",
    collection: "com.example.todo.item",
    rkey: "3k2la7rxjgs2t");

Console.WriteLine(response.Value.Title);
```

### Put Record

```csharp
var response = await client.Repo.PutRecordAsync(
    repo: client.Did!,
    collection: "com.example.myapp.record",
    rkey: "my-key",
    record: new TodoItem { Title = "Updated" },
    validate: true,
    swapRecord: existingCid,  // CAS: fail if record changed
    swapCommit: null);
```

### Delete Record

```csharp
var response = await client.Repo.DeleteRecordAsync(
    repo: client.Did!,
    collection: "com.example.myapp.record",
    rkey: "3k2la7rxjgs2t",
    swapRecord: null,
    swapCommit: null);
```

### List Records

```csharp
var response = await client.Repo.ListRecordsAsync(
    repo: "did:plc:abc123",
    collection: "com.example.myapp.record",
    limit: 100,
    cursor: null,
    reverse: false);

foreach (var entry in response.Records)
{
    Console.WriteLine($"{entry.Uri}: {entry.Value}");
}
```

### Enumerate All Records

```csharp
await foreach (var entry in client.Repo.ListAllRecordsAsync(
    client.Did!, "com.example.myapp.record"))
{
    Console.WriteLine(entry.Uri);
}
```

## Repository Info

```csharp
var info = await client.Repo.DescribeRepoAsync("did:plc:abc123");

Console.WriteLine($"Handle: {info.Handle}");
Console.WriteLine($"DID: {info.Did}");
Console.WriteLine($"Collections: {string.Join(", ", info.Collections ?? [])}");
```

## Blob Operations

### Upload

```csharp
// From file
var result = await client.Repo.UploadBlobAsync("/path/to/file.jpg", "image/jpeg");

// From stream
var result = await client.Repo.UploadBlobAsync(stream, "image/png");

// From bytes
var result = await client.Repo.UploadBlobAsync(bytes, "application/pdf");
```

### List Missing Blobs

```csharp
var missing = await client.Repo.ListMissingBlobsAsync(limit: 100);
```

## Batch Operations

```csharp
var response = await client.Repo.ApplyWritesAsync(
    client.Did!,
    new List<ApplyWriteOperation>
    {
        new ApplyWriteCreate
        {
            Collection = "com.example.todo.item",
            Value = new TodoItem { Title = "Task 1" },
        },
        new ApplyWriteUpdate
        {
            Collection = "com.example.todo.item",
            Rkey = "existing-key",
            Value = new TodoItem { Title = "Updated" },
        },
        new ApplyWriteDelete
        {
            Collection = "com.example.todo.item",
            Rkey = "old-key",
        },
    },
    validate: true,
    swapCommit: null);
```

## When to Use Low-Level API

Use `RepoClient` directly when you need:
- Untyped access to `JsonElement` record values
- `swapCommit` for repo-level CAS
- Direct control over validation
- Operations on behalf of other users (admin)
- Access to response metadata beyond what `RecordCollection<T>` exposes

For most custom app scenarios, prefer `RecordCollection<T>` — see [Custom Records](custom-records.md).
