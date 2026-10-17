# Identity: fetch policy, DIDs, handles, PLC

| | |
|---|---|
| **Status** | Draft |
| **Package** | `ATProtoNet` (namespaces `ATProtoNet.Net`, `ATProtoNet.Identity`, `ATProtoNet.Identity.Plc`; network layer, above the foundation namespaces) |
| **Phase** | 4 (space host and key-agreement lookups included) |
| **Depends on** | [syntax](syntax.md), [data-model](data-model.md) (DRISL for PLC operations), [crypto](crypto.md) (`DidKey`, `VerificationKey`, `SigningKey`, `SignedObject`) |
| **Normative references** | [DID](https://atproto.com/specs/did) · [Handle](https://atproto.com/specs/handle) · [Cryptography](https://atproto.com/specs/cryptography) · [did:plc](https://web.plc.directory/spec/v0.1/did-plc) · [did:web method](https://w3c-ccg.github.io/did-method-web/) · reference `@atproto/identity` (cache semantics) · DID-document entries for Spaces per [spaces.md](spaces.md) (proposal 0016 at its pin) |

## Scope

**In:** the untrusted-fetch layer every component uses to reach attacker-chosen hosts
([D-21](../06-decisions.md#d-21-one-untrusted-fetch-layer)); DNS TXT lookups; DID documents;
`did:plc` and `did:web` resolution; handle resolution and bidirectional verification; caching;
verify-with-refresh; the PLC directory client and typed PLC operations; the DID-document entries
Spaces adds (the space host service, the space signing key and the key-agreement key used by
the space commitment scheme), read with the same typed, strict lookups as every other entry.

**Out:** the PLC export *stream* (a WebSocket event stream; [streaming](streaming.md) owns it on
the shared stream runtime, reusing this spec's `PlcOperation` types); OAuth discovery
([auth](auth.md), which *uses* the fetch layer and resolver); Lexicon resolution
([lexicon](lexicon.md), which uses `IDnsTxtResolver` for `_lexicon` records); service URL rules
for XRPC targets ([xrpc-client](xrpc-client.md)); identifier syntax ([syntax](syntax.md)).

## Public API sketch

```csharp
namespace ATProtoNet.Net;

public sealed record UntrustedFetchPolicy
{
    public bool AllowPrivateNetworks { get; init; }                 // development opt-out (loopback, private, http, ports)
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxConcurrentFetches { get; init; } = 64;
    public int MaxResponseHeadersBytes { get; init; } = 16 * 1024;
}

// Owns the hardened handler and the concurrency gate. One instance per application (hosting
// registers a singleton); no process-wide statics.
public sealed class UntrustedHttp : IDisposable
{
    public UntrustedHttp(UntrustedFetchPolicy? policy = null, TimeProvider? time = null);
    public UntrustedFetchPolicy Policy { get; }
    public bool IsEnforced { get; }                                 // false on platforms without connect callbacks (browser WASM)
    public HttpClient CreateClient();                               // policy handler, no proxy, no redirects
    public Task<UntrustedResponse> GetAsync(Uri url, UntrustedRequest request, CancellationToken ct = default);
}
public sealed record UntrustedRequest
{
    public required int MaxBodyBytes { get; init; }
    public string? Accept { get; init; }
    public int MaxSameHostRedirects { get; init; }                  // 0 unless the caller's protocol allows redirects
    public HttpClient? Client { get; init; }                        // caller-supplied; still checked (IDN-6)
}
public sealed class UntrustedResponse : IDisposable { public HttpStatusCode Status { get; } public ReadOnlyMemory<byte> Body { get; } public Uri FinalUrl { get; } }

public sealed class UntrustedFetchException : AtProtoException
{
    public UntrustedFetchError Error { get; }    // Blocked, Timeout, ResponseTooLarge, HttpStatus, Network, RedirectRefused, Malformed
    public HttpStatusCode? Status { get; }
    public Uri Url { get; }
}

public interface IDnsTxtResolver { Task<IReadOnlyList<string>?> QueryAsync(string name, CancellationToken ct = default); }
public sealed class DnsOverHttpsTxtResolver(UntrustedHttp http, Uri? endpoint = null) : IDnsTxtResolver;  // default https://dns.google/resolve

namespace ATProtoNet.Identity;

public sealed class DidDocument
{
    public Did Id { get; }
    public IReadOnlyList<string> AlsoKnownAs { get; }
    public IReadOnlyList<VerificationMethod> VerificationMethods { get; }
    public IReadOnlyList<DidService> Services { get; }
    public static DidDocument Parse(ReadOnlySpan<byte> json);       // lenient shapes, strict values (IDN-12..IDN-14)

    public Handle? GetHandle();                                     // first at:// entry only
    public DidKey? GetSigningKey();                                 // #atproto
    public DidDocumentEntry<DidKey> TryGetVerificationKey(string fragment);
    public DidDocumentEntry<Uri> TryGetServiceEndpoint(string fragment, string? type = null);
    public Uri? GetPdsEndpoint();                                   // #atproto_pds, AtprotoPersonalDataServer

    // Spaces entries (IDN-55…IDN-59); identifiers pinned with the spaces spec
    public DidDocumentEntry<Uri> TryGetSpaceHostEndpoint();          // #atproto_space_host, AtprotoSpaceHost
    public Uri? GetSpaceHostEndpoint();                              // the entry, else the PDS (IDN-56)
    public DidDocumentEntry<DidKey> TryGetSpaceSigningKey();         // #atproto_space, else #atproto (IDN-57)
    public DidDocumentEntry<KeyAgreementPublicKey> TryGetKeyAgreementKey();   // IDN-58
}
public readonly record struct DidDocumentEntry<T>(DidDocumentEntryStatus Status, T? Value);   // Found, Absent, Malformed

public interface IDidResolver
{
    Task<DidDocument> ResolveAsync(Did did, CancellationToken ct = default);   // may be cached
    Task<DidDocument> RefreshAsync(Did did, CancellationToken ct = default);   // fetch past the cache, rate-limited per DID
    Task<DidDocument> FetchFreshAsync(Did did, CancellationToken ct = default);// fetch past the cache, no rate limit (local security decisions)
    ValueTask InvalidateAsync(Did did, CancellationToken ct = default);
}
public interface IHandleResolver
{
    Task<Did?> ResolveAsync(Handle handle, CancellationToken ct = default);   // null: no answer
    ValueTask InvalidateAsync(Handle handle, CancellationToken ct = default);
}
public interface IIdentityResolver
{
    Task<ResolvedIdentity> ResolveAsync(AtIdentifier identifier, IdentityResolveOptions? options = null, CancellationToken ct = default);
    ValueTask InvalidateAsync(AtIdentifier identifier, CancellationToken ct = default);
}
public sealed record IdentityResolveOptions { public bool BypassCache { get; init; } }
public sealed record ResolvedIdentity(Did Did, Handle? Handle, bool HandleVerified, DidDocument Document)   // null: no handle claimed
{
    public Uri? Pds => Document.GetPdsEndpoint();
    public DidKey? SigningKey => Document.GetSigningKey();
    public Uri? SpaceHost => Document.GetSpaceHostEndpoint();
}

public sealed class DidResolver(IReadOnlyDictionary<string, IDidResolver> methods) : IDidResolver;     // dispatch on method
public sealed class DidWebResolver(UntrustedHttp http, DidWebOptions? options = null) : IDidResolver;
public sealed class CachingDidResolver(IDidResolver inner, DidCacheOptions? options = null,
    IDistributedCache? shared = null, TimeProvider? time = null) : IDidResolver;
public sealed class HandleResolver(UntrustedHttp http, IDnsTxtResolver? dns, HandleResolverOptions? options = null) : IHandleResolver;
public sealed class IdentityResolver(IDidResolver dids, IHandleResolver handles) : IIdentityResolver;
public static class IdentityResolverFactory { public static IIdentityResolver CreateDefault(UntrustedHttp http, IdentityDefaults? d = null); }

public static class DidResolverExtensions
{
    public static Task<KeyVerification> VerifyWithRefreshAsync(this IDidResolver resolver, Did did,
        Func<DidDocument, DidKey?> selectKey, Func<VerificationKey, bool> verify, CancellationToken ct = default);
}
public readonly record struct KeyVerification(bool Verified, DidKey? KeyTried);

public sealed class IdentityResolutionException : AtProtoException
{
    public IdentityResolutionError Error { get; }  // InvalidDid, UnsupportedMethod, NotFound, Deactivated, InvalidDocument,
                                                  // HandleNotFound, HandleConflict, Unavailable (inner: UntrustedFetchException)
    public AtIdentifier? Identifier { get; }
}

namespace ATProtoNet.Identity.Plc;

public sealed class PlcClient(UntrustedHttp http, Uri? directory = null) : IDidResolver
{
    public Task<IReadOnlyList<PlcLogEntry>> GetOperationLogAsync(Did did, CancellationToken ct = default);
    public Task<IReadOnlyList<PlcAuditEntry>> GetAuditLogAsync(Did did, CancellationToken ct = default);
    public Task<PlcLogEntry> GetLastOperationAsync(Did did, CancellationToken ct = default);
    public Task<PlcState> GetStateAsync(Did did, CancellationToken ct = default);           // /data
    public Task SubmitAsync(SignedPlcOperation operation, CancellationToken ct = default);
    public Task<PlcExportPage> ExportAsync(long after = 0, int count = 1000, CancellationToken ct = default);
    public Task<bool> IsHealthyAsync(CancellationToken ct = default);
}
public closed record PlcOperation { public Cid? Prev { get; init; } }                        // closed (AG-6a)
public sealed record PlcUpdate : PlcOperation                                                // type "plc_operation" (genesis when Prev is null)
{
    public required IReadOnlyList<DidKey> RotationKeys { get; init; }
    public required IReadOnlyDictionary<string, DidKey> VerificationMethods { get; init; }
    public required IReadOnlyList<string> AlsoKnownAs { get; init; }
    public required IReadOnlyDictionary<string, PlcService> Services { get; init; }
}
public sealed record PlcTombstone : PlcOperation;                                            // type "plc_tombstone"
public sealed record PlcLegacyCreate : PlcOperation;                                         // type "create", read-only
public sealed class SignedPlcOperation
{
    public static SignedPlcOperation Sign(PlcOperation operation, SigningKey rotationKey);
    public PlcOperation Operation { get; }
    public ReadOnlyMemory<byte> Signature { get; }
    public Cid Cid { get; }                     // what the next operation's prev names
    public Did DeriveDid();                     // genesis only
    public bool Verify(DidKey rotationKey);
    public ReadOnlyMemory<byte> ToJson();       // submission body
}
public sealed class PlcOperationRejectedException : AtProtoException { public string? DirectoryMessage { get; } }
```

## Requirements

<a id="network-policy"></a>

### Untrusted fetch layer (`ATProtoNet.Net`)

- **IDN-1** (MUST) Every connection made through `UntrustedHttp` checks **every** address DNS
  returns for the host (not only the first) and connects only to those addresses. One
  non-public address refuses the connection.
  *Source:* 0.8 `IdentityNetworkPolicy.cs:140-170` (a name with one public and one private address
  would otherwise reach the private one) · test `IdentityFetchPolicyTests.HardenedHandler_NameResolvingToLoopback_IsRefusedWithoutConnecting`
- **IDN-2** (MUST) Public means: IPv4 outside `0/8, 10/8, 100.64/10, 127/8, 169.254/16, 172.16/12,
  192.0.0/24, 192.0.2/24, 192.88.99/24, 192.168/16, 198.18/15, 198.51.100/24, 203.0.113/24,
  224/4 and above`; IPv6 inside `2000::/3` and outside `2001::/23, 2001:db8::/32, 2002::/16,
  3fff::/20`. IPv4-mapped IPv6 and NAT64 `64:ff9b::/96` are judged by their IPv4 address.
  *Source:* 0.8 `IdentityNetworkPolicy.cs:83-136` · test `IdentityFetchPolicyTests.IsPublicAddress_Table_MatchesPolicy`
  (port the table verbatim; consider `System.Net.IPNetwork` for the implementation)
- **IDN-3** (MUST) The handler uses no proxy, does not follow redirects automatically, enforces
  the connect timeout and the response-header limit, and decompresses nothing it was not asked to.
  *Source:* 0.8 `IdentityNetworkPolicy.cs:29-60`
- **IDN-4** (MUST) Every fetch has a total time budget (`RequestTimeout`) covering connect,
  headers and body; caller cancellation propagates as `OperationCanceledException`, never as
  `Timeout`.
  *Source:* tests `DidWebResolverTests.ResolveAsync_HostNeverAnswers_TimesOutWithinTheRequestTimeout`,
  `ResolveAsync_CallerCancels_PropagatesCancellation`
- **IDN-5** (MUST) Bodies are read up to the request's `MaxBodyBytes`; a larger body, declared or
  actual, is `ResponseTooLarge` and is not buffered past the cap. A body cut short is `Network`.
  *Source:* 0.8 `BoundedContent` · tests `ResolveAsync_BodyOverTheCap_IsRefused`,
  `IdentityHardeningTests.DidWeb_BodyCutShort_IsANetworkErrorAndIsRemembered`
- **IDN-6** (MUST) A response is refused (`RedirectRefused`) when its final URL differs from the
  requested URL, even when a caller-supplied `HttpClient` followed the redirect itself. Callers that
  allow redirects (handle well-known) follow them explicitly: same host, `https` only, at most
  `MaxSameHostRedirects`.
  *Source:* 0.8 `IdentityFetch.cs:148` · tests `IdentityHardeningTests.DidWeb_RedirectToAnotherOrigin_IsRefusedWithoutFollowing`,
  `DidWeb_CallersClientThatFollowedARedirect_IsRefused`, `HandleWellKnown_OnlySameHostHttpsRedirectsAreFollowed`
- **IDN-7** (MUST) At most `MaxConcurrentFetches` fetches run at once per `UntrustedHttp` instance;
  further fetches wait (subject to their budget). The gate belongs to the instance, not the process.
  *Source:* 0.8 `IdentityFetch.cs:18-20` (process-wide static) · test `IdentityHardeningTests.Fetches_AreCappedProcessWide`;
  **Decision** D-21 (instance-owned)
- **IDN-8** (MUST) Only `https` URLs are fetched unless `AllowPrivateNetworks` is set, which also
  permits loopback and private addresses and explicit ports. The opt-out is for development and is
  logged at `Warning` once per instance.
  *Source:* 0.8 `IdentityResolverOptions.AllowPrivateNetworks` · tests `DevelopmentHandler_Loopback_IsReachable`,
  `HardenedHandler_PrivateAddressLiteral_IsRefused`
- **IDN-9** (MUST) On platforms where the connect callback cannot run (browser WebAssembly),
  `IsEnforced` is `false` and the factory logs that the address policy is not enforced. Body caps,
  timeouts and redirect rules still apply.
  *Source:* 0.8 behaviour (no address check on WASM, silently) · **Decision**: make it observable
- **IDN-10** (MUST) Failures surface as `UntrustedFetchException` with the error kind, URL and
  status. Callers (identity, OAuth, Lexicon resolution, repo fetchers) wrap it in their own
  exception as the inner exception; none of them catches another component's exception type to
  detect a fetch failure.
  *Source:* D-21 (0.8 OAuth and Lexicon code catch `DidResolutionException` for non-identity fetches)

### DNS TXT

- **IDN-11** (MUST) `DnsOverHttpsTxtResolver` queries `{endpoint}?name={name}&type=TXT` (the JSON API
  Google and Cloudflare serve) through `UntrustedHttp`, requires an `https` endpoint, caps the
  response at 64 KiB, reassembles each record from its character-strings, and returns `null` when
  the endpoint answers with an HTTP error (no answer, not a failure). The default endpoint is
  `https://dns.google/resolve`.
  *Source:* 0.8 `DnsTxtLookup.cs`, `IdentityResolverOptions.cs:10` · tests
  `HandleResolverTests.ResolveAsync_DnsRecordSplitIntoCharacterStrings_IsReassembled`,
  `ResolveAsync_ConfiguredDnsEndpoint_IsQueriedInsteadOfTheDefault`, `Constructor_PlainHttpDnsEndpoint_IsRefused`
- **IDN-11a** (SHOULD) The docs state that DNS-over-HTTPS discloses looked-up handles to the DoH
  provider, and `IDnsTxtResolver` lets an application plug in its own resolver.
  *Source:* **Decision** D-14

### DID documents

- **IDN-12** (MUST) Parsing tolerates the shapes real documents use: a missing or `null` list reads
  as empty, `@context` may be a single string or contain inline objects, a structured (non-string)
  service endpoint reads as absent. It refuses values that cannot be trusted: a `null` *entry*
  inside a list, an entry of the wrong shape, an `id` that is not a valid DID.
  *Source:* 0.8 `DidDocument.cs` (#120) · tests `DidDocumentTests.Deserialize_SingleStringContextAndInlineContextObjects_AreTolerated`,
  `Deserialize_StructuredServiceEndpoint_IsReadAsAbsent`, `Deserialize_MissingOptionalLists_AreEmpty`,
  `Deserialize_InvalidId_Throws`, `IdentityHardeningTests.DidDocument_NullOrMisshapenEntry_FailsToParse`
- **IDN-13** (MUST) Lookups by id use the **first** entry with that id: if it has the wrong type or
  unusable value, the result is `Malformed` and later entries with the same id are not consulted.
  Both `#frag` and `{did}#frag` match; a fragment of another DID does not.
  *Source:* 0.8 `DidDocument.cs:123-234` · tests `IdentityHardeningTests.Lookups_WithDuplicateIds_TakeTheFirstEntry`,
  `DidDocumentTests.GetServiceEndpoint_MatchesBareAndDidQualifiedIds`, `GetServiceEndpoint_AnotherDidsQualifiedId_DoesNotMatch`,
  `GetServiceEndpoint_WrongType_DoesNotMatch`, `DidDocumentKeyTests.GetVerificationKey_AcceptsAFragmentWithoutItsHash`
- **IDN-14** (MUST) `Get*` lookups treat unusable entries as absent; `TryGet*` lookups report
  `Found`, `Absent` or `Malformed`, so security-relevant callers can refuse a malformed entry instead
  of falling back.
  *Source:* tests `TryGetServiceEndpoint_TellsAMalformedEntryFromAnAbsentOne`,
  `TryGetVerificationKey_TellsAMalformedEntryFromAnAbsentOne`
- **IDN-15** (MUST) A service endpoint is usable only if it is an absolute `http` or `https` URL.
  *Source:* test `GetServiceEndpoint_NotAnAbsoluteHttpUrl_IsAbsent`
- **IDN-16** (MUST) Verification method types: `Multikey` passes `publicKeyMultibase` through as a
  `did:key`; `EcdsaSecp256r1VerificationKey2019` and `EcdsaSecp256k1VerificationKey2019` carry an
  uncompressed point, which is validated, compressed and re-tagged with the matching multicodec.
  Unknown types and entries without key material read as absent; malformed legacy key material is
  `Malformed`.
  *Source:* #98 · 0.8 `DidDocument.cs:383` · tests `DidDocumentKeyTests.ToDidKey_*`, `GetSigningKey_ReadsTheLegacyAtprotoEntry`,
  `GetSigningKey_KeyMaterialThatDoesNotDecode_ReturnsNullRatherThanThrowing`
- **IDN-17** (MUST) `GetHandle` reads only the first `alsoKnownAs` entry starting with `at://`;
  if that entry is not a valid handle (or is `at://@`), the result is null and later entries are
  not consulted.
  *Source:* 0.8 `DidDocument.cs:85` · test `DidDocumentTests.GetHandle_ReadsOnlyTheFirstAtUriEntry`
- **IDN-18** (MUST) Well-known ids and types are constants: `#atproto` (signing key),
  `#atproto_label` (label key), `#atproto_pds` / `AtprotoPersonalDataServer`, `#atproto_labeler` /
  `AtprotoLabeler`.
  *Source:* [Label spec](https://atproto.com/specs/label), 0.8 `DidDocument.cs:19-28`

### Spaces entries in DID documents

Spaces are part of the protocol, so resolving where an account's spaces live and which keys
they use is ordinary identity resolution, not Spaces-specific code.

- **IDN-55** (MUST) The well-known constants (IDN-18) include the space host service
  (`#atproto_space_host`, type `AtprotoSpaceHost`), the space signing key (`#atproto_space`) and
  the key-agreement key of the space commitment scheme, with the identifiers and types of the
  pinned proposal ([spaces.md](spaces.md)). They live in one place, so an identifier change
  upstream is a one-line change.
  *Source:* 0.8 `Spaces/SpaceAuthority.cs:25-31` · forum "Post-alpha plans" (DID docs) ·
  "Space commitment changes" (new key in DID documents)
- **IDN-56** (MUST) `GetSpaceHostEndpoint` returns the `#atproto_space_host` endpoint when the
  entry is present and usable, and the PDS endpoint when the document has no such entry. A
  present but malformed entry (wrong type, non-HTTPS or non-absolute endpoint) is an error, not
  a fallback to the PDS.
  *Source:* 0.8 `SpaceCredentialProvider.cs:311-320`, `SpaceAuthority.cs:16-18` (#133) · test
  `SpaceUriTests.HostAudience_NamesTheAuthoritysSpaceHostServiceEntry`
- **IDN-57** (MUST) `TryGetSpaceSigningKey` returns the `#atproto_space` key when present; when
  the document has no such entry it falls back to `#atproto`. A present but malformed
  `#atproto_space` entry is `Malformed`, never a silent fallback.
  *Source:* 0.8 `Spaces/SpaceAuthority.cs:8-18`, `SpaceTokens.cs:176`
- **IDN-58** (MUST) `TryGetKeyAgreementKey` reads the key-agreement key the pinned commitment
  scheme names into a `KeyAgreementPublicKey` ([crypto.md](crypto.md), CRY-53). It never returns
  a signing key, even if no key-agreement key is published.
  *Source:* forum "Space commitment changes" (2026-10-12/13) · CRY-50
- **IDN-59** (MUST) The fallback semantics of IDN-56 and IDN-57 follow the pinned proposal.
  Upstream is still discussing whether dedicated entries are required, whether the space host is
  always the account host, and the account status of a space authority; the GA answer replaces
  these defaults in one place.
  *Source:* forum "Post-alpha plans" (DID docs, account status; 2026-10-01/08)

### DID resolution

- **IDN-19** (MUST) A fetched document's `id` must equal the requested DID **ordinally** (exact
  bytes). A document whose `id` differs, even only in host case, is `InvalidDocument`.
  *Source:* 0.8 `IdentityFetch.cs:199` (#125) · tests `DidWebResolverTests.ResolveAsync_IdDiffersOnlyInHostCase_IsRefused`,
  `ResolveAsync_ASpellingThatIsTheDocumentsOwnId_IsAccepted`, `PlcClientTests.ResolveAsync_DocumentForAnotherDid_IsRefused`.
  **Pinned:** 0.4 compared `did:web` ids case-insensitively, 0.7 changed to ordinal. Ordinal is
  the rule because a DID is compared as an exact string everywhere else (commit `did`, service-auth
  `iss`), and a case-folded match would let two distinct DID strings share one document.
- **IDN-20** (MUST) `DidResolver` dispatches on the method; a method with no resolver is
  `UnsupportedMethod`, and an identifier the method's rules refuse is `InvalidDid`. Neither sends a
  request.
  *Source:* tests `DidResolver_UnsupportedMethod_ThrowsUnsupportedMethod`, `ResolveAsync_RefusedIdentifier_SendsNoRequest`
- **IDN-21** (MUST) `did:plc` resolves with `GET {directory}/{did}`, built so the DID is a path
  segment under the directory URL (a directory path prefix is kept; the DID is never parsed as an
  absolute URI). HTTP 404 is `NotFound`, 410 is `Deactivated`, other errors `Unavailable`.
  *Source:* #47 · tests `PlcClientTests.ResolveAsync_ComposesTheDidOntoTheDirectoryUrl`,
  `ResolveAsync_DirectoryWithAPathPrefix_KeepsThePrefix`, `ResolveAsync_ErrorStatus_IsReportedByKind`,
  `GetPlcDataAsync_Tombstoned_IsDeactivated`
- **IDN-22** (MUST) `did:web` is hostname-only: a path-based DID, an IP literal, a single-label
  name or a malformed host is `InvalidDid`. A port (`%3A`) is allowed only on `localhost`, which is
  fetched over `http` and only when `AllowPrivateNetworks` is set (`Blocked` otherwise). Every other
  `did:web` is fetched from `https://{host}/.well-known/did.json`.
  *Source:* [DID spec, did:web in AT Protocol](https://atproto.com/specs/did) · 0.8 `DidWebResolver.cs` · test
  `IdentityFetchPolicyTests.DidWebResolutionUrl_Table_MatchesPolicy`
- **IDN-23** (MUST) A `did:web` host in a reserved TLD (the handle list, IDN-30) is `InvalidDid`,
  except `localhost` under IDN-22.
  *Source:* DID spec ("the same restrictions on top-level domains that apply to handles also apply
  to did:web domains"); new check if 0.8 lacks it
- **IDN-24** (MUST) Document fetches accept `application/did+ld+json` and `application/json`, cap
  the body at `MaxDidDocumentBytes` (default 64 KiB, minimum 1 KiB), and report an undecodable body
  as `InvalidDocument`.
  *Source:* 0.8 `IdentityFetch.cs:13`, `IdentityResolverOptions.cs:43` · test
  `IdentityHardeningTests.DidWeb_BodyThatDoesNotDecode_IsAnInvalidDocument`

### Handle resolution

- **IDN-25** (MUST) The DNS method queries TXT at `_atproto.{handle}`; records not starting with
  `did=` and values that are not valid DIDs are ignored; two or more distinct valid DIDs are
  `HandleConflict` (fail closed).
  *Source:* [Handle spec, DNS TXT method](https://atproto.com/specs/handle) · test
  `HandleResolverTests.ResolveAsync_DnsPublishesTwoDistinctDids_FailsClosed`
- **IDN-26** (MUST) The HTTPS method fetches `https://{handle}/.well-known/atproto-did`, reads at
  most 2,048 bytes, trims whitespace, and accepts the body only if it is a valid DID. Same-host
  `https` redirects are followed up to 3 times; a redirect to another host is no answer.
  *Source:* 0.8 `HandleResolver.cs:27-30` · tests `ResolveAsync_WellKnownBodyIsNotADid_IsIgnored`,
  `ResolveAsync_WellKnownBody_IsReadUpToExactlyTheCap`, `ResolveAsync_WellKnownRedirectedToAnotherHost_IsIgnored`
- **IDN-27** (MUST) Both methods start concurrently under one shared budget (default 5 s). A DNS
  answer is returned as soon as it arrives, without waiting for HTTPS. The HTTPS answer is used
  only when DNS gives no answer. When neither answers within the budget the result is `null`.
  *Source:* #193 · tests `ResolveAsync_DnsAnswers_DoesNotWaitForHttps`, `ResolveAsync_HttpsHangs_ReturnsTheDnsAnswerWithinTheBudget`,
  `ResolveAsync_OnlyHttpsAnswers_UsesIt`, `ResolveAsync_BothHang_ReturnsNullWithinTheBudget`, #52
- **IDN-28** (MUST) When DNS and HTTPS return different DIDs, **DNS wins**.
  *Source:* [Handle spec, resolution best practices](https://atproto.com/specs/handle) ("the DNS
  TXT result should be preferred") · test `ResolveAsync_AuthoritiesDisagree_DnsWins`.
  **Pinned:** 0.7 failed resolution on disagreement (#120), 0.8 switched to DNS-wins (#193).
  DNS-wins is the rule because the spec prefers it and because failing let an attacker who controls
  only the web host block resolution of a handle whose owner controls DNS.
- **IDN-29** (MUST) A handle whose `_atproto.` name would exceed 253 characters skips the DNS
  method and resolves over HTTPS only. DNS can be disabled by configuration (HTTPS only).
  *Source:* Handle spec (DNS name limit) · test `ResolveAsync_DnsDisabled_ResolvesOverHttpsOnly`
- **IDN-30** (MUST) Handles in the TLDs `alt arpa example internal invalid local localhost onion`
  are never resolved (no request is sent); `.test` resolves only when `AllowPrivateNetworks` is set.
  `handle.invalid` is never resolved.
  *Source:* Handle spec (reserved TLDs) · 0.8 `HandleResolver.cs:32-35` · tests `ResolveAsync_ReservedTld_SendsNoRequest`,
  `IdentityFetchPolicyTests.IsResolvableHandle_Table_MatchesPolicy`
- **IDN-31** (MUST) Only positive answers are cached (default 5 minutes); "no answer" is never
  cached. The handle cache uses `TimeProvider` and has its own capacity setting.
  *Source:* 0.8 `HandleCacheTtl` · tests `ResolveAsync_AnswerIsCached_SecondResolutionSendsNothing`,
  `ResolveAsync_NoAnswer_IsNotCached`; **Decision**: 0.8 used `TickCount64` and borrowed the DID
  cache's capacity (`HandleResolver.cs:75,89`)
- **IDN-32** (MUST) Caller cancellation propagates; a resolver exception from one method is treated
  as "no answer from that method".
  *Source:* test `ResolveAsync_CallerCancels_Propagates`

### Bidirectional verification

- **IDN-33** (MUST) Resolving a **handle**: resolve it to a DID (no answer → `HandleNotFound`),
  resolve the DID's document, and report `HandleVerified = true` only if the document's
  `GetHandle()` equals the input handle; otherwise the identity resolves with `Handle.Invalid`
  (`handle.invalid`) and `HandleVerified = false`.
  *Source:* Handle spec (bidirectional link) · tests `IdentityResolverTests.ResolveAsync_HandleClaimedBack_IsVerified`,
  `ResolveAsync_HandleNotClaimedBack_IsHandleInvalid`, `ResolveAsync_HandleThatDoesNotResolve_ThrowsHandleNotFound`
- **IDN-34** (MUST) Resolving a **DID**: resolve the document; if it claims a handle, resolve that
  handle and mark it verified only if it resolves back to the DID. Any failure of the handle step
  (no answer, timeout, conflict, exception) yields `handle.invalid`; it never fails the resolution.
  A document claiming no handle yields `Handle = null` with `HandleVerified = false` (distinct from a
  claimed but unverified handle).
  *Source:* tests `ResolveAsync_DidClaimingNoHandle_HasNoHandle`, `ResolveAsync_DidWhoseHandleResolvesBack_IsVerified`, `ResolveAsync_DidWhoseHandleResolvesElsewhere_IsHandleInvalid`,
  `ResolveAsync_DidWhoseHandleAuthoritiesFail_IsHandleInvalidButResolved`,
  `IdentityHardeningTests.IdentityResolver_HandleResolverThatThrows_IsHandleInvalidNotAFailure`
- **IDN-35** (MUST) `BypassCache` resolves the handle and the DID document past every cache (via
  `FetchFreshAsync` and handle invalidation) and updates the caches with the fresh results. It is
  for decisions the local user initiates (OAuth callback and refresh issuer checks, [auth](auth.md)).
  *Source:* 0.8 `ResolveUncachedAsync` · test `HandleResolverTests.IdentityResolver_ResolveUncachedAsync_LooksTheHandleUpAfresh`
- **IDN-36** (MUST) `IIdentityResolver.InvalidateAsync` and `IHandleResolver.InvalidateAsync` are
  interface members; no component downcasts to a concrete resolver to invalidate.
  *Source:* **Decision**: 0.8 `IdentityResolver.cs:118` downcasts to `HandleResolver.Forget`

### Caching

- **IDN-37** (MUST) `CachingDidResolver` serves a cached document while fresh (`StaleAfter`,
  default 1 h); between `StaleAfter` and `ExpireAfter` (default 24 h) it serves the cached document
  and refreshes in the background; past `ExpireAfter` it never serves the document and refetches
  first. `ExpireAfter < StaleAfter` is refused at construction.
  *Source:* `@atproto/identity` defaults · tests `CachingDidResolverTests.ResolveAsync_Fresh_IsServedFromMemory`,
  `ResolveAsync_Stale_ServesTheCachedDocumentAndRefreshesInTheBackground`, `ResolveAsync_HardLifetime_NeverServesADocumentPastIt`,
  `Options_ExpireBeforeStale_AreRefused`
- **IDN-38** (MUST) Resolution failures (`NotFound`, `Deactivated`, `InvalidDocument`, `Unavailable`)
  are remembered for `FailureTtl` (default 1 min) in a separate LRU, so failures never evict
  documents. Unexpected exceptions (bugs) are not remembered. A failed background refresh keeps the
  stale document and waits before retrying.
  *Source:* tests `ResolveAsync_Failure_IsRememberedForTheFailureTtl`, `ResolveAsync_UnexpectedException_IsNotRemembered`,
  `IdentityHardeningTests.Failures_DoNotEvictDocuments`, `ResolveAsync_StaleRefreshFails_KeepsServingTheDocumentAndWaitsBeforeRetrying`
- **IDN-39** (MUST) Concurrent misses for one DID share one fetch; the fetch completes for the
  remaining callers when one caller cancels; fetches start on the thread pool (not on the caller's
  synchronization context).
  *Source:* 0.8 `ResolutionCache` · tests `ResolveAsync_ConcurrentMisses_ShareOneFetch`,
  `ResolveAsync_OneCallerCancels_TheSharedFetchCompletesForTheOthers`
- **IDN-40** (MUST) `RefreshAsync` fetches past the cache at most once per `MinRefreshInterval`
  (default 30 s) per DID, counted from the last attempt including failed ones; within the interval
  it returns the cached document. `FetchFreshAsync` is not rate-limited.
  *Source:* 0.8 `DidCacheOptions.MinRefreshInterval` (forged signatures naming any DID must not each
  cost a directory request) · tests `RefreshAsync_WithinTheMinimumInterval_DoesNotFetchAgain`,
  `IdentityHardeningTests.RefreshAsync_AfterAFailedRefresh_TheFloorStillHolds`
- **IDN-41** (MUST) `InvalidateAsync` removes the entry from every tier and detaches an in-flight
  fetch, whose result is then discarded; a distributed-cache write still in flight cannot resurrect
  the old document.
  *Source:* tests `InvalidateAsync_DuringAFetch_DiscardsItsResult`,
  `IdentityHardeningTests.InvalidateAsync_WhileTheDistributedWriteIsInFlight_TheOldDocumentDoesNotSurvive`
- **IDN-42** (MUST) The optional `IDistributedCache` tier is best effort: failures fall back to
  fetching, and an entry whose stored DID differs from its key is ignored.
  *Source:* tests `DistributedCache_EntryUnderTheWrongKey_IsIgnored`, `DistributedCache_Failing_FallsBackToFetching`,
  `DistributedCache_ASecondInstanceReadsWhatTheFirstFetched`
- **IDN-43** (MUST) Capacities default to 10,000 documents and 1,000 failures, LRU-evicted. All
  timing reads `TimeProvider`.
  *Source:* 0.8 `DidCacheOptions` · test `ResolveAsync_OverCapacity_EvictsTheLeastRecentlyUsed`

### Verify with refresh

- **IDN-44** (MUST) `VerifyWithRefreshAsync` verifies against the key selected from the (possibly
  cached) document; if there is no key or verification fails, it calls `RefreshAsync` once and
  retries **only if the selected key changed**. A pass never refreshes. A selector that throws
  refuses the document without refreshing; resolution failures propagate.
  *Source:* Sync and Label specs (one refetch on signature failure) · 0.8 `DidResolverExtensions.cs` · tests
  `DidResolverExtensionsTests.VerifyWithRefreshAsync_*` (all six)
- **IDN-45** (MUST) The selector never falls back to another verification method (no `#atproto`
  fallback for `#atproto_label`, [labels](labels.md)).
  *Source:* test `LabelVerifierTests.VerifyAsync_NoLabelKeyPublished_DoesNotFallBackToTheAccountKey`

### PLC directory and operations

- **IDN-46** (MUST) `PlcClient` reaches the directory through `UntrustedHttp`; the directory URL
  must be `https` unless `AllowPrivateNetworks` is set. Requests use absolute URLs; an
  `HttpClient.BaseAddress` is never used.
  *Source:* tests `Constructor_PlainHttpDirectoryWithoutTheOptOut_IsRefused`, `Constructor_PlainHttpDirectoryWithTheOptOut_IsAccepted`
- **IDN-47** (MUST) Endpoints: `GET /{did}` (document), `/log`, `/log/audit`, `/log/last`, `/data`,
  `POST /{did}` (submit), `GET /export?after=&count=` (JSON lines, `count` 1–1,000), `/_health`.
  Response caps: log 8 MiB, export 32 MiB, error body 64 KiB.
  *Source:* 0.8 `PlcClient.cs:27-34` · tests `GetAuditLogAsync_ParsesEveryEntry`, `ExportAsync_ParsesTheSequencedJsonLines`,
  `ExportAsync_OutOfRangeArguments_Throw`, `IsHealthyAsync_*`
- **IDN-48** (MUST) A rejected submission throws `PlcOperationRejectedException` carrying the
  directory's message; it is not an identity-resolution error.
  *Source:* test `SubmitOperationAsync_Rejected_SurfacesTheDirectorysMessage`; **Decision**: 0.8 reported it as
  `DidResolutionException(OperationRejected)`
- **IDN-49** (MUST) Operations are typed: `plc_operation` (`PlcUpdate`; genesis when `prev` is null),
  `plc_tombstone`, and the legacy `create` (read-only). Reading the log or audit log maps each entry
  to its type; an unknown `type` is `InvalidDocument`.
  *Source:* [did:plc spec](https://web.plc.directory/spec/v0.1/did-plc); **Decision**: 0.8 wrote operations as
  `JsonObject` and supported only genesis
- **IDN-50** (MUST) A `PlcUpdate` has 1–5 rotation keys (K-256 or P-256 `did:key`),
  verification methods as a name → `did:key` map, `alsoKnownAs` URIs and services as id → `{type,
  endpoint}`; service endpoints are written without a trailing slash.
  *Source:* did:plc spec · tests `PlcOperationBuilderTests.CreateGenesisOperation_HasTheFieldsThePlcSpecRequires`,
  `CreateGenesisOperation_TrimsTrailingSlashFromEndpoint`, `CreateGenesisOperation_NoRotationKeys_Throws`,
  `CreateGenesisOperation_MoreThanFiveRotationKeys_Throws`
- **IDN-51** (MUST) Signing encodes the operation without `sig` as canonical DRISL, signs it with a
  rotation key (low-S, via `SignedObject`), and stores `sig` as unpadded base64url. Signing an
  operation that already has `sig` is an error; the input is not mutated.
  *Source:* did:plc spec · tests `Sign_AddsBase64UrlSignatureWithoutPadding`, `Sign_SignatureVerifiesAgainstTheUnsignedOperationEncoding`,
  `Sign_DoesNotMutateTheInputOperation`, `Sign_OperationAlreadySigned_Throws`
- **IDN-52** (MUST) A genesis operation's DID is `did:plc:` + the first 24 characters of the
  lowercase base32 encoding of SHA-256 over the **signed** operation's DRISL encoding. An
  operation's CID (what the next operation's `prev` names) is the dag-cbor/sha-256 CIDv1 of the
  signed encoding.
  *Source:* did:plc spec · 0.8 `PlcOperationBuilder.cs:115` · tests `Sign_DerivesAWellFormedDidPlc`,
  `DeriveDid_IsDeterministicForTheSameSignedOperation`
- **IDN-53** (MUST) A pinned genesis vector (fixed rotation and signing keys, handle, PDS endpoint;
  expected unsigned bytes, signature-independent fields, signed CID and DID, generated with the
  reference `@did-plc/lib`) is in the conformance suite. Because ECDSA signatures are randomized,
  the vector checks DID derivation from fixed signed bytes and verification of the reference
  signature, not byte equality of a fresh signature.
  *Source:* **Decision**: 0.8 has no pinned PLC vector (primitives audit, test gaps)
- **IDN-54** (MUST) `SignedPlcOperation.Verify` checks the signature against a given rotation key
  over the operation encoded without `sig`.
  *Source:* did:plc spec

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `UntrustedFetchPolicy.ConnectTimeout` | 5 s | IDN-3 |
| `UntrustedFetchPolicy.RequestTimeout` | 5 s | IDN-4 |
| `UntrustedFetchPolicy.MaxConcurrentFetches` | 64 per instance | IDN-7 |
| `UntrustedFetchPolicy.MaxResponseHeadersBytes` | 16 KiB | IDN-3 |
| `DnsOverHttpsTxtResolver.MaxResponseBytes` | 64 KiB | IDN-11 |
| `DidWebOptions.MaxDidDocumentBytes` / PLC document | 64 KiB (configurable, ≥ 1 KiB) | IDN-24 |
| `HandleResolver.MaxWellKnownBytes` | 2,048 bytes | IDN-26 |
| `HandleResolver.MaxWellKnownRedirects` | 3 (same host, https) | IDN-26 |
| `HandleResolverOptions.Budget` | 5 s for both methods | IDN-27 |
| `HandleResolverOptions.CacheTtl` | 5 min, positive answers only | IDN-31 |
| `DidCacheOptions.StaleAfter` / `ExpireAfter` / `FailureTtl` / `MinRefreshInterval` | 1 h / 24 h / 1 min / 30 s | IDN-37..IDN-40 |
| `DidCacheOptions.Capacity` / `FailureCapacity` | 10,000 / 1,000 | IDN-43 |
| `PlcClient.MaxLogBytes` / `MaxExportBytes` / `MaxErrorBodyBytes` | 8 MiB / 32 MiB / 64 KiB | IDN-47 |
| `PlcClient.MaxExportCount` | 1,000 | IDN-47 |
| `PlcUpdate.MaxRotationKeys` | 5 | IDN-50 |

## Errors

| Failure | Result |
|---|---|
| Network policy refusal, timeout, size, redirect, HTTP status | `UntrustedFetchException` (inner of the component's exception) |
| Bad identifier, unsupported method | `IdentityResolutionException(InvalidDid / UnsupportedMethod)`, no request sent |
| 404 / 410 / bad document / id mismatch | `IdentityResolutionException(NotFound / Deactivated / InvalidDocument)` |
| Handle does not resolve (handle input) | `IdentityResolutionException(HandleNotFound)` |
| DNS publishes two DIDs | `IdentityResolutionException(HandleConflict)` from `IHandleResolver`; `handle.invalid` inside DID resolution |
| PLC submission refused | `PlcOperationRejectedException` |
| Caller cancellation | `OperationCanceledException`, never wrapped |

All derive from `AtProtoException` (AG-19).

## Conformance and tests

**Vectors:** DID-document fixtures (PLC directory document, legacy `EcdsaSecp256k1/r1VerificationKey2019`
keys, `MultibaseToBytes_LegacyVerificationKey_…` interop case); the address-policy table, did:web URL
table, handle resolvability table (from `IdentityFetchPolicyTests`); the new PLC genesis vector
(IDN-53). The `syntax/` vectors live in [syntax](syntax.md).

**0.8 scenarios to port:** `IdentityFetchPolicyTests`, `IdentityHardeningTests`, `DidDocumentTests`,
`DidDocumentKeyTests`, `DidWebResolverTests`, `HandleResolverTests`, `IdentityResolverTests`,
`CachingDidResolverTests`, `DidResolverExtensionsTests`, `PlcClientTests` (export-stream tests move
to [streaming](streaming.md)), `PlcOperationBuilderTests`. Use TestKit's `FakeIdentityNetwork`
served through the real resolvers instead of resolver substitutes ([04 — Testing](../04-testing.md)).

## Changes from 0.8

| Change | Reason |
|---|---|
| Fetch layer is public (`ATProtoNet.Net`), instance-owned, with its own exception | D-21; OAuth, Lexicon resolution and repo fetchers use it; no process-wide statics |
| `IHandleResolver`/`IIdentityResolver` gain `InvalidateAsync`; `IDidResolver` gains `FetchFreshAsync` | remove the downcast; make "uncached for local security decisions" explicit and distinct from the rate-limited refresh |
| Keys are `DidKey`, endpoints `Uri` | AG-9a (0.8 returns `did:key` strings) |
| Options split per component (`DidWebOptions`, `HandleResolverOptions`, `DidCacheOptions`, `UntrustedFetchPolicy`) | 0.8's `IdentityResolverOptions` configured four classes, each re-validating it |
| Handle cache uses `TimeProvider` and its own capacity | AG-28 |
| Typed PLC operations (genesis, update, tombstone, legacy create), `PlcOperationRejectedException` | 0.8 had genesis-only `JsonObject` building and reported rejections as resolution failures |
| PLC export stream moves to the streaming namespaces (`ATProtoNet.Streaming.Plc`) | one stream runtime (D-07); 0.8's export stream ended silently on a dropped connection |
| Reserved TLDs apply to `did:web` hosts | DID spec |
| `IsEnforced` reports when the address policy cannot run | 0.8 skipped it silently on WASM |
| Space host, space signing key and key-agreement key are typed `DidDocument` lookups | Spaces is core protocol; 0.8 resolved them inside `Spaces/SpaceAuthority` and the credential provider |

## Open points

1. **Native DNS.** Ship a UDP/TCP DNS TXT resolver against the system's configured resolvers as a
   second `IDnsTxtResolver`, to avoid disclosing handles to a DoH provider? Proposed: not in 0.9;
   keep the seam and document the privacy trade-off (IDN-11a).
2. **PLC audit-chain verification.** Verify a DID's full operation log (signatures by rotation
   keys in force, `prev` chain, the 72-hour recovery window for higher-priority keys)? Proposed:
   phase 4 ships per-operation verification (IDN-54); full chain verification is a later addition
   behind `PlcAuditVerifier`.
3. **PLC operation size and field limits** enforced by the directory (operation byte size,
   `alsoKnownAs` and service counts): mirror them client-side so a bad operation fails before
   submission? Proposed: yes, as named constants confirmed against the directory implementation in
   phase 4.
4. **WASM behaviour.** Should `UntrustedHttp` refuse to run when `IsEnforced` is false unless the
   caller opts in? Proposed: no (browser apps cannot do otherwise); log and expose the flag.
5. **Spaces DID-document identifiers.** Dedicated `#atproto_space_host` / `#atproto_space`
   entries versus reusing `#atproto_pds` / `#atproto`, the key-agreement key's id and type, and a
   space authority's account status are open upstream (IDN-59). Proposed: implement the pinned
   proposal and switch to the GA identifiers before 0.9.0; cover both in identity vectors.
