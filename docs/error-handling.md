# Error Handling

ATProto.NET uses typed exceptions for XRPC errors and standard .NET patterns for error handling.

## AtProtoHttpException

All XRPC API errors throw `AtProtoHttpException`:

```csharp
using ATProtoNet.Http;

try
{
    var item = await todos.GetAsync("nonexistent-key");
}
catch (AtProtoHttpException ex)
{
    Console.WriteLine($"Error Type: {ex.ErrorType}");      // e.g., "RecordNotFound"
    Console.WriteLine($"Message: {ex.ErrorMessage}");      // Human-readable message
    Console.WriteLine($"Status Code: {ex.StatusCode}");    // HttpStatusCode
    Console.WriteLine($"Response: {ex.ResponseBody}");     // Raw response body
}
```

## Common Error Types

| Error Type | Status Code | Description |
|------------|-------------|-------------|
| `InvalidRequest` | 400 | Malformed request or invalid parameters |
| `RecordNotFound` | 400 | The requested record doesn't exist |
| `AuthRequired` | 401 | Authentication needed |
| `ExpiredToken` | 400 | Access token has expired |
| `InvalidToken` | 400 | Token is invalid or malformed |
| `AccountNotFound` | 400 | The specified account doesn't exist |
| `RepoNotFound` | 400 | The specified repository doesn't exist |
| `InvalidSwap` | 400 | CAS (compare-and-swap) condition failed |
| `MethodNotImplemented` | 501 | The XRPC method isn't supported by this server |

## Pattern Matching

Use pattern matching to handle specific error types:

```csharp
try
{
    await todos.CreateAsync(item);
}
catch (AtProtoHttpException ex) when (ex.ErrorType == "RecordNotFound")
{
    // Handle missing record
}
catch (AtProtoHttpException ex) when (ex.ErrorType == "ExpiredToken")
{
    // Token expired — try refreshing
    await client.RefreshSessionAsync();
    await todos.CreateAsync(item);  // Retry
}
catch (AtProtoHttpException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
{
    // Rate limited
    await Task.Delay(TimeSpan.FromSeconds(30));
}
```

## Authentication Errors

```csharp
try
{
    await client.LoginAsync("user", "wrong-password");
}
catch (AtProtoHttpException ex) when (ex.ErrorType == "AuthenticationRequired")
{
    Console.WriteLine("Invalid credentials");
}
catch (AtProtoHttpException ex) when (ex.ErrorType == "AuthFactorTokenRequired")
{
    Console.WriteLine("2FA token required");
    // Prompt user for token, then retry with authFactorToken parameter
}
```

## Not-Authenticated State

Operations requiring authentication throw `InvalidOperationException` if the client isn't authenticated:

```csharp
try
{
    var todos = client.GetCollection<TodoItem>("com.example.todo.item");
    await todos.CreateAsync(new TodoItem { Title = "Test" });
}
catch (InvalidOperationException ex) when (ex.Message.Contains("Not authenticated"))
{
    Console.WriteLine("Call LoginAsync first");
}
```

## Existence Checking

Use `ExistsAsync` to avoid exceptions when checking for records:

```csharp
// Instead of try/catch:
bool exists = await todos.ExistsAsync("some-key");

if (exists)
{
    var item = await todos.GetAsync("some-key");
}
```

## Optimistic Concurrency (CAS)

Use `swapRecord` to implement compare-and-swap:

```csharp
var item = await todos.GetAsync("key");

try
{
    await todos.PutAsync("key", updatedItem, swapRecord: item.Cid);
}
catch (AtProtoHttpException ex) when (ex.ErrorType == "InvalidSwap")
{
    Console.WriteLine("Record was modified by someone else — refetch and retry");
}
```

## Retry Pattern

```csharp
async Task<T> WithRetryAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            return await operation();
        }
        catch (AtProtoHttpException ex) when (
            ex.StatusCode == HttpStatusCode.TooManyRequests ||
            ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            if (i == maxRetries - 1) throw;
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
        }
        catch (AtProtoHttpException ex) when (ex.ErrorType == "ExpiredToken")
        {
            await client.RefreshSessionAsync();
        }
    }
    throw new InvalidOperationException("Should not reach here");
}
```
