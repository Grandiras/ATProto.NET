# Error Handling

ATProto.NET raises one family of exceptions for everything that goes wrong at the protocol level, and leaves .NET's own exceptions for argument errors, misuse, cancellation and transport faults.

## The exception hierarchy

```
AtProtoException                        (ATProtoNet)          catch-all for SDK failures
├── XrpcException                       (ATProtoNet.Http)     a service answered with an XRPC error
│   ├── XrpcRateLimitException          (ATProtoNet.Http)     429 that could not be waited out
│   ├── XrpcAuthenticationException     (ATProtoNet.Http)     401, ExpiredToken, InvalidToken
│   └── SpaceVerificationException      (ATProtoNet.Server.Spaces)  server side: a space credential did not verify
├── XrpcResponseFormatException         (ATProtoNet.Http)     2xx whose body does not match the Lexicon
├── OAuthException                      (ATProtoNet.Auth.OAuth)
├── SpaceCredentialException            (ATProtoNet.Spaces)
├── SpaceTokenException                 (ATProtoNet.Spaces)
├── SpaceRepoVerificationException      (ATProtoNet.Spaces)
├── JetstreamConnectException           (ATProtoNet.Streaming)
└── JetstreamArchiveException           (ATProtoNet.Streaming)
```

`AtProtoException` lives in the root `ATProtoNet` namespace; each concrete exception lives next to the component that raises it. Every exception that carries a protocol error name exposes it as `Error`.

What the SDK does **not** wrap:

| Exception | Meaning |
|---|---|
| `ArgumentException` and subtypes | An invalid argument, such as a malformed identifier or an HTTP service URL where HTTPS is required |
| `InvalidOperationException` | Misuse, such as calling an authenticated method before `LoginAsync` |
| `OperationCanceledException` | Your `CancellationToken` was cancelled |
| `TimeoutException` | A per-call `XrpcCallOptions.Timeout` expired |
| `HttpRequestException` | No response arrived at all (DNS, connection refused, TLS). Any response, even a 500, becomes an `XrpcException` |

