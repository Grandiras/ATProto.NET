# XRPC Endpoint Handlers

ATProtoNet.Server supports defining server-side XRPC endpoint handlers using a DI-friendly interface pattern. This is useful for building AT Protocol services (PDS, appview, relay, etc.) that expose `/xrpc/{nsid}` routes.

## Quick Start

### 1. Define an Endpoint

```csharp
using ATProtoNet.Identity;
using ATProtoNet.Server.Xrpc;

public class GetStatusEndpoint : IXrpcQuery<StatusOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.getStatus");

    public Task<StatusOutput> HandleAsync(HttpContext context, CancellationToken ct)
    {
        return Task.FromResult(new StatusOutput { Status = "ok", Version = "1.0" });
    }
}

public class StatusOutput
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}
```

The NSID is a static property, so it is read once at registration without constructing the
handler. `Nsid.Parse` validates it; an invalid NSID fails at startup.

### 2. Register and Map

```csharp
// Register endpoints
builder.Services.AddXrpcEndpointsFromAssembly(typeof(Program).Assembly);

var app = builder.Build();

// Map all registered endpoints as /xrpc/{nsid} routes
app.MapXrpcEndpoints();
```

## Endpoint Types

A handler implements exactly one of these interfaces. Queries map to `GET`, procedures to `POST`.

| Interface | Input | Output |
|-----------|-------|--------|
| `IXrpcQuery<TParams, TOutput>` | Query string, bound to `TParams` | JSON |
| `IXrpcQuery<TOutput>` | None | JSON |
| `IXrpcBlobQuery<TParams>` | Query string | Bytes (`XrpcBlobResult`) |
| `IXrpcProcedure<TInput, TOutput>` | JSON body | JSON |
| `IXrpcProcedure<TOutput>` | None (any body is ignored) | JSON |
| `IXrpcProcedureVoid<TInput>` | JSON body | None (`200`, empty body) |
| `IXrpcProcedureVoid` | None | None |
| `IXrpcBlobProcedure<TOutput>` | Bytes (`XrpcBlobInput`) | JSON |

### Query (GET)

For read-only operations:

```csharp
// Without parameters
public class GetStatusEndpoint : IXrpcQuery<StatusOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.getStatus");

    public Task<StatusOutput> HandleAsync(HttpContext context, CancellationToken ct)
    {
        return Task.FromResult(new StatusOutput { Status = "ok" });
    }
}

// With query parameters
public class SearchEndpoint : IXrpcQuery<SearchParams, SearchOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.search");

    public Task<SearchOutput> HandleAsync(SearchParams parameters, HttpContext context, CancellationToken ct)
    {
        var results = DoSearch(parameters.Query, parameters.Limit, parameters.Authors);
        return Task.FromResult(new SearchOutput { Results = results });
    }
}

public class SearchParams
{
    [JsonPropertyName("q")]
    public required string Query { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("authors")]
    public IReadOnlyList<Did>? Authors { get; init; }
}
```

### Query parameter binding

Parameters bind from the query string by the type each property is declared as:

- A **collection** property (`List<T>`, `IReadOnlyList<T>`, `T[]`, …) takes every value of its key.
  `?authors=did:plc:a` binds a one-element list, `?authors=did:plc:a&authors=did:plc:b` two.
  `authors[]=…` is accepted too.
- A **scalar** property takes exactly one value. The same key twice answers `InvalidRequest`.
- **Identifier** types (`Did`, `Handle`, `AtIdentifier`, `Nsid`, `AtUri`, `Tid`, `RecordKey`, `Cid`)
  are validated by their own parsers, numbers and `bool` parse from their text, and a `required`
  property that is absent answers `InvalidRequest`.
- Unknown keys are ignored.

A value that does not bind answers `400 InvalidRequest` naming the parameter:

```json
{
  "error": "InvalidRequest",
  "message": "Invalid value for query parameter 'authors'."
}
```

### Procedure (POST)

For write operations:

