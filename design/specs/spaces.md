# Spaces (permissioned data)

| | |
|---|---|
| **Status** | Draft · **stable surface**, targeting the November 2026 general-availability (GA) protocol. Fallback only: if the GA spec is not published when 0.9.0 ships, the Spaces APIs ship with `[Experimental("ATPROTO001")]` and lose it in the 0.9.x release that follows GA ([D-08](../06-decisions.md#d-08-spaces-is-experimental)). |
| **Packages and namespaces** | `ATProtoNet`, across the layers ([02 — Architecture](../02-architecture.md#layers-inside-atprotonet)): **layer 0:** `SpaceUri`/`SpaceRecordUri` (root namespace), `ATProtoNet.Data.Star` (STAR reader/writer), `ATProtoNet.Crypto` (BLAKE3, key-encapsulation keys) and `ATProtoNet.Crypto.HttpSignatures` (RFC 9421 and RFC 8941), `ATProtoNet.Spaces` (LtHash, space repo state, MAC computation, token formats and verification). **Layer 1:** `ATProtoNet.Identity` (space host, space signing key and key-encapsulation key entries of DID documents). **Layer 2:** `ATProtoNet.Spaces.Access` (credential provider and issuer, signed-request handler, space-scoped client), generated `ATProtoNet.Lexicons.Com.AtProto.Space.*` and `…SimpleSpace.*`. **Layer 3:** `ATProtoNet.Spaces.Sync` (syncer, notification handling, checkpoints). **Layer 4:** `ATProtoNet.Hosting` (`AddSpaces(…)` on the builder). `ATProtoNet.AspNetCore` (namespace `ATProtoNet.AspNetCore.Spaces`): space authority, repo host, `simplespace` and notification-receiver endpoints. `ATProtoNet.EntityFrameworkCore`: the Spaces stores. |
| **Phases** | 1 primitives (`SpaceUri`, STAR, BLAKE3, KEM keys, HTTP signatures) · 2 space repos (LtHash state, MAC) · 3 generated Lexicons · 4 DID document entries · 5–6 client, credentials, signed requests · 7 sync · 9 server roles and stores · 10 end-to-end and interop against the GA reference PDS |
| **Depends on** | [syntax](syntax.md), [data-model](data-model.md) (`DrislReader`, `Cid`, bounded readers), [crypto](crypto.md) (`SigningKey`, `VerificationKey`, `DidKey`, JOSE), [identity](identity.md) (`IIdentityResolver`, DID document lookups), [xrpc-client](xrpc-client.md) (`AtProtoClient`, `IXrpcHandler`, `XrpcInvoker`, records API), [auth](auth.md) (`ScopeSet`, `ServiceAuthVerifier`, `IReplayStore`), [streaming](streaming.md) (`#account`/`#identity` events), [server](server.md), [stores](stores.md), [hosting](hosting.md) |
| **Normative references** | [Proposal 0016 *Atproto Spaces*](https://github.com/bluesky-social/proposals/tree/main/0016-permissioned-data) (pinned to a commit in phase 0, then to the GA specification) · open proposal PRs [#119](https://github.com/bluesky-social/proposals/pull/119) (updatable space policy) and [#122](https://github.com/bluesky-social/proposals/pull/122) (shared-secret commitment) · [AT Protocol Spaces alpha](https://atproto.com/blog/atproto-spaces-alpha) (2026-08-20) · [RFC 9421](https://www.rfc-editor.org/rfc/rfc9421) HTTP Message Signatures · [RFC 8941](https://www.rfc-editor.org/rfc/rfc8941) Structured Field Values · [RFC 5869](https://www.rfc-editor.org/rfc/rfc5869) HKDF · [RFC 2104](https://www.rfc-editor.org/rfc/rfc2104) HMAC · BLAKE3 specification. Informative: Atmosphere community forum, Private Data WG threads "Permissioned Data Spaces Protocol Timeline", "Post-alpha plans", "Space commitment changes", "Atproto spaces alpha updates", "Bringing back at://" |

> **Protocol status.** Spaces is AT Protocol's second data protocol: access-controlled data
> stored in per-space repos, sharing DIDs, OAuth, Lexicon and the rest of the framework with the
> public protocol. It is expected to become the basis for most Atmosphere apps. The alpha opened
> on 2026-08-20. The Bluesky protocol team plans to batch the remaining breaking changes into a
> draft specification in October and coordinate general availability (reviewed specs, stable
> SDKs, reference PDS rollout) in November 2026. This spec tracks the proposal and the agreed
> post-alpha changes. Requirements marked **(in flux)** follow a change that is agreed but not yet
> specified in final form, or still under debate. They are re-validated against the then-current
> text in phase 0 and against the GA specification in phase 10. 0.8 implemented the 2026-10-01
> alpha (proposal `0c9c2e88`, reference implementation `679724ad`). Where GA departs from that
> alpha, this spec follows GA.

## Scope

**In: the whole protocol, client and server** ([D-24](../06-decisions.md#d-24-spaces-scope-in-the-rewrite)):

- addressing;
- LtHash and BLAKE3;
- space repos, their lifecycle and their STAR serialization;
- the shared-secret commitment and its header MACs;
- blobs;
- the three token types, consumed and issued;
- credential revocation;
- RFC 9421 request signing with an explicit audience;
- `space:` OAuth scopes;
- the space-scoped client and generated `com.atproto.space.*`/`com.atproto.simplespace.*`
  methods;
- the syncer: incremental sync, full-state recovery, sequenced notifications and `listRepos`
  checkpoints;
- the notification receiver;
- the server roles: space authority (credential issuance, write tracking, sequencing,
  forwarding, revocation, deletion), repo host and `simplespace` (read and write policies,
  member flags, open write spaces, app access, managing app, updatable policy);
- durable notification retries;
- account lifecycle;
- the EF Core stores behind all of it.

**Out:**

- **Public-repo MST machinery** ([repo](repo.md)).
- **A firehose for space data.** Spaces has no relay; apps pull from each host, guided by write
  notifications. Public spaces get no rebroadcastable commitments either, per the post-alpha
  plans.
- **Confidentiality.** Spaces provide access control, not encryption.
- **The Atmosphere groups proposal.** It is a standard layered on top of Spaces. Its Lexicons are
  generated like any other once vendored; no special SDK machinery is planned.

## Public API sketch

```csharp
// ── Layer 0: addressing (root namespace ATProtoNet) ──────────────────────────
public sealed class SpaceUri : IAtIdentifier<SpaceUri>          // a space
{
    public Did Authority { get; }  public Nsid SpaceType { get; }  public RecordKey Skey { get; }
    public SpaceRecordUri Record(Did author, Nsid collection, RecordKey rkey);
}
public sealed class SpaceRecordUri : IAtIdentifier<SpaceRecordUri>   // a record in a space
{
    public SpaceUri Space { get; }  public Did Author { get; }  public Nsid Collection { get; }
    public RecordKey Rkey { get; }  public string Path { get; }      // "{collection}/{rkey}"
}
public union AtAddress(AtUri, SpaceUri, SpaceRecordUri);         // see open point 2

// ── Layer 0: ATProtoNet.Data.Star, ATProtoNet.Crypto(.HttpSignatures) ─────────
public sealed class StarReader { /* bounded, streaming; format per GA (SPC-59) */ }
public sealed class StarWriter { /* repo-host side */ }
public static class Blake3 { public static void Hash(ReadOnlySpan<byte> input, Span<byte> output); }   // XOF
public sealed class KemPublicKey { public KemAlgorithm Algorithm { get; } /* from #atproto_kem */ }
public sealed class KemPrivateKey : IDisposable { public KemPublicKey PublicKey { get; } }
public closed record KemAlgorithm;                               // EcdhP256 now, ML-KEM later (open point 1)
public static class HttpMessageSigner { /* RFC 9421 sign: label, components, keyid, alg */ }
public static class HttpMessageSignatureVerifier { /* RFC 9421 verify; RFC 8941 parsing */ }

// ── Layer 0: ATProtoNet.Spaces ───────────────────────────────────────────────
public sealed class LtHash                                       // mutable accumulator; no value equality
{
    public static LtHash Empty();
    public void Add(Nsid collection, RecordKey rkey, Cid cid);
    public void Remove(Nsid collection, RecordKey rkey, Cid cid);
    public byte[] Digest();                                      // SHA-256 of the 2,048-byte state
    public LtHash Clone();
}
public sealed class SpaceRepoState { public Tid Rev { get; }  public ReadOnlyMemory<byte> Hash { get; }  /* index */ }
public readonly record struct SpaceMacContext(SpaceUri Space, Did Author, Tid Rev);
public static class SpaceMac                                     // in flux (SPC-54…58)
{
    public static SpaceMacRequest CreateRequest(KemPublicKey hostKey, out SharedSecret secret);   // syncer side
    public static SharedSecret Decapsulate(KemPrivateKey hostKey, SpaceMacRequest request);       // host side
    public static byte[] Compute(SharedSecret secret, SpaceMacContext context, ReadOnlySpan<byte> message);
    public static bool Verify(SharedSecret secret, SpaceMacContext context, ReadOnlySpan<byte> message, ReadOnlySpan<byte> mac);
}
public enum SpaceTokenType { Delegation, Credential, ClientAttestation }   // the protocol's three
public sealed class SpaceToken
{
    public SpaceTokenType Type { get; }  public string Issuer { get; }  public string Subject { get; }
    public string? Audience { get; }  public string? KeyId { get; }  public DidKey? ConfirmationKey { get; }
    public DateTimeOffset? IssuedAt { get; }  public DateTimeOffset ExpiresAt { get; }  public string Jti { get; }
    public static SpaceToken Parse(string jwt, SpaceTokenType expected, TimeProvider? time = null);
    public bool VerifySignature(VerificationKey key);
    public static string CreateCredential(SpaceCredentialClaims claims, SigningKey authorityKey);   // authority side
    public static string CreateClientAttestation(ClientAttestationClaims claims, SigningKey clientKey);
}

// ── Layer 2: ATProtoNet.Spaces.Access ────────────────────────────────────────
public sealed class SpaceCredentialProvider(AtProtoClient account, IIdentityResolver identity,
    SpaceCredentialOptions? options = null, TimeProvider? time = null) : IAsyncDisposable
{
    public Task<SpaceCredential> GetCredentialAsync(SpaceUri space, CancellationToken cancellationToken = default);
}
public sealed class SpaceRequestHandler : IXrpcHandler { /* signs for one explicit audience (SPC-25) */ }
public sealed class SpaceCredentialIssuer(SigningKey authorityKey, IIdentityResolver identity,
    ISpacePolicy policy, IReplayStore replay, TimeProvider? time = null);    // authority side (SPC-74)

// extension(AtProtoClient client) { public SpaceClient Space(SpaceUri space); }
public sealed class SpaceClient
{
    public SpaceUri Uri { get; }
    public RecordCollection<T> Records<T>() where T : class, IAtProtoRecord<T>;              // the caller's own space repo
    public RecordReader<T> Records<T>(Did author) where T : class, IAtProtoRecord<T>;        // read-only, any member's repo
    public AtProtoClient ForAuthority();                         // audience: the authority's DID
    public AtProtoClient ForRepo(Did author);                    // audience: the author's DID, at their host
    public Task<XrpcBinaryResponse> GetBlobAsync(Did author, Cid cid, CancellationToken cancellationToken = default);
}

// ── Layer 3: ATProtoNet.Spaces.Sync ──────────────────────────────────────────
public interface ISpaceRepoStore                                 // the application's copy of space data
{
    ValueTask PutAsync(SpaceRecordUri uri, Cid cid, ReadOnlyMemory<byte> record, CancellationToken cancellationToken);
    ValueTask DeleteAsync(SpaceRecordUri uri, CancellationToken cancellationToken);
    ValueTask ReplaceAllAsync(SpaceUri space, Did author, VerifiedSpaceRepo repo, CancellationToken cancellationToken);
    ValueTask DeleteSpaceAsync(SpaceUri space, CancellationToken cancellationToken);
}
public sealed class SpaceSyncer(ISpaceRepoStore store, IIdentityResolver identity,
    SpaceSyncerOptions? options = null, TimeProvider? time = null)
{
    public Task<SpaceSyncResult> SyncRepoAsync(SpaceClient space, SpaceRepoCursor cursor, CancellationToken cancellationToken = default);
    public Task<SpaceSyncResult> RecoverAsync(SpaceClient space, Did author, CancellationToken cancellationToken = default);
    public IAsyncEnumerable<SpaceRepoView> ListChangedReposAsync(SpaceClient space, Tid? checkpoint, CancellationToken cancellationToken = default);
    public static bool NeedsCatchUp(Tid? checkpoint, SpaceWriteNotification notification);
}
public enum SpaceSyncOutcome { UpToDate, Partial, Recovered, NoRepo, SpaceDeleted }
public sealed class SpaceNotificationVerifier(ServiceAuthVerifier serviceAuth, IIdentityResolver identity, SpaceNotificationOptions options);
public interface ISpaceNotificationHandler
{
    ValueTask OnWriteAsync(SpaceWriteNotification notification, CancellationToken cancellationToken);
    ValueTask OnSpaceDeletedAsync(SpaceUri space, CancellationToken cancellationToken);
}

// ── Server roles: ATProtoNet.AspNetCore.Spaces, stores in ATProtoNet.EntityFrameworkCore ─
public interface ISpacePolicy { /* read/write decisions per space (SPC-71, SPC-72) */ }
//   services.AddAtProto()
//       .AddSpaces(o => o.ServiceDid = serviceDid)
//       .AddSpaceAuthority(o => o.SigningKey = authorityKey)
//       .AddSimpleSpace()
//       .AddSpaceRepoHost<MySpaceRepoStorage>()
//       .AddSpaceNotifications<MyNotificationHandler>()
//       .UseEfCoreSpaceStores<AppDbContext>();
//   app.MapAtProtoXrpc();                                       // serves every registered Spaces method
```

## Requirements

### Protocol status, pins and placement

- **SPC-1** (MUST) The implementation targets a pinned protocol revision: a commit of proposal
  0016 chosen in phase 0, replaced by the GA specification once it is published. The pin is
  recorded in an assembly-level attribute next to the Spaces code, in the release notes and in
  `docs/spaces.md`. The integration environment pins the reference PDS image digest.
  *Source:* 0.8 `docs/spaces.md:12-16`, `docs/testing-spaces.md:13` · **Decision** (pin to GA)
- **SPC-2** (MUST) Moving to a new protocol revision is one change set. It updates the pin, the
  vendored Spaces Lexicons, the reference vectors (regenerated from the reference
  implementation) and the affected requirements of this spec.
  - Before 0.9.0, such a change may alter the Spaces API freely.
  - After 0.9.0, it follows the compatibility rules of the 0.9 line
    ([D-02](../06-decisions.md#d-02-ship-as-09)): additive where the protocol change allows,
    otherwise called out as a breaking change in the release notes. If the experimental fallback
    in the header applies, the experimental rules apply instead.
  *Source:* **Decision** [D-08](../06-decisions.md#d-08-spaces-is-experimental)
- **SPC-3** (MUST) The `com.atproto.space.*` and `com.atproto.simplespace.*` Lexicons are vendored
  under `lexicons/` from the pinned revision (source recorded in `lexicons.json`) and generated
  into `ATProtoNet.Lexicons.Com.AtProto.Space` and `…SimpleSpace` like every other first-party
  family, without experimental markers unless the header's fallback applies.
  *Source:* **Decision** D-08 · audit (0.8 hand-writes ~1.75k lines of Space/SimpleSpace clients)
- **SPC-4** (MUST) Spaces code lives in the namespaces listed in the header, at the layer the
  header gives each. The core XRPC pipeline has no Spaces-specific code or credential slot:
  request signing is an `IXrpcHandler` (SPC-26). Stable code outside those namespaces references
  Spaces only where the protocol does: DID document entries in `ATProtoNet.Identity`, `space:`
  scopes in `ATProtoNet.Auth`, and the `AtAddress` union (SPC-52). An architecture test enforces
  the placement.
  *Source:* **Decision** · audit (0.8 `XrpcClient.cs:132-138,690-697`,
  `InMemoryDefaultsWarning.cs:42-66`)

### Addressing

- **SPC-5** (MUST) A space and a record in it are addressed as below:
  - a space: `at://{authority}/space/{spaceType}/{skey}`;
  - a record in it: `at://{authority}/space/{spaceType}/{skey}/{author}/{collection}/{rkey}`.

  `authority` and `author` are DIDs (never handles), `spaceType` and `collection` are NSIDs, and
  `skey` and `rkey` are record keys. The literal `space` segment distinguishes a space address from
  a public AT-URI, because a collection NSID always contains a dot. **(in flux:** the scheme is
  still debated, `at://` versus a separate `ats://`; see SPC-51 and open point 2.**)**
  *Source:* Proposal 0016, *Addressing* · forum "Bringing back at://" · 0.8 `SpaceUri.cs:33-35` ·
  tests `SpaceUriTests`
- **SPC-6** (MUST) `SpaceUri` and `SpaceRecordUri` follow the identifier rules of
  [syntax](syntax.md) ([AG-8](../03-api-guidelines.md#types)): validated on construction,
  sealed classes, `Parse` throws `FormatException`, no conversions from `string`, line breaks
  refused.
  *Source:* **Decision** AG-8 · 0.8 reuses the internal `IIdentifier`

### LtHash and BLAKE3

- **SPC-7** (MUST) The LtHash element for a record is the UTF-8 string
  `"{collection}/{rkey}/{cid}"`, with the CID in its canonical base32 form. It is expanded with
  BLAKE3 in XOF mode to `LtHashStateBytes` (2,048) bytes and read as `LtHashLanes` (1,024)
  little-endian `u16` lanes.
  *Source:* Proposal 0016, *Commit digest* · 0.8 `LtHash.cs:29-32` · tests `LtHashTests` (reference vectors)
- **SPC-8** (MUST) Adding an element adds its lanes to the state modulo 2¹⁶; removing subtracts
  them. The state is a multiset: adding one element twice does not collapse, and adding then
  removing restores the prior state exactly. The repo hash is the SHA-256 of the 2,048-byte state.
  *Source:* Proposal 0016 · tests `LtHashTests`
- **SPC-9** (MUST) `LtHash` is a mutable accumulator and does not override
  `Equals`/`GetHashCode` (0.8's mutable instance with a hash code breaks dictionaries). Digests
  are compared as bytes, in constant time wherever a digest is checked against a commitment.
  *Source:* audit (0.8 `LtHash` overrides `GetHashCode`)
- **SPC-10** (MUST) `Blake3` produces output identical to the BLAKE3 reference for every official
  test vector, for any output length (XOF). It lives in `ATProtoNet.Crypto` (layer 0) as a
  general hash primitive.
  *Source:* official BLAKE3 test vectors (pasted in 0.8 `Blake3Tests`; extracted to the conformance
  suite) · **Decision** (Spaces is core, so its primitives live with the other primitives)

### Space repos and their lifecycle

- **SPC-53** (MUST) There is no explicit repo creation or deletion. A space repo comes into
  existence with the first record written to the space by `createRecord`, `putRecord` or
  `applyWrites`. An empty repo and a nonexistent repo are equivalent. Deleting every record
  removes the repo but not the space. When the authority is the writer's own DID, the space must
  already exist; otherwise the writer's host does not check.
  *Source:* Proposal 0016, *Repo lifecycle* · alpha update 2026-10-15 (space must exist when the
  authorities are the same)
- **SPC-11** ~~(MUST) A space commit is a DRISL-CBOR map `{ver, hash, ikm, sig, mac, rev}`.~~
  **Superseded** by SPC-54…58: the signed-HMAC commit gives no authenticity beyond the transport
  (forum "Space commitment changes") and is replaced by a shared-secret MAC carried in headers.
- **SPC-12** ~~(MUST) The signing context is `"atproto-space-v1"` followed by the length-prefixed
  space URI, author, revision and `ikm`.~~ **Superseded** by SPC-56, which keeps the
  length-prefixed context convention unless the GA text defines another encoding.
- **SPC-13** ~~(MUST) `sig` signs the context with the author's key; `mac` = HMAC-SHA256(HKDF-Expand(`ikm`, context), `hash`).~~
  **Superseded** by SPC-54…58.
- **SPC-14** ~~(MUST) Commit verification checks `ver`, `rev`, MAC, signature in that order.~~
  **Superseded** by SPC-57; deniability is SPC-58.

### Commitment: shared secret and header MACs (in flux)

- **SPC-54** (MUST, in flux) A repo host that offers authenticated space data publishes a
  key-encapsulation public key in the account's DID document as the verification method
  `#atproto_kem`. The KEM is ECDH-based at GA, and a post-quantum KEM (ML-KEM) is expected
  later. `KemPublicKey` and `KemPrivateKey` carry a `KemAlgorithm`, and the implementation is
  algorithm-agile: adding ML-KEM is a new case, not a new API. Signing keys are never used for
  key agreement.
  *Source:* PR #122 (open) · forum "Space commitment changes" (dholms, bnewbold, 2026-10-12/13)
- **SPC-55** (MUST, in flux) A syncer that wants authenticated data generates a fresh shared
  secret per request and encapsulates it to the author's `#atproto_kem` key. It sends the
  encapsulation in a request header holding an RFC 8941 dictionary with at least `type`, `ver`
  and the encapsulated secret. The header name is not settled: `Atproto-Content-Mac` in PR #122,
  `Atproto-Want-Mac` in review. The header name and fields are constants in one place.
  *Source:* PR #122 and its review
- **SPC-56** (MUST, in flux) The host decapsulates the secret and answers with the MAC in a
  response header (`Atproto-Mac` or the GA name). The MAC is HMAC-SHA256 over the message.
  - **Key:** derived from the shared secret with HKDF over a context that binds the space URI,
    the author DID and the repo revision, length-prefixed as in 0.8, unless the GA text says
    otherwise.
  - **Message:** the repo hash (for `getRepo`, `getLatestCommit` and the head of `listRepoOps`)
    or the record bytes (for `getRecord`).
  *Source:* PR #122 ("MAC in HTTP headers", "MACs on individual record requests") · forum "Space
  commitment changes"
- **SPC-57** (MUST) The MAC is optional on the wire: a request without the header gets none.
  - **Requesting.** The SDK's syncer and space client request a MAC whenever the author's DID
    document publishes `#atproto_kem`.
  - **Failing closed.** A requested MAC that is missing or wrong fails verification
    (`SpaceVerificationException`). It never silently degrades to unauthenticated data.
  - **Turning it off.** Callers may disable MACs explicitly for hosts that do not publish a KEM
    key. The SDK then records the data as transport-authenticated only.
  *Source:* PR #122 ("optional, depending on whether the client sets the header") · **Decision**
  (fail closed once requested)
- **SPC-58** (MUST) A MAC proves authenticity to the requester only. Space data is deniable and
  cannot be rebroadcast with proof, including from public spaces. The docs say so.
  *Source:* Proposal 0016 · forum "Post-alpha plans" (no redistributable commitments for public
  spaces) · 0.8 `docs/spaces.md` ("Commits: why they prove nothing")

### Serialization

- **SPC-15** ~~(MUST) A space repo export is a CAR with two roots: commit, then index.~~
  **Superseded** by SPC-59: GA ships STAR files instead of CAR (forum "Post-alpha plans",
  2026-10-08).
- **SPC-16** ~~(MUST) `SpaceRepoCar.VerifyAsync` verifies commit, index hash and blocks in order.~~
  **Superseded** by SPC-60.
- **SPC-59** (MUST, in flux) A full space repo export (`getRepo`) is a STAR file.
  - `StarReader` and `StarWriter` live in `ATProtoNet.Data.Star`.
  - The reader is streaming and bounded: total bytes, record count and record size are named
    limits in [data-model](data-model.md) style. Failures are `FormatException`s from our own
    checks, never library exceptions.
  - The GA format details (layout, framing, ordering, how the repo state and its revision are
    carried) are recorded here when published (open point 1).
  *Source:* forum "Post-alpha plans" (bnewbold, 2026-10-08: "expect to ship with STAR files not CAR
  files")
- **SPC-60** (MUST) Verifying an export checks, in order:
  1. the record index's LtHash equals the repo hash;
  2. the repo hash is authenticated by the MAC (SPC-56/57) when one was requested;
  3. every record matches its CID and is strict record CBOR (SPC-17).

  The first failure decides the verdict. Verification reads at most `MaxSpaceRepoBytes` and fails
  with `SpaceVerificationException`.
  *Source:* 0.8 `SpaceRepoCar.cs` · #133 · test `SpaceRepoCarTests.Verify_MalformedIndexBlock_…`
  (a `CborContentException` used to leak)
- **SPC-17** (MUST) A space record is a DRISL-CBOR map in canonical encoding with text keys only,
  no floats and no tag other than 42. Anything else fails verification. The check is a thin rule
  over the strict `DrislReader` of [data-model](data-model.md), not a second strictness
  implementation.
  *Source:* Proposal 0016 · 0.8 `SpaceRecordCbor.cs`

### Blobs

- **SPC-61** (MUST) Blobs stay on the author's host and are not part of an export.
  - `com.atproto.space.getBlob` fetches one by CID; `com.atproto.space.listBlobs` lists the CIDs a
    repo references.
  - Both accept a space credential or a covering OAuth scope.
  - The client verifies that the fetched bytes match the requested CID (raw codec, SHA-256).
  - The syncer offers blob references found in records to the application's store; it does not
    fetch blobs unless asked.
  *Source:* Proposal 0016, *Blob sync*

### Tokens

- **SPC-18** (MUST) There are three JWT types, distinguished by `typ`:
  `atproto-space-delegation+jwt` (delegation), `atproto-space-credential+jwt` (credential) and
  `atproto-client-attestation+jwt` (client attestation). A token whose `typ` is not the one
  expected is rejected.
  *Source:* Proposal 0016 · 0.8 `SpaceTokens.cs:127-133` · tests `SpaceTokensTests`
- **SPC-19** (MUST) Claims:
  - **Every token** has `iss`, `sub`, `exp` and `jti` (128-bit, hex).
  - **Delegation tokens and attestations** also have `aud`.
  - **An attestation** has `iss` = `sub` = the client ID, and no `kid`.
  - **The other types** default `kid` to the issuer's space signing key (SPC-31).
  - **A credential** has `cnf.kid` set to a P-256 `did:key` (the key the credential is bound to)
    and has `iat`, with `0 < exp − iat ≤ MaxSpaceCredentialLifetime` and
    `iat ≤ now + SpaceTokenClockSkew`.
  *Source:* Proposal 0016, *Access control* · 0.8 `SpaceTokens.cs:136-145,195-222` · tests
  `SpaceTokensTests.Parse_TimeClaimOutsideTheRepresentableRange_Throws`, `SpaceTokenKeyIdTests`
- **SPC-20** (MUST) Default lifetimes are `DefaultSpaceCredentialLifetime` (10 min) for
  credentials and `DefaultShortTokenLifetime` (60 s) for delegation tokens and attestations.
  Credentials are capped at `MaxSpaceCredentialLifetime` (60 min). Time claims outside the
  representable range are refused, not thrown as overflow.
  *Source:* Proposal 0016, *Credential expiration and revocation* · #214 · 0.8 `SpaceTokens.cs:136-142`
- **SPC-21** (MUST) Token encoding and signature checks use the JOSE primitives of
  [crypto](crypto.md) (strict compact JWS, `alg` matching the key's curve). The SDK both
  **verifies** all three types and **issues** credentials (authority side, SPC-74) and client
  attestations (client side). Delegation tokens are minted by the user's PDS
  (`com.atproto.space.getDelegationToken`). There is no second JWT implementation.
  *Source:* **Decision** · audit (0.8 `SpaceTokens` used the internal `Auth.Jwt`)

### HTTP message signatures

- **SPC-22** (MUST) Requests are signed per RFC 9421 with signature label `atproto-space` and
  algorithm `ecdsa-p256-sha256`, as a 64-byte `r‖s` signature. Verification refuses
  DER-encoded signatures and accepts high-S. `Signature-Input` and `Signature` are RFC 8941
  structured fields, parsed strictly, and a repeated label is refused. The RFC 9421 and RFC 8941
  code lives in `ATProtoNet.Crypto.HttpSignatures` (layer 0).
  *Source:* Proposal 0016 · #212 · 0.8 `SpaceHttpSignature.cs:33-45` · tests
  `SpaceHttpSignatureTests`, `StructuredFieldsTests`
- **SPC-23** (MUST) The credential exchange (`com.atproto.space.getSpaceCredential` at the
  authority) carries the delegation token as `Authorization: Bearer …` when one is presented,
  and signs exactly the covered components `("authorization")`, with `keyid` set to the `did:key`
  the credential will be bound to. **(in flux:** GA is expected to make the delegation token
  optional too (SPC-62). The covered components for a tokenless exchange follow the GA text.**)**
  *Source:* Proposal 0016, *Credential flow*
- **SPC-24** (MUST) Every other credentialed request carries `Authorization: Atproto-Space
  {credential}` and `Atproto-Space-Audience: {did}`, and signs
  `("authorization" "atproto-space-audience")` in exactly that order. `keyid`, when present, must
  equal the credential's `cnf.kid`.
  *Source:* Proposal 0016 · 0.8 `SpaceHttpSignature.cs` · tests `SpaceHttpSignatureTests`
- **SPC-25** (MUST) The audience is **explicit**. `SpaceClient.ForRepo(author)` signs for the
  author's DID (repo operations at the author's host), and `SpaceClient.ForAuthority()` signs for
  the authority's bare DID. The handler never derives the audience from request parameters.
  *Source:* **Decision** · audit (0.8 guesses it from a query parameter named `repo`,
  `XrpcClient.cs:690-697`, and `Did.Parse(repo)` throws on a handle)
- **SPC-26** (MUST) Signing is `SpaceRequestHandler : IXrpcHandler` in `ATProtoNet.Spaces.Access`.
  It delegates to `XrpcInvoker` and adds the credential headers and signature. The core XRPC
  pipeline has no Spaces-specific code.
  *Source:* **Decision**, [xrpc-client](xrpc-client.md)

### OAuth scopes

- **SPC-63** (MUST) The `space:` scope grammar and matching rules are implemented by `ScopeSet`
  ([auth](auth.md)).
  - **Grammar:** `space:<spaceType>[?authority=<did>][&skey=<skey>][&collection=<nsid>…][&action=<action>…][&manage=<op>…]`.
  - **Defaults:** `authority=self`, `skey=*`; an omitted `action` grants read, create, update and
    delete; an omitted `collection` falls back to the space-type declaration's `collections`.
  - **Matching:** `read` covers every repo in the space and implies `read_self`; create, update
    and delete are constrained by `collection`; `manage` governs the spaces themselves.
  - **Permission sets:** a `"resource": "space"` entry needs a concrete `spaceType`.
  - **Type declarations:** a space-type NSID resolves to a Lexicon `space` definition, which the
    generator emits as a typed value ([lexicon](lexicon.md)).
  *Source:* Proposal 0016, *OAuth scopes*, *Space type declarations*

### Credential provider

- **SPC-27** (MUST) Each credential is bound to a fresh P-256 key generated for it, held only in
  memory, and disposed when the credential is replaced and no request still uses it.
  *Source:* 0.8 `SpaceCredentialProvider.cs` (`SpaceCredential.Key`)
- **SPC-28** (MUST) Minting fetches a single-use delegation token from the account's PDS
  (`com.atproto.space.getDelegationToken`) immediately before the exchange (SPC-23). It first
  exchanges **without** a client attestation. Only if the authority answers `AppNotAuthorized` and
  a `ClientAttestationFactory` is set does it retry once with an attestation for the authority's
  host audience.
  *Source:* Proposal 0016 (attestation only when the space gates on client identity) · 0.8
  `SpaceCredentialProvider.cs:364-430` · tests `SpaceCredentialMintTests`
- **SPC-29** (MUST) An issued credential is accepted only if its `cnf.kid` is the minted key's
  `did:key` and its `sub` is the requested space URI. Otherwise the provider throws
  `SpaceCredentialException` and discards the credential.
  *Source:* 0.8 `SpaceCredentialProvider.cs:386-389`
- **SPC-30** (MUST) A credential is reused until `exp − min(RenewalWindow, (exp − iat) / 2)`
  (default window 1 min), measured on the injected `TimeProvider`. Concurrent callers for one space
  share one mint. A replaced credential stays usable by requests that hold it until it expires.
  *Source:* 0.8 `SpaceCredentialProvider.cs:83,245` · tests `SpaceCredentialProviderTests`
- **SPC-31** (MUST, in flux) An authority's host is its `#atproto_space_host` service (type
  `AtprotoSpaceHost`, absolute `https` endpoint), and its credential signing key is its
  `#atproto_space` verification method. Only an **absent** entry falls back (to `#atproto_pds` and
  `#atproto` respectively); a published but malformed entry is an error. The lookups live in
  `ATProtoNet.Identity`. Whether dedicated entries stay, and what they mean, is still debated
  (open point 3).
  *Source:* Proposal 0016 · #133 · 0.8 `SpaceAuthority.cs:3-31` · tests `SpaceAuthorityTests` ·
  forum "Post-alpha plans" (DID docs)
- **SPC-32** (MUST) The provider implements `IAsyncDisposable`: disposal releases its keys and
  cancels the in-flight mints it owns. Disposing a client releases nothing it does not own.
  *Source:* audit (0.8's `SpaceReader.Dispose` did nothing) · AG-26
- **SPC-62** (MUST, in flux) If the authority allows it, the provider can mint a credential with
  no delegation token and no attestation. This is how public spaces are read without an atproto
  account. It is exposed as an explicit option, never as a silent fallback.
  *Source:* forum "Post-alpha plans" (bnewbold, 2026-10-08: delegation token and client attestation
  both optional)
- **SPC-65** (MUST) A `SpaceDeleted` error from `getSpaceCredential` stops renewal for that space.
  It surfaces as `SpaceCredentialException` with `Reason = SpaceDeleted`, and the syncer maps it to
  `SpaceSyncOutcome.SpaceDeleted`. Other renewal failures do not imply deletion.
  *Source:* Proposal 0016, *Space deletion*

### Space-scoped client

- **SPC-66** (MUST) `client.Space(spaceUri)` (an extension member on `AtProtoClient`) returns a
  `SpaceClient`.
  - **`Records<T>()`** writes to the caller's own space repo through their own session (OAuth
    `space:` scope); own writes need no space credential.
  - **`Records<T>(author)`** reads any member's repo through a space credential and
    `ForRepo(author)`.
  - **Generated methods** for the space are bound to the right audience.
  *Source:* **Decision** (spaces are core, so the records API covers them) · Proposal 0016
  (writes via the PDS write methods)
- **SPC-67** (MUST) The space records API has the shape of the public records API
  ([xrpc-client](xrpc-client.md)): the same `RecordCollection<T>`/`RecordReader<T>` types bound to
  `T.Collection`, the same `RecordNotFound` mapping and the same swap semantics. Code that works
  on public records works on space records by changing how the collection is obtained.
  *Source:* **Decision**

### Syncer

- **SPC-33** (MUST) `SyncRepoAsync` walks `com.atproto.space.listRepoOps` from the cursor's
  revision (entries `{rev, collection, rkey, cid, prev}`, record values inlined). It applies
  each op to the application's `ISpaceRepoStore` and to a running `LtHash` seeded from the
  cursor's state.
  *Source:* Proposal 0016, *Incremental sync* · 0.8 `SpaceSyncer.cs:238-302` · tests `SpaceSyncerTests`
- **SPC-34** (MUST) At the head of the log, the syncer compares the running digest with the
  repo hash and authenticates it with the MAC (SPC-56/57).
  - **Equal:** `UpToDate`.
  - **Different, or MAC verification failed:** `RecoverAsync` downloads the export, verifies it
    per SPC-60, calls `ReplaceAllAsync` and returns `Recovered`.

  0.8 verified a signed commit against `#atproto` at this point; that is superseded together with
  SPC-11.
  *Source:* Proposal 0016, *Full-state recovery* · 0.8 `SpaceSyncer.cs:302,343-384` · #133
- **SPC-35** (MUST) A page that applies ops or offers a continuation cursor without reaching the
  head returns `Partial`; the caller syncs again to continue.
  *Source:* 0.8 `SpaceSyncer.cs:10-25,287`
- **SPC-36** (MUST) `NoRepo` when the host refuses with `RepoNotFound`, `RepoDeactivated`,
  `RepoSuspended` or `RepoTakendown`, or serves an empty page with no head and no continuation
  while the cursor has no revision. The same empty page when the cursor *has* a revision triggers
  recovery: the repo was emptied, replaced or migrated (SPC-76).
  *Source:* #99 · 0.8 `SpaceSyncer.cs:26-36,256,327,375` · tests `SpaceSyncerTests` (`NoRepo` cases)
- **SPC-37** (MUST) A 4xx other than 401, 403 and 429 from `listRepoOps` triggers recovery; 401,
  403, 429 and 5xx propagate as `XrpcException`. An export larger than `MaxSpaceRepoBytes`
  (declared or actual) fails without reading further.
  *Source:* 0.8 `SpaceSyncer.cs:363-369` · tests `SpaceSyncerTests`
- **SPC-38** (MUST) `ListChangedReposAsync` pages `com.atproto.space.listRepos` (the writer set,
  with each repo's `rev` and `hash`, served by the authority) by an exclusive space-revision
  checkpoint, in ascending order, stopping on a null, empty or repeated cursor. The writer set is
  not authoritative: each listed repo is confirmed by syncing it from its host.
  `NeedsCatchUp(checkpoint, n)` is true when `n`'s space revision is after the checkpoint and its
  previous space revision is not the checkpoint.
  *Source:* Proposal 0016, *The sync boundary* · #213 · 0.8 `SpaceSyncer.cs:189-221`
- **SPC-39** (MUST) Limits (`MaxSpaceRepoBytes`) are option values fixed at construction, not
  settable properties on a shared instance.
  *Source:* audit (0.8 `SpaceSyncer.MaxRepoSize` is settable) · AG-7
- **SPC-68** (MUST) Forwarded write notifications carry the space's current and previous space
  revision (the authority sequences writes across the space). A gap between the syncer's
  checkpoint and a notification's previous revision triggers catch-up through
  `ListChangedReposAsync`.
  *Source:* Proposal 0016, *Write notifications* · alpha release 2026-10-01 (space authorities
  sequence write notifications)
- **SPC-69** (MUST) The syncer registers with `com.atproto.space.registerNotify` (with a space
  credential) and renews the registration before the expiry time the response returns. It
  withdraws with `unregisterNotify` when the application stops following a space.
  *Source:* Proposal 0016, *Write notifications*
- **SPC-70** (MUST) On a verified `notifySpaceDeleted` or a `SpaceDeleted` credential error
  (SPC-65), the syncer calls `ISpaceRepoStore.DeleteSpaceAsync` and stops following the space.
  *Source:* Proposal 0016, *Space deletion*

### Notification receiver

- **SPC-40** (MUST) `ATProtoNet.AspNetCore` (namespace `ATProtoNet.AspNetCore.Spaces`) serves
  `com.atproto.space.notifyWrite` and `com.atproto.space.notifySpaceDeleted` as XRPC procedures
  ([server](server.md)). It dispatches verified notifications to the application's
  `ISpaceNotificationHandler`, which feeds the syncer.
  *Source:* **Decision** · audit (0.8 could register but shipped no receiver)
- **SPC-41** (MUST) A forwarded notification is authenticated with service auth
  ([auth](auth.md)):
  - `aud` must equal the service identifier the application registered, fragment included (for
    example `did:web:app.example#atproto_space_syncer`);
  - `lxm` must equal the called method;
  - `iss` must be the space's authority DID;
  - the `jti` is consumed in an `IReplayStore` only after the signature verifies.
  *Source:* 0.8 `SpaceWriteNotifier.cs:27-37,405-410` · [auth](auth.md) service-auth verification rules
- **SPC-42** (MUST) The receiver validates the input (`space`, `repo`, `rev`, `hash`, current and
  previous space revision) before any authentication work that costs a DID resolution. It
  answers malformed input with `400 InvalidRequest`. A handler failure answers `500`: delivery is
  best-effort, and correctness comes from catch-up (SPC-38, SPC-68).
  *Source:* 0.8 `SpaceModels.cs:421-446` · `SpaceWriteNotifier.cs:18-24`

### Server roles: space authority, repo host, simplespace

These are normative (0.8's "deferred server" knowledge, updated to the GA protocol). They live
in `ATProtoNet.AspNetCore.Spaces` with stores in `ATProtoNet.EntityFrameworkCore`, register
through the standard builder ([hosting](hosting.md)), and use only public XRPC server APIs.

- **SPC-43** (MUST) The authority accepts a delegation token only if its `aud` is
  `{authority}#atproto_space_host` (or the GA equivalent, SPC-31) for the authority named in its
  `sub`.
  *Source:* Proposal 0016 · 0.8 `SpaceTokenVerifier` · tests `SpaceTokenVerifierTests`
- **SPC-44** (MUST) A repo host accepts a credential only if its `iss` is the space's authority
  and it verifies against exactly the key named by `kid` in the authority's DID document, with no
  fallback to another verification method. The request signature is checked per SPC-24 against
  `cnf.kid`, and the credential's `jti` must not be revoked (SPC-48).
  *Source:* 0.8 `SpaceCredentialVerifier.cs` · tests `SpaceServerEndpointTests`
- **SPC-45** (MUST) Single-use tokens (delegation, attestation) are tracked by `(iss, jti, exp)`
  until they can no longer be accepted, with lifetime capped at `MaxSingleUseTokenLifetime`
  (5 min). Verified credentials are cached in an LRU of `VerifiedCredentialCacheCapacity`
  (10,000) entries, at most a quarter per authority.
  *Source:* #125 · 0.8 `SpaceServerOptions.cs:28,64`, `SpaceCredentialVerifier.cs:78,229`
- **SPC-46** (MUST) `notifyWrite` at the authority:
  - Input is validated before authentication.
  - `iss` must equal the writer.
  - The writer must be authorized by the space's write policy (SPC-71); unauthorized writes are
    rejected. No app perimeter applies to writes.
  - `rev` must increase monotonically per repo; a stale `rev` is ignored, and one more than
    `MaxFutureRepoRev` ahead is refused with `FutureRev`.
  - The `hash` is not verified.
  - Each accepted write gets a strictly increasing space revision (a TID) and is forwarded,
    best-effort, with the current and previous space revision to registered syncers, skipping
    the authority's own registration.
  *Source:* Proposal 0016, *Write notifications* · 0.8 `SpaceAuthorityEndpoints.cs:221-269` ·
  tests `SpaceServerEndpointTests`
- **SPC-47** (MUST) Registrations and retries:
  - `registerNotify` registrations expire at a time the response states (default
    `NotifyRegistrationLifetime`, 7 days).
  - A repo host's notification to the authority retries network failures and transient statuses
    (408, 429, 5xx) with jittered backoff from 5 s for `NotifyRetryWindow` (1 h), coalescing per
    space and repo to the latest revision, and stops on permanent errors.
  - Retry state is **durable**: it lives in a store with a contract suite ([stores](stores.md)),
    so a restart loses nothing. 0.8 kept it in memory.
  *Source:* Proposal 0016 · alpha release 2026-10-01 (better retry logic) · 0.8
  `SpaceServerOptions.cs:53`, `SpaceWriteNotifier.cs:18-26,55-58,194-203`
- **SPC-48** (MUST) `notifyCredentialRevoked` at a repo host requires `iss` to be the authority
  and `aud` to be an account hosted there. It takes 1–100 ids per call, and each is retained for at
  least `RevocationRetention` (`MaxSpaceCredentialLifetime + 2 × SpaceTokenClockSkew`, ≥ 60 min),
  so a revoked credential cannot revive.
  *Source:* Proposal 0016 (repo hosts retain revocations ≥ 60 min) · #214 · 0.8
  `SpaceRepoEndpoints.cs:295-317`
- **SPC-49** (MUST) Under the `managing-app` policy, the authority calls
  `com.atproto.simplespace.checkUserAccess` on the managing app, with service auth `iss` = the
  authority and `aud` = the app's service identifier. It passes the space, the user, `access`
  (`read` or `write`) and the attested client if any. An app that cannot be reached means access
  is refused. The space owner is always admitted.
  *Source:* Proposal 0016, *The managing app* · #113 · 0.8 `SimpleSpaceAccessPolicyTests`
- **SPC-50** (MUST) A space created through the policy layer is declared to the authority store,
  so `listRepos`, `registerNotify` and `notifyWrite` find it.
  *Source:* #105 (0.8 issue) · tests `SimpleSpaceAuthorityStoreTests`
- **SPC-71** (MUST) `simplespace`:
  - **Policies:** `readPolicy` and `writePolicy` each accept `public`, `member-list` or
    `managing-app`, defaulting to `member-list`. Hosts reject unimplemented values at create and
    update.
  - **Members:** `putMember` takes required `read` and `write` booleans and replaces both. Read
    access controls credential minting; write access controls whether writes are tracked and
    forwarded.
  - **Open write spaces** are `writePolicy = public`.
  - **App access:** `appAccess` is an open union (`#open` by default, or `#allowList` matched
    against the attested `client_id`), and it does not apply to writes.
  *Source:* Proposal 0016, *Required PDS space management: simplespace* · proposals PR #105
  (merged) · alpha update 2026-09-10
- **SPC-72** (MUST, in flux) Space management sits behind `ISpacePolicy`. Space creation, update
  and deletion may move up to `com.atproto.space` with a `policy` value (PR #119), with
  `simplespace` as the baseline policy every host supports. The generated methods follow the
  vendored Lexicons. Policies are pluggable without changing the authority code; whether a policy
  can be changed on an existing space follows the GA text.
  *Source:* proposals PR #119 (open) and its discussion
- **SPC-73** (MUST) Deleting a space (authority side):
  - credential issuance stops, and `getSpaceCredential` answers `SpaceDeleted`;
  - the authority deletes its own repo's records in the space;
  - it sends `notifySpaceDeleted` to registered syncers, best-effort.

  Members' repo hosts are not notified; member records remain the member's data.
  *Source:* Proposal 0016, *Space deletion*
- **SPC-74** (MUST) Issuing a credential (`SpaceCredentialIssuer`):
  1. validate the delegation token when one is presented (SPC-18…20, SPC-43, single-use per SPC-45);
  2. verify the `atproto-space` signature (keyid a P-256 `did:key`, covered components exactly
     `("authorization")`, `alg` if present `ecdsa-p256-sha256`);
  3. if an attestation is present, resolve its `iss` to client metadata and verify it against the
     client's JWKS by `kid`;
  4. ask the space's `ISpacePolicy` for a decision (the delegation token asserts no membership);
  5. issue a credential bound to the keyid, with the default lifetime and within the maximum.

  Tokenless issuance for public spaces follows SPC-62.
  *Source:* Proposal 0016, *Credential flow*
- **SPC-75** (MUST) Repo host role. Over an application-provided `ISpaceRepoHostStorage`, the
  host serves:
  - `getRepo` (STAR, SPC-59), `listRepoOps`, `getLatestCommit` and `getRecord`, each with
    credential verification (SPC-44) and the optional MAC (SPC-56);
  - `getBlob` and `listBlobs` (SPC-61);
  - the repo lifecycle rules of SPC-53.

  The SDK does not implement a PDS; this role lets PDS and space-host implementations built on the
  SDK speak the protocol.
  *Source:* Proposal 0016, *XRPC API* · 0.8 `AddSpaceRepoHost<T>` (application-provided storage)

### Account lifecycle

- **SPC-76** (MUST) Account and identity changes follow the public protocol's events.
  - **Events.** `#account` and `#identity` events from `com.atproto.sync.subscribeRepos` apply
    to space repos too. When a deactivated, suspended, taken-down or deleted status arrives
    through the firehose integration ([streaming](streaming.md)), the syncer stops syncing that
    author's space repos, and on deletion removes them from the application's store.
  - **Migration.** An account moves its space repos with `listSpaces` and its blobs with
    `listBlobs`. Migration resets the operation log, so the syncer treats a revision that moved
    backwards, or a missing history, as a reason for recovery (SPC-36).
  - **Account status of authorities** is open (open point 4).
  *Source:* Proposal 0016, *Account lifecycle*

### Addressing implementation

- **SPC-51** (MUST) One parser in the syntax layer recognizes space addresses and yields
  `SpaceUri` or `SpaceRecordUri`. The textual scheme (`at://` with the literal `space` segment
  today, possibly `ats://` at GA) is a single implementation point, so either outcome of the URI
  debate is a contained change. `AtUri.Parse` never accepts a space address as a public record URI.
  *Source:* **Decision** · forum "Bringing back at://", "Post-alpha plans" (URI scheme)
- **SPC-52** (MUST) Where a Lexicon `at-uri` field may hold either kind of address, it maps to the
  union `AtAddress(AtUri, SpaceUri, SpaceRecordUri)`, so `switch` handles both protocols. Fields
  that can only hold public addresses keep `AtUri`. How the generator decides is settled with
  [syntax](syntax.md) and [lexicon](lexicon.md) (open point 2).
  *Source:* **Decision** · forum "Bringing back at://" (a single scheme means `at-uri` fields can
  carry space addresses)

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `LtHashLanes` / `LtHashStateBytes` | 1,024 / 2,048 bytes | LtHash state |
| `MaxSpaceRepoBytes` | 256 MiB | one export download |
| `MaxSpaceCredentialLifetime` | 60 min | `exp − iat` of a credential |
| `DefaultSpaceCredentialLifetime` | 10 min | credentials the SDK requests or issues |
| `DefaultShortTokenLifetime` | 60 s | delegation tokens, attestations |
| `SpaceTokenClockSkew` | 5 s | `iat` in the future, expiry tolerance |
| `DefaultRenewalWindow` | 1 min | credential renewal (capped at half the lifetime) |
| `MaxSpaceContextFieldBytes` | 65,535 | MAC context field (u16 prefix) |
| `MaxSingleUseTokenLifetime` | 5 min | delegation and attestation lifetime ceiling at the authority |
| `VerifiedCredentialCacheCapacity` | 10,000 (¼ per authority) | repo host credential cache |
| `NotifyRegistrationLifetime` | 7 days (default) | `registerNotify` |
| `MaxFutureRepoRev` | 5 min | `notifyWrite` `rev` skew |
| `NotifyRetryWindow` / base delay | 1 h / 5 s | repo host → authority retries |
| `RevocationRetention` | ≥ 60 min (3,610 s) | revoked credential ids |
| revocation ids per call | 1–100 | `notifyCredentialRevoked` |
| STAR limits (total, records, record size) | per GA (open point 1) | `StarReader` |

DRISL bounds come from [data-model](data-model.md).

## Errors

| Failure | Result |
|---|---|
| Export, record, MAC or signature verification fails | `SpaceVerificationException : AtProtoException` with a verdict naming the failed check |
| Token malformed, wrong `typ`, claims invalid | `SpaceTokenException : AtProtoException` (parse); verdicts for verification |
| Credential minting refused, credential not bound to the key or space, space deleted | `SpaceCredentialException : AtProtoException` with a `Reason` (`Refused`, `NotBound`, `SpaceDeleted`, …), wrapping the `XrpcException` when there is one |
| Host refusals during sync (401, 403, 429, 5xx) | `XrpcException`, propagated |
| Receiver or server authentication failure | `401`/`403` with the XRPC error envelope ([server](server.md)) |

0.8 has five Spaces exception types; the rewrite keeps three.

## Conformance and tests

**Conformance vectors** (Spaces area, [04 — Testing](../04-testing.md#conformance-areas)), taken from
the GA reference implementation and regenerated on every pin bump (SPC-2):

- **Carried over** (still valid; re-verified against GA): BLAKE3 official vectors, LtHash
  reference vectors, HTTP message signature vectors and structured-field cases, token vectors.
- **New:** STAR export files, KEM encapsulation and decapsulation, header MACs for `getRepo` and
  `getRecord`.
- **Not carried:** 0.8's signed-commit and space-repo CAR vectors (superseded by SPC-54…60).

**0.8 test classes whose scenarios must be ported:** `SpaceUriTests`, `Blake3Tests`,
`LtHashTests`, `SpaceTokensTests`, `SpaceTokenKeyIdTests`, `SpaceHttpSignatureTests`,
`StructuredFieldsTests`, `SpaceAuthorityTests`, `SpaceCredentialProviderTests`,
`SpaceCredentialMintTests` (on `FakeTimeProvider`; 0.8 sleeps 1.3 s of real time),
`SpaceSyncerTests`, `SpaceClientTests`, `SpaceServerEndpointTests`, `SpaceTokenVerifierTests`,
`SpaceWriteNotifierTests` (without real sleeps), `SimpleSpaceEndpointTests`,
`SimpleSpaceAccessPolicyTests`, `SimpleSpaceAuthorityStoreTests`, the store tests and the
`SpaceCredentialRevocationStoreContractTests` contract. `SpaceCommitTests` and `SpaceRepoCarTests`
are replaced by tests for SPC-54…60.

**New tests:**
- the space-scoped records API (own repo through the session, others through credentials);
- the MAC flow (requested, missing, wrong, disabled);
- STAR bounds and hostile input;
- the notification receiver (audience with fragment, `iss` binding, replay consumed only after
  the signature verifies, malformed input before authentication);
- registration renewal;
- space deletion end to end;
- the `simplespace` policy matrix (read and write policies × member flags × app access);
- durable retry state across a restart;
- account-status handling;
- `AtAddress` parsing for both address kinds.

**Integration:** `Requirement=Spaces` runs in PR CI against the pinned reference PDS image once
a GA image exists (nightly before that). It uses its own PLC directory (`ATPROTO_PLC_URL`),
self-provisioned accounts (`ATPROTO_PDS_ADMIN_PASSWORD`) and `ATPROTO_SPACES_PDS_URL`. Phase 10
adds end-to-end flows: an OAuth login with `space:` scopes, space creation, writes by several
members, notification, sync, recovery, revocation and deletion.

## Changes from 0.8

| Change | Reason |
|---|---|
| Spaces is part of the stable surface, spread across the protocol layers, not an experimental leaf | Spaces is core to the protocol and expected to underpin most apps; GA is targeted for November 2026 |
| Full scope, including all server roles, `simplespace` and durable retries | nothing deferred; server roles are needed for apps that act as authorities or hosts |
| Signed-HMAC commit (`ikm`/`sig`/`mac`) replaced by a KEM shared secret and header MACs, also on `getRecord` | the old construction added no authenticity beyond TLS (forum "Space commitment changes", PR #122) |
| CAR export replaced by STAR | upstream ships STAR files at GA |
| BLAKE3 in `ATProtoNet.Crypto`, HTTP signatures in `ATProtoNet.Crypto.HttpSignatures`, STAR in `ATProtoNet.Data.Star` | primitives live with the other primitives (layer 0) |
| `client.Space(uri)` with the same records API as public data | spaces are a first-class place for records |
| Audience is explicit per client (`ForRepo`, `ForAuthority`) | 0.8 guessed it from a `repo` query parameter inside the core transport |
| Signing is an `IXrpcHandler` | no Spaces code in the core pipeline |
| Generated `com.atproto.space.*`/`simplespace.*` | 0.8 hand-wrote them |
| Notification receiver, registration renewal, space-deletion handling | 0.8 could register but not receive |
| Tokenless credentials for public spaces (opt-in) | upstream makes delegation token and attestation optional |
| `simplespace` with independent read/write policies, open write, app access, pluggable `ISpacePolicy` | upstream policy model (PR #105, PR #119) |
| `LtHash` drops value equality; settable limits become options; three exception types instead of five | correctness and AG-7 |

## Open points

1. **GA format details.** Still to be recorded once the text is published:
   - the STAR layout, framing and limits (SPC-59);
   - the KEM algorithm and key type behind `#atproto_kem` (SPC-54);
   - the MAC header names and dictionary fields, `Atproto-Content-Mac` vs
     `Atproto-Want-Mac`/`Atproto-Mac` (SPC-55/56);
   - the HKDF context encoding and which fields live in commit metadata vs headers (SPC-56).

   *Proposed default:* implement against PR #122 as merged, re-validate in phase 10.
2. **URI scheme.** `at://…/space/…` (one scheme, proposed) or `ats://` (debated).
   - The scheme stays a single implementation point (SPC-51), so either outcome is contained.
   - If GA keeps `at://`, Lexicon `at-uri` fields can carry space addresses. Syntax and Lexicon
     specs must then choose between `AtUri` gaining space forms and generated fields becoming
     `AtAddress` where they may hold either.

   *Proposed default:* `AtAddress` (SPC-52) for `at-uri` fields of Lexicons that reference
   spaces, `AtUri` elsewhere.
3. **DID document entries.** Debated: whether dedicated `#atproto_space_host`/`#atproto_space`
   entries stay, whether a credential may be signed with `#atproto` when `#atproto_space` exists,
   and whether the space host is simply the account host. *Proposed default:* the current
   proposal with fallbacks only for absent entries (SPC-31); re-validate at GA.
4. **Account status of space authorities.** Whether an authority without a PDS account has an
   account status, and whether `#atproto_pds` and `#atproto_space_host` statuses can differ.
   *Proposed default:* an authority has no account status unless it also has a PDS account.
5. **Receiver `iss`.** *Proposed default:* only the space authority (SPC-41). Revisit if the
   protocol defines host-signed forwards.
6. **Space discovery for recovering services.** A "which spaces will you give me access to"
   endpoint was raised on the forum and is not in the protocol. *Proposed default:* nothing in
   0.9; re-index as users sign back in.
7. **Atmosphere groups.** Layered on top of Spaces. *Proposed default:* vendor and generate its
   Lexicons when the standard stabilises; no special SDK machinery.
