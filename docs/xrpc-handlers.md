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

## Serving XRPC to other services

A feed generator, labeler or AppView is called by other AT Protocol services on a user's behalf —
most often by the user's PDS, proxying a request the app sent it with an `atproto-proxy` header.
Such a call carries a **service auth token**: a short-lived JWT signed with the calling account's
`#atproto` key, addressed (`aud`) to your service's DID and service entry, and scoped (`lxm`) to
one method. `AddAtProtoServiceAuth()` verifies it:

```csharp
using ATProtoNet.Server.Authentication;

builder.Services.AddAuthentication()
    .AddAtProtoServiceAuth(options =>
    {
        // Your DID and the service entry callers address, as in your DID document.
        options.Audiences.Add("did:web:feed.example.com#bsky_fg");
        // The bare DID too, for as long as callers still send it: PDS proxying does for now.
        options.Audiences.Add("did:web:feed.example.com");
    });
builder.Services.AddAuthorization();
builder.Services.AddXrpcEndpoint<GetFeedSkeletonEndpoint>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapXrpcEndpoints().RequireServiceAuth();
```

Require it on the whole group as above, or on single handlers with `[RequireServiceAuth]`
(`[AllowAnonymous]` still opens a handler inside a group that requires it, and the space server's
endpoints, which authenticate their callers themselves, stay out of it too). A token is bound to
the NSID of the endpoint it reached, so the handler needs no check of its own; the caller's DID is
the principal's name:

```csharp
[RequireServiceAuth]
public sealed class GetFeedSkeletonEndpoint : IXrpcQuery<FeedSkeletonParams, FeedSkeletonOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("app.bsky.feed.getFeedSkeleton");

    public Task<FeedSkeletonOutput> HandleAsync(FeedSkeletonParams parameters, HttpContext context, CancellationToken ct)
    {
        var viewer = Did.Parse(context.User.Identity!.Name!); // also the "did" claim
        // …
    }
}
```

What a token must satisfy, following the service auth spec as revised in 2026
([proposal 0014](https://github.com/bluesky-social/proposals/tree/main/0014-service-auth-revised)):

| Check | Default | Option |
| --- | --- | --- |
| `typ` is `JWT` or absent: no `…+jwt` type of another kind of token | always | — |
| `aud` is one of this service's audiences | — (required) | `Audiences` |
| `lxm` names the endpoint's NSID | required | `RequireLexiconMethod`: off tolerates a missing `lxm` from a sender that has not caught up, never a wrong one |
| The `kid` header names an accepted key (no `kid` means `#atproto`, held to the same list) | `#atproto` only | `AllowedKeyIds`: add `#atproto_label` or similar only if that key should authenticate |
| `iss` is a bare DID | always | — |
| `exp` has not passed; `iat` and any `nbf` are not in the future | 30 seconds of clock skew | `ClockSkew` |
| `exp` is no further ahead, and `iat` no further back, than | 5 minutes | `MaxTokenLifetime`: a reference PDS mints tokens of up to an hour on request |
| The signature verifies against the issuer's key | refetched once on failure | the `IDidResolver` from `AddAtProtoIdentity()` |
| The `jti` has not been used; it is kept until `exp` plus the skew | always | the registered `IJtiReplayStore` |

A refused token is answered `401` with the XRPC error envelope and the error names the reference
`xrpc-server` uses (`ServiceAuthErrors`): `BadJwt`, `BadJwtType`, `JwtExpired`, `BadJwtAudience`,
`BadJwtLexiconMethod`, `BadJwtIss` or `BadJwtSignature`, or `AuthenticationRequired` when there was
no token.

Some things to know:

- **Run more than one instance? Share the replay store.** The default `InMemoryJtiReplayStore` is
  per-process, so a captured token replayed against another instance is accepted. Register
  `AddAtProtoEfCoreJtiReplayStore<JtiReplayDbContext>()` (with `AddDbContextFactory<JtiReplayDbContext>`),
  or implement `IJtiReplayStore` over anything with an atomic "set if absent, with expiry", such as
  Redis `SET key 1 NX EXAT exp`. A space server uses the same store.
- **Keys come from the DID document cache.** A token that fails against a cached key is retried
  once against a refreshed document, so a caller's key rotation is picked up at once. A key rotated
  *away* keeps verifying until the cached document expires, though: shorten the cache through
  `AddAtProtoIdentity(o => o.Cache…)`, or call `IDidResolver.InvalidateAsync` on `#identity` events.
- **A token is verified only where service auth is asked for.** Verifying spends it, so the
  scheme verifies a token only on an endpoint whose authorization names the scheme
  (`[RequireServiceAuth]`, `.RequireServiceAuth()`, or a policy listing it), as the reference
  `xrpc-server` verifies only for methods that declare an auth verifier. Anywhere else — an
  `[AllowAnonymous]` endpoint, one with no authorization, a space server endpoint checking its own
  credentials — the token is left alone and stays usable.
- **Registered alone, the scheme is the default.** ASP.NET Core makes the only registered scheme the
  default one, so `UseAuthentication()` runs it on every request, and a plain `[Authorize]` or
  `.RequireAuthorization()` authenticates with it. That is safe by the rule above: the middleware's
  run declines on every endpoint that does not ask for service auth. With another default scheme
  (cookies, say), only the endpoints that name this one use it.
- **Only XRPC endpoints accept service auth.** A token is valid for the one method it names, so an
  endpoint that asks for service auth but serves no XRPC method refuses every token, without
  spending it. To accept service auth on a route you map yourself, attach
  `new XrpcMethodMetadata(nsid)` to it.
- **Optional authentication** (a feed that personalizes when a token comes along) is not the
  scheme's job: an `[AllowAnonymous]` handler that wants to read a token verifies it with
  `ServiceAuthVerifier` itself.
- **Authentication needs the endpoint**, so `UseAuthentication()` must come after routing, as it does
  in a `WebApplication` by default.

To verify a token that does not arrive as an ASP.NET Core request, use `ServiceAuthVerifier`
directly: `await verifier.VerifyAsync(token, audiences, nsid)` returns the `VerifiedServiceAuth` or
throws `ServiceAuthException`. To *call* another service, mint the token with
[`ServiceAuthGenerator`](crypto.md#service-authentication).

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
