# XRPC server and ASP.NET Core — spec

**Status:** Draft · **Package:** `ATProtoNet.AspNetCore` (replaces 0.8's `ATProtoNet.Server`;
depends on `ATProtoNet` and the ASP.NET Core shared framework; namespaces `ATProtoNet.AspNetCore`,
`.Xrpc`, `.Authentication`, `.OAuth`, `.Labels`, `.Tap`, `.Spaces`) · **Phase:** 9 (Spaces server roles
included) ·
**Normative references:** [XRPC](https://atproto.com/specs/xrpc),
[Event Stream](https://atproto.com/specs/event-stream),
[OAuth](https://atproto.com/specs/oauth), [Labels](https://atproto.com/specs/label),
[RFC 6265bis](https://datatracker.ietf.org/doc/draft-ietf-httpbis-rfc6265bis/) (cookie prefixes),
[RFC 7617](https://www.rfc-editor.org/rfc/rfc7617) (HTTP Basic), Spaces server roles per [spaces.md](spaces.md)

## Scope

In scope:

- serving `/xrpc/{nsid}` queries, procedures and **subscriptions** (WebSocket event streams),
  built on the generated method descriptors ([specs/lexicon.md](lexicon.md));
- the service-auth **authentication scheme** and the principal types it produces;
- the **OAuth web login** (browser endpoints, CSRF binding, cookie sign-in, client metadata);
- **labeler endpoints** (`com.atproto.label.queryLabels`, `com.atproto.label.subscribeLabels`);
- the **Tap webhook** receiver;
- the **Spaces server roles** (namespace `ATProtoNet.AspNetCore.Spaces`): space authority,
  space repo host, `simplespace` and the write-notification receiver, plus the space-credential
  authentication scheme they share with applications' own endpoints. They are regular features of
  this package, registered and mapped like the ones above (SRV-65…SRV-70).

Out of scope, specified elsewhere:

- the DI builder, client factory, options, named HTTP clients, health checks and stream
  workers: [hosting.md](hosting.md);
- service-auth token verification rules (claims, times, keys, replay order): [auth.md](auth.md);
  this spec covers how the scheme applies them to endpoints;
- OAuth protocol rules (PAR, PKCE, DPoP, issuer binding, token validation): [auth.md](auth.md);
  this spec covers the browser flow around `OAuthClient`;
- label signing and the label frame codec: [labels.md](labels.md);
- Tap event parsing and the acknowledgement rule: [streaming.md](streaming.md);
- store contracts: [stores.md](stores.md);
- the Spaces protocol rules those roles apply (token and credential verification, policy,
  MACs, notification sequencing and retries): [spaces.md](spaces.md); this spec covers how they
  are hosted.

## Public API sketch

```csharp
// ── Registration (AG-25) ──────────────────────────────────────────────
builder.Services.AddAtProto(o => o.ServiceUrl = new Uri("https://pds.example"))
    .AddXrpcEndpoints(x => x
        .Add<GetStatusHandler>()
        .AddFromAssembly(typeof(Program).Assembly))
    .AddOAuthLogin(o => { o.ClientMetadata = metadata; o.ServeClientMetadata = true; })
    .AddLabeler<MyLabelSource>(o => o.LabelerDid = labelerDid)
    .AddTapWebhook<MyTapHandler>(o => o.AdminPassword = config["Tap:AdminPassword"])
    .AddSpaces(o => o.ServiceDid = serviceDid)                  // Spaces roles (SRV-65), spaces.md
    .AddSpaceAuthority(o => o.SigningKey = authorityKey)
    .AddSimpleSpace()
    .AddSpaceRepoHost<MySpaceRepoStorage>()
    .AddSpaceNotifications<MyNotificationHandler>();

builder.Services.AddAuthentication()
    .AddAtProtoServiceAuth(o => o.Audiences.Add(XrpcServiceRef.Parse("did:web:feed.example#bsky_fg")))
    .AddAtProtoSpaceCredentials();                              // SRV-67

app.MapAtProtoXrpc().RequireServiceAuth();      // returns the /xrpc RouteGroupBuilder
app.MapAtProtoOAuth();                          // {prefix}/login, /callback, /logout, client metadata
app.MapAtProtoTapWebhook("/tap/webhook");

// ── Handlers: one interface per Lexicon method kind, keyed by the generated descriptor ──
public interface IXrpcQueryHandler<TParameters, TOutput>
{
    static abstract XrpcQuery<TParameters, TOutput> Method { get; }
    ValueTask<TOutput> HandleAsync(TParameters parameters, XrpcContext context);
}

public interface IXrpcProcedureHandler<TInput, TOutput>
{
    static abstract XrpcProcedure<TInput, TOutput> Method { get; }
    ValueTask<TOutput> HandleAsync(TInput input, XrpcContext context);
}

public interface IXrpcProcedureHandler<TInput>                // procedure without output
{
    static abstract XrpcProcedure<TInput> Method { get; }
    ValueTask HandleAsync(TInput input, XrpcContext context);
}

public interface IXrpcSubscriptionHandler<TParameters, TMessage>
{
    static abstract XrpcSubscription<TParameters, TMessage> Method { get; }
    IAsyncEnumerable<TMessage> SubscribeAsync(TParameters parameters, XrpcContext context);
}

// "No parameters", "no input" and binary bodies are type arguments, not more interfaces. They are
// the same types the client descriptors use (xrpc-client.md, lexicon.md LEX-60):
//   XrpcEmpty                       parameters or input of a method that declares none
//   XrpcBinaryContent               binary input (request stream, Content-Type, length) and binary
//                                   output (written, then disposed)
// A procedure without output uses the XrpcProcedure<TInput> descriptor and
// IXrpcProcedureHandler<TInput>, whose HandleAsync returns ValueTask.

public sealed class GetFeedSkeletonHandler(IFeedStore feeds)
    : IXrpcQueryHandler<GetFeedSkeleton.Parameters, GetFeedSkeleton.Output>
{
    public static XrpcQuery<GetFeedSkeleton.Parameters, GetFeedSkeleton.Output> Method => GetFeedSkeleton.Descriptor;

    public async ValueTask<GetFeedSkeleton.Output> HandleAsync(GetFeedSkeleton.Parameters p, XrpcContext context) =>
        await feeds.PageAsync(p.Feed, p.Limit, p.Cursor, context.RequestAborted);
}

public sealed class XrpcContext
{
    public Nsid Method { get; }
    public HttpContext HttpContext { get; }              // escape hatch; handlers should not need it
    public CancellationToken RequestAborted { get; }
    public ServiceAuthCaller? Caller { get; }            // set when service auth authenticated the request
    public AtProtoUser? User { get; }                    // set when the OAuth login authenticated the request
    public SpaceCaller? SpaceCaller { get; }             // set when a space credential authenticated the request
    public IHeaderDictionary ResponseHeaders { get; }
}

// ── Principals ────────────────────────────────────────────────────────
public sealed record ServiceAuthCaller(Did Issuer, XrpcServiceRef Audience, Nsid? Method, string TokenId);
public sealed record AtProtoUser(Did Did, Handle? Handle);
public sealed record SpaceCaller(SpaceUri Space, Did? Subject, string? ClientId, Did Audience, string CredentialId);
public static class AtProtoPrincipalExtensions
{
    public static ServiceAuthCaller? GetServiceAuthCaller(this ClaimsPrincipal principal);
    public static AtProtoUser? GetAtProtoUser(this ClaimsPrincipal principal);   // OAuth-login identities only
    public static SpaceCaller? GetSpaceCaller(this ClaimsPrincipal principal);
}

// ── Labeler and Tap seams ─────────────────────────────────────────────
public interface ILabelSource
{
    ValueTask<LabelPage> QueryAsync(LabelQuery query, CancellationToken cancellationToken);
    IAsyncEnumerable<LabelBatch> SubscribeAsync(long? cursor, CancellationToken cancellationToken);
    ValueTask<long?> GetHeadSequenceAsync(CancellationToken cancellationToken);
}

public interface ITapEventHandler
{
    ValueTask HandleAsync(TapEvent tapEvent, CancellationToken cancellationToken);
}
```

The handler interfaces live in this package. The generator MAY additionally emit a nested
`{Method}.IHandler` convenience interface for users' Lexicons when the project references
`ATProtoNet.AspNetCore` (see Open points). First-party generated code never references this
package.

## Requirements

### Routing

- **SRV-1** (MUST) `MapAtProtoXrpc()` maps one route group at `/xrpc` and, inside it, one route
  per registered handler at `/{nsid}` restricted to the handler's HTTP method (query and
  subscription: `GET`; procedure: `POST`), followed by a fallback route for `/{nsid}`.
  *Source:* 0.8 `XrpcEndpointExtensions.cs:110-118` · test `XrpcEndpointTests.QueryWithParams_ReturnsCorrectResponse`
- **SRV-2** (MUST) The fallback does not shadow routes the application maps under `/xrpc`
  itself. *Source:* 0.8 · test `XrpcRouteGroupTests.Fallback_DoesNotShadowRoutesTheAppMapsUnderXrpc`
- **SRV-3** (MUST) A path with more than one segment below `/xrpc`, or outside `/xrpc`, is not
  handled by the SDK and falls through to the application's 404.
  *Source:* 0.8 · test `PathOutsideXrpcNsidRoutes_IsLeftTo404`
- **SRV-4** (MUST) When the route group requires authorization, the fallback challenges before
  answering, so an unauthenticated caller cannot enumerate which NSIDs are served.
  *Source:* 0.8 · tests `Fallback_GroupRequiresAuthorization_ChallengesBeforeAnsweringUnknownNsid`,
  `RequireServiceAuthOnGroup_UnmatchedNsid_GetsTheChallengeNotAMethodList`
- **SRV-5** (MUST) `MapAtProtoXrpc()` throws `InvalidOperationException` when no handler is
  registered. *Source:* 0.8 · test `MapXrpcEndpoints_NothingRegistered_Throws`
- **SRV-6** (MUST) `MapAtProtoXrpc()` returns the `RouteGroupBuilder`, so conventions
  (`RequireAuthorization`, `RequireRateLimiting`, `RequireServiceAuth`, metadata) apply to every
  XRPC endpoint. *Source:* 0.8 · tests `MapXrpcEndpoints_RequireAuthorization_AppliesToEveryEndpoint`,
  `MapXrpcEndpoints_GroupMetadata_ReachesEveryEndpoint`

### Error envelope and status mapping

- **SRV-7** (MUST) Every error the SDK writes for an XRPC endpoint, including the fallback, the
  service-auth scheme, the Tap webhook and non-redirect OAuth login errors, has the body
  `{"error": <name>, "message": <text>}` with `Content-Type: application/json`, and resets any
  `Content-Length` set before the failure. *Source:* [XRPC spec](https://atproto.com/specs/xrpc) ·
  0.8 `XrpcErrorResponse.cs:116-133` · **Decision** to extend it to Tap and OAuth (0.8 Tap
  answers `{error}` only and the login answers plain text)
- **SRV-8** (MUST) The SDK maps failures as follows:

  | Condition | Status | `error` | Notes |
  |---|---|---|---|
  | Path segment is not a valid NSID | 400 | `InvalidRequest` | |
  | Registered NSID, wrong HTTP method | 405 | `InvalidRequest` | `Allow` header names the method |
  | NSID nothing serves | 501 | `MethodNotImplemented` | message `Method Not Implemented` |
  | Missing, repeated-scalar or invalid parameter | 400 | `InvalidRequest` | message names the parameter |
  | Missing or wrong `Content-Type`, missing required body | 400 | `InvalidRequest` | message names the expected type |
  | Malformed or mistyped JSON body | 400 | `InvalidRequest` | `Invalid request body at $.path`, no CLR type names |
  | Body over the limit | 413 | `PayloadTooLarge` | from `BadHttpRequestException` 413 |
  | Other `BadHttpRequestException` | its status | `InvalidRequest` | |
  | Handler throws `XrpcException` | its status | its error | message passed through |
  | No or invalid service-auth credentials | 401 | verifier's error name | `WWW-Authenticate: Bearer` |
  | Authorization policy not met | 403 | `Forbidden` | |
  | Unhandled exception, or `null` returned for a method with output | 500 | `InternalServerError` | message `Internal Server Error` |

  *Source:* 0.8 `XrpcErrorResponse.cs:34-99`, `AtProtoServiceAuthentication.cs:171-197` · tests
  `UnknownNsid_ReturnsMethodNotImplemented`, `QueryEndpoint_PostMethod_Returns405WithAllowAndEnvelope`,
  `PathSegmentThatIsNotAnNsid_ReturnsInvalidRequest`, `Procedure_WrongFieldType_ReturnsInvalidRequestNamingTheField`,
  `BadHttpRequest413_ReturnsPayloadTooLarge`, `HandlerReturnsNull_ReturnsInternalServerError`
- **SRV-9** (MUST NOT) The message of an unhandled exception never reaches the caller. It is
  logged at `Error` with the exception. An `XrpcException` thrown by a handler is not logged as a
  failure. *Source:* 0.8 · tests `HandlerThrowsUnknownException_ReturnsInternalServerErrorWithoutItsMessage`,
  `HandlerThrowsUnknownException_IsLoggedWithTheException`, `HandlerThrowsXrpcException_IsNotLoggedAsAFailure`
- **SRV-10** (MUST) For a handler's `XrpcException`, the SDK copies only an allow-list of
  response headers from the exception (`Retry-After`, `RateLimit-Limit`, `RateLimit-Remaining`,
  `RateLimit-Reset`, `WWW-Authenticate`). Other headers are set through
  `XrpcContext.ResponseHeaders`. *Source:* **Decision**: 0.8 copies every header of the exception
  (`XrpcErrorResponse.cs:81-85`), so a handler rethrowing a client-side `XrpcException` from an
  upstream call leaks the upstream's response headers.
- **SRV-11** (MUST) If the client aborted the request, nothing is written.
  *Source:* 0.8 `XrpcErrorResponse.cs:99`
- **SRV-12** (MUST) If the response has already started (a binary output failing half-way), the
  exception propagates so the server aborts the response instead of appending an envelope to a
  truncated body. *Source:* 0.8 `XrpcErrorResponse.cs:34` · test `BlobQuery_HandlerThrows_ReturnsJsonEnvelope`
  (the not-yet-started case)
- **SRV-13** (SHOULD) Rejections by ASP.NET Core rate limiting on XRPC endpoints are written as
  429 `RateLimitExceeded` envelopes with `Retry-After` when the limiter provides one; the SDK
  provides an `OnRejected` helper for this. *Source:* **Decision**: XRPC clients branch on the
  envelope.

### Parameters, bodies and content types

- **SRV-14** (MUST) Query parameters bind to the descriptor's `Parameters` type using
  parameter metadata generated from the Lexicon (name, type, format, required, array), with no
  reflection. *Source:* **Decision** (G6); 0.8 binds by reflection (`XrpcQueryBinder.cs`)
- **SRV-15** (MUST) Binding rules: a repeated key binds a list in order; `name[]` is accepted
  for array parameters and combines with plain `name`; a single value binds a one-element list;
  a repeated scalar is rejected (400); `name[]` for a scalar is ignored; booleans bind from
  `true`/`false`; integers bind from invariant text; identifier formats bind through their
  `Parse` and a failure names the parameter without CLR type names; unknown parameters are
  ignored; a missing required parameter is a 400 naming it; values round-trip verbatim
  (no JSON escaping artefacts). *Source:* 0.8 `XrpcQueryBinder.cs:73-139` · tests
  `XrpcQueryBindingTests` (all 21 cases), CHANGELOG 0.7
- **SRV-16** (MUST) Bound parameters are validated against the Lexicon constraints the
  descriptor carries (integer `minimum`/`maximum`, string `maxLength`/`minLength`/`maxGraphemes`,
  `enum`, array `maxLength`), with a 400 naming the parameter. *Source:* **Decision**:
  0.8 checks types only; Lexicon-declared bounds are part of the method contract.
- **SRV-17** (MUST) A procedure with JSON input requires a JSON content type
  (`application/json`, parameters allowed) and a body; a missing or other type, or a missing
  body (`Request body is required.`), is a 400. *Source:* 0.8 `XrpcEndpointRegistration.cs:216-239` ·
  tests `Procedure_NoBody_ReturnsInvalidRequest`, `Procedure_NonJsonContentType_ReturnsInvalidRequest`,
  `Procedure_MalformedJson_ReturnsInvalidRequest`
- **SRV-18** (MUST) A procedure without input accepts a request with no body and ignores any
  body sent. *Source:* 0.8 · tests `ProcedureWithoutInput_NoBody_ReturnsOutput`, `ProcedureVoidWithoutInput_NoBody_Returns200WithNoBody`
- **SRV-19** (MUST) A procedure with binary input (`XrpcBinaryContent`) requires a `Content-Type`,
  receives the request body as an unbuffered stream with the declared content type and length,
  and checks the content type against the Lexicon's `encoding` (exact type or `*/*`).
  *Source:* 0.8 `XrpcEndpointRegistration.cs:203-206` · tests `BlobProcedure_PassesBodyContentTypeAndLength`,
  `BlobProcedure_NoContentType_ReturnsInvalidRequest` · encoding check is **Decision**
- **SRV-20** (MUST) JSON input bodies are read with the source-generated contracts of the
  descriptor, bounded by the endpoint's request size limit (default
  `XrpcServerLimits.DefaultJsonInputBytes`, overridable with `[RequestSizeLimit]`).
  *Source:* **Decision** (G6, principle 2)
- **SRV-21** (SHOULD) JSON input bodies are validated against the Lexicon schema
  ([LEX validation](lexicon.md)) before the handler runs; failures are 400 `InvalidRequest` with
  the JSON path. *Source:* **Decision** (upstream servers validate inputs); see Open points for
  the per-endpoint opt-out.
- **SRV-22** (MUST) JSON output is written with the descriptor's source-generated contracts;
  Lexicon union values (C# 15 `union` types) serialize exactly as on the client, through the
  generated union contracts ([lexicon](lexicon.md)). A method with no output
  answers 200 with an empty body. A binary output streams the body with its `Content-Type` and,
  when known, `Content-Length`, and the stream is disposed afterwards. *Source:* 0.8
  `XrpcEndpointRegistration.cs:249-250` · tests `ProcedureVoid_Returns200WithNoBody`,
  `BlobQuery_StreamsBodyWithContentTypeAndLength`

### Registration

- **SRV-23** (MUST) A handler type implements exactly one handler interface; registering one that
  implements several throws. *Source:* 0.8 · test `AddXrpcEndpoint_HandlerImplementingTwoShapes_Throws`
- **SRV-24** (MUST) Two handlers for the same NSID throw at registration, comparing NSIDs
  case-insensitively, because ASP.NET Core routes match case-insensitively. Registering the same
  handler type twice is a no-op. *Source:* 0.8 `XrpcEndpointRegistration.cs:31` · tests
  `AddXrpcEndpoint_SecondHandlerForSameNsid_Throws`, `AddXrpcEndpoint_NsidDifferingOnlyInCase_ThrowsLikeAnyConflict`,
  `AddXrpcEndpoint_SameHandlerTwice_RegistersOnce`
- **SRV-25** (MUST) The NSID, HTTP method and encodings of an endpoint come from the handler's
  static `Method` descriptor; there is no hand-typed NSID. *Source:* **Decision** (one definition
  of a method shared by client and server, [02 — Architecture](../02-architecture.md#6-hosting-atprotonethosting-atprotonetaspnetcore))
- **SRV-26** (MUST) `AddFromAssembly` registers exactly what individual `Add<T>` calls would,
  skipping open generic types, and is AOT-safe through a source-generated registration list
  (no runtime assembly scanning in AOT builds). *Source:* 0.8 · tests
  `AddXrpcEndpointsFromAssembly_RegistersWhatAddXrpcEndpointRegisters`, `AddXrpcEndpointsFromAssembly_SkipsOpenGenericHandlers` ·
  AOT path is **Decision**
- **SRV-27** (MUST) Handlers are resolved from the request's service scope (scoped lifetime by
  default). *Source:* 0.8
- **SRV-28** (MUST) Attributes on the handler class (`[Authorize]`, `[AllowAnonymous]`,
  `[EnableRateLimiting]`, `[DisableRateLimiting]`, `[RequestSizeLimit]`, `[RequireServiceAuth]`)
  become endpoint metadata, and a handler's `[AllowAnonymous]` overrides group authorization.
  *Source:* 0.8 `XrpcEndpointRegistration.cs:112-114` · tests
  `MapXrpcEndpoints_AllowAnonymousOnHandler_OverridesGroupAuthorization`, `AuthorizeOnHandler_IsHonouredWithoutAGroupConvention`
- **SRV-29** (MUST) Every XRPC endpoint carries `XrpcMethodMetadata(nsid)`. Non-XRPC endpoints
  may add it explicitly to bind service-auth tokens to a method.
  *Source:* 0.8 · tests `MapXrpcEndpoints_AttachesTheMethodToEveryEndpoint`, `NonXrpcEndpointWithMethodMetadata_BindsTheTokenToIt`

### Subscriptions (new)

- **SRV-30** (MUST) A subscription endpoint accepts a WebSocket upgrade on `GET /xrpc/{nsid}`,
  binds query parameters as for queries, and then writes one binary WebSocket message per frame:
  a DRISL-CBOR header `{op: 1, t: "#<def>"}` followed by the DRISL-CBOR body, where `t` is the
  short name of the active case of the subscription's message union, as the descriptor maps it.
  *Source:* [Event Stream spec](https://atproto.com/specs/event-stream) · **Decision** (0.8 cannot
  serve subscriptions; `docs/labeler.md:384` hand-writes `subscribeLabels`)
- **SRV-31** (MUST) A `GET` without a WebSocket upgrade on a subscription endpoint is a 400
  `InvalidRequest` envelope. *Source:* **Decision**
- **SRV-32** (MUST) Parameter and authorization failures detected before the upgrade are HTTP
  envelopes (SRV-8). A failure after the upgrade (an `XrpcException` from the handler, for example
  `FutureCursor`) is sent as an error frame `{op: -1}` followed by `{error, message}`, and then the
  socket is closed. An unhandled exception sends `{op: -1}` + `{error: "InternalServerError"}`
  without the exception message and closes. *Source:* [Event Stream spec](https://atproto.com/specs/event-stream) · SRV-9
- **SRV-33** (MUST) The server bounds the frames queued for one subscriber
  (`XrpcServerLimits.SubscriptionSendQueueFrames`). A subscriber that falls behind gets an error
  frame `ConsumerTooSlow` and is disconnected; it never blocks the handler of another subscriber.
  *Source:* upstream relay/PDS behaviour (`ConsumerTooSlow`) · 0.8 #120 (one hostile subscriber
  must not stop the others)
- **SRV-34** (MUST) A client close or `RequestAborted` cancels the handler's enumeration; the
  handler's `DisposeAsync` runs. *Source:* **Decision**
- **SRV-35** (SHOULD) The server sends WebSocket pings at `XrpcServerLimits.SubscriptionPingInterval`
  and closes connections that miss pongs for twice that interval. *Source:* **Decision**

### Service-auth authentication scheme

- **SRV-36** (MUST) `AddAtProtoServiceAuth` registers an authentication scheme on
  `AuthenticationBuilder` that verifies `Authorization: Bearer <jwt>` with one
  `ServiceAuthVerifier` instance built from validated options at startup (0.8 builds one per
  request, `AtProtoServiceAuthentication.cs:115`). The verification rules are AUT's.
  *Source:* 0.8 · [auth.md](auth.md)
- **SRV-37** (MUST) The scheme verifies, and therefore spends the token's `jti`, only on
  endpoints whose authorization requires it: endpoints whose policy names the scheme, or any
  `[Authorize]` endpoint when it is the default scheme. Anonymous endpoints, endpoints without
  authorization, and `[AllowAnonymous]` handlers in a protected group leave the token unspent.
  *Source:* 0.8 `AtProtoServiceAuthentication.cs:98-112` · tests `OnlyScheme_AnonymousEndpoint_LeavesTheTokenUnspent`,
  `OnlyScheme_EndpointWithoutAuthorization_LeavesTheTokenUnspent`, `RequireServiceAuthOnGroup_AllowAnonymousHandler_LeavesTheTokenUnspent`,
  `OnlyScheme_PlainAuthorize_AuthenticatesWithServiceAuth`
- **SRV-38** (MUST) On an endpoint carrying `XrpcMethodMetadata`, the token's `lxm` is required
  and must equal that NSID exactly. A non-XRPC endpoint without method metadata refuses service
  auth without spending the token. *Source:* 0.8 · tests
  `RequireServiceAuthOnHandler_TokenForAnotherMethod_IsRefusedWithTheXrpcEnvelope`,
  `NonXrpcEndpoint_RefusesServiceAuthWithoutSpendingTheToken`
- **SRV-39** (MUST) Challenges are 401 envelopes with `WWW-Authenticate: Bearer`; forbids are
  403 `Forbidden` envelopes. *Source:* 0.8 `AtProtoServiceAuthentication.cs:171-197` · tests
  `RequireServiceAuthOnHandler_NoToken_IsAskedToAuthenticate`, `AuthorizationPolicyNotMet_IsForbiddenWithTheXrpcEnvelope`
- **SRV-40** (MUST) Configured audiences are validated at host start: none configured, or a
  malformed one, fails startup. A bare DID audience accepts tokens addressed to the DID with or
  without a service fragment only when both forms are added. *Source:* 0.8 · tests
  `AddAtProtoServiceAuth_NoAudiences_FailsTheHostStart`, `AddAtProtoServiceAuth_MalformedAudience_FailsTheHostStart`,
  `Audiences_WithTheBareDidAdded_AcceptsBothForms`
- **SRV-41** (MUST) The scheme uses the registered `IReplayStore`. When none is chosen, the
  in-memory store is used and a startup warning is logged (HST's per-feature warning); the
  fallback is never silent. *Source:* 0.8 · tests `AddAtProtoServiceAuth_KeepsAReplayStoreAlreadyRegistered`,
  `InMemoryDefaultsWarningTests.StartAsync_WithTheDefaultReplayStore_Warns`
- **SRV-42** (MUST) A successful verification produces a principal whose identity has
  authentication type `AtProtoServiceAuth` and claim types distinct from the OAuth login's
  (`atproto:service:iss`, `atproto:service:aud`, `atproto:service:lxm`, `atproto:service:jti`);
  `GetServiceAuthCaller()` returns its `ServiceAuthCaller`; `GetAtProtoUser()` returns `null`.
  *Source:* **Decision**: 0.8 uses one `did` claim for both and sniffs the authentication type
  (`OAuthUser.DidOf`), the root of the 0.7 security fix "a service auth caller could drive an
  account's stored OAuth session".
- **SRV-43** (MUST) `[RequireServiceAuth]` and `RequireServiceAuth()` name the scheme and
  apply to a handler or a group respectively. *Source:* 0.8 · tests `RequireServiceAuthAttribute_NamesTheScheme`,
  `RequireServiceAuthOnGroup_AppliesToEveryHandlerButAllowAnonymous`

### Spaces server roles

Spaces is a core data protocol, so serving it is an ordinary feature of this package: the roles
are XRPC handlers over the generated `com.atproto.space.*`/`simplespace.*` descriptors, with
their protocol behaviour in [spaces.md](spaces.md).

- **SRV-65** (MUST) The Spaces roles register on `AtProtoBuilder` like every other feature
  (AG-25): `AddSpaces(…)` (shared options: the service DID, the space-credential scheme, the
  stores), `AddSpaceAuthority(…)`, `AddSimpleSpace()`, `AddSpaceRepoHost<TStorage>()` and
  `AddSpaceNotifications<THandler>()`. `MapAtProtoXrpc()` serves every registered Spaces method
  with the others; there is no separate mapping call and no `[Experimental]` attribute (unless the
  fallback in the [spaces.md](spaces.md) header applies).
  *Source:* **Decision** (Spaces is core; maintainer review 2026-10-17) · 0.8 `AddAtProtoSpaces`,
  `AddSpaceRepoHost<T>` (marked experimental)
- **SRV-66** (MUST) The roles are built only on this spec's public endpoint API (handler
  interfaces, `XrpcContext`, the error envelope, the authentication schemes). Routing, binding,
  error mapping and the service-auth scheme contain no Spaces-specific branches; an architecture
  test checks that only `ATProtoNet.AspNetCore.Spaces` references `ATProtoNet.Spaces.*`.
  *Source:* **Decision** · audit (0.8 central hosting code knew the Spaces stores,
  `InMemoryDefaultsWarning.cs:42-66`)
- **SRV-67** (MUST) `AddAtProtoSpaceCredentials` registers an authentication scheme on
  `AuthenticationBuilder`, parallel to the service-auth scheme. It verifies
  `Authorization: Atproto-Space …` with the RFC 9421 signature and `Atproto-Space-Audience` per
  [spaces.md](spaces.md) (SPC-24, SPC-44, revocation per SPC-48), using one verifier built at
  startup. It follows SRV-37 (verifies only where authorization requires it) and SRV-39
  (challenges are 401 envelopes, here with `WWW-Authenticate: Atproto-Space`).
  *Source:* **Decision** (space credentials are a first-class mechanism, [auth.md](auth.md) AUT-75) ·
  0.8 `SpaceCredentialVerifier.cs` (verification inside the repo-host endpoints only)
- **SRV-68** (MUST) A verified credential produces a principal with authentication type
  `AtProtoSpaceCredential` and its own claim types (`atproto:space:space`, `atproto:space:sub`,
  `atproto:space:client`, `atproto:space:aud`, `atproto:space:jti`). `GetSpaceCaller()` returns
  its `SpaceCaller`; `GetServiceAuthCaller()` and `GetAtProtoUser()` return `null` for it (the
  separation SRV-42 makes for service auth). Applications may protect their own endpoints that
  serve space data with `[RequireSpaceCredential]` / `RequireSpaceCredential()`.
  *Source:* **Decision** (no claim sharing between schemes, SRV-42)
- **SRV-69** (MUST) The write-notification receiver (`notifyWrite`, `notifySpaceDeleted` at a
  syncer) uses the service-auth scheme with the audience the application registered, validates
  input before authentication work, and dispatches to `ISpaceNotificationHandler`
  ([spaces.md](spaces.md), SPC-40…SPC-42).
  *Source:* audit (0.8 could register for notifications but shipped no receiver)
- **SRV-70** (MUST) The Spaces roles use the stores selected on the builder ([stores.md](stores.md));
  when a role falls back to an in-memory store, the per-feature startup warning of
  [hosting.md](hosting.md) names it, as for every other feature.
  *Source:* 0.8 `InMemoryDefaultsWarning.cs` (Spaces stores) · HST per-feature warning

### OAuth web login

- **SRV-44** (MUST) `MapAtProtoOAuth()` maps, under `OAuthLoginOptions.RoutePrefix` (default
  `/atproto`, must be a local path): `GET {prefix}/login` (parameter `identifier`: handle, DID or
  server URL; optional `returnUrl`), `GET {prefix}/callback`, and `POST {prefix}/logout`.
  *Source:* 0.8 `AtProtoOAuthExtensions.cs:208-303`
- **SRV-45** (MUST) Login binds the authorization to the browser: it sets an `HttpOnly`,
  `SameSite=Lax` binding cookie (`__Host-atproto-oauth-binding` over HTTPS,
  `atproto-oauth-binding` otherwise) holding a random value, stores only its SHA-256 hash in the
  pending authorization's app state, and the callback compares hashes with
  `CryptographicOperations.FixedTimeEquals`. *Source:* 0.8 `OAuthLoginBinding.cs:71-141` · tests
  `Callback_FromTheBrowserThatStarted_SignsInAndReturnsTheLocalUrl`, `Callback_WithAnotherLoginsCookie_IsRefused`
- **SRV-46** (MUST) A callback whose binding is missing or does not match stores nothing,
  revokes the tokens it obtained (best effort), and redirects with an error code.
  *Source:* 0.8 · test `Callback_FromAnotherBrowser_IsRefusedStoresNothingAndRevokes`
- **SRV-47** (MUST) `returnUrl` is honoured only if it is local by `IUrlHelper.IsLocalUrl`
  rules, both when the login starts and when the stored value is read back at the callback;
  otherwise the configured default is used. *Source:* 0.8 · tests
  `StartLoginAsync_AReturnUrlThatIsNotLocal_IsIgnored`, `Callback_AStoredReturnUrlThatIsNotLocal_IsIgnored`,
  `Callback_AReturnUrlCookieFromElsewhere_IsIgnored`
- **SRV-48** (MUST) The redirect URI sent to the authorization server is one of the client
  metadata's registered `redirect_uris`, never derived from the request's `Host` header.
  *Source:* 0.8 `AtProtoOAuthService.cs:458`
- **SRV-49** (MUST) A successful callback writes the `OAuthSession` to the `ISessionStore`
  (CAS insert, [stores.md](stores.md)) and signs in with the configured cookie scheme, producing
  an identity with authentication type `AtProtoOAuth` and claim `atproto:did` (plus
  `atproto:handle` when verified). `GetAtProtoUser()` recognises only such identities.
  *Source:* 0.8 `OAuthUser.cs` · **Decision** on claim names (SRV-42)
- **SRV-50** (MUST) Login and callback failures redirect to `OAuthLoginOptions.LoginPath` with
  `?error=<code>` only. An `OAuthException`'s code, or an authorization server's `error`, is
  passed if it is 1–64 characters of ASCII letters, digits, `_`, `-`, `.`; anything else,
  including every non-OAuth failure, becomes `login_failed`. Descriptions and exception
  messages are logged, never sent. *Source:* 0.8 `AtProtoOAuthExtensions.cs:383-404` · tests
  `Login_AnyOtherFailure_RedirectsWithAFixedCodeAndNoMessage`, `Callback_AnAuthorizationServerError_PassesItsCodeButNotItsDescription`,
  `ErrorCode_KeepsOnlyShortTokens`
- **SRV-51** (MUST) Logout signs the cookie out, then removes the stored session and revokes it
  through the session (AUT logout rules), waiting for a refresh already under way. A logout by a
  principal that is not an OAuth-login identity leaves every stored session untouched.
  *Source:* 0.8 · tests `LogoutAsync_WaitsForARefreshUnderWay_ThenRemovesAndRevokesTheSession`,
  `LogoutAsync_ForAServiceAuthCaller_LeavesTheAccountsSessionAlone`
- **SRV-52** (MUST) `POST {prefix}/logout` requires antiforgery validation when the application
  has antiforgery configured. *Source:* **Decision**: logout CSRF is a standard web risk.
- **SRV-53** (SHOULD) `OAuthLoginOptions.ValidateSessionOnRequest` (opt-in) installs a cookie
  `OnValidatePrincipal` that rejects the cookie when the store holds no session for its DID,
  checked at most once per `SessionValidationInterval` per principal. *Source:* **Decision**:
  0.8's 7-day cookie outlives an expired or revoked session, leaving a user who looks signed in
  while the factory returns `null`.
- **SRV-54** (MUST) With `ServeClientMetadata`, the client metadata document is served at the
  path of its `client_id` (and the JWKS at `jwks_uri`'s path for confidential clients) with
  `Content-Type: application/json`, no `null` fields, public keys only, and
  `Cache-Control: max-age=300`. Startup fails if metadata is missing or the `client_id` has no
  path. *Source:* 0.8 `AtProtoOAuthExtensions.cs:326-359` · tests
  `ServeClientMetadata_AConfidentialClient_PublishesOnlyThePublicKeys`, `ServeClientMetadata_AClientIdWithoutAPath_FailsAtStartup`
- **SRV-55** (MUST) The scope requested at login is the client metadata's `scope`; the login
  has no separate scope setting. *Source:* **Decision** ([auth.md](auth.md): one scope source;
  0.8 configures it in three places)
- **SRV-56** (MUST NOT) The loopback cookie relay of 0.8 is not carried over (Q-9).
  Local development runs the login on `http://127.0.0.1:{port}` with an atproto loopback
  `client_id`. *Source:* [06 — Decisions Q-9](../06-decisions.md#open-questions)

### Labeler endpoints

- **SRV-57** (MUST) `AddLabeler<TSource>()` registers `com.atproto.label.queryLabels` and
  `com.atproto.label.subscribeLabels` handlers over the registered `ILabelSource`.
  *Source:* 0.8 `QueryLabelsEndpoint.cs` · subscription is **Decision** (SRV-30)
- **SRV-58** (MUST) `queryLabels`: `uriPatterns` is required (at least one); `*` is allowed only
  as the last character and means prefix match, a pattern without `*` is an exact match;
  `sources` filters by labeler DID; `limit` defaults to 50, values above 250 are capped to 250,
  values below 1 are a 400; `cursor` is opaque to the caller.
  *Source:* 0.8 `QueryLabelsEndpoint.cs:99-135` · tests `Query_PrefixAndExactPatterns_MatchAsTheLexiconDescribes`,
  `Query_Limit_DefaultsTo50AndIsCappedAt250`, `Query_InvalidRequest_Answers400`
- **SRV-59** (MUST) When a `LabelSigner` is registered, labels whose `src` is the labeler's DID
  and that carry no signature are signed on the way out; already-signed labels and labels from
  other sources are served as stored. Without a signer, labels are served as stored.
  *Source:* 0.8 · tests `Query_AlreadySignedLabel_IsServedAsStored`, `Query_WithoutASigner_ServesLabelsAsStored`
- **SRV-60** (MUST) `subscribeLabels` streams `#labels` frames `{seq, labels}` from the source,
  every label in a frame signed (LBL frame rules); a `cursor` greater than the source's head
  sequence ends the subscription with a `FutureCursor` error frame; a `cursor` older than the
  source retains yields an `#info` frame `{name: "OutdatedCursor"}` before streaming from the
  oldest retained label. *Source:* [Labels spec](https://atproto.com/specs/label) ·
  [Event Stream spec](https://atproto.com/specs/event-stream) · [labels.md](labels.md)

### Tap webhook

- **SRV-61** (MUST) `MapAtProtoTapWebhook(pattern)` accepts `POST` with HTTP Basic credentials,
  user `admin` and the configured password; user and password are compared in constant time and
  a password containing `:` works. Missing or wrong credentials are a 401 envelope.
  *Source:* 0.8 `TapWebhookExtensions.cs:89-109` · tests `Post_WithoutCredentials_IsUnauthorized`,
  `Post_PasswordWithColons_IsAccepted`, `IsAuthorized_AcceptsTheHeaderTheTapClientFormats`
- **SRV-62** (MUST) A password is required at startup unless `AllowUnauthenticated` is set
  explicitly. *Source:* 0.8 · tests `MapTapWebhook_NoPassword_Throws`, `MapTapWebhook_AllowUnauthenticated_AcceptsWithoutCredentials`
- **SRV-63** (MUST) Bodies over `TapWebhookOptions.MaxBodyBytes` are a 413 envelope whether the
  length is declared or not, and no buffer is reserved from a declared length before the bytes
  arrive. *Source:* 0.8 · tests `Post_DeclaredBodyOverTheLimit_Is413`, `Post_UndeclaredBodyOverTheLimit_Is413`,
  `ReadBodyAsync_DeclaredLength_ReservesNothingBeforeTheBytesArrive`
- **SRV-64** (MUST) Acknowledgement follows STR's Tap acknowledgement rule
  ([streaming.md](streaming.md) STR-85/86) so the webhook and the Tap channel behave the same: a
  handled event is acknowledged with 200; a handler exception is a 500 envelope so Tap
  redelivers; an event the SDK cannot read (malformed, or of a type this SDK version does not
  model) is acknowledged with 200, logged as a warning and counted, because Tap holds back a
  repository's later events until the in-flight one is acknowledged and an unreadable event does
  not become readable on redelivery. *Source:* 0.8 · tests `Post_HandlerFails_Is500SoTapRetries`,
  `Post_NotATapEvent_IsAcknowledgedWithAWarning` · STR's rule resolves 0.8's contradiction with
  the channel (`TapClient.cs:272-275`)

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `XrpcServerLimits.DefaultJsonInputBytes` | 1 MiB | JSON procedure input without `[RequestSizeLimit]` |
| `XrpcServerLimits.MaxErrorMessageChars` | 1,024 | `message` of envelopes written by the SDK (truncated) |
| `XrpcServerLimits.SubscriptionSendQueueFrames` | 1,000 | frames queued per subscriber before `ConsumerTooSlow` |
| `XrpcServerLimits.SubscriptionMaxFrameBytes` | 5 MiB | one outgoing frame; a larger message is a handler error |
| `XrpcServerLimits.SubscriptionPingInterval` | 30 s | WebSocket keep-alive |
| `QueryLabelsDefaults.DefaultLimit` / `MaxLimit` | 50 / 250 | `queryLabels` |
| `OAuthLoginDefaults.ErrorCodeMaxChars` | 64 | login error codes |
| `OAuthLoginDefaults.ClientDocumentMaxAge` | 5 min | client metadata `Cache-Control` |
| `OAuthLoginDefaults.SessionValidationInterval` | 1 min | SRV-53 store check |
| `TapWebhookOptions.MaxBodyBytes` (default) | 4 MiB | Tap webhook bodies |

## Errors

| Failure | Result |
|---|---|
| Protocol errors on XRPC routes | envelope per SRV-8 |
| Errors after a subscription upgrade | `{op:-1}` error frame, then close (SRV-32) |
| Service-auth verification failure | 401 envelope with the verifier's error name; `jti` unspent if the signature did not verify (AUT) |
| Space-credential verification failure | 401 envelope with `WWW-Authenticate: Atproto-Space` and the verifier's reason (SRV-67) |
| Login/callback failures | redirect with `?error=<code>` (SRV-50) |
| Logout and client-metadata request failures | envelope |
| Tap webhook failures | 401 / 413 / 500 envelopes (SRV-61–64) |
| Misconfiguration (no handlers, no audiences, missing Tap password, missing client metadata) | `OptionsValidationException` or `InvalidOperationException` at host start |

## Conformance and tests

- Golden XRPC wire fixtures from the conformance suite ([04 — Testing](../04-testing.md#conformance-areas))
  run against an in-process `TestServer` hosting generated handlers, asserting status, envelope
  and headers byte for byte.
- Port these 0.8 test classes as scenarios: `XrpcEndpointTests`, `XrpcRouteGroupTests`,
  `XrpcQueryBindingTests`, `ServiceAuthSchemeTests`, `AtProtoOAuthCallbackBindingTests`
  (minus the relay cases), `AtProtoOAuthEndpointTests`, `QueryLabelsEndpointTests`,
  `TapWebhookTests`, `SpaceServerEndpointTests`, `SpaceStoreRegistrationTests`,
  `SpaceCredentialCacheTests`, `ServiceAuthWithSpacesTests` (the protocol assertions of these stay
  with [spaces.md](spaces.md); here they pin registration, mapping and the schemes).
- New tests: subscription framing against the conformance label-frame vectors, `ConsumerTooSlow`
  with a stalled client, error frame after upgrade, principal separation (a service-auth token
  and an OAuth cookie for the same DID on one request), `OnValidatePrincipal`, header allow-list
  (SRV-10), AOT registration list (SRV-26), parameter constraint validation (SRV-16).

## Changes from 0.8

| Change | Reason |
|---|---|
| Three handler interfaces keyed by descriptors replace eight shape interfaces with hand-typed `static Nsid` | One definition per method shared with the client; fewer shapes |
| `XrpcContext` is the handler input instead of `HttpContext` | Typed access to the caller; testable handlers |
| Reflection binder replaced by generated parameter metadata | AOT/trim (G6) |
| Lexicon constraints on parameters enforced; input bodies validated | Contract enforcement upstream servers already do |
| Only allow-listed headers copied from a handler's `XrpcException` | Prevents leaking upstream response headers |
| Subscriptions can be served | 0.8 could not serve `subscribeLabels` |
| Distinct claim types and principal accessors for service auth vs OAuth login | Removes the authentication-type sniffing behind a 0.7 security fix |
| `ServiceAuthVerifier` built once per scheme | 0.8 rebuilt and revalidated it per request |
| Envelope for Tap and OAuth non-redirect errors | One error format |
| Package renamed `ATProtoNet.Server` → `ATProtoNet.AspNetCore`; the DI builder, client factory, stores, stream workers, health checks and Aspire client integration move to the base `ATProtoNet` package | Workers and console apps need only `ATProtoNet`; this package holds what genuinely needs ASP.NET Core |
| Loopback cookie relay dropped | Q-9 |
| Public `XrpcBooleanConverter` dropped | Generated binding handles booleans |
| Spaces server roles are regular features (`AddSpaces(…)` and companions on the builder), not experimental | SRV-65; Spaces is a core data protocol targeting GA |
| Space credentials have an authentication scheme and principal of their own | SRV-67, SRV-68; 0.8 verified them only inside its repo-host endpoints |
| A notification receiver is served | SRV-69; 0.8 could register but not receive |

## Open points

- **Tap acknowledgement of unreadable events** follows STR (acknowledge, warn, count). If STR's
  rule changes, SRV-64 changes with it.
- **Generated `{Method}.IHandler` stubs** for users' Lexicons when the project references this
  package. Proposed: yes, emitted by the source generator only; LEX to confirm.
- **Input validation opt-out.** Proposed: `[SkipLexiconValidation]` attribute per handler for hot
  paths; on by default.
- **`subscribeRepos` serving.** Not in scope (no PDS implementation); the subscription support
  makes it possible for users. Proposed: no SDK handler.
- **Subscription upgrade without WebSocket** (SRV-31): 400 vs 426 `Upgrade Required`. Proposed:
  400 envelope, to keep the XRPC status set.
