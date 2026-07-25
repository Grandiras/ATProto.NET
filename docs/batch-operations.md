# Batch Operations

ATProto.NET supports atomic batch operations using `ApplyWrites`, which lets you create, update, and delete multiple records in a single commit.

## ApplyWrites

`ApplyWrites` executes multiple write operations atomically — either all succeed or all fail.

```csharp
using ATProtoNet.Lexicon.Com.AtProto.Repo;

await client.Repo.ApplyWritesAsync(
    client.Did!,
    new List<ApplyWriteOperation>
    {
        new ApplyWriteCreate
        {
            Collection = "com.example.todo.item",
            Value = new TodoItem { Title = "Task 1" },
        },
        new ApplyWriteCreate
        {
            Collection = "com.example.todo.item",
            Value = new TodoItem { Title = "Task 2" },
        },
        new ApplyWriteDelete
        {
            Collection = "com.example.todo.item",
            Rkey = "old-task-key",
        },
    });
```

## Operation Types

### Create

```csharp
new ApplyWriteCreate
{
    Collection = "com.example.todo.item",
    Rkey = "optional-custom-key",  // Optional: server generates TID if omitted
    Value = new TodoItem { Title = "New task" },
}
```

### Update

```csharp
new ApplyWriteUpdate
{
    Collection = "com.example.todo.item",
    Rkey = "existing-key",
    Value = new TodoItem { Title = "Updated task", Completed = true },
}
```

### Delete

```csharp
new ApplyWriteDelete
{
    Collection = "com.example.todo.item",
    Rkey = "key-to-delete",
}
```

## Atomic Operations Across Collections

You can mix operations across different collections in the same atomic commit:

```csharp
await client.Repo.ApplyWritesAsync(client.Did!, new List<ApplyWriteOperation>
{
    // Create a project
    new ApplyWriteCreate
    {
        Collection = "com.example.todo.project",
        Rkey = "project-1",
        Value = new Project { Name = "My Project" },
    },
    // Create related tasks
    new ApplyWriteCreate
    {
        Collection = "com.example.todo.item",
        Value = new TodoItem { Title = "Task in project", ProjectId = "project-1" },
    },
    new ApplyWriteCreate
    {
        Collection = "com.example.todo.item",
        Value = new TodoItem { Title = "Another task", ProjectId = "project-1" },
    },
});
```

## Optimistic Concurrency

Use `swapCommit` for compare-and-swap on the repository state:

`swapCommit` takes the commit CID the write must apply on top of. The client tracks the revision the
server last reported on `client.LatestRepoRev`, and every write response carries a `Commit` with the
new `Cid` and `Rev`:

```csharp
var created = await client.Repo.CreateRecordAsync(
    client.Did!, "com.example.todo.item", new TodoItem { Title = "First" });

await client.Repo.ApplyWritesAsync(
    client.Did!,
    operations,
    swapCommit: created.Commit?.Cid  // Fails if the repo moved on since that commit
);
```

## Error Handling

```csharp
try
{
    await client.Repo.ApplyWritesAsync(client.Did!, operations);
}
catch (AtProtoHttpException ex) when (ex.ErrorType == "InvalidSwap")
{
    // Repository was modified concurrently
}
catch (AtProtoHttpException ex)
{
    Console.WriteLine($"Batch failed: {ex.ErrorType} — {ex.ErrorMessage}");
}
```