```csharp
// With input and output
public class CreateItemEndpoint : IXrpcProcedure<CreateItemInput, CreateItemOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.createItem");

    public Task<CreateItemOutput> HandleAsync(CreateItemInput input, HttpContext context, CancellationToken ct)
    {
        // input is deserialized from the JSON request body
        var item = SaveItem(input);
        return Task.FromResult(new CreateItemOutput { Uri = item.Uri, Cid = item.Cid });
    }
}

// No output
public class DeleteItemEndpoint : IXrpcProcedureVoid<DeleteItemInput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.deleteItem");

    public Task HandleAsync(DeleteItemInput input, HttpContext context, CancellationToken ct)
    {
        DeleteItem(input.Uri);
        return Task.CompletedTask;
    }
}

// No input
public class RotateKeyEndpoint : IXrpcProcedure<RotateKeyOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.rotateKey");

    public Task<RotateKeyOutput> HandleAsync(HttpContext context, CancellationToken ct) => RotateAsync(ct);
}
```

A JSON procedure requires `Content-Type: application/json`; a missing or malformed body answers
`400 InvalidRequest`.

### Binary input and output

A method whose Lexicon input or output `encoding` is not JSON uses the blob shapes. The request
body arrives as a stream, unbuffered; its size is bounded by the server's request size limit
(Kestrel's `MaxRequestBodySize`, or `[RequestSizeLimit]` on the handler class), and reading past it
answers `413 PayloadTooLarge`.

```csharp
public class UploadBlobEndpoint : IXrpcBlobProcedure<UploadBlobOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.uploadBlob");

    public async Task<UploadBlobOutput> HandleAsync(XrpcBlobInput input, HttpContext context, CancellationToken ct)
    {
        // input.Content is the request body, input.ContentType the declared MIME type,
        // input.ContentLength the declared length when the client sent one.
        var blob = await _store.SaveAsync(input.Content, input.ContentType, ct);
        return new UploadBlobOutput { Blob = blob };
    }
}

public class GetBlobEndpoint : IXrpcBlobQuery<GetBlobParams>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.getBlob");

    public async Task<XrpcBlobResult> HandleAsync(GetBlobParams parameters, HttpContext context, CancellationToken ct)
    {
        var blob = await _store.OpenAsync(parameters.Cid, ct);

        // Streamed to the client, then disposed.
        return new XrpcBlobResult(blob.Stream, blob.MimeType, blob.Length);
    }
}
```

A blob procedure without a `Content-Type` answers `400 InvalidRequest`.

## Registration

### Individual Registration

```csharp
builder.Services.AddXrpcEndpoint<GetStatusEndpoint>();
builder.Services.AddXrpcEndpoint<CreateItemEndpoint>();
```

### Assembly Scanning

Registers every non-abstract, non-generic class in the assembly that implements `IXrpcEndpoint`,
exactly as `AddXrpcEndpoint<T>()` registers one:

```csharp
builder.Services.AddXrpcEndpointsFromAssembly(typeof(Program).Assembly);
```

Registration fails fast with an `InvalidOperationException` when a handler implements no endpoint
interface or more than one, or when two handlers declare the same NSID (compared
case-insensitively, as routes match). Registering the same handler twice is harmless.

## Route Mapping

```csharp
var app = builder.Build();
app.MapXrpcEndpoints();
```

`MapXrpcEndpoints()` returns the `/xrpc` route group, so endpoint conventions apply to every XRPC
endpoint at once:

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapXrpcEndpoints()
    .RequireAuthorization()
    .RequireRateLimiting("xrpc");
```

Attributes on a handler class become that endpoint's metadata, so `[Authorize]`,
`[AllowAnonymous]`, `[EnableRateLimiting]` and `[RequestSizeLimit]` apply per handler:

```csharp
[AllowAnonymous] // reachable even though the group requires authorization
public class DescribeServerEndpoint : IXrpcQuery<DescribeServerOutput> { /* ... */ }

[Authorize(Policy = "admin")]
public class TakedownEndpoint : IXrpcProcedureVoid<TakedownInput> { /* ... */ }
```

A `401`/`403` from authorization or a `429` from rate limiting is written by that middleware, not
by the XRPC routing; set the scheme's challenge or the limiter's `OnRejected` to answer with an
XRPC error body.

### Unmatched routes

The group also answers any `/xrpc/{nsid}` request that no endpoint matched, as the reference
`xrpc-server` does:

| Request | Status | `error` |
|---------|--------|---------|
| An NSID no handler serves | `501` | `MethodNotImplemented` |
| A registered NSID with the wrong HTTP method (`POST` to a query, `GET` to a procedure) | `405`, with `Allow` | `InvalidRequest` |
| A path segment that is not an NSID (`/xrpc/foo`) | `400` | `InvalidRequest` |

The fallback is ordered after every other endpoint, so a route the application maps under `/xrpc`
itself (such as `/xrpc/_health`) still answers, and it matches only a single segment below
`/xrpc`. Group conventions apply to it too: when the group requires authorization, an
unauthenticated caller gets the challenge, not a hint of which methods exist.

## Errors

Every exception an endpoint throws is answered with the XRPC error envelope:

- `XrpcException` (from `ATProtoNet.Http`, the same type the client throws for a failed call) answers
  with its status, error name, message, and any `Headers` you add.
- A request the server refused (`BadHttpRequestException`) answers with its status: `413
  PayloadTooLarge` for a body over the size limit, `InvalidRequest` otherwise.
- Anything else answers `500 InternalServerError` with the generic message `Internal Server Error`.
  The exception is logged under the `ATProtoNet.Server.Xrpc` category, and its message never
  reaches the client.

```csharp
using System.Net;
using ATProtoNet.Http;

throw new XrpcException(XrpcErrors.RecordNotFound, $"No profile for {parameters.Actor}.", HttpStatusCode.NotFound);
```

```json
{
  "error": "RecordNotFound",
  "message": "No profile for did:plc:ewvi7nxzyoun6zhxrhs64oiz."
}
```

## Dependency Injection

Endpoint handlers are registered as scoped services and resolved per request, so you can inject
services:

```csharp
public class GetProfileEndpoint : IXrpcQuery<ProfileParams, ProfileOutput>
{
    private readonly IProfileService _profiles;
    private readonly ILogger<GetProfileEndpoint> _logger;

    public GetProfileEndpoint(IProfileService profiles, ILogger<GetProfileEndpoint> logger)
    {
        _profiles = profiles;
        _logger = logger;
    }

    public static Nsid Nsid { get; } = Nsid.Parse("com.example.getProfile");

    public async Task<ProfileOutput> HandleAsync(ProfileParams parameters, HttpContext context, CancellationToken ct)
    {
        _logger.LogInformation("Fetching profile for {Did}", parameters.Did);
        return await _profiles.GetAsync(parameters.Did, ct);
    }
}
```

## Upgrading from 0.6

Endpoints declare their NSID once, as a static property. Delete the `[XrpcEndpoint(Nsid = …)]`
attribute and turn the instance property into a static one:

```csharp
// 0.6
[XrpcEndpoint(Nsid = "com.example.getStatus")]
public class GetStatusEndpoint : IXrpcQuery<StatusOutput>
{
    public string Nsid => "com.example.getStatus";
    // ...
}

// 0.7
public class GetStatusEndpoint : IXrpcQuery<StatusOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.getStatus");
    // ...
}
```

`MapXrpcEndpoints()` now returns a `RouteGroupBuilder` rather than the `IEndpointRouteBuilder` it
was called on; code that chained other `Map…` calls onto its result calls them on the app instead.

## Next Steps

- [Managed PDS](managed-pds.md) — run the Bluesky PDS container and administer it
- [Server Integration](server.md) — Backend AT Proto access patterns
- [ASP.NET Core](aspnet-core.md) — DI integration