> Identity resolution reports every failure, network ones included, as `DidResolutionException`, with a `Kind` naming what went wrong — see [Identity Resolution](did-resolution.md#errors).

## XrpcException

Every non-success XRPC response throws `XrpcException` (or one of its subtypes):

```csharp
using ATProtoNet.Http;

try
{
    var item = await todos.GetAsync(RecordKey.Parse("nonexistent-key"));
}
catch (XrpcException ex)
{
    Console.WriteLine(ex.Nsid);          // "com.atproto.repo.getRecord"
    Console.WriteLine(ex.Error);         // "RecordNotFound"
    Console.WriteLine(ex.ErrorMessage);  // human-readable message, if the service sent one
    Console.WriteLine(ex.StatusCode);    // HttpStatusCode.BadRequest
    Console.WriteLine(ex.ResponseBody);  // the raw body
    Console.WriteLine(ex.Headers["RateLimit-Remaining"]);  // response headers
}
```

`Error` is never null. When a response carries no XRPC error envelope (a reverse proxy's HTML error page, say), it holds the generic name XRPC gives the status: `InvalidRequest` for 400, `InternalServerError` for 500, `UpstreamFailure` for 502, and so on.

`XrpcException` is **not** an `HttpRequestException`. Code that caught `HttpRequestException` to see XRPC errors must catch `XrpcException` (or `AtProtoException`) instead.

## Matching errors by name

Branch on the error name, not the status code — a Lexicon declares the names its method may answer with, and several share a status. `XrpcErrors` has constants for the common ones:

```csharp
try
{
    await todos.PutAsync(RecordKey.Parse("key"), updatedItem, swapRecord: item.Cid);
}
catch (XrpcException ex) when (ex.Is(XrpcErrors.InvalidSwap))
{
    Console.WriteLine("Record was modified by someone else — refetch and retry");
}
catch (XrpcException ex) when (ex.Is(XrpcErrors.RecordNotFound))
{
    // Handle missing record
}
```

| Constant | Status | Meaning |
|---|---|---|
| `InvalidRequest` | 400 | Malformed request or invalid parameters |
| `RecordNotFound` | 400 | `getRecord`: no record at that key |
| `InvalidSwap` | 400 | A compare-and-swap (`swapRecord` / `swapCommit`) failed |
| `ExpiredToken` | 400 | Access token expired; refresh the session |
| `InvalidToken` | 400 | Token malformed, revoked, or of the wrong kind |
| `AuthFactorTokenRequired` | 401 | `createSession`: a second factor is needed |
| `AccountTakedown` | 401 | The account has been taken down |
| `RepoNotFound`, `RepoTakendown`, `RepoSuspended`, `RepoDeactivated` | 400 | Sync methods: the repository is unavailable |
| `BlobNotFound` | 400 | `getBlob`: no such blob |
| `AuthenticationRequired`, `AuthMissing` | 401 | Credentials missing or rejected |
| `RateLimitExceeded` | 429 | A rate limit was exceeded |
| `MethodNotImplemented` | 501 | The service does not implement the method |

Permissioned-space methods have their own names in `SpaceErrors` and `SimpleSpaceErrors`. For your own Lexicons, match the literal names they declare: `ex.Is("TodoListFull")`.

## Authentication errors

`XrpcAuthenticationException` covers a rejected credential: any 401, plus the `ExpiredToken` and `InvalidToken` errors a PDS answers with a 400.

```csharp
try
{
    await client.LoginAsync("alice.example.com", "wrong-password");
}
catch (XrpcException ex) when (ex.Is(XrpcErrors.AuthFactorTokenRequired))
{
    // Prompt for the emailed token, then retry with authFactorToken
}
catch (XrpcAuthenticationException)
{
    Console.WriteLine("Invalid credentials");
}
```

A DPoP nonce challenge (`use_dpop_nonce`) never reaches your code: the client retries once with the nonce the server supplied.

## Rate limits

On a 429 the client waits as long as the service asks — `Retry-After` in seconds or as an HTTP date, otherwise until `RateLimit-Reset` — plus up to 10 % jitter, and retries. Two limits keep this from hanging a request:

```csharp
var client = new AtProtoClient(new AtProtoClientOptions
{
    RateLimit = new XrpcRateLimitOptions
    {
        MaxRetries = 3,                         // default 3; 0 disables retrying
        MaxDelay = TimeSpan.FromSeconds(30),    // default 30 s
    },
});
```

When the service asks for a longer wait than `MaxDelay` (a daily window on `createSession`, for example), or the retries run out, the call throws `XrpcRateLimitException` straight away:

```csharp
catch (XrpcRateLimitException ex)
{
    Console.WriteLine($"Try again in {ex.RetryAfter}");
    Console.WriteLine($"{ex.RateLimit?.Remaining}/{ex.RateLimit?.Limit}, resets {ex.RateLimit?.Reset}");
}
```

Set `MaxRetries = 0` when the `HttpClient` you pass in already has a retrying resilience handler, so the two do not multiply.

## Responses that do not match

A success status with a body that does not deserialize into the method's response type throws `XrpcResponseFormatException`, with the method in `Nsid` and the `JsonException` as `InnerException`. `RecordCollection<T>` and `RepoClient.GetRecordAsync<T>` throw it too when a record's value is not a `T`.

## Per-call timeouts

```csharp
try
{
    var feed = await client.QueryAsync<FeedResponse>(
        Nsid.Parse("app.bsky.feed.getTimeline"),
        new XrpcParams().Add("limit", 50),
        new XrpcCallOptions { Timeout = TimeSpan.FromSeconds(5) });
}
catch (TimeoutException)
{
    // The call took longer than 5 s. Your own CancellationToken still surfaces as
    // OperationCanceledException.
}
```

## Not-authenticated state

Operations that act on the signed-in account (`RecordCollection<T>` calls on your own repository,
the `client.Bsky` helpers) throw `XrpcAuthenticationException` with `AuthenticationRequired` when
no session is installed, before sending anything. It is the same exception a service's 401 raises,
so one handler covers both, for password and OAuth sessions alike:

```csharp
try
{
    var todos = client.GetCollection<TodoItem>();
    await todos.CreateAsync(new TodoItem { Title = "Test" });
}
catch (XrpcAuthenticationException ex) when (ex.Is(XrpcErrors.AuthenticationRequired))
{
    Console.WriteLine("Sign in first");
}
```

## Existence checking

Use `FindAsync` to read a record that may be absent without a try/catch: it returns `null` only for `RecordNotFound`, and `ExistsAsync` is `FindAsync(...) is not null`. Any other error (a malformed key, an unavailable repo) still throws.

```csharp
bool exists = await todos.ExistsAsync(RecordKey.Parse("some-key"));
```

## Retry pattern

The client already retries 429s within `XrpcRateLimitOptions`, and with `AutoRefreshSession` on it
refreshes an expired session and resends the request by itself. For transient server errors, a small
wrapper does the rest:

```csharp
async Task<T> WithRetryAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
{
    for (var attempt = 0; ; attempt++)
    {
        try
        {
            return await operation();
        }
        catch (XrpcException ex) when (
            attempt < maxRetries - 1 &&
            ex.StatusCode is HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
        }
    }
}
```

## Hosting XRPC endpoints

The same `XrpcException` is what an endpoint hosted with `ATProtoNet.Server` throws to answer with a named error. The routing writes the `{"error", "message"}` body, the status and any `Headers`:

```csharp
using ATProtoNet.Http;

throw new XrpcException(XrpcErrors.RecordNotFound, "No such todo.", HttpStatusCode.NotFound);

var ex = new XrpcException(XrpcErrors.AuthenticationRequired, "Proof expired.", HttpStatusCode.Unauthorized);
ex.Headers["WWW-Authenticate"] = "DPoP error=\"invalid_dpop_proof\"";
throw ex;
```
