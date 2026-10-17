# Authentication: sessions, OAuth, service auth — spec

**Status:** Accepted (maintainer, 2026-10-17) · **Package:** `ATProtoNet` (namespaces `ATProtoNet.Auth`, `ATProtoNet.Auth.OAuth`,
`ATProtoNet.Auth.ServiceAuth`, and `ATProtoNet.Admin` for the admin handler; space credentials plug in from
`ATProtoNet.Spaces.Access`) · **Phases:** 5 (session model, password sessions, PDS admin
handler), 6 (OAuth, scopes including `space:`, service auth, space credentials) · **Prefix:** `AUT`

**Normative references:** [atproto OAuth](https://atproto.com/specs/oauth) ·
[atproto HTTP API: auth, service auth](https://atproto.com/specs/xrpc) ·
[atproto permissions](https://atproto.com/specs/permission) ·
[RFC 9126](https://www.rfc-editor.org/rfc/rfc9126) PAR · [RFC 7636](https://www.rfc-editor.org/rfc/rfc7636) PKCE ·
[RFC 9449](https://www.rfc-editor.org/rfc/rfc9449) DPoP · [RFC 9207](https://www.rfc-editor.org/rfc/rfc9207) `iss` ·
[RFC 9728](https://www.rfc-editor.org/rfc/rfc9728) protected-resource metadata ·
[RFC 8414](https://www.rfc-editor.org/rfc/rfc8414) AS metadata · [RFC 7009](https://www.rfc-editor.org/rfc/rfc7009) revocation ·
[RFC 7638](https://www.rfc-editor.org/rfc/rfc7638) JWK thumbprint · [RFC 7523](https://www.rfc-editor.org/rfc/rfc7523) `private_key_jwt` ·
[RFC 7519](https://www.rfc-editor.org/rfc/rfc7519) JWT · [RFC 9421](https://www.rfc-editor.org/rfc/rfc9421) HTTP message
signatures (space credentials) · Spaces access control and `space:` scopes per [spaces.md](spaces.md) (proposal 0016 at
its pin, then the GA specification).

## Scope

In scope: the session model (sessions as `IXrpcHandler`s, [D-06](../06-decisions.md#d-06-a-session-is-the-authenticated-transport));
credential generations, single-flight refresh, persistence through `ISessionStore` with
compare-and-swap ([D-22](../06-decisions.md#d-22-session-stores-support-compare-and-swap)),
sign-out and disposal; password sessions; the OAuth client (discovery, PAR, PKCE, DPoP, callback,
token validation, refresh, revocation, confidential clients); DPoP proofs and nonces; scopes; the
PDS admin handler's credential side; service-auth issuing and verification; **space credentials**
as the fourth authentication mechanism beside password sessions, OAuth and service auth: how the
delegation-token exchange, the space credential, the optional client attestation, revocation and
the HTTP-message-signature binding fit the session model, and the `space:` OAuth scopes.

Out of scope: the XRPC pipeline the handlers plug into ([xrpc-client.md](xrpc-client.md)); store
contracts ([stores.md](stores.md)); identity resolution and the untrusted-fetch layer
([identity.md](identity.md)); JWS/JWK primitives ([crypto.md](crypto.md)); the ASP.NET Core OAuth
login, cookie binding, `ClaimsPrincipal` mapping and the service-auth authentication scheme
([server.md](server.md)); per-user client factory and refresh-coordinator registration
([hosting.md](hosting.md)); the Spaces token formats, covered signature components, credential
provider internals and authority-side issuing ([spaces.md](spaces.md), which this spec's
space-credential requirements defer to for wire details).

## Public API sketch

```csharp
namespace ATProtoNet.Auth;

public closed class AtProtoSession : IXrpcHandler, IAsyncDisposable, IDisposable     // closed (AG-6a): PasswordSession, OAuthSession
{
    public Did Did { get; }
    public Handle Handle { get; }                       // Handle.Invalid when not bidirectionally verified
    public Uri ServiceUrl { get; }                      // the account's PDS
    public SessionData Data { get; }                    // immutable, persistable snapshot (current generation)
    public Tid? LatestRepoRev { get; }                  // per account; xrpc-client.md XRC-31
    public event EventHandler<SessionChangedEventArgs>? Changed;   // Refreshed | Adopted | Expired | SignedOut
    public Task RefreshAsync(CancellationToken cancellationToken = default);
    public Task SignOutAsync(CancellationToken cancellationToken = default);
}

public sealed class PasswordSession : AtProtoSession
{
    public static Task<PasswordSession> CreateAsync(Uri service, AtIdentifier identifier, string password,
        PasswordLoginOptions? login = null, SessionOptions? options = null, CancellationToken cancellationToken = default);
    public static Task<PasswordSession> CreateAccountAsync(Uri service, CreateAccount.Input input,
        SessionOptions? options = null, CancellationToken cancellationToken = default);
    public static Task<PasswordSession> ResumeAsync(PasswordSessionData data, SessionOptions? options = null,
        CancellationToken cancellationToken = default); // installs, then getSession (refreshing if expired)
    public static PasswordSession Restore(PasswordSessionData data, SessionOptions? options = null); // no request
}

public sealed class OAuthSession : AtProtoSession
{
    public ScopeSet GrantedScope { get; }
    public Uri Issuer { get; }
}

public sealed record SessionOptions
{
    public HttpClient? HttpClient { get; init; }        // for XRPC; never disposed by the session
    public XrpcInvokerOptions? Invoker { get; init; }
    public ISessionStore? Store { get; init; }
    public IRefreshCoordinator? RefreshCoordinator { get; init; }
    public IIdentityResolver? IdentityResolver { get; init; }
    public bool BackgroundRefresh { get; init; }        // opt-in timer, AUT-21
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public ILoggerFactory? LoggerFactory { get; init; }
}

public closed record SessionData { public required Did Did { get; init; } /* + version, see AUT-12 */ }
public sealed record PasswordSessionData : SessionData { /* accessJwt, refreshJwt, handle, service, … */ }
public sealed record OAuthSessionData : SessionData { /* tokens, expiry, scope, grant (issuer, endpoints, DPoP key, client key id) */ }

public interface IRefreshCoordinator { ValueTask<IAsyncDisposable> AcquireAsync(Did account, CancellationToken cancellationToken); }

namespace ATProtoNet.Auth.OAuth;

public sealed class OAuthClient
{
    public OAuthClient(OAuthClientOptions options);
    public Task<OAuthAuthorizationRequest> StartAuthorizationAsync(OAuthLoginHint hint, Uri redirectUri,
        OAuthAuthorizationOptions? options = null, CancellationToken cancellationToken = default);
    public Task<OAuthCompletion> CompleteAuthorizationAsync(OAuthCallback callback,
        CancellationToken cancellationToken = default);       // (OAuthSession Session, string? AppState)
    public Task<OAuthSession?> RestoreAsync(Did account, CancellationToken cancellationToken = default);
    public OAuthSession Restore(OAuthSessionData data);
}

public sealed record OAuthClientOptions
{
    public required OAuthClientMetadata ClientMetadata { get; init; }  // the single scope source (AUT-31)
    public IReadOnlyList<OAuthClientKey> ClientKeys { get; init; } = [];
    public required IOAuthStateStore StateStore { get; init; }
    public ISessionStore? SessionStore { get; init; }
    public IIdentityResolver? IdentityResolver { get; init; }
    public UntrustedFetchPolicy? FetchPolicy { get; init; }            // AS and metadata requests
    public HttpClient? ResourceHttpClient { get; init; }               // PDS requests of created sessions
    public IRefreshCoordinator? RefreshCoordinator { get; init; }
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

public closed record OAuthLoginHint { /* Account(AtIdentifier) | Server(Uri) */ }
public sealed record OAuthCallback(string Code, string State, string? Issuer);
public sealed class OAuthClientKey : IDisposable { /* ES256 P-256 private key + kid */ }

public sealed class ScopeSet : IReadOnlyCollection<Scope>      // parse, format, match
{
    public static ScopeSet Parse(string value);
    public bool Permits(Scope requested);
    public bool IsSubsetOf(ScopeSet other);
}
public closed record Scope { /* Atproto, Transition, Repo, Rpc, Blob, Account, Identity, Include, Space */ }

// A published permission set (Lexicon `permission-set` def), generated as a static value per set
// (lexicon.md); Include(aud) yields the `include:` scope for one audience.
public sealed record PermissionSetRef(Nsid Nsid)
{
    public Scope Include(XrpcServiceRef? audience = null);
}

namespace ATProtoNet.Auth.ServiceAuth;

public sealed class ServiceAuthIssuer                  // mints tokens; never disposes its key
{
    public ServiceAuthIssuer(Did issuer, SigningKey key, string? keyId = null, TimeProvider? timeProvider = null);
    public string CreateToken(XrpcServiceRef audience, Nsid method, TimeSpan? lifetime = null);
    public string CreateToken(Did audience, Nsid method, TimeSpan? lifetime = null);
}
public sealed class ServiceAuthHandler : IXrpcHandler   // per-request token: aud = target, lxm = request NSID
{
    public ServiceAuthHandler(ServiceAuthIssuer issuer, Uri serviceUrl, XrpcServiceRef audience, XrpcInvoker invoker);
}
public sealed class ServiceAuthVerifier
{
    public ServiceAuthVerifier(IIdentityResolver resolver, IReplayStore replayStore, ServiceAuthVerifierOptions options,
        TimeProvider? timeProvider = null);
    public Task<ServiceAuthVerification> VerifyAsync(string token, ServiceAuthExpectations expectations,
        CancellationToken cancellationToken = default);
}
public sealed record ServiceAuthExpectations(IReadOnlySet<string> Audiences, Nsid? Method);
public sealed record ServiceAuthVerification { public bool IsValid; public string? Error; public string? Message; public ServiceAuthClaims? Claims; }

namespace ATProtoNet.Admin;
public sealed class PdsAdminHandler : IXrpcHandler { /* Basic or AdminAccount; xrpc-client.md XRC-54 */ }

// Space credentials (AUT-75…AUT-81). Types are owned by spaces.md; shown here because they are an
// authentication mechanism of the same shape as the ones above.
namespace ATProtoNet.Spaces.Access;
public sealed class SpaceCredentialProvider : IAsyncDisposable { /* per account session: delegation token → credential, renewal */ }
public sealed class SpaceRequestHandler : IXrpcHandler { /* credential + RFC 9421 signature for one explicit audience */ }
public sealed class SpaceCredentialIssuer { /* authority side; verifies delegation tokens and attestations */ }
```

## Requirements

### Session model

- **AUT-1** (MUST) Authentication is supplied by an `IXrpcHandler`. `AnonymousHandler`,
  `PasswordSession`, `OAuthSession`, `ServiceAuthHandler`, `PdsAdminHandler` and
  `SpaceRequestHandler` (AUT-75) are the shipped implementations. `AtProtoClient` has no
  knowledge of logins, tokens, refresh or stores.
  *Source:* [D-06](../06-decisions.md#d-06-a-session-is-the-authenticated-transport)
- **AUT-2** (MUST) A session object is the single owner of its credentials and is complete on its
  own: an `OAuthSession` carries its grant (issuer, token and revocation endpoints, DPoP key, client
  key ID, the `OAuthClient` configuration needed to refresh and revoke), so it can refresh, revoke
  and sign out without any other object being passed alongside it.
  *Source:* Decision (0.8 required passing the `OAuthClient` to every install and silently kept
  the previous one when omitted: `SessionManager.cs:155`, test
  `ReApplyingWithoutAnOAuthClient_KeepsTheOneGivenBefore`)
- **AUT-3** (MUST) Grant-specific behaviour (how to refresh, revoke, update account details,
  compute expiry) is implemented by the session subclass, not by `switch`ing on a session kind in
  shared code.
  *Source:* Decision (0.8 `SessionManager` branches on kind in seven places)
- **AUT-4** (MUST) `SessionData` is an immutable, serialisable snapshot of one credential
  generation. Its JSON form (`SessionDataSerializer`, [STO-9](stores.md)) keeps 0.8's
  `$kind`-discriminated shape plus a format version, so 0.8 sessions convert without users
  signing in again. The session hot path reads only the 0.9 format; 0.8 and 0.6 payloads are
  read only by the one-off store migration helper ([stores.md](stores.md#open-points)).
  *Source:* 0.8 `AtProtoSessionJson.cs` · Decision for moving the 0.6 reader
  (`AtProtoSessionJson.cs:52-91`) out of the hot path
- **AUT-5** (MUST) `Handle` is the verified handle, or `Handle.Invalid` when bidirectional
  verification fails or cannot complete. Verification failure never fails a sign-in.
  *Source:* 0.8 `OAuthClient.cs:345` · test
  `Complete_StartedFromAServer_HandleAuthoritiesTimeOut_CompletesUnverifiedInsteadOfThrowing` (#42)

### Credentials, refresh and resend

- **AUT-6** (MUST) Each set of tokens is one **credential generation**. Every call resolves its
  target ([XRC-7](xrpc-client.md#handler-contract)) against the current generation once, after a
  proactive refresh when the access token expires within `RefreshSkew` (60 s). A failed proactive
  refresh while the token is still unexpired sends the call with the current token.
  *Source:* 0.8 `SessionManager.cs:42,297` · tests `AnAccessTokenAboutToExpire_IsRefreshedBeforeTheRequestIsSent`,
  `AProactiveRefreshThatFailsTransiently_SendsTheRequestWithTheCurrentToken`
- **AUT-7** (MUST) Refresh is **single-flight per generation**: concurrent callers that observed
  the same generation share one exchange; a caller whose generation has already been replaced does
  not exchange again and uses the replacement.
  *Source:* 0.8 `SessionManager.cs:336-360` · tests `ConcurrentCallsWithAnExpiredToken_ShareOneRefresh`,
  `ConcurrentInvalidTokenChallenges_SpendTheRefreshTokenOnce`, `ConcurrentRefreshSessionAsyncCalls_ShareOneRefresh`
- **AUT-8** (MUST) An exchange that has started is never cancelled by callers giving up or by
  disposal; it is bounded only by `RefreshTimeout` (30 s), and its result is always persisted.
  *Source:* 0.8 `SessionManager.cs:26-29,438` · tests
  `ARefreshWhoseCallerGivesUpAndWhoseClientIsDisposed_StillStoresTheRotatedToken`,
  `DisposeAsync_WithARefreshInFlight_WaitsForTheExchangeAndStoresIt`,
  `Dispose_WithARefreshInFlight_ReturnsAtOnceAndTheExchangeStillLands`
- **AUT-9** (MUST) On `XrpcAuthenticationException` with error `ExpiredToken`, or a 401 whose
  `WWW-Authenticate` carries `error="invalid_token"`, the session refreshes (AUT-7) and resends the
  call **once**, only if the new generation is for the same account and service and the body is
  replayable ([XRC-40](xrpc-client.md#retry-policy)). A refresh happens even when the resend
  cannot. A nonce carried by an `invalid_token` challenge is recorded and used by the resend; it is
  not a separate nonce retry.
  *Source:* 0.8 `XrpcClient.cs:475-512`, `SessionManager.cs:331` · tests
  `ACallRejectedAfterAnAccountSwitch_IsNotResentWithTheOtherAccountsToken`,
  `ARequestRejectedWithExpiredToken_IsRetriedOnceAfterARefresh`,
  `AnInvalidTokenChallengeCarryingANewNonce_IsRecoveredWithoutASeparateNonceRetry`,
  `ANonSeekableUploadRejectedAsExpired_RefreshesButIsNotResent`
- **AUT-10** (MUST) A session **expires** only when the refresh credential itself is refused
  (password: `XrpcAuthenticationException` from `refreshSession`; OAuth: `invalid_grant`) **and**
  the store does not hold a newer generation (AUT-14), or when OAuth issuer re-verification
  definitively names another authorization server (AUT-46). Transient failures (5xx, network,
  timeout, including a transient failure of issuer re-verification) keep the session.
  *Source:* 0.8 `SessionManager.cs:666` · tests `RefreshSessionAsync_RefreshJwtRejected_ExpiresTheSession`,
  `RefreshSessionAsync_ServiceUnavailable_KeepsTheSession`, `RefreshSessionAsync_InvalidGrant_ExpiresTheSessionAndStoresNoToken`
- **AUT-11** (MUST) A refresh response that is an error, or a success without a usable access
  token, is never installed or stored.
  *Source:* 0.8 tests `RefreshSessionAsync_ErrorStatus_IsNeverTakenForTokens`,
  `RefreshSessionAsync_SuccessWithoutAnAccessToken_IsRejected`

### Persistence and multiple instances

- **AUT-12** (MUST) When a store is configured, every generation change is written with the
  store's compare-and-swap (`TryReplaceAsync(did, expectedVersion, next)`, [stores.md](stores.md)):
  new sign-in (insert), refresh, adoption, expiry (conditional delete), sign-out (conditional
  delete). The session never holds a store version older than the generation it uses.
  *Source:* [D-22](../06-decisions.md#d-22-session-stores-support-compare-and-swap)
- **AUT-13** (MUST) Before spending a refresh credential, the session (under the coordinator
  lease, if one is configured) reads the store. If it holds a newer generation of the **same
  grant**, the session adopts it without an exchange (keeping the grant's DPoP key). If it holds a
  **different grant** for the account (a new sign-in), the session adopts it with that grant's own
  key. If it holds **no entry**, the account was signed out elsewhere: the session ends without
  spending the credential.
  *Source:* 0.8 `SessionManager.cs:412-435,583-617` · tests
  `ARefreshAfterAnotherClientRefreshed_TakesUpTheStoredSession`,
  `ASessionTheStoreNoLongerHolds_EndsWithoutSpendingItsRefreshToken`,
  `AStoredSessionOfANewSignIn_IsTakenUpWithItsOwnKey`
- **AUT-14** (MUST) After an exchange, a failed CAS means another instance changed the entry: the
  session re-reads and adopts the stored generation if it is newer, otherwise retries the CAS. A
  refused refresh credential is final only if the store still holds the version it was refreshed
  from; otherwise the session adopts the stored generation instead of expiring. This is what makes
  concurrent instances without a shared coordinator correct.
  *Source:* [D-22](../06-decisions.md#d-22-session-stores-support-compare-and-swap) · 0.8 test
  `ALosingConcurrentRefresh_LeavesTheWinnersStoredSessionAlone`
- **AUT-15** (MUST) On expiry the store entry is removed only if it still holds the refused
  generation (conditional delete).
  *Source:* 0.8 `SessionManager.cs:849-864`
- **AUT-16** (MUST) A store write failure after a successful exchange keeps the new generation in
  memory, logs at Warning, and retries the write with the next generation change.
  *Source:* 0.8 `SessionManager.cs:827-842`
- **AUT-17** (MUST) Store writes that follow a completed transition are not cancellable by the
  caller's token; `Changed` is raised after the write completes.
  *Source:* 0.8 tests `ApplySessionAsync_CallerCancelsDuringTheStoreWrite_TheWriteAndTheEventStillHappen`,
  `DisposingDuringTheStoreWrite_LetsTheWriteFinish`
- **AUT-18** (SHOULD) `IRefreshCoordinator` is an optimisation that serialises refreshes of one
  account in-process (`InProcessRefreshCoordinator`, keyed lock; a cancelled wait holds nothing).
  Correctness MUST NOT depend on it (AUT-14). Install, adoption, expiry and sign-out take the same
  lease when one is configured.
  *Source:* 0.8 `SessionRefreshCoordinatorTests` (`InProcessCoordinator_ACancelledWait_HoldsNothing`,
  `ConcurrentRequests_WithACoordinator_SpendTheRefreshTokenOnce`)

### Lifecycle

- **AUT-19** (MUST) `SignOutAsync` tears down locally first (the session refuses further calls and
  the store entry is conditionally deleted), then revokes remotely (password: `deleteSession` with
  the refresh JWT; OAuth: RFC 7009 revocation, AUT-48). If the store holds a later generation, that
  one is revoked. A remote refusal because the token is already invalid counts as success. Other
  failures are rethrown after teardown (`AggregateException` if both steps fail). An OAuth session
  never calls `deleteSession`.
  *Source:* 0.8 `SessionManager.cs:705-806` · tests `LogoutAsync_ServiceFailure_TearsDownLocallyThenThrows`,
  `LogoutAsync_RefreshJwtAlreadyInvalid_Succeeds`, `LogoutAsync_NeverCallsDeleteSessionForAnOAuthSession`,
  `SigningOutWhileAnotherClientRefreshes_LeavesTheStoreEmpty_AndRevokesTheLiveToken`
- **AUT-20** (MUST) `Changed` handlers that throw are caught and logged; they never fail the
  transition.
  *Source:* 0.8 test `ASessionChangedHandlerThatThrows_DoesNotFailTheSignIn`
- **AUT-21** (MUST) Background refresh is off by default. When enabled, a timer fires at
  `expiry − RefreshSkew`; a transient failure retries after 30 s, doubling to a 10-minute cap; the
  timer is armed at most 7 days ahead at a time. All timing uses `SessionOptions.TimeProvider`.
  *Source:* 0.8 `SessionManager.cs:48-54` · tests `BackgroundRefresh_RefreshesShortlyBeforeExpiryWhileIdle`,
  `ABackgroundRefreshThatFailsTransiently_IsRetriedWithBackoff`, `WithoutBackgroundRefresh_NoTimerIsScheduled`;
  [AG-27](../03-api-guidelines.md#resources-and-lifetimes)
- **AUT-22** (MUST) `Dispose` returns at once (an in-flight exchange still completes and is
  persisted, AUT-8); `DisposeAsync` waits for it. After disposal calls throw
  `ObjectDisposedException`. A session never disposes what it was given (`HttpClient`, store,
  coordinator, resolver, keys).
  *Source:* 0.8 tests `DisposeAsync_LeavesWhatItWasGivenUsable`, `AfterDispose_SessionChangesAreRefused` ·
  [AG-26](../03-api-guidelines.md#resources-and-lifetimes)
- **AUT-23** (MUST) If a session's DPoP key has been released (session ended or replaced) while a
  call is in flight, the call fails with `XrpcAuthenticationException(AuthenticationRequired)`, not
  `ObjectDisposedException`, and the released key never signs.
  *Source:* 0.8 `XrpcClient.cs:619-629` · test `ACallOfASessionReplacedMeanwhile_FailsInsteadOfSigningWithTheReleasedKey`

### Password sessions

- **AUT-24** (MUST) `CreateAsync` calls `com.atproto.server.createSession` with `identifier`,
  `password`, `authFactorToken` when given, and `allowTakendown` only when `true`. Expiry is the
  access JWT's `exp`, read without signature verification.
  *Source:* 0.8 tests `LoginAsync_InstallsAPasswordSessionExpiringWhenItsAccessJwtDoes`,
  `LoginAsync_AllowTakendown_IsSentOnlyWhenAskedFor`
- **AUT-25** (MUST) When the response (or a refresh response) carries a `didDoc` whose
  `#atproto_pds` endpoint passes the runtime-URL rule ([XRC-10](xrpc-client.md#service-urls-and-http)),
  the session's service becomes that PDS (entryway → PDS). Otherwise the session stays on the
  current service and logs at Warning.
  *Source:* 0.8 `SessionManager.cs:456-470,1045-1068` · tests `LoginAsync_ThroughAnEntryway_MovesToThePdsTheDidDocumentNames`,
  `LoginAsync_DidDocumentThatCannotBeFollowed_StaysOnTheConfiguredService`,
  `ARefreshWhoseDidDocumentNamesANewPds_MovesTheSessionThere`
- **AUT-26** (MUST) Refresh calls `refreshSession` with the refresh JWT as the bearer token, and
  `deleteSession` likewise; both are direct calls ([XRC-22](xrpc-client.md#headers)).
  *Source:* 0.8 `XrpcClient.cs:324-330`
- **AUT-27** (MUST) `ResumeAsync` installs the data, then calls `getSession` (refreshing first if
  expired) and takes the account details the service reports. A token the service rejects ends the
  session and removes it from the store; an unreachable service keeps it.
  *Source:* 0.8 tests `ResumeSessionAsync_OnAFreshClientWithAnExpiredToken_RefreshesAndRetries`,
  `ResumeSessionAsync_ATokenTheServiceRejects_RemovesTheSession`, `ResumeSessionAsync_ServiceUnreachable_KeepsTheSession`
- **AUT-28** (MUST) `CreateAccountAsync` creates the account and returns its session. Generated
  `client.Server.CreateSessionAsync`/`CreateAccountAsync` remain plain calls that return tokens
  without creating a session.
  *Source:* 0.8 tests `CreateAccountAndLoginAsync_InstallsTheNewAccountsSession`,
  `ServerCreateSessionAsync_ReturnsTokensWithoutInstallingThem`

### OAuth client configuration

- **AUT-29** (MUST) `token_endpoint_auth_method` is `none` (public client) or `private_key_jwt`
  (confidential). A public client has no keys. A confidential client has at least one key, all
  ES256 (P-256), with unique `kid`s, and either inline `jwks` containing every key's public half
  or a `jwks_uri`. Violations throw at construction.
  *Source:* 0.8 `OAuthClient.cs:784-836` · tests `Constructor_InconsistentClientAuthentication_Throws`,
  `Constructor_TwoKeysWithOneKeyId_Throws`, `Constructor_KeysPublishedAtAJwksUri_AreAccepted`,
  `Import_AK256Key_Throws`
- **AUT-30** (MUST) `OAuthClientMetadata.ToJson()` produces the client metadata document with
  unset fields omitted (never `null`) and public keys only.
  *Source:* 0.8 #41 · test `CreateKeySet_SerializesThePublicHalvesOnly`
- **AUT-31** (MUST) The client metadata's `scope` is the **only** configured scope. A request MAY
  ask for a subset (`OAuthAuthorizationOptions.Scope`), which MUST parse as a `ScopeSet`, MUST
  include `atproto` and MUST be a subset of the metadata scope; otherwise it throws before any
  request.
  *Source:* Decision (0.8 configured scope in three places that "must match" with nothing
  enforcing it: `OAuthOptions.Scope`, `OAuthClientMetadata.Scope`, `AtProtoOAuthServerOptions.Scopes`)
- **AUT-32** (MUST) `redirect_uri` is HTTPS, or HTTP on a loopback address, and is one of the
  metadata's `redirect_uris`.
  *Source:* 0.8 `OAuthClient.cs:155-165` · [specs/oauth](https://atproto.com/specs/oauth) client metadata

### Discovery and metadata validation

- **AUT-33** (MUST) From an account hint: resolve the identifier ([identity.md](identity.md)) to a
  DID document, take its PDS, fetch `{pds}/.well-known/oauth-protected-resource`, then the
  authorization server's `/.well-known/oauth-authorization-server`. A malformed identifier is
  refused without any request. From a server hint: fetch its protected-resource metadata; if it
  has none, treat the server as its own authorization server (entryway); if that also fails,
  report the PDS failure.
  *Source:* [specs/oauth](https://atproto.com/specs/oauth) · 0.8 `AuthorizationServerDiscovery.cs:160-185` · tests
  `AServerWithoutResourceMetadata_IsTriedAsAnAuthorizationServer`,
  `SignIn_StartedAtAnEntryway_CompletesOnTheAccountsPds`,
  `ResolveFromIdentifier_MalformedIdentifier_IsRefusedWithoutARequest`
- **AUT-34** (MUST) Protected-resource metadata MUST be a 200 JSON object whose `resource` names
  the PDS it was fetched from (trailing slash ignored) and whose `authorization_servers` has
  **exactly one** element, a canonical issuer (HTTPS, lower-case host, no default port, no
  trailing `/`, no userinfo, query or fragment).
  *Source:* RFC 9728 §3.3 · [specs/oauth](https://atproto.com/specs/oauth) · 0.8
  `AuthorizationServerDiscovery.cs:212-235,373-381` · tests `ResourceMetadataDescribingAnotherResource_IsRefused`,
  `AResourceWithATrailingSlash_StillNamesThePds`
- **AUT-35** (MUST) Authorization-server metadata MUST have `issuer` ordinally equal to the issuer
  it was looked up as; absolute HTTPS endpoints without userinfo, query or fragment;
  `require_pushed_authorization_requests = true`; `authorization_response_iss_parameter_supported
  = true`; `client_id_metadata_document_supported = true`; `atproto` in `scopes_supported`; `ES256`
  in `dpop_signing_alg_values_supported`. If it lists `protected_resources`, the PDS MUST be among
  them.
  *Source:* RFC 8414 · [specs/oauth](https://atproto.com/specs/oauth) · 0.8
  `AuthorizationServerDiscovery.cs:143,316-369` · tests `AuthorizationServerMetadataValidationTests` (all)
- **AUT-36** (MUST) Metadata fetches go through the untrusted-fetch layer
  ([D-21](../06-decisions.md#d-21-one-untrusted-fetch-layer)): public addresses only, HTTPS only,
  no redirects, no proxy, body ≤ 64 KiB, 10 s timeout. `AllowPrivateNetworks` on the fetch policy
  is the development opt-out.
  *Source:* 0.8 `AuthorizationServerDiscovery.cs:29-30,298` · tests `MetadataFetchPolicyTests`
- **AUT-37** (MUST) Validated metadata is cached per `OAuthClient` instance for 5 minutes, at most
  256 entries; failures are not cached; each caller gets its own copy.
  *Source:* 0.8 `AuthorizationServerDiscovery.cs:26,33`

### Authorization request

- **AUT-38** (MUST) `StartAuthorizationAsync` sends a PAR (RFC 9126) with `response_type=code`,
  `client_id`, `redirect_uri`, `scope`, PKCE `code_challenge` (S256 over a 43-character verifier
  from 32 random bytes), a `state` from 32 random bytes, and `login_hint` when the hint is an
  account. A PAR response without `request_uri` is an error. It returns the authorization URL
  (`authorization_endpoint?client_id=…&request_uri=…`).
  *Source:* RFC 9126, RFC 7636 · 0.8 `OAuthClient.cs:197-224` · test
  `APushedAuthorizationResponseWithoutARequestUri_IsRejected`
- **AUT-39** (MUST) A fresh P-256 DPoP key is generated for every authorization and stored with
  the pending state.
  *Source:* 0.8 `OAuthClient.cs:200,238`
- **AUT-40** (MUST) Pending state is saved in `IOAuthStateStore` with a 10-minute lifetime and
  holds the issuer, token and revocation endpoints, redirect URI, PKCE verifier, DPoP key, client
  key ID, expected DID (account hints only), requester ID and `AppState` (≤ 4,096 characters).
  *Source:* 0.8 `OAuthClient.cs:42`, `OAuthModels.cs:436`
- **AUT-41** (MUST) A confidential client uses its first key for new authorizations and fails with
  `unsupported_client_auth` **before** the PAR if the server does not list `private_key_jwt` in
  `token_endpoint_auth_methods_supported` and `ES256` in
  `token_endpoint_auth_signing_alg_values_supported`.
  *Source:* 0.8 `OAuthClient.cs:750-764` · test `AServerThatDoesNotAcceptPrivateKeyJwt_IsRefusedBeforeThePushedRequest`

### Callback and token validation

- **AUT-42** (MUST) `CompleteAuthorizationAsync` takes the pending state from the store
  **single-use, before any other check**, looking it up only if `state` is at most 256
  characters. Then, in order: `iss` MUST equal the pending issuer (RFC 9207); the pending state
  MUST be unexpired; then the code is exchanged with the PKCE verifier and a DPoP proof.
  *Source:* RFC 9207 · 0.8 `OAuthClient.cs:294-306`
- **AUT-43** (MUST) A token response is accepted only if: the access token is non-empty;
  `token_type` is `DPoP` (case-insensitive); `sub` is present and a valid DID; `sub` equals the
  expected DID when one is known; `scope` contains `atproto` as an exact space-separated token.
  *Source:* [specs/oauth](https://atproto.com/specs/oauth) · 0.8 `OAuthClient.cs:546-575` · tests
  `SecurityTests.ScopeValidation_UsesExactTokenMatch`, `Complete_StartedFromAHandle_TokensForAnotherAccount_AreRefused`,
  `SignIn_ATokenThatIsNotDPoPBound_IsRejectedAndRevoked`
- **AUT-44** (MUST) After the token response, the client resolves `sub` **bypassing every cache**
  (`IIdentityResolver.ResolveAsync` with `IdentityResolveOptions { BypassCache = true }`,
  [IDN-35](identity.md); DID document → PDS → protected-resource metadata → authorization
  server, metadata fetched fresh too) and requires that
  authorization server to equal the issuer. It fails closed (`verification_failed`); only the
  caller's own cancellation propagates as cancellation.
  *Source:* [specs/oauth](https://atproto.com/specs/oauth) (sub/issuer binding) · 0.8
  `OAuthClient.cs:842-881` (#42) · tests `SignIn_TheMetadataCacheFromStartingTheLogin_IsBypassedAtTheCallback`,
  `SignIn_StartedFromAServerUrl_ADidThatResolvesToAnotherAuthorizationServer_IsRejectedAndRevoked`
- **AUT-45** (MUST) Tokens refused by AUT-43 or AUT-44 are revoked best-effort before the
  exception is thrown. The session's service URL is the PDS from the freshly resolved DID
  document; its handle follows AUT-5.
  *Source:* 0.8 `OAuthClient.cs:339-345,512-537`

### Refresh and revocation (OAuth)

- **AUT-46** (MUST) Before spending a refresh token, the client re-verifies the issuer uncached
  (AUT-44). An account that now resolves to another authorization server, or does not resolve,
  spends no refresh token. A definitive answer naming another authorization server fails with
  `auth_server_mismatch` and **ends the session** (the grant can no longer be used safely; 0.8
  retried it forever). A resolution that fails transiently (network error, timeout, 5xx) fails
  with `verification_failed` and **keeps the session** for a later attempt, like any transient
  refresh failure (AUT-10). Both happen after AUT-13's store check.
  *Source:* 0.8 `OAuthClient.cs:419` · tests `Refresh_AnAccountNowOnAnotherAuthorizationServer_SpendsNoRefreshToken`,
  `Refresh_AnUnresolvableAccount_SpendsNoRefreshToken` · Decision for ending the session (0.8
  retried `auth_server_mismatch` indefinitely)
- **AUT-47** (MUST) A refresh response is validated with AUT-43 against the session's DID; on
  failure the new tokens are revoked and the current generation is kept. A response without
  `refresh_token` keeps the current one. The service URL follows the freshly resolved DID document.
  *Source:* 0.8 `OAuthClient.cs:432-450` · tests `Refresh_ThroughAnAtProtoClient_KeepsTheSessionWhenTheResponseIsForAnotherAccount`,
  `Refresh_TokensForAnotherAccount_AreRevoked`, `Refresh_AnAccountNowOnAnotherPdsOfTheSameServer_MovesTheSession`
- **AUT-48** (MUST) Revocation (RFC 7009) revokes the refresh token if present, else the access
  token, with a DPoP proof and client authentication. If the session has no revocation endpoint it
  is looked up from metadata; if the server has none, nothing is sent.
  *Source:* 0.8 `OAuthClient.cs:481-493` · tests `LogoutAsync_RevokesTheRefreshTokenWithADPoPProof`,
  `LogoutAsync_RevocationEndpointNotKnown_LooksItUp`, `LogoutAsync_WithoutARefreshToken_RevokesTheAccessToken`
- **AUT-49** (MUST) A confidential session records the `kid` it was issued to; refresh and
  revocation MUST be authenticated with that key, or fail with `client_key_unavailable` without a
  request. A public client's session is always sent as a public client.
  *Source:* 0.8 `OAuthClient.cs:771-782` · tests `RefreshAndRevocation_AreSignedByTheKeyTheSessionWasIssuedTo`,
  `ASessionWhoseKeyIsNoLongerConfigured_IsNotSent`, `APublicClientsSession_IsStillSentAsAPublicClients`

### Requests to the authorization server

- **AUT-50** (MUST) Every AS request (PAR, token, revocation) takes one path: the endpoint MUST
  pass the endpoint URL rules before anything is sent; the request is a form POST with a DPoP proof
  carrying the cached nonce for the origin and, for confidential clients, a fresh client assertion;
  one `RequestTimeout` (30 s) budget covers the attempt, any retry and the body read; bodies are
  capped at 64 KiB; responses are always disposed.
  *Source:* 0.8 `OAuthClient.cs:605-692` · tests `OAuthTransportTests` (`AnEndpointTheUrlRulesRefuse_GetsNoRequest`,
  `ASuccessBodyOverTheCap_IsRejected`, `EveryResponse_IsDisposed`), `OAuthRequestFailureTests`
- **AUT-51** (MUST) The request is retried **exactly once**, and only when the error is
  `use_dpop_nonce` and the response carries a new `DPoP-Nonce`. A second nonce challenge fails.
  The retry carries a new proof and a new client assertion.
  *Source:* RFC 9449 §8 · 0.8 `OAuthClient.cs:679` · tests `UseDpopNonceWithoutANonce_IsNotRetried`,
  `ASecondNonceChallenge_Fails`, `ANonceRetry_SendsANewAssertion`
- **AUT-52** (MUST) A client assertion (RFC 7523) has `iss` = `sub` = `client_id`, `aud` = the
  issuer, a unique `jti`, `iat`, and `exp` = `iat` + 60 s; its header has `alg` (`ES256`) and
  `kid` only, no `typ`.
  *Source:* 0.8 `OAuthClientKey.cs:29,343-360` · tests `CreateAssertion_VerifiesAgainstTheRfc7517ExamplePublicKey`,
  `SignIn_ThePushedRequestAndTheExchangeCarryFreshAssertions`
- **AUT-53** (MUST) Error responses map to `OAuthException` carrying the OAuth `error` and
  `error_description`; a non-OAuth error body maps to `server_error`; timeouts to
  `request_timeout`; transport failures to `request_failed`.
  *Source:* 0.8 tests `AnErrorBody_IsAnOAuthExceptionCarryingTheError`, `AnErrorStatusWithoutAnOAuthBody_IsServerError`

### DPoP proofs and nonces

- **AUT-54** (MUST) DPoP proofs are ES256 only; a K-256 key is refused at construction. Header:
  `typ: dpop+jwt`, `alg: ES256`, `jwk` = exactly the public key. Claims: `jti` (128-bit random,
  hex), `htm` (upper-case method), `htu` (scheme and host lower-cased, no query, fragment or
  userinfo), `iat`, `nonce` when known, `ath` = base64url(SHA-256(access token)) on resource
  requests.
  *Source:* RFC 9449 §4.2 · 0.8 `DPoPProofGenerator.cs:155-196`, `DPoP.cs:277-284` · tests
  `DPoPProofGeneratorTests` (incl. `Constructor_WithAK256Key_Throws`), `DPoPTests`
- **AUT-55** (MUST) Nonces are cached **per `OAuthClient` instance**, shared by the sessions it
  creates or restores, keyed by origin: at most 1,024 origins, nonces of at most 1,024 characters,
  sent only to their own origin, updated from every response that carries `DPoP-Nonce`.
  *Source:* 0.8 `DPoPNonceCache.cs:21-24` · tests `DPoPNonceCacheTests` · Decision for instance
  ownership (0.8's `DPoPNonceCache.Shared` was process-global, AG-10)
- **AUT-56** (MUST) On a resource request, a 401 that carries a new `DPoP-Nonce` and whose
  challenge is `error="use_dpop_nonce"` (and not `invalid_token`) is retried once with the new
  nonce. An `invalid_token` challenge is handled by AUT-9.
  *Source:* RFC 9449 §9 · 0.8 `XrpcClient.cs:444-454` · test `DPoPNonceChallenge_IsRetriedOnceWithTheNonce` ·
  Decision for requiring `use_dpop_nonce` (0.8's resource path retried any 401 with a nonce
  header; unified with AUT-51)
- **AUT-57** (MUST) `OAuthSession`'s authorizer declares `Authorization` and `DPoP` as reserved
  headers and writes `Authorization: DPoP {access token}` plus a fresh proof on every HTTP attempt.
  *Source:* [XRC-25](xrpc-client.md#headers), [XRC-39](xrpc-client.md#retry-policy)

### Scopes

- **AUT-58** (MUST) `ScopeSet.Parse` accepts the atproto scope grammar: `atproto`,
  `transition:generic`, `transition:chat.bsky`, `transition:email`, `repo:{collection|*}[?action=…]`,
  `rpc:{lxm|*}?aud={did#service|*}`, `blob:{mime pattern}`, `account:{attr}[?action=…]`,
  `identity:{attr|*}`, `include:{nsid}[?aud=…]`, and `space:{spaceType}[?authority=…][&skey=…]`
  `[&collection=…][&action=…][&manage=…]` ([SPC-63](spaces.md#oauth-scopes)). Unknown scope
  strings are kept verbatim and never match. Formatting round-trips.
  *Source:* [specs/permission](https://atproto.com/specs/permission) · Decision (0.8 could build
  scopes but not parse or match them; include parsing lived in `LexiconResolver.ParseIncludeScope`)
- **AUT-59** (MUST) Scope builders reject empty action sets (no silent widening).
  *Source:* 0.8 #94 (`AtProtoScopes.Repo` widened `RepoAction.None` to full write)
- **AUT-60** (MUST) `ScopeSet.Permits` implements the permission spec's matching rules (wildcards,
  action defaults, MIME patterns, `aud` matching); `atproto` alone grants nothing beyond identity.
  The exact-`atproto` check of AUT-43 uses `ScopeSet`.
  *Source:* [specs/permission](https://atproto.com/specs/permission)
- **AUT-61** (MUST) Generated permission-set types (Lexicon `permission-set` defs,
  [lexicon.md](lexicon.md)) format to `include:` scopes; the client never expands permission sets
  itself (the authorization server does).
  *Source:* [specs/permission](https://atproto.com/specs/permission) · 0.8 #130

### Space credentials

Spaces gate reads with short-lived credentials issued by a space's authority. They are an
authentication mechanism of the same rank as sessions and service auth, so they follow this
spec's model; the token formats, covered components and authority policy are in
[spaces.md](spaces.md).

- **AUT-75** (MUST) Space credentials plug into the session model as handlers. A
  `SpaceRequestHandler` (an `IXrpcHandler`) composes over the account's session: the
  `SpaceCredentialProvider` uses the session to fetch a delegation token from the account's PDS,
  exchanges it at the space authority for a space credential bound to a fresh in-memory key, and
  the handler attaches the credential with an RFC 9421 signature for one explicit audience.
  `XrpcInvoker`, `AtProtoSession` and `SessionData` have no space-credential slot.
  *Source:* **Decision** (Spaces is core; maintainer review 2026-10-17) · [SPC-23…SPC-29](spaces.md#credential-provider) ·
  0.8 `XrpcClient.cs:132-138,690-697` (Spaces signing inside the transport)
- **AUT-76** (MUST) Credential renewal follows the refresh rules of this spec: one mint in flight
  per space (single-flight, as AUT-7), a mint that has started is not cancelled by a caller giving
  up (as AUT-8), a replaced credential stays usable by requests already holding it, and every time
  read comes from the injected `TimeProvider` (AUT-70). The renewal window is in
  [spaces.md](spaces.md) (SPC-30).
  *Source:* 0.8 `SpaceCredentialProvider.cs:83,245` · tests `SpaceCredentialProviderTests`
- **AUT-77** (MUST) A request refused because its credential expired or was revoked discards that
  credential, mints once more and resends **once** when the body is replayable (as AUT-9). A
  second refusal surfaces as `SpaceCredentialException`. Revocation never ends the account session.
  *Source:* Proposal 0016, *Credential expiration and revocation* · proposal PR #109
  (revocation, shorter expiry)
- **AUT-78** (MUST) Space credentials, delegation tokens, client attestations and their keys live
  in memory only. `ISessionStore` persists the account session, never space credentials; a
  restarted process mints new ones. The provider owns the keys it generates and disposes them
  (AUT-71 for keys it was given).
  *Source:* **Decision** (credentials last minutes, SPC-20; persisting a key-bound bearer adds risk
  for no gain)
- **AUT-79** (MUST) The delegation token and the client attestation are each optional, as the
  protocol allows. A tokenless credential (no account session, for public spaces) is an explicit
  provider option combined with an `AnonymousHandler`, never a fallback taken when a session is
  missing; a client attestation is sent only when the authority asks for one or the caller
  configures it.
  *Source:* forum "Post-alpha plans" (2026-10-08: delegation token and client attestation both
  optional) · [SPC-28](spaces.md#credential-provider), [SPC-62](spaces.md#credential-provider)
- **AUT-80** (MUST) Writes to the account's own space repo go through the account session under a
  covering `space:` scope ([SPC-66](spaces.md#space-scoped-client)); they need no space
  credential. A session whose `GrantedScope` does not permit the write fails before sending, with
  the missing scope named, when the scope is known locally.
  *Source:* Proposal 0016, *OAuth scopes* · **Decision** (fail early, as AUT-60 makes scopes checkable)
- **AUT-81** (MUST) Verifying space tokens (authority and repo-host side) reuses this spec's
  machinery: the JOSE primitives (AUT-73), `IReplayStore` for single-use delegation tokens with the
  same consume-after-all-checks rule as AUT-68, and redaction (AUT-72) for every space token and
  key. There is no second JWT or replay implementation.
  *Source:* **Decision** · audit (0.8 `SpaceTokens` used the internal `Auth.Jwt` and its own replay checks)

### Service auth: issuing

- **AUT-62** (MUST) A service-auth token is a compact JWS (`alg` ES256 for P-256 keys, ES256K for
  K-256) with exactly the claims `iss` (bare DID), `aud` (a DID, optionally with `#service_id`),
  `exp`, `iat`, `jti` (unique) and `lxm` (the method NSID, required), and a `kid` header only when
  configured (a fragment such as `#atproto_label`; absent means `#atproto`).
  *Source:* [specs/xrpc](https://atproto.com/specs/xrpc) inter-service auth · 0.8
  `ServiceAuthGenerator.cs` · tests `CreateToken_WritesExactlyTheClaimsTheReferenceImplementationDoes`,
  `Constructor_KeyIdThatIsNotAFragment_Throws`, `CreateToken_NullLxm_Throws`
- **AUT-63** (MUST) Default lifetime is 60 s; a lifetime over 5 minutes is refused.
  *Source:* 0.8 `ServiceAuthGenerator.cs:98-99` · test `CreateToken_RejectsExpiryOver5Minutes`
- **AUT-64** (MUST) `ServiceAuthHandler` mints a new token per call with `aud` = its configured
  audience and `lxm` = the request NSID, writes `Authorization: Bearer {token}`, and never resends
  a token. `client.Server.GetServiceAuthAsync` (PDS-minted tokens) stays a generated call.
  *Source:* Decision (0.8 `SpaceServiceCall` attached service auth on a raw `HttpClient`, bypassing the transport)

### Service auth: verification

- **AUT-65** (MUST) `ServiceAuthVerifier` returns a `ServiceAuthVerification` (it does not throw
  for invalid tokens) whose `Error` uses the reference implementation's names: `BadJwt`,
  `BadJwtType`, `BadJwtIss`, `BadJwtAudience`, `BadJwtLexiconMethod`, `JwtExpired`,
  `BadJwtSignature`. Infrastructure failures that prevent a verdict (resolver unreachable) produce
  `BadJwt` without echoing the underlying reason.
  *Source:* 0.8 `ServiceAuthVerifier.cs` · test `VerifyAsync_IssuerThatDoesNotResolve_IsRefusedWithoutItsReason`
- **AUT-66** (MUST) Checks: `alg` is ES256 or ES256K and matches the key's curve; `typ` is absent
  or `JWT` (anything else, including `*+jwt`, is refused); `iss` is a bare DID; `kid` (absent means
  `#atproto`) is in `AllowedKeyIds` (default `{#atproto}`), and a full DID URL naming the issuer's
  own key is normalised to its fragment; `aud` is ordinally in the expected audiences; when a
  method is expected, `lxm` is required and ordinally equal (case-sensitive); `exp` and `iat` are
  required whole numbers; `exp` not past (30 s skew); `iat` not in the future (skew) and not older
  than the lifetime ceiling; `exp − iat` ≤ `MaxTokenLifetime` (5 min); `nbf`, if present, not in
  the future; `jti` present, ≤ 255 characters, printable ASCII. Time arithmetic is overflow-safe:
  out-of-range values are refused, never thrown.
  *Source:* 0.8 `ServiceAuthVerifier.cs:235-394` · tests `ServiceAuthVerifierTests` (all, incl.
  `VerifyAsync_TimeClaimOutsideTheRepresentableRange_IsRefusedNotThrown`,
  `VerifyAsync_MaxRepresentableExpiry_IsRefusedAsTooLongRatherThanOverflowing`,
  `VerifyAsync_MethodDifferingOnlyInCase_IsRefused`, `VerifyAsync_TypOfAnotherKindOfToken_IsRefused`)
- **AUT-67** (MUST) The signature is verified against the issuer's key from its DID document (via
  `IIdentityResolver`, with one uncached refresh on failure); high-S signatures are accepted,
  out-of-range scalars are refused.
  *Source:* 0.8 tests `VerifyAsync_SignedByAnotherKey_IsRefusedAfterOneRefresh`,
  `VerifyAsync_KeyRotatedSinceTheCachedDocument_VerifiesAgainstTheRefreshedOne`,
  `VerifyAsync_HighSSignature_IsAccepted`, `VerifyAsync_SignatureScalarOutOfRange_IsRefusedNotThrown`
- **AUT-68** (MUST) The `jti` is consumed in `IReplayStore` (keyed by issuer, `jti`, `exp` + skew)
  **only after** every other check has passed, signature included. A forged token never spends the
  genuine token's `jti`; a token refused for its audience or method does not spend its `jti`; a
  `jti` the store cannot accept is refused, not thrown.
  *Source:* 0.8 tests `VerifyAsync_ForgedToken_DoesNotSpendTheGenuineTokensJti`,
  `VerifyAsync_RefusedForItsAudience_DoesNotSpendItsJti`, `VerifyAsync_JtiThatCannotBeSpent_IsRefusedNotThrown`,
  `VerifyAsync_SameTokenTwice_IsRefusedTheSecondTime`
- **AUT-69** (MUST) Verifier options are validated once at construction (at least one allowed
  `kid`, each a fragment; at least one audience per expectation), not per request.
  *Source:* 0.8 `ServiceAuthVerifier.cs:71-77` · Decision (0.8's handler rebuilt the verifier per request,
  `AtProtoServiceAuthentication.cs:115`)

### Cross-cutting

- **AUT-70** (MUST) Every time read in this spec (expiry, proactive refresh, DPoP `iat`, client
  assertions, service-auth `iat`/`exp`, nonce and metadata cache ages, background timers) comes
  from the injected `TimeProvider`.
  *Source:* [AG-28](../03-api-guidelines.md#resources-and-lifetimes) · fixes 0.8 wall-clock reads in
  `DPoPProofGenerator.cs:155`, `ServiceAuthGenerator.cs:102`, `AtProtoOAuthService.cs:573`
- **AUT-71** (MUST NOT) No component disposes a key it was given (`OAuthClientKey`, `SigningKey`,
  DPoP keys restored from data are owned by the session that imported them).
  *Source:* [AG-26](../03-api-guidelines.md#resources-and-lifetimes) · fixes 0.8 `ServiceAuthGenerator.Dispose` disposing the caller's key
- **AUT-72** (MUST NOT) `ToString()` of sessions, session data, credentials, keys, tokens and
  OAuth pending state never includes tokens, passwords, private keys, PKCE verifiers or proofs;
  logs and exception messages never include them.
  *Source:* 0.8 tests `Credentials_ToString_NeverShowsTheTokens`, `ToString_NamesTheAccountButNotItsCredentials`
- **AUT-73** (MUST) JWS encoding, strict decoding (strict base64url, three segments, JSON objects),
  JWK and RFC 7638 thumbprints come from `ATProtoNet.Crypto.Jose` in Primitives
  ([crypto.md](crypto.md)); this spec adds no second JWT implementation.
  *Source:* Decision (0.8 `Jwt` lived in `Auth`, JWK in `OAuthModels`, thumbprint in `DPoP`, used by Spaces)
- **AUT-74** (MUST) OAuth wire DTOs (AS/resource metadata, PAR and token responses, error
  bodies) are `internal`; the public surface is `OAuthClient`, its options and metadata,
  `OAuthSession`, `OAuthClientKey`, `ScopeSet`/`Scope`, `OAuthException` and the hint/callback
  records.
  *Source:* Decision (0.8 exposed 294 public API entries in the OAuth namespace)

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `RefreshSkew` | 60 s | proactive refresh (AUT-6) |
| `RefreshTimeout` | 30 s | one refresh exchange (AUT-8) |
| `BackgroundRetryDelay` / max | 30 s / 10 min | background refresh backoff (AUT-21) |
| `MaxTimerDelay` | 7 days | background timer step |
| `OAuthClient.AuthorizationLifetime` | 10 min | pending authorization (AUT-40) |
| `MaxStateLength` | 256 chars | callback `state` lookup (AUT-42) |
| `MaxAppStateLength` | 4,096 chars | caller app state |
| `MaxOAuthResponseBytes` | 64 KiB | AS responses and metadata |
| `MetadataTimeout` | 10 s | metadata fetch |
| `RequestTimeout` | 30 s default | one AS request incl. nonce retry |
| `MetadataCacheLifetime` / capacity | 5 min / 256 | AUT-37 |
| `DpopNonceCache` capacity / max nonce | 1,024 origins / 1,024 chars | AUT-55 |
| `ClientAssertionLifetime` | 60 s | AUT-52 |
| PKCE verifier / state entropy | 32 bytes each | AUT-38 |
| Service-auth default / max lifetime (issue) | 60 s / 5 min | AUT-63 |
| Verifier `ClockSkew` / `MaxTokenLifetime` | 30 s / 5 min | AUT-66 |
| `MaxTokenIdLength` | 255 chars | `jti` |
| Space credential default / max lifetime | 10 min / 60 min | owned by [spaces.md](spaces.md#limits) (SPC-20); AUT-76 |

## Errors

| Failure | Raised as |
|---|---|
| Invalid configuration (keys, scope, redirect URI) | `ArgumentException` at construction / call start |
| OAuth protocol failure (discovery, metadata, PAR, token, issuer binding, nonce) | `OAuthException` (`Error`: OAuth or SDK code: `invalid_metadata`, `verification_failed`, `auth_server_mismatch`, `did_mismatch`, `invalid_scope`, `unsupported_client_auth`, `client_key_unavailable`, `request_timeout`, `request_failed`, `server_error`, …) |
| Password login/refresh refused | `XrpcAuthenticationException` from the generated call |
| Session ended, call attempted | `XrpcAuthenticationException(AuthenticationRequired, StatusCode = null)` |
| Session disposed | `ObjectDisposedException` |
| Sign-out partial failure | the remote failure, or `AggregateException` (local + remote) |
| Invalid service-auth token | `ServiceAuthVerification { IsValid = false, Error = … }` (no exception) |
| Untrusted-fetch failure during discovery | `OAuthException` wrapping `UntrustedFetchException` |
| Space credential refused, not bound, revoked twice, space deleted | `SpaceCredentialException` with a `Reason` ([spaces.md](spaces.md#errors)) |
| Own space write without a covering `space:` scope (AUT-80) | `XrpcAuthenticationException` with the scope named, before sending |

## Conformance and tests

- **Conformance** ([04 — Testing](../04-testing.md#conformance-areas), "Tokens"): the
  service-auth verdict table (from `ServiceAuthVerifierTests`), DPoP proof and PKCE vectors, the
  AS-metadata validation table, RFC 7517 example key vectors.
- **Port as scenarios (phase 5):** `PasswordSessionLifecycleTests`, `SessionRaceTests`,
  `SessionRefreshCoordinatorTests`, `SessionTests`; plus new CAS scenarios (two instances, no
  coordinator, same refresh token).
- **Port as scenarios (phase 6):** `OAuthSessionLifecycleTests`, `OAuthTransportTests`,
  `OAuthRequestFailureTests`, `OAuthCallbackHandleVerificationTests`,
  `AuthorizationServerMetadataValidationTests`, `ConfidentialClientTests`, `DPoPProofGeneratorTests`,
  `DPoPTests`, `DPoPNonceCacheTests`, `PkceGeneratorTests`, `SecurityTests`, `AtProtoScopesTests`,
  `MetadataFetchPolicyTests`, `DynamicPdsTests`, `ServiceAuthGeneratorTests`, `ServiceAuthVerifierTests`,
  `JwtTests` (to Primitives); `space:` scope parsing and matching vectors from the pinned proposal;
  credential renewal and resend scenarios from `SpaceCredentialProviderTests` and
  `SpaceCredentialMintTests` (on `FakeTimeProvider`), shared with [spaces.md](spaces.md).
- **Regression corpus** ([04 — Testing](../04-testing.md#regression-corpus)): K-256 key cannot
  sign an ES256 DPoP proof; service-auth time claims outside range refused not thrown; missing or
  unusable `jti` refused; forged token does not spend the genuine `jti`; audience refusal does not
  spend the `jti`; callback resolves afresh and handle timeout yields `handle.invalid` (#42);
  client metadata omits unset fields (#41); after an account switch a rejected call is never resent
  with the other account's token; a call waiting out a 429 is resent to its own service with its
  own token; a rotated refresh token is stored even if the caller gives up or the session is
  disposed.
- **Drop:** `OAuthModelsTests` (DTO round-trips), tests of the 0.6 session format,
  `ReApplyingWithoutAnOAuthClient_KeepsTheOneGivenBefore` and `RefreshSessionAsync_WithoutAnOAuthClient_Throws`
  (the situation no longer exists).

## Changes from 0.8

| Change | Reason |
|---|---|
| Sessions are handlers; no `client.LoginAsync`/`ApplySessionAsync`/`ResumeSessionAsync`/`TryRestoreSessionAsync` | D-06 |
| `OAuthSession` refreshes and revokes itself; no `OAuthClient` passed alongside | AUT-2; removes a silent-staleness bug class |
| Store compare-and-swap; refusal final only if the store agrees | D-22, AUT-14; multi-instance correctness without a distributed lock |
| One scope source (client metadata), requests may only narrow it | AUT-31 |
| `ScopeSet` parses and matches | AUT-58 |
| DPoP nonce cache owned by the `OAuthClient` instance | AG-10 (no process-global state) |
| Resource-side nonce retry requires `use_dpop_nonce` | AUT-56; consistent with the AS path |
| `auth_server_mismatch` ends the session | 0.8 retried it forever |
| Service-auth issuing and verification live in the base `ATProtoNet` package (`ATProtoNet.Auth.ServiceAuth`; the verifier moved from `ATProtoNet.Server`) | it has no ASP.NET dependency; `ATProtoNet.AspNetCore` only wires it into an authentication scheme |
| Verifier returns a verdict instead of throwing | invalid tokens are an expected outcome |
| `ServiceAuthIssuer` does not dispose its key | AG-26 |
| 0.6 and 0.8 stored sessions are read only by the one-off migration helper, not the hot path | Compatibility code stays out of the session path; users still keep their sign-ins |
| OAuth wire DTOs internal | AUT-74 |
| Space credentials are an authentication mechanism (`SpaceRequestHandler : IXrpcHandler`), not transport code | AUT-75; 0.8 signed Spaces requests inside `XrpcClient` |
| `ScopeSet` parses and matches `space:` scopes | AUT-58; Spaces is a core data protocol |
| `AutoRefreshSession = false` removed | refresh is the session's job; callers who never want refresh use the generated `createSession` and an `AnonymousHandler` with their own bearer authorizer |

## Open points

Defaults accepted (maintainer, 2026-10-17). The implementing phase applies them without further
sign-off, settles any point left to it, follows upstream where a point waits on upstream, and
records the outcome here in the same PR.

1. **Bearer-token handler for externally managed tokens.** Some apps hold access tokens from
   elsewhere. Default: ship `BearerTokenHandler(Func<CancellationToken, ValueTask<string>>)` with
   no refresh; decide in phase 5 whether it is public.
2. **atproto loopback client IDs** (`http://localhost?redirect_uri=…&scope=…`) for development
   without hosting client metadata. 0.8 does not support them; Q-9 suggests them as the
   replacement for the cookie relay. Default: support them in phase 6 per [specs/oauth](https://atproto.com/specs/oauth).
3. **Event vs. observable for `Changed`.** Default: a plain .NET event (AG-18 concerns streams,
   not state-change notifications).
4. **Storing DPoP private keys.** Session data carries the PKCS#8 DPoP key; encryption at rest is
   the store's job ([stores.md](stores.md)). Default: the in-memory store does not encrypt; file
   and EF stores (and any custom shared store) must.
5. **Token-response `expires_in` absent.** Default: treat the access token as expiring after
   `RefreshSkew` × 2 (forcing early refresh) rather than never; 0.8 stored `null` expiry.
6. **`ServiceAuthHandler` audience per call.** Some callers proxy to several services. Default:
   one audience per handler; use one handler per audience.
7. **Space credentials across instances.** Several instances serving one account each mint their
   own credentials (AUT-78). Default: accept it (credentials are cheap and short-lived); revisit if
   an authority rate-limits `getSpaceCredential` per account.
