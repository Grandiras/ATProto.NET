# Cryptography and JOSE — spec

**Status:** Draft · **Package:** `ATProtoNet` (namespaces `ATProtoNet.Crypto`,
`ATProtoNet.Crypto.Jose`, `ATProtoNet.Crypto.HttpSignatures`; foundation layer) · **Phase:** 1

**Normative references:** [Cryptography](https://atproto.com/specs/cryptography) ·
[DID: did:key](https://atproto.com/specs/did) · [W3C did:key](https://w3c-ccg.github.io/did-method-key/) ·
[Multikey](https://www.w3.org/TR/controller-document/#multikey) ·
[RFC 7515 (JWS)](https://www.rfc-editor.org/rfc/rfc7515) · [RFC 7517 (JWK)](https://www.rfc-editor.org/rfc/rfc7517) ·
[RFC 7518 (JWA)](https://www.rfc-editor.org/rfc/rfc7518) · [RFC 8812 (ES256K)](https://www.rfc-editor.org/rfc/rfc8812) ·
[RFC 7638 (JWK thumbprint)](https://www.rfc-editor.org/rfc/rfc7638) ·
[Repository: commit signatures](https://atproto.com/specs/repository#commit-objects) ·
[BLAKE3](https://github.com/BLAKE3-team/BLAKE3-specs) ·
[RFC 9421 (HTTP Message Signatures)](https://www.rfc-editor.org/rfc/rfc9421) ·
[RFC 8941 (Structured Field Values)](https://www.rfc-editor.org/rfc/rfc8941) ·
[RFC 5869 (HKDF)](https://www.rfc-editor.org/rfc/rfc5869) · [FIPS 203 (ML-KEM)](https://csrc.nist.gov/pubs/fips/203/final) ·
the space commitment scheme of [spaces.md](spaces.md) (key agreement)

## Scope

**In:**
- the two atproto curves; private (`SigningKey`) and public (`VerificationKey`) keys;
- key generation, import and export (PKCS#8, compressed points, multikey, `did:key`) and the
  `DidKey` value type;
- signing (SHA-256, ECDSA, low-S) and verification under an explicit `SignaturePolicy`, with
  scalar-range checks;
- an injectable verification-key cache;
- JOSE primitives: compact JWS encode/decode/sign/verify, `JsonWebKey`, RFC 7638 thumbprints;
- signing and verifying `SignedObject`s;
- BLAKE3 (hash and extendable output);
- key-agreement keys and key encapsulation, kept apart from signing keys (the space commitment
  scheme establishes a shared secret with a repository host's published key);
- HTTP message signatures (RFC 9421) and the RFC 8941 structured fields they are carried in;
- the key-ownership rule.

**Out:**
- conversion of legacy DID-document verification-method types
  (`EcdsaSecp256k1VerificationKey2019`, `EcdsaSecp256r1VerificationKey2019`) into a `DidKey`
  ([identity.md](identity.md));
- which claims a JWT carries and what a verifier checks for DPoP proofs, service-auth tokens and
  client assertions ([auth.md](auth.md));
- label signing rules ([labels.md](labels.md));
- LtHash, space commits, and *which* components and keys a Spaces request or commitment uses
  ([spaces.md](spaces.md));
- the DRISL bytes a signature covers ([data-model.md](data-model.md#signed-objects-encoding-side)).

## Public API sketch

```csharp
namespace ATProtoNet.Crypto;

public enum KeyCurve { P256, K256 }                 // closed by protocol
public enum SignaturePolicy { LowSOnly, AcceptHighS }

public sealed class DidKey : IAtIdentifier<DidKey>  // "did:key:z…"
{
    public KeyCurve Curve { get; }
    public string Multikey { get; }                 // "z…" (base58btc multibase)
    public ReadOnlySpan<byte> CompressedPublicKey { get; }   // 33 bytes
    public static DidKey FromMultikey(string multikey);
    public VerificationKey ToVerificationKey();
}

public sealed class VerificationKey : IEquatable<VerificationKey>   // immutable, thread-safe
{
    public KeyCurve Curve { get; }
    public DidKey DidKey { get; }
    public static VerificationKey FromCompressedPoint(KeyCurve curve, ReadOnlySpan<byte> point);
    public static VerificationKey FromUncompressedPoint(KeyCurve curve, ReadOnlySpan<byte> point);
    public static VerificationKey FromJsonWebKey(JsonWebKey jwk);
    public bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature,
        SignaturePolicy policy = SignaturePolicy.LowSOnly);
    public JsonWebKey ToJsonWebKey();
}

public sealed class SigningKey : IDisposable                        // private; ToString is redacted
{
    public static SigningKey Generate(KeyCurve curve);
    public static SigningKey ImportPkcs8(ReadOnlySpan<byte> pkcs8, KeyCurve curve);
    public static SigningKey ImportRawPrivateScalar(ReadOnlySpan<byte> scalar, KeyCurve curve);  // hex/env keys
    public KeyCurve Curve { get; }
    public VerificationKey PublicKey { get; }
    public byte[] Sign(ReadOnlySpan<byte> message);                 // 64-byte r||s, low-S
    public int Sign(ReadOnlySpan<byte> message, Span<byte> destination);
    public byte[] ExportPkcs8();
}

public sealed class VerificationKeyCache                            // injectable; no static instance
{
    public VerificationKeyCache(int capacity = 1024);
    public VerificationKey Get(DidKey key);
    public bool Verify(DidKey key, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature,
        SignaturePolicy policy = SignaturePolicy.LowSOnly);
}

public static class SignedObject                                    // the one sign/verify path for DRISL objects
{
    public static ReadOnlyMemory<byte> GetUnsignedBytes(ReadOnlyMemory<byte> signedDrisl,
        string signatureKey = "sig");                               // encoding side: data-model.md DAT-65/66
    public static byte[] Sign(DataModelMap unsigned, SigningKey key,
        SignatureEncoding encoding = SignatureEncoding.Bytes, string signatureKey = "sig");
    public static bool Verify(ReadOnlyMemory<byte> signedDrisl, VerificationKey key,
        SignatureEncoding encoding = SignatureEncoding.Bytes, string signatureKey = "sig");
}
public enum SignatureEncoding { Bytes, Base64Url }                 // Base64Url: PLC operations

namespace ATProtoNet.Crypto.Jose;

public sealed record JwsHeader
{
    public required string Algorithm { get; init; }                 // "ES256" | "ES256K"
    public string? Type { get; init; }
    public string? KeyId { get; init; }
    public JsonWebKey? Jwk { get; init; }
}
public readonly record struct DecodedJws(JwsHeader Header, JsonElement HeaderJson, JsonElement Payload,
    ReadOnlyMemory<byte> SigningInput, ReadOnlyMemory<byte> Signature);
public static class Jws
{
    public const int MaxTokenLength = 16 * 1024;
    public static string Sign(JwsHeader header, ReadOnlySpan<byte> payloadJson, SigningKey key);
    public static bool TryDecode(string token, out DecodedJws jws, [NotNullWhen(false)] out string? error);
    public static bool Verify(in DecodedJws jws, VerificationKey key);   // alg/curve bound, AcceptHighS
    public static string AlgorithmFor(KeyCurve curve);
}
public sealed record JsonWebKey                                    // EC keys only
{
    public required string Kty { get; init; }   // "EC"
    public required string Crv { get; init; }   // "P-256" | "secp256k1"
    public required string X { get; init; }
    public required string Y { get; init; }
    public string? D { get; init; }             // private; never serialised unless explicitly exported
    public string? Kid { get; init; }  public string? Alg { get; init; }  public string? Use { get; init; }
    public string Thumbprint();                 // RFC 7638, base64url SHA-256
    public JsonWebKey ToPublic();
}
public sealed record JsonWebKeySet(IReadOnlyList<JsonWebKey> Keys);

namespace ATProtoNet.Crypto;

public static class Blake3
{
    public static void Hash(ReadOnlySpan<byte> input, Span<byte> output);   // any output length (XOF)
    public static Blake3Hasher CreateHasher();                              // incremental
}

// Key agreement: never the same key material as a SigningKey (CRY-50).
public enum KeyAgreementAlgorithm { /* pinned by the GA space commitment scheme: ECDH today, ML-KEM later */ }
public sealed class KeyAgreementPublicKey : IEquatable<KeyAgreementPublicKey>
{
    public KeyAgreementAlgorithm Algorithm { get; }
    public string Multikey { get; }                                         // as published in a DID document
    public static KeyAgreementPublicKey FromMultikey(string multikey);
    public KeyEncapsulation Encapsulate();                                  // fresh shared secret + ciphertext for the holder
}
public sealed class KeyAgreementKey : IDisposable                           // private; ToString is redacted
{
    public static KeyAgreementKey Generate(KeyAgreementAlgorithm algorithm);
    public static KeyAgreementKey ImportPkcs8(ReadOnlySpan<byte> pkcs8, KeyAgreementAlgorithm algorithm);
    public KeyAgreementPublicKey PublicKey { get; }
    public byte[] Decapsulate(ReadOnlySpan<byte> encapsulation);           // the shared secret
}
public readonly record struct KeyEncapsulation(byte[] SharedSecret, byte[] Encapsulation);

namespace ATProtoNet.Crypto.HttpSignatures;

public sealed record HttpSignatureParameters
{
    public required string Label { get; init; }                             // e.g. "atproto-space"
    public required IReadOnlyList<string> Components { get; init; }         // e.g. ["authorization", "atproto-space-audience"]
    public string? KeyId { get; init; }
    public DateTimeOffset? Created { get; init; }
}
public static class HttpMessageSignature
{
    public static void Sign(HttpRequestMessage request, HttpSignatureParameters parameters, SigningKey key);
    public static HttpSignatureVerdict Verify(HttpSignatureRequest request, string label,
        IReadOnlyList<string> requiredComponents, VerificationKey key);
}
public static class StructuredFields { /* RFC 8941 parse and serialize: items, lists, dictionaries, parameters */ }
```

## Requirements

### Curves and keys

- **CRY-1** (MUST) The supported curves are exactly P-256 (NIST secp256r1) and K-256
  (secp256k1). `KeyCurve` is a closed enum: the set is fixed by the protocol, and AG-9's
  open-set rule applies to Lexicon strings, not here.
  *Source:* [specs/cryptography](https://atproto.com/specs/cryptography) · 0.8 `AtProtoCrypto.cs:620`
- **CRY-2** (MUST) Private and public keys are separate types. `SigningKey` holds private
  material and is `IDisposable`. `VerificationKey` holds only public material, is immutable and
  is safe for concurrent `Verify` calls. In 0.8, `AtProtoKey` mixed both: `Sign` threw on a
  public key, and it was not thread-safe, so `LabelSigner` locked and `DidKeyCache` pooled
  instances.
  *Source:* **Decision** · primitives audit, problem 14
- **CRY-3** (MUST) `SigningKey.Sign` is safe for concurrent callers.
  *Source:* **Decision** (removes the per-caller locking 0.8 needed, `LabelSigner.cs:296`)
- **CRY-4** (MUST) `ImportPkcs8` checks that the key's curve OID matches the declared curve.
  *Source:* 0.8 `AtProtoCrypto.cs:63` · tests `AtProtoCryptoTests.ImportPrivateKey_RejectsCurveMismatch`,
  `ImportPrivateKey_AcceptsCorrectCurve`
- **CRY-5** (MUST) A compressed point is 33 bytes with prefix `0x02` or `0x03` and x below the
  field prime, and it is checked to lie on the curve (y² = x³ + ax + b has a root) before use.
  *Source:* 0.8 `AtProtoCrypto.cs:86` · tests `ImportCompressedPublicKey_XWithNoPointOnTheCurve_ThrowsFormatException`,
  `ImportCompressedPublicKey_XNotBelowTheFieldPrime_ThrowsFormatException`,
  `ImportCompressedPublicKey_InvalidPrefix_ThrowsFormatException`, `ImportCompressedPublicKey_RejectsWrongLength`
- **CRY-6** (MUST) An uncompressed point (65 bytes, prefix `0x04`) converts to the same key as
  its compressed form. Any other point encoding is rejected.
  *Source:* 0.8 `AtProtoCrypto.cs:205` · tests `CompressPublicKey_RejectsUnknownPointEncoding`,
  `FormatDidKey_UncompressedPoint_MatchesTheKeysOwnDidKey`
- **CRY-7** (MUST) Key material is generated with the platform CSPRNG. All other randomness in
  the SDK (nonces, `jti`s, PKCE verifiers, IKM) also comes from `RandomNumberGenerator`.
  *Source:* specs/cryptography · 0.8 `AtProtoCrypto.cs:33-56`

### Signing and verification

- **CRY-10** (MUST) Signing hashes the message with SHA-256 and signs with ECDSA. The output is
  the 64-byte IEEE P1363 `r || s` form, always normalised to low-S (`s ≤ n/2`).
  *Source:* specs/cryptography ("low-S signatures are required") · 0.8 `AtProtoCrypto.cs:597-615`
  (normalisation was once a no-op; fixed) · test `AtProtoCryptoTests.P256_Sign_ProducesLowSSignature`
- **CRY-11** (MUST) `Verify` with `SignaturePolicy.LowSOnly` (the default) returns false unless
  the signature is exactly 64 bytes (DER is rejected), both scalars lie in `[1, n − 1]`, `s ≤ n/2`,
  and the ECDSA check passes.
  *Source:* specs/cryptography · 0.8 `AtProtoCrypto.cs:581,674-690` · tests
  `P256_Verify_RejectsHighSSignature`, `Verify_SignatureOfTheWrongLength_ReturnsFalse`
- **CRY-12** (MUST) `SignaturePolicy.AcceptHighS` normalises a high-S signature before
  verifying, applies the same length and range checks, and is used only where a foreign signer
  may legitimately produce high-S: JWS verification (CRY-31) and HTTP message signatures
  (CRY-55). Repository commits, labels and PLC operations verify with `LowSOnly`.
  *Source:* 0.8 `DidKeyCache.VerifyJws` (`DidKeyCache.cs:56-70`) · service-auth verifier accepts
  high-S (auth audit §3.M)
- **CRY-13** (MUST) A scalar of `n` or more makes verification return false. It MUST NOT
  overflow or throw during low-S normalisation or in the platform verifier.
  *Source:* 0.8 `AtProtoCrypto.cs:575-589` · tests `SignatureScalarRangeTests.VerifySignature_ScalarOutOfRange_IsFalseRatherThanThrowing`,
  `VerifyJwtSignature_ScalarOutOfRange_IsFalseRatherThanThrowing`,
  `NormalizeLowSSignature_SOfTheOrderOrMore_IsLeftForVerificationToRefuse`
- **CRY-14** (MUST) Verification never throws for a malformed signature: it returns false.
  Malformed keys fail earlier, when the key is parsed, with `FormatException`.
  *Source:* 0.8 behaviour · test `Verify_SignatureOfTheWrongLength_ReturnsFalse`
- **CRY-15** (MUST) The interop signature fixtures, including their high-S and DER cases, verify
  with exactly the expected validity under `LowSOnly`.
  *Source:* atproto-interop-tests `crypto/signature-fixtures.json` · tests
  `AtProtoInteropCryptoTests.VerifySignature_InteropFixture_MatchesExpectedValidity`,
  `SignatureFixtures_CoverHighSAndDerCases`
- **CRY-16** (MUST) K-256 **verification** works on every platform the SDK supports. Where the
  platform's ECDSA lacks secp256k1 (macOS without OpenSSL), the SDK uses a managed verification
  fallback. Verification handles only public data, so it need not be constant-time. K-256
  **signing** on such a platform throws `PlatformNotSupportedException` with an explanatory
  message.
  *Source:* new requirement: 0.8 throws on any K-256 operation there (`AtProtoCrypto.cs:39-56`),
  which breaks firehose verification for most `did:plc` accounts, since their signing keys are
  K-256 · see Open points

### did:key and multikey

- **CRY-20** (MUST) A multikey is the multibase `z` (base58btc) encoding of a multicodec prefix
  followed by a 33-byte compressed point. The prefixes are `0x80 0x24` for P-256 (varint of
  `0x1200`) and `0xE7 0x01` for K-256 (varint of `0xE7`). Any other multibase prefix, unknown
  multicodec or key length is rejected. A wrong length is reported as a length error, not as
  garbage.
  *Source:* specs/cryptography · 0.8 `AtProtoCrypto.cs:242,362-375` · tests
  `FromMultikey_WrongKeyLength_ThrowsFormatException`, `MultibaseToBytes_RejectsUnsupportedPrefix`,
  `FromMultikey_RejectsWrongPrefix`
- **CRY-21** (MUST) A `did:key` is `did:key:` followed by a multikey. `DidKey` parses and formats
  it, with the parsing contract of SYN-3/SYN-4. Keys round-trip through `did:key` and multikey,
  for either point parity.
  *Source:* W3C did:key · tests `P256_DidKey_RoundTrips`, `P256_Multikey_RoundTrips`,
  `FromDidKey_ParsedKey_HasTheSameCompressedPointBothParities`
- **CRY-22** (MUST) The W3C did:key test vectors for P-256 and K-256 produce the published
  `did:key` strings from their private keys.
  *Source:* `w3c_didkey_P256.json`, `w3c_didkey_K256.json` · tests
  `AtProtoInteropCryptoTests.ToDidKey_P256InteropFixture_MatchesPublishedDidKey`,
  `ToDidKey_K256InteropFixture_MatchesPublishedDidKey`
- **CRY-23** (MUST) Signing keys, DID-document keys and verification APIs use `DidKey` or
  `VerificationKey`, never a `string`.
  *Source:* **Decision** [AG-9a](../03-api-guidelines.md#types) · primitives audit, problem 14
  (`VerifySignature(string didKey…)`, `GetSigningKey(): string?`)

### Verification-key cache

- **CRY-25** (MUST) `VerificationKeyCache` is an ordinary instance with an LRU of parsed keys
  (default capacity 1,024), shared by whoever is given it (identity resolution, the firehose
  verifier, the service-auth verifier). There is no process-wide static cache.
  *Source:* **Decision** AG-10 · 0.8 `DidKeyCache.Shared` (`DidKeyCache.cs:31`)
- **CRY-26** (MUST) Malformed or unsupported keys are never cached. Concurrent callers across
  evictions always get correct results.
  *Source:* tests `DidKeyCacheTests.Verify_MalformedOrUnsupportedDidKey_ThrowsAndCachesNothing`,
  `Verify_ConcurrentCallersAcrossEvictions_AllGetCorrectResults`,
  `Verify_MoreKeysThanCapacity_EvictsTheLeastRecentlyUsed`

### Compact JWS

- **CRY-30** (MUST) `Jws.TryDecode` accepts exactly three `.`-separated parts of at most
  `MaxTokenLength` characters in total. Each part is base64url (RFC 7515 §2) with no whitespace
  and no characters outside the URL-safe alphabet. Trailing `=` padding is tolerated on read and
  never written. The header and payload must each decode to a JSON object. The signature must
  decode to exactly 64 bytes. `SigningInput` is the ASCII bytes of `header.payload` exactly as
  received. Failure returns false with a short reason and never throws.
  *Source:* RFC 7515 · 0.8 `Auth/Jwt.cs:106-160` (stricter than `Base64Url.DecodeFromChars`,
  which skips whitespace) · `MaxTokenLength` is new
- **CRY-31** (MUST) `Jws.Verify` binds the algorithm to the key: `ES256` only with a P-256 key,
  `ES256K` only with a K-256 key. `none`, HMAC and RSA algorithms and any mismatch return false.
  The signature is checked under `SignaturePolicy.AcceptHighS`.
  *Source:* RFC 7518, RFC 8812 · 0.8 `DidKeyCache.cs:59` · tests
  `AtProtoCryptoTests.JwsAlgorithm_KnownCurve_ReturnsItsAlg`
- **CRY-32** (MUST) `Jws.Sign` writes the header with `alg` from the key's curve (CRY-31), plus
  only the `typ`, `kid` and `jwk` members the caller sets. It base64url-encodes without padding
  and signs with CRY-10.
  *Source:* 0.8 `Auth/Jwt.cs:45-85` · `typ` and `kid` rules per token type are in
  [auth.md](auth.md)
- **CRY-33** (MUST) The JWS layer is claim-agnostic. Which claims a token carries, the clock
  rules and `jti` handling belong to each token type ([auth.md](auth.md),
  [spaces.md](spaces.md)).
  *Source:* 0.8 `Auth/Jwt.cs` header comment ("only the encoding lives here")

### JSON Web Keys

- **CRY-35** (MUST) `JsonWebKey` supports EC keys only: `kty` `EC`, `crv` `P-256` or
  `secp256k1`, and `x` and `y` as unpadded base64url of 32 bytes each. A key whose point is not on
  its curve is rejected when converted to a `VerificationKey`.
  *Source:* RFC 7517/7518, RFC 8812 · 0.8 `Auth/OAuth/OAuthModels.cs:220-250`
- **CRY-36** (MUST) `Thumbprint()` is RFC 7638: SHA-256 over the JSON object
  `{"crv":…,"kty":…,"x":…,"y":…}` with members in lexicographic order and no whitespace,
  base64url-encoded without padding.
  *Source:* RFC 7638 §3 · 0.8 `Auth/OAuth/DPoP.cs:60-80` · tests `DPoPTests`
- **CRY-37** (MUST) The private member `d` is serialised only when the caller explicitly asks for
  a private export. `ToPublic()` drops it. `ToString()` never shows it. A key set served as client
  metadata contains public keys only.
  *Source:* AG-29 · auth audit §3.N ("public keys only")
- **CRY-38** (MUST) `JsonWebKey`, the JWS primitives and thumbprints live in the foundation
  layer (`ATProtoNet.Crypto.Jose`), not in the OAuth namespace, because DPoP, service auth, client assertions and
  Spaces all use them.
  *Source:* **Decision** · auth audit, problem 9 (`JsonWebKey` in `OAuthModels`, the thumbprint
  in `DPoP` used by Spaces)

### Signed objects

- **CRY-40** (MUST) `SignedObject.Sign` encodes the map without its signature entry
  (DAT-65), signs those bytes with CRY-10, and returns the canonical DRISL map with the signature
  added as a byte string (`SignatureEncoding.Bytes`) or as an unpadded base64url string
  (`SignatureEncoding.Base64Url`, used by PLC operations).
  *Source:* specs/repository · [PLC spec](https://web.plc.directory/spec/v0.1/did-plc) · 0.8
  `RepoCommit.cs`, `PlcOperationBuilder.cs`, `LabelSigning.cs`
- **CRY-41** (MUST) `SignedObject.Verify` derives the signed bytes with
  `GetUnsignedBytes` (DAT-66, splicing from the received bytes) and verifies under
  `LowSOnly`. A missing, empty or wrongly typed signature yields false. Bytes that are not strict
  DRISL throw `FormatException`, which callers map to their own verdict (for example, a commit
  with a malformed `data` link is `Invalid`, not a crash, per #180).
  *Source:* 0.8 `CommitBlock.cs:70` · test `VerifySyncAsync_SignedCommitWhoseDataIsNotACid_IsInvalidRatherThanThrowing`
- **CRY-42** (MUST) This is the only sign/verify path for DRISL signed objects in the SDK.
  Commits, labels, PLC operations and space commits use it.
  *Source:* **Decision** DAT-67 · primitives audit, problem 15

### Ownership and secrecy

- **CRY-45** (MUST) No SDK component disposes a `SigningKey` it was given. A component that
  generates a key owns it and disposes it.
  *Source:* **Decision** [AG-26](../03-api-guidelines.md#resources-and-lifetimes) · 0.8
  `ServiceAuthGenerator.Dispose` disposed the caller's key (`ServiceAuthGenerator.cs:313`), while
  `OAuthClient` did not
- **CRY-46** (MUST) `SigningKey.ToString()` names the curve and public key only. Private material
  leaves the object only through an explicit export method.
  *Source:* AG-29
- **CRY-47** (MUST) Using a disposed `SigningKey` throws `ObjectDisposedException`. Callers that
  must not leak that exception type (the DPoP path) translate it at their boundary
  ([auth.md](auth.md)).
  *Source:* test `AtProtoCryptoTests.Sign_ThrowsWhenDisposed` · 0.8 `XrpcClient.cs:619-629`

### BLAKE3

- **CRY-48** (MUST) `Blake3` implements the BLAKE3 hash, keyed mode and extendable output, one-shot
  and incremental, and matches every official BLAKE3 test vector (all input lengths, outputs past
  64 bytes). It lives in the foundation layer because it is a general primitive; LtHash
  ([spaces.md](spaces.md)) is its first user.
  *Source:* BLAKE3 specification · 0.8 `Crypto/Blake3.cs` (internal; tests sat under `Spaces/`) ·
  tests `Blake3Tests` (official vectors)
- **CRY-49** (SHOULD) The implementation is vectorised where the runtime offers it (0.8's SIMD
  path) and allocation-free for one-shot hashing.
  *Source:* 0.8 #112 (vectorised BLAKE3 and LtHash)

### Key agreement

- **CRY-50** (MUST) Key-agreement keys are their own types (`KeyAgreementKey`,
  `KeyAgreementPublicKey`). The SDK never derives one from, or uses one as, a `SigningKey`:
  using the same material for ECDSA and key agreement is unsound, which is why upstream adds a
  separate key to DID documents for the space commitment scheme.
  *Source:* forum "Space commitment changes" (2026-10-12: shared secret via a KEM; "you're not
  supposed to use the same key material for ECDSA & ECDH")
- **CRY-51** (MUST) The API is a key-encapsulation mechanism (encapsulate to a public key, the
  holder decapsulates), so an elliptic-curve construction (an ephemeral key and ECDH) and ML-KEM
  (FIPS 203) fit behind the same types. The algorithms offered are exactly those the pinned space
  commitment scheme names; nothing else is accepted.
  *Source:* forum "Space commitment changes" (KEM framing for post-quantum agility) ·
  **Decision** (closed algorithm set)
- **CRY-52** (MUST) Shared secrets are never used directly as MAC keys; consumers derive keys
  with HKDF (`System.Security.Cryptography.HKDF`) bound to a context string their spec defines.
  Secrets and private keys are redacted in `ToString()` and zeroed on disposal where the platform
  allows.
  *Source:* RFC 5869 · 0.8 `SpaceCommit` (HKDF-Expand over a context) · CRY-46 by analogy
- **CRY-53** (MUST) A key-agreement public key round-trips through its DID-document encoding
  (multikey) exactly; a key of an algorithm not in CRY-51's set is a `FormatException`.
  *Source:* **Decision** · DID-document publication per [identity.md](identity.md)

### HTTP message signatures

- **CRY-54** (MUST) `HttpMessageSignature` builds the RFC 9421 signature base from exactly the
  components and parameters given, in the given order, and writes `Signature-Input` and
  `Signature` as RFC 8941 dictionaries under the given label. Which components a protocol signs
  is that protocol's decision ([spaces.md](spaces.md)), not this layer's.
  *Source:* RFC 9421 §2.5, §4 · 0.8 `Spaces/SpaceHttpSignature.cs` · tests `SpaceHttpSignatureTests`
  (vectors from atproto@679724ad)
- **CRY-55** (MUST) Signatures are ES256 or ES256K as 64-byte `r‖s`. Verification rejects DER,
  accepts high-S (`SignaturePolicy.AcceptHighS`), refuses a label that appears more than once,
  refuses a signature whose covered components do not include every required component, and
  refuses a `keyid` parameter that does not name the expected key.
  *Source:* RFC 9421 §3.2 · 0.8 `SpaceHttpSignature.cs` · tests `SpaceHttpSignatureTests`
- **CRY-56** (MUST) `StructuredFields` parses and serialises RFC 8941 items, lists and
  dictionaries strictly (the RFC's failure rules; no lenient recovery) and passes the HTTP WG
  structured-field test suite. Header values are bounded before parsing.
  *Source:* RFC 8941 §4 · 0.8 `Spaces/StructuredFields.cs` (internal) · tests `StructuredFieldsTests`
- **CRY-57** (MUST) Time parameters (`created`, `expires`) read the clock from an injected
  `TimeProvider`; verification applies the clock-skew limit its caller passes.
  *Source:* AG-28

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `Signature.Length` | 64 bytes | CRY-10, CRY-11, CRY-30 |
| `CompressedPoint.Length` | 33 bytes | CRY-5 |
| `UncompressedPoint.Length` | 65 bytes | CRY-6 |
| `Multikey.PrefixLength` | 2 bytes | CRY-20 |
| `Jws.MaxTokenLength` | 16 KiB | CRY-30 (new; 0.8 had no cap) |
| `VerificationKeyCache` default capacity | 1,024 keys | CRY-25 |
| `StructuredFields.MaxHeaderValueLength` | 8 KiB | CRY-56 (new; bounds `Signature`/`Signature-Input` parsing) |

## Errors

| Failure | Result |
|---|---|
| Malformed key, `did:key`, multikey or JWK | `FormatException` |
| PKCS#8 curve does not match the declared curve | `FormatException` |
| Malformed or invalid signature | `false` from `Verify`, never an exception |
| Malformed compact JWS | `false` from `TryDecode`, with a reason |
| Algorithm/curve mismatch in a JWS | `false` from `Verify` |
| Signed-object bytes not strict DRISL | `FormatException` |
| K-256 signing unsupported by the platform | `PlatformNotSupportedException` |
| Disposed `SigningKey` or `KeyAgreementKey` | `ObjectDisposedException` |
| Malformed structured field, duplicate signature label, missing covered component | verdict with a reason from `HttpMessageSignature.Verify`, never an exception |
| Key-agreement algorithm not in the pinned set | `FormatException` |

## Conformance and tests

**Vectors** (conformance area *Crypto*): `signature-fixtures.json`, `w3c_didkey_K256.json`,
`w3c_didkey_P256.json` from atproto-interop-tests `crypto/`, plus the scalar-range cases of
`SignatureScalarRangeTests`. The signed-object path is also exercised by the commit, label
and PLC vectors of [repo.md](repo.md), [labels.md](labels.md) and [identity.md](identity.md).
A pinned PLC genesis operation vector is added (none exists in 0.8; see identity.md).

**More vectors:** the official BLAKE3 vectors (today pasted into `Spaces/Blake3Tests`), the HTTP
WG structured-field test suite, the RFC 9421 examples, the atproto@679724ad HTTP-signature
vectors (refreshed at each spaces pin), and key-agreement vectors from the pinned space
commitment scheme.

**0.8 test classes whose scenarios are ported:** `AtProtoCryptoTests`,
`AtProtoInteropCryptoTests`, `SignatureScalarRangeTests`, `DidKeyCacheTests`, `DPoPTests`
(thumbprint), `Blake3Tests`, `StructuredFieldsTests`, `SpaceHttpSignatureTests` (the generic
RFC 9421 scenarios; the Spaces component choice stays in spaces.md), and the JWS encode/decode
scenarios currently covered through `DPoPProofGeneratorTests` and `ServiceAuthVerifierTests`.

## Changes from 0.8

| Change | Reason |
|---|---|
| `AtProtoKey` split into `SigningKey` and `VerificationKey`; `DidKey` value type. | Public and private material had different lifetimes and thread-safety; did:keys were strings. |
| `AtProtoCrypto` static grab-bag removed. | Members move onto the key types, `Base58Btc` ([data-model.md](data-model.md)) and `Jws`. |
| Explicit `SignaturePolicy` instead of an internal JWS-only normalisation path. | Makes the one deliberate exception (JWS, HTTP signatures) visible and reviewable. |
| `VerificationKeyCache` is injectable; no `DidKeyCache.Shared`. | AG-10. |
| JOSE types move from `Auth`/`OAuth` to `ATProtoNet.Crypto.Jose`. | Shared by OAuth, service auth and Spaces. |
| K-256 verification fallback where the platform lacks secp256k1. | Verifying most `did:plc` accounts must not depend on the OS. |
| The SDK never disposes keys it is given. | One ownership rule. |
| JWS token length cap. | Principle 2. |
| BLAKE3, RFC 9421 HTTP message signatures and RFC 8941 structured fields move into `ATProtoNet.Crypto` (public). | General primitives used by Spaces, the core data protocol for non-public data; 0.8 kept them internal under `Spaces/`. |
| Key-agreement keys and a KEM API are added. | The space commitment scheme moves to a shared secret established with the repository host's published key. |

## Open points

1. **Managed K-256 verification (CRY-16).** It needs a reviewed implementation of secp256k1
   point arithmetic. *Proposed default:* include it, verification only, in its own internal file
   with the W3C and interop vectors plus cross-checks against the platform implementation on
   Linux in CI. If the review cost is judged too high, the fallback is deferred (K-256
   verification is then unavailable on such platforms) and CRY-16 drops to SHOULD.
2. **Raw private-scalar import.** PDS rotation keys and similar are often configured as hex
   scalars (`PDS_PLC_ROTATION_KEY_K256_PRIVATE_KEY_HEX`). *Proposed default:* keep
   `ImportRawPrivateScalar`, validated as `1 ≤ d < n`.
3. **Key-agreement algorithms.** Upstream proposes an elliptic-curve KEM now and ML-KEM later,
   with the exact curve, KDF and multikey codec still in review (proposal PR "Move space repo
   commit to a shared secret"). .NET 10+ ships `MLKem`, available where the platform provides it
   (OpenSSL 3.5+, recent Windows). *Proposed default:* implement exactly the GA algorithm set;
   if ML-KEM is part of it and the platform lacks it, fail with `PlatformNotSupportedException`
   rather than falling back silently.
