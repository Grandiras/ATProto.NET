# Spaces (experimental)

| | |
|---|---|
| **Status** | Draft · **experimental**: every public type carries `[Experimental("ATPROTO001")]` |
| **Packages** | `ATProtoNet.Spaces` (namespace `ATProtoNet.Spaces`: primitives, generated `com.atproto.space.*` and `com.atproto.simplespace.*`, credential provider, signed-request handler, syncer, notification verifier) · `ATProtoNet.Spaces.AspNetCore` (notification receiver endpoints; the deferred server) · `ATProtoNet.Spaces.EntityFrameworkCore` (deferred server stores). All versioned `0.x` prerelease, independently of the stable packages. |
| **Phase** | 10 |
| **Depends on** | [data-model](data-model.md) (`DrislReader`, `CarReader`, `Cid`), [crypto](crypto.md) (`SigningKey`, `VerificationKey`, `DidKey`, JOSE), [identity](identity.md) (`IIdentityResolver`, DID document lookups), [xrpc-client](xrpc-client.md) (`AtProtoClient`, `IXrpcHandler`, `XrpcInvoker`), [auth](auth.md) (`ServiceAuthVerifier`, `IReplayStore`), [server](server.md) (XRPC endpoints, for the receiver) |
| **Normative references** | [Proposal 0016 *Permissioned Data*](https://github.com/bluesky-social/proposals/blob/0c9c2e88ba385809fc9ecba92f362389d9338e16/0016-permissioned-data/README.md) at `0c9c2e88` · reference implementation `679724ad` (`permissioned-data-alpha` branch of `bluesky-social/atproto`) · the 2026-10-01 alpha announcement ([AT Protocol Spaces (alpha)](https://atproto.com/blog/atproto-spaces-alpha)) · [RFC 9421](https://www.rfc-editor.org/rfc/rfc9421) HTTP Message Signatures · [RFC 8941](https://www.rfc-editor.org/rfc/rfc8941) Structured Field Values · [RFC 5869](https://www.rfc-editor.org/rfc/rfc5869) HKDF · [RFC 2104](https://www.rfc-editor.org/rfc/rfc2104) HMAC · BLAKE3 specification |

> **Alpha protocol.** Proposal 0016 is not a final specification and has had no security review.
> No released PDS serves it. Between 0.7 and 0.8 its wire format changed five times (#113, #211,
> #212, #213, #214). This spec records the protocol **at its pins**; phase 10 re-validates every
> requirement against the protocol as it stands then (SPC-2).

## Scope

Per [D-24](../06-decisions.md#d-24-spaces-scope-in-the-rewrite) (proposed default of Q-11):

**In:** the primitives (space URIs, LtHash and BLAKE3, signed space commits, `getRepo` CAR
verification, strict record CBOR, the three token types, HTTP message signatures, structured
fields) with their reference vectors; the client (credential provider, a signed-request
`IXrpcHandler` with an explicit audience, generated `com.atproto.space.*` and
`com.atproto.simplespace.*` client methods); the syncer; and a **notification receiver** for
forwarded `notifyWrite` and `notifySpaceDeleted`, which 0.8 lacks.

**Deferred** (recorded in [Deferred server](#deferred-server) so the 0.8 knowledge is not lost,
built only if the protocol has stabilised by phase 10): the space authority and repo host
endpoints, `simplespace` server endpoints, managing-app access policy, client-attestation
verification, and their EF Core stores.

**Out:** public-repo MST machinery ([repo](repo.md)); event streams (Spaces has no firehose;
applications pull from each host).

## Public API sketch

```csharp
namespace ATProtoNet.Spaces;   // every public type: [Experimental("ATPROTO001")]

// ── Addressing ─────────────────────────────────────────────────────────────
public sealed class SpaceUri : IAtIdentifier<SpaceUri>          // at://{authority}/space/{spaceType}/{skey}
{
    public Did Authority { get; }  public Nsid SpaceType { get; }  public RecordKey Skey { get; }
    public SpaceRecordUri Record(Did author, Nsid collection, RecordKey rkey);
    public static bool IsSpaceUri(ReadOnlySpan<char> value);
}
public sealed class SpaceRecordUri : IAtIdentifier<SpaceRecordUri>
{
    public SpaceUri Space { get; }  public Did Author { get; }  public Nsid Collection { get; }
    public RecordKey Rkey { get; }  public string Path { get; }      // "{collection}/{rkey}"
}

// ── Set hash and commits ───────────────────────────────────────────────────
public static class Blake3 { public static void Hash(ReadOnlySpan<byte> input, Span<byte> output); }   // XOF
public sealed class LtHash                                       // mutable accumulator; no value equality
{
    public static LtHash Empty();
    public void Add(Nsid collection, RecordKey rkey, Cid cid);
    public void Remove(Nsid collection, RecordKey rkey, Cid cid);
    public byte[] Digest();                                      // SHA-256 of the 2,048-byte state
    public LtHash Clone();
}
public readonly record struct SpaceCommitContext(SpaceUri Space, Did Author, Tid Rev);
public sealed class SpaceCommit
{
    public int Version { get; }  public ReadOnlyMemory<byte> Hash { get; }  public ReadOnlyMemory<byte> Ikm { get; }
    public ReadOnlyMemory<byte> Sig { get; }  public ReadOnlyMemory<byte> Mac { get; }  public Tid Rev { get; }
    public static SpaceCommit Create(SpaceCommitContext context, ReadOnlySpan<byte> hash, SigningKey key);
    public static SpaceCommit Read(ReadOnlyMemory<byte> block);  // strict DRISL
    public SpaceCommitVerdict Verify(SpaceCommitContext context, VerificationKey key);
}
public static class SpaceRepoCar
{
    public static ValueTask<VerifiedSpaceRepo> VerifyAsync(Stream car, SpaceUri space, Did author,
        VerificationKey key, SpaceLimits? limits = null, CancellationToken cancellationToken = default);
}
public static class SpaceRecordCbor { public static bool IsStrict(ReadOnlySpan<byte> block); }

// ── Tokens and signatures ──────────────────────────────────────────────────
public enum SpaceTokenType { Delegation, Credential, ClientAttestation }   // closed: the protocol's three
public sealed class SpaceToken
{
    public SpaceTokenType Type { get; }  public string Issuer { get; }  public string Subject { get; }
    public string? Audience { get; }  public string? KeyId { get; }  public DidKey? ConfirmationKey { get; }
    public DateTimeOffset? IssuedAt { get; }  public DateTimeOffset ExpiresAt { get; }  public string Jti { get; }
    public static SpaceToken Parse(string jwt, SpaceTokenType expected, TimeProvider? time = null);   // structure + claims, not signature
    public bool VerifySignature(VerificationKey key);
}
public static class SpaceHttpSignature
{
    public static void SignCredentialExchange(HttpRequestMessage request, SigningKey key);
    public static void SignRequest(HttpRequestMessage request, SpaceCredential credential, Did audience);
    public static SpaceSignatureVerdict Verify(SpaceSignedRequest request, DidKey expectedKey);
}

// ── Client ─────────────────────────────────────────────────────────────────
public sealed class SpaceCredentialProvider(AtProtoClient account, IIdentityResolver identity,
    SpaceCredentialOptions? options = null, TimeProvider? time = null) : IAsyncDisposable
{
    public Task<SpaceCredential> GetCredentialAsync(SpaceUri space, CancellationToken cancellationToken = default);
    public AtProtoClient ForAuthority(SpaceUri space);           // audience: the authority's DID
    public AtProtoClient ForRepo(SpaceUri space, Did author);    // audience: the author's DID, reached at their host
}
public sealed record SpaceCredentialOptions
{
    public Func<string, CancellationToken, ValueTask<string>>? ClientAttestationFactory { get; init; }  // aud → attestation JWT
    public TimeSpan RenewalWindow { get; init; } = TimeSpan.FromMinutes(1);
}

public interface ISpaceRepoStore                                 // the application's copy of one (space, author) repo
{
    ValueTask PutAsync(SpaceRecordUri uri, Cid cid, ReadOnlyMemory<byte> record, CancellationToken cancellationToken);
    ValueTask DeleteAsync(SpaceRecordUri uri, CancellationToken cancellationToken);
    ValueTask ReplaceAllAsync(SpaceUri space, Did author, VerifiedSpaceRepo repo, CancellationToken cancellationToken);
}
public sealed class SpaceSyncer(ISpaceRepoStore store, IIdentityResolver identity,
    SpaceSyncerOptions? options = null, TimeProvider? time = null)
{
    public Task<SpaceSyncResult> SyncRepoAsync(AtProtoClient repoClient, SpaceRepoCursor cursor, CancellationToken cancellationToken = default);
    public Task<SpaceSyncResult> RecoverAsync(AtProtoClient repoClient, SpaceUri space, Did author, CancellationToken cancellationToken = default);
    public IAsyncEnumerable<SpaceRepoView> ListChangedReposAsync(AtProtoClient authorityClient, SpaceUri space,
        Tid? checkpoint, CancellationToken cancellationToken = default);
    public static bool NeedsCatchUp(Tid? checkpoint, SpaceWriteNotification notification);
}
public enum SpaceSyncOutcome { UpToDate, Partial, Recovered, NoRepo }

// ── Notifications ──────────────────────────────────────────────────────────
public sealed class SpaceNotificationVerifier(ServiceAuthVerifier serviceAuth, IIdentityResolver identity,
    SpaceNotificationOptions options);                           // verifies a forwarded notifyWrite / notifySpaceDeleted
public interface ISpaceNotificationHandler
{
    ValueTask OnWriteAsync(SpaceWriteNotification notification, CancellationToken cancellationToken);
    ValueTask OnSpaceDeletedAsync(SpaceUri space, CancellationToken cancellationToken);
}

// ATProtoNet.Spaces.AspNetCore
//   services.AddAtProto().AddSpaceNotifications<THandler>(o => o.ServiceId = "did:web:app.example#atproto_space_syncer");
//   app.MapAtProtoSpaceNotifications();
```

## Requirements

### Protocol pins and packaging

- **SPC-1** (MUST) The implementation targets the pins in the header (proposal `0c9c2e88`,
  reference implementation `679724ad`, the 2026-10-01 alpha). The pins are recorded in each Spaces
  package's metadata (`PackageReleaseNotes` and an assembly attribute) and in
  `docs/spaces.md`, and the integration environment pins the reference PDS image digest.
  *Source:* 0.8 `docs/spaces.md:12-16`, `docs/testing-spaces.md:13`
- **SPC-2** (MUST) Moving to a new protocol revision is one change set that updates the
  pins, the vendored proposal Lexicons, the reference vectors (regenerated from the new reference
  implementation), this spec's affected requirements, and the Spaces packages' minor version. It
  never changes a stable package.
  *Source:* **Decision** [D-08](../06-decisions.md#d-08-spaces-is-quarantined-as-experimental)
- **SPC-3** (MUST) The proposal's `com.atproto.space.*` and `com.atproto.simplespace.*`
  Lexicons are vendored under `lexicons/` at the pinned reference commit (their source recorded in
  `lexicons.json`) and generated into `ATProtoNet.Spaces`, never into `ATProtoNet`.
  *Source:* **Decision** D-08 · audit (0.8 hand-writes ~1.75k lines of Space/SimpleSpace clients)
- **SPC-4** (MUST) Every public type in the Spaces packages carries
  `[Experimental("ATPROTO001")]`, including generated ones. No stable package references a Spaces
  package or contains Spaces knowledge (no Spaces code in the core transport, no Spaces stores in
  a central defaults warning).
  *Source:* **Decision** D-08, [D-16](../06-decisions.md#d-16-experimental-diagnostic-ids) · audit
  (0.8 `XrpcClient.cs:132-138,690-697`, `InMemoryDefaultsWarning.cs:42-66`)

### Addressing

- **SPC-5** (MUST) A space is `at://{authority}/space/{spaceType}/{skey}` and a record in it
  `at://{authority}/space/{spaceType}/{skey}/{author}/{collection}/{rkey}`, where `authority` and
  `author` are DIDs (never handles), `spaceType` and `collection` are NSIDs, and `skey` and `rkey`
  are record keys. The literal `space` segment distinguishes a space URI from a public AT-URI
  (a collection NSID always contains a dot).
  *Source:* Proposal 0016, *Addressing* · 0.8 `SpaceUri.cs:33-35` · tests `SpaceUriTests`
- **SPC-6** (MUST) `SpaceUri` and `SpaceRecordUri` follow the identifier rules of
  [syntax](syntax.md) ([AG-8](../03-api-guidelines.md#types)): validated on construction, sealed
  classes, `Parse` throws `FormatException`, no conversions from `string`, refusal of line breaks.
  *Source:* **Decision** AG-8 · 0.8 reuses the internal `IIdentifier`

### LtHash and BLAKE3

- **SPC-7** (MUST) The LtHash element for a record is the UTF-8 string
  `"{collection}/{rkey}/{cid}"` (CID in its canonical base32 form), expanded with BLAKE3 in XOF mode
  to `LtHashStateBytes` (2,048) bytes, read as `LtHashLanes` (1,024) little-endian `u16` lanes.
  *Source:* Proposal 0016 · 0.8 `LtHash.cs:29-32` · tests `LtHashTests` (reference vectors)
- **SPC-8** (MUST) Adding an element adds its lanes to the state modulo 2¹⁶; removing
  subtracts them. The state is a multiset: adding one element twice does not collapse, and adding
  then removing restores the prior state exactly. The digest is SHA-256 of the 2,048-byte state.
  *Source:* Proposal 0016 · tests `LtHashTests`
- **SPC-9** (MUST) `LtHash` is a mutable accumulator and does not override
  `Equals`/`GetHashCode` (0.8's mutable instance with a hash code breaks dictionaries). Digests
  are compared as bytes, in constant time where a digest is checked against a commit.
  *Source:* audit (0.8 `LtHash` overrides `GetHashCode`)
- **SPC-10** (MUST) `Blake3` produces output identical to the BLAKE3 reference for every
  official test vector, for any output length (XOF). It lives in `ATProtoNet.Spaces`, its only user.
  *Source:* official BLAKE3 test vectors (pasted in 0.8 `Blake3Tests`; extracted to the conformance
  suite) · **Decision** D-08 (moved out of `Crypto/`)

### Signed commits

- **SPC-11** (MUST) A space commit is a DRISL-CBOR map
  `{ver, hash, ikm, sig, mac, rev}`: `ver` = 1; `hash` the LtHash digest (32 bytes); `ikm` 32
  random bytes, fresh for every signature; `sig` the author's signature; `mac` an HMAC; `rev` a TID.
  *Source:* Proposal 0016 · 0.8 `SpaceCommit.cs:29` · tests `SpaceCommitTests`
- **SPC-12** (MUST) The signing context is the ASCII tag `"atproto-space-v1"`
  followed by the space URI, the author DID, the revision and the `ikm`, each prefixed with its
  length as a big-endian `u16` (the TLS 1.3 §3.4 vector convention). A field longer than 65,535
  bytes cannot be encoded and is rejected.
  *Source:* 0.8 `SpaceCommit.cs:90-140` (`SpaceCommitContext.Encode`)
- **SPC-13** (MUST) `sig` is the author's `#atproto` key signing the context
  (low-S `r‖s`, per [crypto](crypto.md)); `mac` = HMAC-SHA256(key = HKDF-Expand(PRK = `ikm`,
  info = context, L = 32), message = `hash`).
  *Source:* Proposal 0016 · 0.8 `SpaceCommit.cs` · reference vectors in `SpaceCommitTests`
- **SPC-14** (MUST) Verification checks, in order: `ver` = 1; `rev` equals the
  expected revision; the MAC; the signature. The first failure decides the verdict. Verification
  of a commit is not proof of authorship to third parties (signatures are deniable on rebroadcast);
  the docs say so.
  *Source:* Proposal 0016 · 0.8 `SpaceCommit.cs:265-331`, `docs/spaces.md` ("Commits: why they
  prove nothing")

### `getRepo` CAR and record CBOR

- **SPC-15** (MUST) A space repo CAR has exactly two roots: the commit block, then an
  index block mapping each record path to its CID. Record blocks follow in index order.
  *Source:* Proposal 0016 · 0.8 `SpaceRepoCar.cs` · tests `SpaceRepoCarTests`
- **SPC-16** (MUST) `SpaceRepoCar.VerifyAsync` verifies in order: (1) the commit
  (SPC-14) for the given space, author and the commit's own `rev`; (2) the LtHash of the
  index equals the commit's `hash`; (3) every record block matches its CID (raw-codec links are
  refused) and is strict record CBOR (SPC-17). It reads at most `MaxSpaceRepoBytes` and
  fails with `SpaceVerificationException`, never a CBOR library exception.
  *Source:* 0.8 `SpaceRepoCar.cs` · #133 · test
  `SpaceRepoCarTests.Verify_MalformedIndexBlock_…` (a `CborContentException` used to leak)
- **SPC-17** (MUST) A space record block is a DRISL-CBOR map in canonical encoding
  with text keys only, no floats, and no tag other than 42. Anything else fails verification.
  *Source:* Proposal 0016 · 0.8 `SpaceRecordCbor.cs` (uses the strict reader from
  [data-model](data-model.md) instead of a second strictness implementation)

### Tokens

- **SPC-18** (MUST) There are three JWT types, distinguished by `typ`:
  `atproto-space-delegation+jwt` (delegation), `atproto-space-credential+jwt` (credential),
  `atproto-client-attestation+jwt` (client attestation). A token whose `typ` is not the one
  expected is rejected.
  *Source:* Proposal 0016 · 0.8 `SpaceTokens.cs:127-133` · tests `SpaceTokensTests`
- **SPC-19** (MUST) Every token has `iss`, `sub`, `exp` and `jti` (128-bit, hex).
  Delegation tokens and attestations also have `aud`. An attestation has `iss` = `sub` = the
  client ID and no `kid`; the other types default `kid` to `#atproto`. A credential has
  `cnf.kid` = a P-256 `did:key` (the key the credential is bound to) and `iat`, with
  `0 < exp − iat ≤ MaxSpaceCredentialLifetime` and `iat ≤ now + SpaceTokenClockSkew`.
  *Source:* Proposal 0016 · 0.8 `SpaceTokens.cs:136-145,195-222` · tests
  `SpaceTokensTests.Parse_TimeClaimOutsideTheRepresentableRange_Throws`, `SpaceTokenKeyIdTests`
- **SPC-20** (MUST) Default lifetimes: credentials `DefaultSpaceCredentialLifetime`
  (10 min); delegation tokens and attestations `DefaultShortTokenLifetime` (60 s). Time claims
  outside the representable range are refused, not thrown as overflow.
  *Source:* 0.8 `SpaceTokens.cs:136-142`
- **SPC-21** (MUST) Token encoding and signature checks use the JOSE primitives of
  [crypto](crypto.md) (strict compact JWS, `alg` matching the key's curve). The Spaces package
  contains no second JWT implementation.
  *Source:* **Decision** · audit (0.8 `SpaceTokens` relies on the internal `Auth.Jwt`, reachable
  only because Spaces shares the core assembly; the rewrite makes JOSE public in Primitives)

### HTTP message signatures

- **SPC-22** (MUST) Requests are signed per RFC 9421 with signature label
  `atproto-space` and algorithm `ecdsa-p256-sha256`, as a 64-byte `r‖s` signature. Verification
  refuses DER-encoded signatures and accepts high-S. `Signature-Input` and `Signature` are RFC 8941
  structured fields, parsed strictly; a repeated label is refused.
  *Source:* 0.8 `SpaceHttpSignature.cs:33-45` · tests `SpaceHttpSignatureTests` (reference vectors
  from `679724ad`), `StructuredFieldsTests`
- **SPC-23** (MUST) The credential exchange (`com.atproto.space.getSpaceCredential` at
  the authority) carries `Authorization: Bearer {delegation token}` and signs the covered
  components `("authorization")` with `keyid` = the `did:key` the credential will be bound to.
  *Source:* Proposal 0016 · 0.8 `SpaceCredentialProvider.cs` (exchange)
- **SPC-24** (MUST) Every other space request carries
  `Authorization: Atproto-Space {credential}` and `Atproto-Space-Audience: {did}` and signs
  `("authorization" "atproto-space-audience")` in exactly that order. `keyid`, when present, must
  equal the credential's `cnf.kid`.
  *Source:* Proposal 0016 · 0.8 `SpaceHttpSignature.cs` · tests `SpaceHttpSignatureTests`
- **SPC-25** (MUST) The audience is **explicit**: a client obtained from
  `ForRepo(space, author)` signs for the author's DID (repo operations), a client from
  `ForAuthority(space)` signs for the authority's bare DID. The handler never derives the
  audience from request parameters.
  *Source:* **Decision** · audit (0.8 guesses it from a query parameter named `repo`,
  `XrpcClient.cs:690-697`, and `Did.Parse(repo)` throws on a handle)
- **SPC-26** (MUST) Signing is an `IXrpcHandler` in `ATProtoNet.Spaces` that wraps the
  account's transport via `XrpcInvoker`; the core XRPC pipeline has no Spaces-specific code and no
  Spaces credential slot.
  *Source:* **Decision** D-24 item 2, [xrpc-client](xrpc-client.md)

### Credential provider

- **SPC-27** (MUST) Each credential is bound to a fresh P-256 key generated for it, held
  only in memory, and disposed when the credential is replaced and no reader uses it.
  *Source:* 0.8 `SpaceCredentialProvider.cs` (`SpaceCredential.Key`)
- **SPC-28** (MUST) Minting fetches a single-use delegation token from the account's PDS
  (`com.atproto.space.getDelegationToken`) immediately before the exchange, then performs the
  exchange (SPC-23) **without** an attestation; only if the authority answers
  `AppNotAuthorized` and a `ClientAttestationFactory` is set does it retry once with an
  attestation for the authority's host audience.
  *Source:* 0.8 `SpaceCredentialProvider.cs:364-430` · tests `SpaceCredentialMintTests`
- **SPC-29** (MUST) An issued credential is accepted only if its `cnf.kid` is the minted
  key's `did:key` and its `sub` is the requested space URI; otherwise the provider throws
  `SpaceCredentialException` and discards it.
  *Source:* 0.8 `SpaceCredentialProvider.cs:386-389`
- **SPC-30** (MUST) A credential is reused until `exp − min(RenewalWindow, (exp − iat) / 2)`
  (default window 1 min), measured on the injected `TimeProvider`. Concurrent callers for one space
  share one mint. A replaced credential stays usable by readers that hold it until it expires.
  *Source:* 0.8 `SpaceCredentialProvider.cs:83,245` · tests `SpaceCredentialProviderTests`
- **SPC-31** (MUST) An authority's host is its `#atproto_space_host` service (type
  `AtprotoSpaceHost`, absolute `https` endpoint) and its credential signing key its
  `#atproto_space` verification method. Only an **absent** entry falls back (`#atproto_pds`,
  `#atproto`); a published but malformed entry is an error.
  *Source:* Proposal 0016 · #133 · 0.8 `SpaceAuthority.cs:3-31` · tests `SpaceAuthorityTests`
- **SPC-32** (MUST) The provider is `IAsyncDisposable` only if it holds asynchronous
  resources; disposal releases its keys and cancels in-flight mints it owns. Disposing a reader
  client releases nothing it does not own (0.8's `SpaceReader.Dispose` did nothing and the
  provider was synchronous underneath both disposal interfaces).
  *Source:* audit · AG-26

### Syncer

- **SPC-33** (MUST) `SyncRepoAsync` walks `com.atproto.space.listRepoOps` from the
  cursor's revision, applying each op to the application's `ISpaceRepoStore` and to a running
  `LtHash` seeded from the cursor's state.
  *Source:* Proposal 0016 · 0.8 `SpaceSyncer.cs:238-302` · tests `SpaceSyncerTests`
- **SPC-34** (MUST) On reaching the head of the log, the syncer verifies the head commit
  against the author's `#atproto` key (one DID-document refresh on failure) and compares digests:
  equal → `UpToDate`; different → `RecoverAsync`, which downloads `getRepo`, verifies it per
  SPC-16, calls `ReplaceAllAsync` and returns `Recovered`.
  *Source:* Proposal 0016 · 0.8 `SpaceSyncer.cs:302,343-384` · #133 (verify against `#atproto`)
- **SPC-35** (MUST) A page that applies ops or offers a continuation cursor without
  reaching the head returns `Partial`; the caller syncs again to continue.
  *Source:* 0.8 `SpaceSyncer.cs:10-25,287`
- **SPC-36** (MUST) `NoRepo` when the host refuses with `RepoNotFound`,
  `RepoDeactivated`, `RepoSuspended` or `RepoTakendown`, or serves an empty page with no commit and
  no continuation while the cursor has no revision. The same empty page when the cursor *has* a
  revision triggers recovery (the repo was emptied or replaced).
  *Source:* #99 · 0.8 `SpaceSyncer.cs:26-36,256,327,375` · tests
  `SpaceSyncerTests` (`NoRepo` cases)
- **SPC-37** (MUST) A 4xx other than 401, 403 and 429 from `listRepoOps` triggers
  recovery; 401, 403, 429 and 5xx propagate as `XrpcException`. A repo download larger than
  `MaxSpaceRepoBytes` (declared or actual) fails without reading further.
  *Source:* 0.8 `SpaceSyncer.cs:363-369` · tests `SpaceSyncerTests`
- **SPC-38** (MUST) `ListChangedReposAsync` pages `com.atproto.space.listRepos` by an
  exclusive `spaceRev` checkpoint, in ascending `spaceRev` order, stopping on a null, empty or
  repeated cursor. `NeedsCatchUp(checkpoint, n)` is true when `n.SpaceRev` is after the checkpoint
  and `n.PrevSpaceRev` is not the checkpoint.
  *Source:* #119, #213 · 0.8 `SpaceSyncer.cs:189-221`
- **SPC-39** (MUST) Limits (`MaxSpaceRepoBytes`) are options values fixed at
  construction, not settable properties on a shared instance.
  *Source:* audit (0.8 `SpaceSyncer.MaxRepoSize` is settable) · AG-7

### Notification receiver (new)

- **SPC-40** (MUST) `ATProtoNet.Spaces.AspNetCore` serves
  `com.atproto.space.notifyWrite` and `com.atproto.space.notifySpaceDeleted` as XRPC procedures
  ([server](server.md)) and dispatches verified notifications to the application's
  `ISpaceNotificationHandler`. 0.8 could register for notifications but shipped no receiver.
  *Source:* **Decision** D-24 item 3 · audit
- **SPC-41** (MUST) A forwarded notification is authenticated with service auth
  ([auth](auth.md)): `aud` must equal the service identifier the application registered (fragment
  included, e.g. `did:web:app.example#atproto_space_syncer`), `lxm` must equal the called method,
  `iss` must be the space's authority DID, and the `jti` is consumed in an `IReplayStore` only after
  the signature verifies.
  *Source:* 0.8 `SpaceWriteNotifier.cs:27-37,405-410` (how deliveries are addressed and signed) ·
  [auth](auth.md) service-auth verification rules
- **SPC-42** (MUST) The receiver validates the input (`space`, `repo`, `repoRev`,
  `hash`, `spaceRev`, `prevSpaceRev`) before authentication work that costs a DID resolution, and
  answers malformed input with `400 InvalidRequest`. A handler failure answers `500` (the
  notification is best-effort; correctness comes from catch-up, SPC-38).
  *Source:* 0.8 `SpaceModels.cs:421-446` (`NotifyWriteRequest`) · `SpaceWriteNotifier.cs:18-24`

## Deferred server

The rules below describe 0.8's space server (authority and repo host). They are recorded so the
knowledge survives; they are **not** in phase 10 scope unless Q-11 is answered "server too" and
the protocol has stabilised. If built, they go in `ATProtoNet.Spaces.AspNetCore` and
`ATProtoNet.Spaces.EntityFrameworkCore`, register through the standard builder
([hosting](hosting.md)), and use only public XRPC server APIs.

- **SPC-43** (MUST) A delegation token's `aud` must be
  `{authority}#atproto_space_host` for the authority named in its `sub`.
  *Source:* Proposal 0016 · 0.8 `SpaceTokenVerifier` · tests `SpaceTokenVerifierTests`
- **SPC-44** (MUST) A credential's `iss` must be the space's authority, and its key
  is resolved by exactly the named `kid` in the authority's DID document (no fallback to another
  verification method). The signature over each request is checked per
  SPC-24 against `cnf.kid`.
  *Source:* 0.8 `SpaceCredentialVerifier.cs` · tests `SpaceServerEndpointTests`
- **SPC-45** (MUST) Single-use tokens (delegation, attestation) are tracked by
  `(iss, jti, exp)` until they could no longer be accepted, with lifetime capped at
  `MaxSingleUseTokenLifetime` (5 min). Verified credentials are cached in an LRU of
  `VerifiedCredentialCacheCapacity` (10,000) entries, at most a quarter per authority.
  *Source:* #125 · 0.8 `SpaceServerOptions.cs:28,64`, `SpaceCredentialVerifier.cs:78,229`
- **SPC-46** (MUST) `notifyWrite` at the authority: input validated before
  authentication; `aud` may be the authority, `{authority}#atproto_space_host` or the server's own
  service DID; `iss` must equal the writer; a `repoRev` more than 5 min ahead answers `FutureRev`;
  only the space's write policy applies (no app perimeter on writes); a stale `repoRev` is ignored;
  each accepted write gets a strictly increasing `spaceRev` and is forwarded best-effort to
  registered services, skipping the authority's own registration.
  *Source:* 0.8 `SpaceAuthorityEndpoints.cs:221-269` · tests `SpaceServerEndpointTests`
- **SPC-47** (MUST) `registerNotify` registrations last
  `NotifyRegistrationLifetime` (7 days). A repo host's notification to its authority retries
  transient failures (408, 429, 5xx) with jittered backoff from 5 s for `RetryWindow` (1 h),
  coalescing per space and repo to the latest revision; other deliveries are not retried. Retry
  state SHOULD be durable (0.8 loses it on restart).
  *Source:* 0.8 `SpaceServerOptions.cs:53`, `SpaceWriteNotifier.cs:18-26,55-58,194-203`
- **SPC-48** (MUST) `notifyCredentialRevoked`: `iss` must be the authority, `aud` an
  account hosted here, 1–100 ids per call, each retained for
  `MaxSpaceCredentialLifetime + 2 × SpaceTokenClockSkew` (3,610 s), so a revoked credential cannot
  revive.
  *Source:* #214 · 0.8 `SpaceRepoEndpoints.cs:295-317`
- **SPC-49** (MUST) A managing-app access policy that cannot be reached refuses access;
  the space owner is always admitted.
  *Source:* #113 · 0.8 `SimpleSpaceAccessPolicyTests`
- **SPC-50** (MUST) A space created through `com.atproto.simplespace` is
  declared to the authority store, so `listRepos`, `registerNotify` and `notifyWrite` find it.
  *Source:* #105 · tests `SimpleSpaceAuthorityStoreTests`

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `LtHashLanes` / `LtHashStateBytes` | 1,024 / 2,048 bytes | LtHash state |
| `SpaceCommitVersion` | 1 | accepted commit version |
| `SpaceCommitIkmBytes` | 32 | per-signature nonce |
| `MaxSpaceRepoBytes` | 256 MiB | one `getRepo` download |
| `MaxSpaceCredentialLifetime` | 1 h | `exp − iat` of a credential |
| `DefaultSpaceCredentialLifetime` | 10 min | credentials the SDK requests or issues |
| `DefaultShortTokenLifetime` | 60 s | delegation tokens, attestations |
| `SpaceTokenClockSkew` | 5 s | `iat` in the future, expiry tolerance |
| `DefaultRenewalWindow` | 1 min | credential renewal (capped at half the lifetime) |
| `MaxSpaceContextFieldBytes` | 65,535 | commit context field (u16 prefix) |
| *Deferred server:* `MaxSingleUseTokenLifetime` | 5 min | delegation/attestation lifetime ceiling |
| *Deferred server:* `VerifiedCredentialCacheCapacity` | 10,000 (¼ per authority) | credential cache |
| *Deferred server:* `NotifyRegistrationLifetime` | 7 days | `registerNotify` |
| *Deferred server:* `MaxFutureRepoRev` | 5 min | `notifyWrite` `repoRev` skew |
| *Deferred server:* `NotifyRetryWindow` / base delay | 1 h / 5 s | repo host → authority retries |
| *Deferred server:* `RevocationRetention` | 3,610 s | revoked credential ids |
| *Deferred server:* revocation ids per call | 1–100 | `notifyCredentialRevoked` |

DRISL and CAR bounds come from [data-model](data-model.md).

## Errors

| Failure | Result |
|---|---|
| Commit, CAR, record or signature verification fails | `SpaceVerificationException : AtProtoException` with a verdict naming the failed check |
| Token malformed, wrong `typ`, claims invalid | `SpaceTokenException : AtProtoException` (parse); verdicts for verification |
| Credential minting refused or credential not bound to the key or space | `SpaceCredentialException : AtProtoException` (wrapping the `XrpcException` when there is one) |
| Host refusals during sync (401, 403, 429, 5xx) | `XrpcException`, propagated |
| Receiver authentication failure | `401`/`403` with the XRPC error envelope ([server](server.md)) |

0.8 has five Spaces exception types (`SpaceCredentialException`, `SpaceTokenException`,
`SpaceSignatureException`, `SpaceRepoVerificationException`, `SpaceVerificationException`); the
rewrite keeps three, with signature and repo failures folded into `SpaceVerificationException`.

## Conformance and tests

**Conformance vectors** (Spaces area, [04 — Testing](../04-testing.md#conformance-areas)): BLAKE3
official vectors, LtHash reference vectors (@atproto/space), HTTP message signature vectors
(`679724ad`), structured-field cases, space commit and token vectors. All are extracted from 0.8
C# literals into data files with provenance in `MANIFEST.md`, and regenerated on every pin bump
(SPC-2).

**0.8 test classes whose scenarios must be ported:** `SpaceUriTests`, `Blake3Tests`,
`LtHashTests`, `SpaceCommitTests`, `SpaceRepoCarTests`, `SpaceTokensTests`,
`SpaceHttpSignatureTests`, `StructuredFieldsTests`, `SpaceAuthorityTests`,
`SpaceCredentialProviderTests`, `SpaceCredentialMintTests` (on `FakeTimeProvider`; 0.8 sleeps
1.3 s of real time), `SpaceSyncerTests`, `SpaceClientTests`. If the server is built:
`SpaceServerEndpointTests`, `SpaceTokenVerifierTests`, `SpaceWriteNotifierTests` (without real
sleeps), `SimpleSpaceEndpointTests`, `SimpleSpaceAccessPolicyTests`, the store tests and the
`SpaceCredentialRevocationStoreContractTests` contract.

**New tests:** notification receiver (audience with fragment, `iss` binding, replay consumed only
after signature, malformed input before authentication); explicit-audience handler (no audience
derived from parameters).

**Integration (nightly):** `Requirement=Spaces` against a permissioned-data PDS at the pinned
image digest, with its own PLC directory (`ATPROTO_PLC_URL`), self-provisioned accounts
(`ATPROTO_PDS_ADMIN_PASSWORD`) and `ATPROTO_SPACES_PDS_URL`, as `docs/testing-spaces.md` describes
today.

## Changes from 0.8

| Change | Reason |
|---|---|
| Spaces moves out of `ATProtoNet` and `ATProtoNet.Server` into its own experimental packages | D-08; alpha churn stops breaking stable packages |
| Audience is explicit per client (`ForRepo`, `ForAuthority`) | 0.8 guessed it from a `repo` query parameter inside the core transport |
| Signing is an `IXrpcHandler` in the Spaces package | no Spaces code in the core pipeline |
| Generated `com.atproto.space.*`/`simplespace.*` from vendored proposal Lexicons | 0.8 hand-wrote them |
| Notification receiver added | 0.8 could register but not receive |
| Server, `simplespace` server, managing-app policy and attestation verification deferred | D-24; the largest and least settled part |
| `LtHash` drops value equality | mutable type with a hash code |
| Settable limits become options | AG-7 |
| Three exception types instead of five | fewer types, same information |
| BLAKE3 moves into the Spaces package | its only user |

## Open points

1. **Scope (Q-11).** *Proposed default:* primitives, client, syncer and receiver; server only if
   the protocol has stabilised by phase 10.
2. **Receiver `iss`.** 0.8 forwards notifications signed as the authority when it holds the
   authority's key, otherwise as the forwarding service. *Proposed default:* the receiver accepts
   only the space authority as `iss`; revisit if the protocol defines host-signed forwards.
3. **Strict CBOR helper.** `SpaceRecordCbor` could be a mode of the data-model strict reader
   rather than a Spaces type. *Proposed default:* implement it as a thin check over the strict
   `DrislReader` ([data-model](data-model.md)), exposed in Spaces only.
4. **Durable notification retries** (deferred server). *Proposed default:* if the server is built,
   persist the repo host → authority retry queue in a store with a contract suite.
