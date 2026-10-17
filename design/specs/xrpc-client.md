# XRPC client — spec

**Status:** Draft · **Package:** `ATProtoNet` (namespaces `ATProtoNet` for `AtProtoClient`,
`ATProtoNet.Xrpc`, `ATProtoNet.Records`, `ATProtoNet.Admin`) · **Phase:** 5 (client core) ·
**Prefix:** `XRC`

**Normative references:** [atproto HTTP API (XRPC)](https://atproto.com/specs/xrpc) ·
[Lexicon](https://atproto.com/specs/lexicon) · [Sync: `Atproto-Repo-Rev`](https://atproto.com/specs/sync) ·
[RFC 9110](https://www.rfc-editor.org/rfc/rfc9110) (HTTP semantics, `Retry-After`) ·
[IETF draft RateLimit headers](https://datatracker.ietf.org/doc/draft-ietf-httpapi-ratelimit-headers/) ·
[RFC 3986](https://www.rfc-editor.org/rfc/rfc3986) (URI encoding).

## Scope

In scope: `AtProtoClient` and its call-option views; the generated method descriptors as seen by
the runtime; the `IXrpcHandler` contract that sessions implement; `XrpcInvoker`, the one HTTP
implementation of XRPC; request building, headers, response reading, response metadata, the error
model, the retry policy, pagination, the records API and `PdsAdminClient`.

Out of scope: how sessions obtain, refresh and attach credentials ([auth.md](auth.md)); what the
generator emits ([lexicon.md](lexicon.md)); serving XRPC ([server.md](server.md)); event streams
([streaming.md](streaming.md)); the untrusted-fetch layer used for identity and OAuth metadata
([identity.md](identity.md)). The XRPC calls those components make go through `XrpcInvoker`.

## Public API sketch

```csharp
namespace ATProtoNet;

public sealed class AtProtoClient                       // immutable, thread-safe, cheap to create
{
    public AtProtoClient(IXrpcHandler handler, XrpcCallOptions? defaults = null);

    public IXrpcHandler Handler { get; }
    public XrpcCallOptions Defaults { get; }
    public Did? Account => Handler.Account;            // null for anonymous handlers

    public AtProtoClient With(XrpcCallOptions options); // view: same handler, options merged over Defaults

    public Task<TOutput> QueryAsync<TParams, TOutput>(
        XrpcQuery<TParams, TOutput> method, TParams parameters, CancellationToken cancellationToken = default);
    public Task<TOutput> ProcedureAsync<TInput, TOutput>(
        XrpcProcedure<TInput, TOutput> method, TInput input, CancellationToken cancellationToken = default);
    public Task ProcedureAsync<TInput>(
        XrpcProcedure<TInput> method, TInput input, CancellationToken cancellationToken = default);
    public Task<TOutput> ProcedureAsync<TParams, TInput, TOutput>(
        XrpcProcedure<TParams, TInput, TOutput> method, TParams parameters, TInput input,
        CancellationToken cancellationToken = default);

    // Generated com.atproto.* namespaces: Server, Repo, Identity, Sync, Admin, Label, Moderation, Temp, Lexicon
    public RepoNamespace Repo => new(this);

    public RecordCollection<T> Records<T>() where T : class, IAtProtoRecord<T>;
    public RecordReader<T> Records<T>(AtIdentifier repo) where T : class, IAtProtoRecord<T>;  // read-only, any repo

    public static Task<AtProtoClient> LoginAsync(Uri service, AtIdentifier identifier, string password,
        CancellationToken cancellationToken = default);                                  // scripts; see auth.md
}

namespace ATProtoNet.Xrpc;

public abstract class XrpcMethod                        // generated descriptors derive from this
{
    public Nsid Nsid { get; }
    public XrpcMethodKind Kind { get; }                 // Query | Procedure
    public string? InputEncoding { get; }               // null = no input; "application/json"; other MIME = binary
    public string? OutputEncoding { get; }              // null = no output
    public XrpcServiceRef? DefaultProxy { get; }        // e.g. chat.bsky.* → did:web:api.bsky.chat#bsky_chat
    public bool IsDirect { get; }                       // session-management calls: never proxied
    public IReadOnlySet<string> Errors { get; }         // Lexicon-declared error names
}
public sealed class XrpcQuery<TParams, TOutput> : XrpcMethod { /* JsonTypeInfo<TOutput>, parameter writer */ }
public sealed class XrpcProcedure<TInput, TOutput> : XrpcMethod { /* JsonTypeInfo<TInput>, JsonTypeInfo<TOutput> */ }
public sealed class XrpcProcedure<TInput> : XrpcMethod { }   // no output
public sealed class XrpcProcedure<TParams, TInput, TOutput> : XrpcMethod { } // query parameters + body (only app.bsky.video.uploadPart upstream)
public readonly struct XrpcEmpty;                       // TParams/TInput of methods that declare none
public sealed record XrpcBinaryContent(Stream Content, string ContentType, long? ContentLength = null); // binary input (and server-side binary output)
public sealed class XrpcBinaryResponse : IAsyncDisposable, IDisposable            // binary output; owns the response
{ public Stream Content { get; } public string? ContentType { get; } public long? ContentLength { get; } }

public interface IXrpcHandler
{
    Did? Account { get; }
    Task<XrpcResponse> SendAsync(XrpcRequest request, CancellationToken cancellationToken);
}

public sealed record XrpcCallOptions
{
    public XrpcServiceRef? Proxy { get; init; }
    public IReadOnlyDictionary<XrpcServiceRef, XrpcServiceRef>? ProxyOverrides { get; init; }
    public IReadOnlyList<LabelerRef>? AcceptLabelers { get; init; }
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public TimeSpan? Timeout { get; init; }
    public IXrpcResponseObserver? Observer { get; init; }
}

public sealed record XrpcServiceRef(Did Did, string ServiceId);  // "did:web:api.bsky.app#bsky_appview"
public sealed record LabelerRef(Did Did, bool Redact = false);    // "did:plc:x;redact"

public sealed class XrpcInvoker                          // stateless apart from its options
{
    public XrpcInvoker(HttpClient httpClient, XrpcInvokerOptions? options = null, TimeProvider? timeProvider = null);
    public Task<XrpcResponse> SendAsync(XrpcRequest request, XrpcTarget target, CancellationToken cancellationToken);
}
public sealed record XrpcTarget(Uri ServiceUrl, IXrpcAuthorizer? Authorizer);
public interface IXrpcAuthorizer                         // implemented by sessions; see auth.md
{
    IReadOnlyCollection<string> ReservedHeaders { get; } // e.g. Authorization, DPoP
    ValueTask AuthorizeAsync(HttpRequestMessage message, CancellationToken cancellationToken); // once per HTTP attempt
}
public sealed record XrpcResponseMetadata(Tid? RepoRev, RateLimitInfo? RateLimit, IReadOnlyList<LabelerRef>? ContentLabelers);
public sealed record XrpcInvokerOptions
{
    public string? UserAgent { get; init; }              // default "ATProtoNet/{version}"
    public XrpcRateLimitPolicy RateLimit { get; init; } = new();   // MaxRetries 3, MaxDelay 30 s
    public long MaxJsonResponseBytes { get; init; } = 16 * 1024 * 1024;
}

public static class Pagination
{
    public static IAsyncEnumerable<T> EnumerateAsync<TPage, T>(
        Func<string?, CancellationToken, Task<TPage>> fetchPage, CancellationToken cancellationToken = default)
        where TPage : ICursorPage<T>;
}
```

## Requirements

### Client and views

- **XRC-1** (MUST) `AtProtoClient` is immutable after construction: no settable state, no
  login/logout members, no client-wide "latest" values. Concurrent calls on one client or on
  views of it MUST NOT observe each other's options.
  *Source:* Decision [D-06](../06-decisions.md#d-06-a-session-is-the-authenticated-transport) · 0.8
  test `ConcurrentCallsWithDifferentOptions_DoNotSeeEachOthersHeaders`
- **XRC-2** (MUST) `With(options)` returns a new client sharing the handler, whose defaults are
  `options` merged over the current defaults, property by property (a property left `null` keeps
  the current value; `Headers` merge by key, the new value winning). Creating a view MUST NOT
  allocate per-namespace sub-clients; namespace accessors are structs over the client.
  *Source:* Decision (replaces 0.8's mutable `SetProxy`/`SetLabelers` and the
  per-call-options gap: no built-in method accepted `XrpcCallOptions`)
- **XRC-3** (MUST) `QueryAsync` and `ProcedureAsync` over an `XrpcMethod` descriptor are the only
  call primitives. Generated namespace clients, hand-written helpers, the records API and
  `PdsAdminClient` call them; no other code path builds XRPC requests from strings.
  *Source:* Decision [D-04](../06-decisions.md#d-04-generate-the-lexicon-surface-from-vendored-lexicon-json) ·
  0.8 problem: two transports (internal `XrpcClient` with 308 string-NSID call sites, public `IXrpcTransport`)
- **XRC-4** (MUST) A method descriptor is created only by generated code or by
  `XrpcMethod.Create…` factories taking an `Nsid`, encodings and `JsonTypeInfo`s, so user code can
  call methods of Lexicons it has not generated. Descriptors are immutable and safe to cache in
  `static readonly` fields.
  *Source:* 0.8 `client.QueryAsync<TOut>(nsid, …)` capability, typed
- **XRC-5** (MUST) A call that needs an account (records writes, any API documented as acting on
  "the signed-in repo") on a client whose `Handler.Account` is `null` MUST fail before any request
  is sent with `XrpcAuthenticationException` whose `Error` is `AuthenticationRequired`,
  `StatusCode` is `null` and `Nsid` is the method.
  *Source:* 0.8 `AtProtoClient.cs:703`, tests `OwnRepoCalls_WithoutASession_…`,
  `Helpers_NotAuthenticated_…`; status `null` instead of a fabricated 401 is a Decision (local
  precondition, no response)

### Handler contract

- **XRC-6** (MUST) `IXrpcHandler.SendAsync` sends one logical call and returns the successful
  response or throws. It decides the target service URL and the authorizer, and it owns credential
  recovery (DPoP nonce retry, refresh-and-resend; [auth.md](auth.md)). It delegates HTTP to an
  `XrpcInvoker`.
  *Source:* [D-06](../06-decisions.md#d-06-a-session-is-the-authenticated-transport)
- **XRC-7** (MUST) A handler reads its service URL and credentials **once per call** as one
  `XrpcTarget` snapshot, after any proactive refresh, and every HTTP attempt of that call (429
  waits included) uses that snapshot or a recovery of it for the same account and service. A
  session change during a call MUST NOT send that call's request with another account's
  credentials or to another account's service.
  *Source:* 0.8 `XrpcClient.cs:417` · tests `ACallWaitingOutA429_IsResentToItsOwnServiceWithItsOwnToken_AfterASwitch`,
  `ACallRejectedAfterAnAccountSwitch_IsNotResentWithTheOtherAccountsToken`
- **XRC-8** (MUST) `AnonymousHandler(Uri serviceUrl, XrpcInvoker invoker)` sends without
  credentials. It is the handler for public AppView reads, unauthenticated PDS endpoints
  (`describeServer`, `createAccount`) and tests.
  *Source:* Decision

### Service URLs and HTTP

- **XRC-9** (MUST) A service URL is absolute `http` or `https`, has no query or fragment, and is
  normalised to end in `/`. Violations throw `ArgumentException` at construction.
  *Source:* 0.8 `AtProtoHttp.NormalizeBaseUrl` (`AtProtoHttp.cs:187`) · tests
  `Constructor_WithAnInvalidInstanceUrl_Throws`, `SetServiceUrl_WithAQueryOrFragment_Throws`
- **XRC-10** (MUST) A URL the developer configures (an `AnonymousHandler`, the service passed to
  `PasswordSession.CreateAsync`, the PDS admin URL with its own rule in XRC-55) is trusted as
  given, plain HTTP to any host included. A URL learned at runtime (a PDS endpoint from a DID
  document, an OAuth session's resource server, a password refresh that moves the session) MUST be
  HTTPS, or HTTP to a loopback address. A runtime URL that fails this is rejected and the session
  stays where it was (logged at Warning).
  *Source:* 0.8 `AtProtoClient.cs:129` (`allowInsecure: true`), `XrpcClient.cs:101`, `AtProtoHttp.cs:260` ·
  tests `Constructor_TrustsAConfiguredPlainHttpHost`, `SecurityTests.SetServiceUrl_RejectsNonTlsPublicUrls`,
  `LoginAsync_DidDocumentThatCannotBeFollowed_StaysOnTheConfiguredService`
- **XRC-11** (MUST) Requests go to `{scheme}://{authority}/xrpc/{nsid}`; any path on the service
  URL is ignored.
  *Source:* [specs/xrpc](https://atproto.com/specs/xrpc) · 0.8 `XrpcClient.cs:597`
- **XRC-12** (MUST NOT) The SDK MUST NOT read or modify a supplied `HttpClient`'s `BaseAddress`,
  `DefaultRequestHeaders` or `Timeout`. Every header is set per request, so one `HttpClient` can
  serve any number of clients, sessions and services, and a target can change after the first
  request.
  *Source:* 0.8 #117 · tests `InstanceUrl_WinsOverASuppliedClientsBaseAddress`,
  `ManyClientsOnOneHttpClient_SendOneUserAgentEachAndLeaveTheClientUntouched`,
  `SetServiceUrl_AfterARequestOnASharedClient_TakesEffect`
- **XRC-13** (MUST) When the SDK creates its own `HttpClient`, it uses one process-wide
  `SocketsHttpHandler` with automatic decompression, a 5-minute `PooledConnectionLifetime` and a
  10-second `ConnectTimeout`, falling back to `HttpClientHandler` where `SocketsHttpHandler` is
  unsupported (browser/WASM). Disposing an SDK-owned `HttpClient` MUST NOT dispose the shared
  handler. The handler is immutable configuration, not a cache, so it does not violate AG-10.
  *Source:* 0.8 `AtProtoHttp.cs:131-147` · test `AtProtoHttpTests`
- **XRC-14** (MUST) Requests ask for HTTP/2 with `HttpVersionPolicy.RequestVersionOrLower`.
  *Source:* 0.8 `XrpcClient.cs:604-609` (#201)
- **XRC-15** (MUST) The `User-Agent` header is `XrpcInvokerOptions.UserAgent`, default
  `ATProtoNet/{InformationalVersion}`. When it is set to `null` the SDK sends none of its own, so
  the `HttpClient`'s default (if any) applies.
  *Source:* 0.8 tests `UserAgent_IsConfigurablePerClient`, `UserAgent_Null_LeavesTheHttpClientsDefault`

### Request building

- **XRC-16** (MUST) A query is `GET`, a procedure is `POST`.
  *Source:* [specs/xrpc](https://atproto.com/specs/xrpc)
- **XRC-17** (MUST) Query parameters are written in the declaration order of the generated
  `Parameters` record; `null` values are omitted; arrays are written as one repeated key per
  element, never comma-joined; booleans as `true`/`false`; integers in invariant culture;
  identifiers, `AtDatetime` and known-value structs as their `ToString()`; keys and values are
  percent-encoded per RFC 3986 (`Uri.EscapeDataString`).
  *Source:* 0.8 `XrpcParams` · tests `XrpcParamsTests`,
  `EndpointRequestTests.SearchPostsAsync_Tags_SendsOneTagParameterEach` (#123, 0.6 fix)
- **XRC-18** (MUST) A procedure whose Lexicon declares a JSON input always sends a body, even
  when every field is optional (`{}`), serialised **once** with the source-generated
  `JsonTypeInfo<TInput>`, with `Content-Type: application/json` and a `Content-Length`. A
  procedure that declares no input sends no body and no `Content-Type`.
  *Source:* 0.8 `XrpcClient.cs:580-590` · tests `DeactivateAccountAsync_NoArguments_SendsAnEmptyJsonBody`,
  `ProcedureAsync_WithANullInput_PostsNoBody`
- **XRC-19** (MUST) Binary input (`XrpcBinaryContent`) is sent from the stream's current
  position with the given content type, with `Content-Length` when the stream is seekable, and the
  stream is never disposed by the SDK.
  *Source:* 0.8 test `Upload_SendsFromTheCallersPositionAndLeavesTheStreamOpen`
- **XRC-20** (MUST) A `Parameters` record whose required member is missing cannot be constructed
  (C# `required`); the runtime MUST NOT re-validate parameter presence. Lexicon constraints
  (`maxLength`, `minimum`, …) are not enforced on the client before sending unless the caller
  opts into validation ([lexicon.md](lexicon.md)).
  *Source:* Decision (the server is the authority on its own constraints)

### Headers

- **XRC-21** (MUST) The `atproto-proxy` header value is chosen in this order: (1) if the
  descriptor has a `DefaultProxy`, that value, replaced by `ProxyOverrides[DefaultProxy]` when the
  call options map it; (2) otherwise the call options' `Proxy`; (3) otherwise no header. The
  header is sent with or without a session.
  *Source:* 0.8 `XrpcClient.cs:654-668`, chat clients pinning `did:web:api.bsky.chat#bsky_chat`
  (`ConvoClient.cs:17`) · tests `ClientWideProxyAndLabelers_AreSentOnUnauthenticatedCalls`,
  `CallOptions_OverrideTheClientDefaultsForThatCallOnly`
- **XRC-22** (MUST) Descriptors marked `IsDirect` (`com.atproto.server.createSession`,
  `refreshSession`, `getSession`, `deleteSession`, `createAccount`) never carry `atproto-proxy` or
  `atproto-accept-labelers`.
  *Source:* 0.8 `XrpcClient.cs:335` · test `SessionCalls_AreNotRoutedThroughTheDefaultProxy`
- **XRC-23** (MUST) `atproto-accept-labelers` lists the call options' labelers as
  `did[;redact]` joined with `", "`. An empty or `null` list sends no header.
  *Source:* [specs/xrpc](https://atproto.com/specs/xrpc) labelers · 0.8 `Labelers_AreSentOnlyWhileSet`
- **XRC-24** (MUST) `XrpcServiceRef` and `LabelerRef` parse and format their wire forms
  (`did#service_id`; `did` or `did;redact`), reject anything else with `FormatException`, and are
  the only way to set those headers.
  *Source:* Decision (0.8 used raw strings: `SetProxy(string)`, `ServiceProxy.Build(string, string)`)
- **XRC-25** (MUST) `XrpcCallOptions.Headers` are added last and override `User-Agent`,
  `atproto-proxy` and `atproto-accept-labelers`. A header named in the active authorizer's
  `ReservedHeaders` (at least `Authorization` and `DPoP`, compared case-insensitively) MUST be
  rejected with `ArgumentException` when the call starts.
  *Source:* 0.8 `XrpcClient.cs:732-747` · test `CallOptions_Headers_CannotReplaceTheSessionsCredentials`

### Responses

- **XRC-26** (MUST) A JSON success response is deserialised while it streams
  (`HttpCompletionOption.ResponseHeadersRead`), through a stream that fails the read once more than
  `MaxJsonResponseBytes` have arrived, whatever `Content-Length` declared.
  *Source:* 0.8 test `QueryAsync_DeserializesFromTheStreamWithoutBufferingTheBody`; the cap is a
  Decision ([D-20](../06-decisions.md#d-20-json-reads-are-case-sensitive-and-strict-about-number-types);
  0.8 capped only error bodies) · test pattern `BoundedContentTests.ReadBoundedAsync_DeclaredLengthThatUnderstatesTheBody_IsStillCapped`
- **XRC-27** (MUST) A success response for a method that declares output, whose body is empty,
  is JSON `null`, does not match the output type, has a `Content-Type` whose media type is present
  and not `application/json`, or exceeds the cap, throws `XrpcResponseFormatException` (inner
  `JsonException` where one exists) carrying the NSID and status.
  *Source:* 0.8 tests `QueryAsync_WhenTheBodyIsEmpty_ThrowsResponseFormatException`,
  `QueryAsync_WhenTheBodyIsNotTheExpectedType_ThrowsResponseFormatException`; the content-type
  check is a Decision
- **XRC-28** (MUST) For a method that declares no output the SDK MUST NOT read or parse the
  success body; any 2xx with any body (empty included) succeeds, and the response is disposed.
  *Source:* 0.8 #69 · tests `EmptyResponseBodyTests` (19 NSIDs),
  `PdsAdminClientTests.VoidAdminProcedures_TolerateAnEmptyResponseBody`
- **XRC-29** (MUST) A binary-output method returns `XrpcBinaryResponse`, which owns the HTTP
  response and its stream until disposed. The SDK MUST NOT buffer it. On failure the response is
  released before the exception propagates.
  *Source:* 0.8 tests `Download_ReturnsTheStreamWithItsMetadataAndReleasesTheResponse`,
  `Download_WhenTheServiceFails_ThrowsAndReleasesTheResponse`

### Response metadata

- **XRC-30** (MUST) Every response, success or error, is inspected for `Atproto-Repo-Rev`,
  `RateLimit-Limit`, `RateLimit-Remaining`, `RateLimit-Reset` (and `RateLimit-Policy` when
  present) and `atproto-content-labelers`, producing an `XrpcResponseMetadata`. A repo rev that is
  not a valid TID reads as `null`.
  *Source:* [specs/sync](https://atproto.com/specs/sync) · 0.8 `XrpcResponseReader.cs:275` · tests
  `XrpcClientRepoRevTests`, `QueryAsync_ParsesRateLimitHeaders`
- **XRC-31** (MUST) Metadata is delivered to `XrpcCallOptions.Observer` (if any) once per call,
  after the final attempt, and is attached to every `XrpcException`. Account-bound handlers
  (sessions) additionally keep `LatestRepoRev` per account: updated from each response that carries
  the header, kept when a later response lacks it, cleared when the session ends. There is no
  client-wide or process-wide "latest" value.
  *Source:* Decision (0.8 `LatestRepoRev`/`LatestRateLimitInfo` were client-wide, not per account,
  never reset on sign-out) · 0.8 test `LatestRepoRev_RetainsPreviousValueWhenHeaderMissing`

### Errors

- **XRC-32** (MUST) Every failure the SDK raises for a remote or protocol reason derives from
  `AtProtoException`. Transport faults (`HttpRequestException`, socket errors) surface unwrapped;
  caller cancellation surfaces as `OperationCanceledException`, never wrapped.
  *Source:* 0.8 test `SdkExceptions_ShareTheAtProtoExceptionBase` ·
  [AG-19](../03-api-guidelines.md#errors)
- **XRC-33** (MUST) A non-2xx response maps as follows, checked top to bottom:

  | Response | Exception | `Error` |
  |---|---|---|
  | 429 | `XrpcRateLimitException` (`RetryAfter`, `RateLimit`) | from body, else `RateLimitExceeded` |
  | 401, or 400 with error `ExpiredToken` or `InvalidToken` | `XrpcAuthenticationException` | from body, else `AuthenticationRequired` |
  | any other | `XrpcException` | from body, else by status: 400 `InvalidRequest`, 401 `AuthenticationRequired`, 403 `Forbidden`, 404 `XRPCNotSupported`, 413 `PayloadTooLarge`, 500 `InternalServerError`, 501 `MethodNotImplemented`, 502 `UpstreamFailure`, 503 `NotEnoughResources`, 504 `UpstreamTimeout`, else `Unknown` |

  "From body" means the JSON envelope `{"error": string, "message"?: string}`; a missing,
  malformed, non-JSON or oversized envelope falls back to the status name and `Message = null`.
  *Source:* [specs/xrpc](https://atproto.com/specs/xrpc) error responses · 0.8
  `XrpcResponseReader.cs:240-271`, `XrpcErrors.ForStatus` · tests `XrpcExceptionTests`
- **XRC-34** (MUST) Error bodies are read up to `MaxErrorBodyBytes`; an oversized or unreadable
  body yields `Error` from the status and `ResponseBody = null`, never an exception of its own.
  *Source:* 0.8 `XrpcResponseReader.cs:24,199-218` (Security, #117) · test
  `ReadErrorAsync_DoesNotReadAnOversizedBody`
- **XRC-35** (MUST) `XrpcException` exposes `Nsid`, `StatusCode` (`HttpStatusCode?`, `null` only
  for local preconditions), `Error`, `Message`, `ResponseBody` (bounded text), read-only
  `ResponseHeaders` and `Metadata`. `Is(string error)` compares `Error` ordinally. Generated
  descriptors expose their Lexicon-declared error names as constants
  (`GetRecord.Errors.RecordNotFound`) for use with `Is`.
  *Source:* 0.8 `XrpcException.cs` · Decision for read-only headers (0.8's mutable `Headers`
  doubled as server response headers) and per-method constants (0.8 had 28 of 124 upstream names)
- **XRC-36** (MUST NOT) Exception messages and `ToString()` MUST NOT include request
  `Authorization`/`DPoP` values, request bodies or tokens. Response bodies appear only through
  `ResponseBody`.
  *Source:* [AG-29](../03-api-guidelines.md#security)

### Retry policy

- **XRC-37** (MUST) The only automatic resends are: (a) one DPoP-nonce retry, owned by the
  handler ([auth.md](auth.md)); (b) 429 retries per XRC-38; (c) one resend after session recovery,
  owned by the handler ([auth.md](auth.md)). There MUST NOT be retries on 5xx, timeouts or network
  errors, and the SDK MUST NOT add a resilience handler under its `HttpClient`s.
  *Source:* 0.8 CHANGELOG 0.7 "Removed" (#136): resilience handlers resend POSTs and spend DPoP proofs
- **XRC-38** (MUST) On 429 the invoker waits and resends up to `RateLimit.MaxRetries` times
  (default 3; 0 disables). The wait is `Retry-After` (delta-seconds or HTTP-date), else
  `RateLimit-Reset`, else exponential back-off of 1, 2, 4 … seconds capped at `MaxDelay` (default
  30 s), plus up to 10 % random jitter. A requested wait longer than `MaxDelay` is not honoured:
  the call fails at once with `XrpcRateLimitException` whose `RetryAfter` is the requested wait.
  Waiting uses the injected `TimeProvider`.
  *Source:* 0.8 `XrpcClient.cs:457-535`, `XrpcRateLimitOptions` · tests `XrpcClientRateLimitTests`
  (all), `AtProtoClient_BacksOffOnItsClock` (#187)
- **XRC-39** (MUST) Each resend calls `IXrpcAuthorizer.AuthorizeAsync` again on a fresh
  `HttpRequestMessage`, so DPoP proofs are never reused.
  *Source:* RFC 9449 §4.2 (`jti` uniqueness) · 0.8 behaviour
- **XRC-40** (MUST) A body is replayable when it is a JSON body (buffered once), no body, or a
  seekable stream; a seekable stream is rewound to the position it had when the call started, not
  to 0. A resend that would need a non-replayable body fails with `InvalidOperationException`
  whose inner exception is the `XrpcException` that triggered it. A session refresh triggered by
  that response still happens.
  *Source:* 0.8 `XrpcClient.cs:540-551` · tests `Upload_RetryRewindsToTheCallersPositionNotZero`,
  `Upload_OfANonSeekableStreamThatNeedsARetry_FailsClearly`,
  `ANonSeekableUploadRejectedAsExpired_RefreshesButIsNotResent`

### Timeouts and cancellation

- **XRC-41** (MUST) `XrpcCallOptions.Timeout` bounds the whole call (all attempts and waits, and
  the body read for JSON output). Expiry throws `TimeoutException`; for binary output the timeout
  covers response headers only.
  *Source:* 0.8 tests `CallOptions_Timeout_ThrowsTimeoutException`, `Download_*`
- **XRC-42** (MUST) Cancellation of the caller's token throws `OperationCanceledException`
  carrying that token, distinguishable from a timeout.
  *Source:* 0.8 test `CallerCancellation_StaysAnOperationCanceledException`
- **XRC-43** (MUST) Public call methods are `async`; argument validation failures surface through
  the returned task.
  *Source:* 0.8 test `UploadAsync_InvalidArguments_FaultTheTaskRatherThanThrowing`

### Pagination

- **XRC-44** (MUST) `Pagination.EnumerateAsync` requests the first page with a `null` cursor and
  yields every item of each page in order. It stops when a page's cursor is `null`, empty, or equal
  to **any** cursor already seen in this enumeration (catching multi-step cycles). Cancellation is
  checked between pages.
  *Source:* 0.8 #119 · tests `PaginationTests` (`…StopsOnARepeatedCursor`,
  `EnumerateAsync_Cancelled_StopsBetweenPages`)
- **XRC-45** (MUST) Generated `Enumerate{Method}Async` methods (one per cursor-paginated query,
  [AG-17](../03-api-guidelines.md#pagination-and-streams)) are implemented with
  `Pagination.EnumerateAsync` and `parameters with { Cursor = c }`; no other cursor loop exists in
  the SDK.
  *Source:* Decision

### Records API

- **XRC-46** (MUST) `client.Records<T>()` binds `RecordCollection<T>` to `T.Collection` (the
  static abstract member of `IAtProtoRecord`) and to `client.Account` as the repo (XRC-5 applies to
  every member). `client.Records<T>(repo)` returns a read-only reader for any repo.
  *Source:* 0.8 `RecordCollection.cs` · Decision: the collection comes only from `T.Collection`
  (0.8's `GetCollection<T>(nsid)` overload and interface-map reflection are dropped)
- **XRC-47** (MUST) Members: `CreateAsync(T, RecordKey? rkey, RecordWriteOptions?)`,
  `GetAsync(RecordKey)`, `FindAsync(RecordKey)`, `ExistsAsync(RecordKey)`,
  `PutAsync(RecordKey, T, RecordWriteOptions?)`, `DeleteAsync(RecordKey, RecordWriteOptions?)`,
  `ListAsync(ListRecordsOptions?)` (one page), `EnumerateAsync(…)`. `RecordWriteOptions` carries
  `SwapRecord` (`Cid?`), `SwapCommit` (`Cid?`) and `Validate` (`bool?`). Writes return a
  `RecordRef` (URI + CID); reads return `RecordView<T>` (URI, CID, value).
  *Source:* 0.8 `RecordCollection<T>`
- **XRC-48** (MUST) `FindAsync` returns `null` and `ExistsAsync` returns `false` **only** for the
  error `RecordNotFound`; every other failure propagates. `GetAsync` throws on `RecordNotFound`.
  *Source:* 0.8 #117 · `RecordCollectionTests`
- **XRC-49** (MUST) A record read from the repo whose value does not deserialise as `T` throws
  `XrpcResponseFormatException` naming the collection and the record URI; enumeration does not
  skip it silently.
  *Source:* 0.8 behaviour
- **XRC-50** (MUST) Swap semantics are explicit: `SwapRecord = null` sends no `swapRecord` (no
  guard); a `Cid` value guards the write; Lexicon-`nullable` `swapRecord` on `putRecord` is written
  as JSON `null` only when the caller asks to assert absence (`RecordWriteOptions.SwapRecordAbsent
  = true`). A failed guard surfaces as `XrpcException` with `Is("InvalidSwap")`.
  *Source:* Lexicon `com.atproto.repo.putRecord` (`swapRecord` is nullable) · 0.8
  `UpdateProfileAsync` swap loop
- **XRC-51** (MUST) A record type that implements `ICreatedAtStamped` (an `AtDatetime? CreatedAt`
  with an `init` accessor) and has `CreatedAt == null` gets the current time (from the client's
  `TimeProvider`, canonical form) on `CreateAsync` only; `PutAsync` never stamps. Generated records
  whose Lexicon requires `createdAt` declare it `required` and do not implement the interface.
  *Source:* 0.8 tests `CreateAsync_WithoutCreatedAt_StampsTheCurrentTime`,
  `PutAsync_OfARecordReadWithoutCreatedAt_DoesNotAddOne`, `AtProtoRecord_DeserializedWithoutCreatedAt_DoesNotFabricateOne`
- **XRC-52** (MUST) Records serialise exactly one `$type`, first, equal to `T.Collection`; a
  different incoming `$type` on read is not kept or echoed.
  *Source:* 0.8 #49 · tests `RecordTypeDiscriminatorTests`,
  `AtProtoRecord_TypeFromAnotherNsid_IsNotKeptOrEchoed` (generation rule in [lexicon.md](lexicon.md))

### PDS administration

- **XRC-53** (MUST) `PdsAdminClient` is a thin wrapper over two `AtProtoClient`s on the same
  service URL: one with a `PdsAdminHandler` (admin credentials) for `com.atproto.admin.*` and
  admin-gated server calls, one with an `AnonymousHandler` for `describeServer` and
  `createAccount`. It calls generated methods only.
  *Source:* Decision (0.8 `PdsAdminClient` ran two internal transports with raw NSID strings,
  `PdsAdminClient.cs:126-127,249-251`) · test `CreateAccountAsync_SendsSignupWithoutAdminCredentials`
- **XRC-54** (MUST) `PdsAdminHandler` supports two modes: `Basic` (HTTP Basic `admin:<password>`
  on every request) and `AdminAccount` (a `PasswordSession` of an admin account, signed in lazily
  once under concurrency). In `AdminAccount` mode, any `XrpcAuthenticationException` (401, or 400
  `ExpiredToken`/`InvalidToken`) on an admin call discards the admin session, signs in again and
  resends once.
  *Source:* 0.8 `PdsAdminClientAccountAuthTests` · **fixes** 0.8 `PdsAdminClient.cs:235`
  (`ShouldReauthenticate` only checks 401)
- **XRC-55** (MUST) Admin credentials MUST NOT be sent over plain HTTP unless the host is a
  loopback address or `PdsAdminOptions.AllowInsecureHttp` is set; the construction error names
  the opt-out.
  *Source:* 0.8 tests `Constructor_WithPlaintextHttpUrl_Throws`, `Constructor_WithLoopbackHttpUrl_IsAllowed`,
  `Constructor_PlaintextHostError_NamesTheOptOut`
- **XRC-56** (MUST) `CreateAccountAsync` mints a single-use invite code first when
  `describeServer` reports `inviteCodeRequired` and the caller supplied none; with an explicit
  code it skips `describeServer`.
  *Source:* 0.8 tests `CreateAccountAsync_WhenInviteRequired_MintsCodeThenCreatesAccount`,
  `CreateAccountAsync_WithExplicitInviteCode_SkipsDescribeServer`

### Diagnostics

- **XRC-57** (MUST) Each call creates an `Activity` named `xrpc {nsid}` from the
  `ATProtoNet.Xrpc` `ActivitySource` with tags `rpc.system = "atproto-xrpc"`, `rpc.method =
  {nsid}`, `server.address`, `http.response.status_code` and, on failure, `error.type =
  {Error}`; and records `atproto.xrpc.client.duration` (histogram, seconds) with NSID, status and
  outcome. Retries are recorded as events on the activity.
  *Source:* Decision ([02 — Architecture](../02-architecture.md#diagnostics); 0.8 has no
  `ActivitySource` or `Meter`)
- **XRC-58** (MUST NOT) Logs at `Information` or above MUST NOT be emitted per call; 429 waits
  log at `Debug` with NSID, attempt and delay.
  *Source:* [AG-31](../03-api-guidelines.md#logging-and-diagnostics)

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `XrpcInvoker.MaxErrorBodyBytes` | 64 KiB | error envelope read (XRC-34) |
| `XrpcInvokerOptions.MaxJsonResponseBytes` | 16 MiB default, configurable | JSON success bodies (XRC-26) |
| `XrpcRateLimitPolicy.MaxRetries` | 3 default, ≥ 0 | 429 resends (XRC-38) |
| `XrpcRateLimitPolicy.MaxDelay` | 30 s default, ≥ 0 | longest honoured 429 wait (XRC-38) |
| `XrpcRateLimitPolicy.JitterFraction` | 0.10 | added to every 429 wait |
| `XrpcInvoker.MaxResponseBodyTextChars` | 8,192 | `XrpcException.ResponseBody` text kept |
| Shared handler `PooledConnectionLifetime` | 5 min | SDK-owned `HttpClient` |
| Shared handler `ConnectTimeout` | 10 s | SDK-owned `HttpClient` |

## Errors

| Failure | Raised as |
|---|---|
| Non-2xx response | per XRC-33 |
| Success body unusable (empty, `null`, wrong shape, wrong media type, too large) | `XrpcResponseFormatException` |
| Call needs an account, handler has none | `XrpcAuthenticationException` (`AuthenticationRequired`, `StatusCode = null`) |
| Reserved header in call options; invalid service URL | `ArgumentException` |
| Resend needs a non-replayable body | `InvalidOperationException` (inner: the triggering `XrpcException`) |
| Per-call timeout | `TimeoutException` |
| Caller cancellation | `OperationCanceledException` |
| Network failure | `HttpRequestException` (unwrapped) |
| Record value does not match `T` | `XrpcResponseFormatException` |

## Conformance and tests

- **Conformance:** the golden XRPC wire fixtures ([04 — Testing](../04-testing.md#conformance-areas))
  extracted from 0.8 `EndpointRequestTests` (≈147 rows: NSID, verb, exact query string or body,
  `atproto-proxy`) and `WireFidelityTests` run against generated clients via `FakeXrpcServer`.
- **Port as scenarios:** `XrpcTransportTests`, `XrpcClientRateLimitTests`, `XrpcClientRepoRevTests`,
  `XrpcExceptionTests`, `XrpcParamsTests`, `BoundedContentTests`, `PaginationTests`,
  `EmptyResponseBodyTests` (now driven by every no-output descriptor instead of a hand list),
  `RecordCollectionTests`, `PdsAdminClientTests`, `PdsAdminClientAccountAuthTests` (plus a new
  case for 400 `ExpiredToken`), `AtProtoHttpTests`.
- **Regression corpus** ([04 — Testing](../04-testing.md#regression-corpus)): no-output procedure
  accepts an empty 200 (#69); no-argument procedure sends `{}` and arrays repeat their key (#123);
  understated `Content-Length` still capped; pagination stops on a repeated cursor (#119);
  `Retry-After` beyond `MaxDelay` throws at once.
- **Drop:** `ServiceProxyTests` (trivial), `AtProtoClientConstructionTests`, `CustomXrpcTests`'
  forwarding cases (no forwarding layer exists).

## Changes from 0.8

| Change | Reason |
|---|---|
| No `LoginAsync`/`LogoutAsync`/`SetServiceUrl`/`SetProxy`/`SetLabelers` on the client; views via `With` | D-06; immutable client, no "logged out" state |
| One call path over descriptors; no internal string-NSID transport, no `IXrpcTransport` forwarders | XRC-3; built-in clients become fakeable and NSIDs typed |
| Descriptor default proxy wins over the view's `Proxy`; overrides go through `ProxyOverrides` | per-call views replace 0.8's per-call options; chat must stay on the chat service under a client-wide AppView proxy |
| Direct (session-management) calls never carry proxy or labelers, even when a view sets them | 0.8 sent an *explicit* per-call proxy on direct calls (`DirectCall_WithAnExplicitProxy_StillSendsIt`); the new model has no separate per-call layer, and proxying session calls is never correct |
| JSON success bodies capped; non-JSON `Content-Type` rejected | D-20; hostile-input principle |
| `LatestRepoRev` per account session, not per client; rate-limit info per call | 0.8 values leaked across accounts and survived sign-out |
| No-session precondition has `StatusCode = null` | it is not a server response |
| `XrpcException.ResponseHeaders` read-only | 0.8's mutable `Headers` doubled as server output headers |
| `PdsAdminClient` re-authenticates on 400 `ExpiredToken` | bug fix (XRC-54) |
| Collection only from `T.Collection`; `AtProtoRecord` base dropped | one record pattern ([lexicon.md](lexicon.md)) |
| Case-insensitive property reads and numbers-from-strings dropped | D-20 |

## Open points

1. **`MaxJsonResponseBytes` default.** Proposed 16 MiB; `com.atproto.repo.listRecords` with
   `limit=100` of maximum-size records could exceed it. Default: keep 16 MiB, document raising it
   per invoker.
2. **Content-type strictness (XRC-27).** Some servers omit or mislabel `Content-Type`. Default:
   reject only when present and not JSON; revisit if a mainstream PDS or AppView trips it in
   integration tests.
3. **Response observer vs. return-with-metadata.** Default: observer on call options (XRC-31);
   add `XrpcResult<T>` variants only if a real use case needs metadata inline.
4. **`atproto-content-labelers` parsing.** The response header lists labelers that were applied;
   default: parse into `LabelerRef` list, ignore malformed entries.
5. **`ICreatedAtStamped` (XRC-51).** It keeps a 0.8 convenience for hand-written records. Default:
   keep; drop if phase 5 review finds no user of it outside samples.
