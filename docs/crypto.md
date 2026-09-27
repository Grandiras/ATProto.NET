# Cryptography

ATProto.NET includes the cryptographic operations the AT Protocol requires: key generation, signing and verification, the multikey and `did:key` encodings, and service authentication tokens. The repository data structures built on them (DAG-CBOR, CIDs, CAR files, the Merkle Search Tree) are on [Low-Level Repo API](low-level-repo.md#repository-data-structures).

## Key Generation

### P-256 (NIST secp256r1)

```csharp
using ATProtoNet.Crypto;

// Generate a P-256 key pair
using var key = AtProtoCrypto.GenerateP256Key();

// Export the public key (compressed, 33 bytes)
byte[] publicKey = key.GetCompressedPublicKey();

// Export the private key (PKCS#8)
byte[] privateKey = key.ExportPrivateKey();

// Import a private key
using var imported = AtProtoCrypto.ImportPrivateKey(privateKey, KeyCurve.P256);
```

### K-256 (secp256k1)

```csharp
// Generate a K-256 key pair (used by some AT Protocol operations)
using var key = AtProtoCrypto.GenerateK256Key();
```

`GenerateK256Key` throws `PlatformNotSupportedException` where the platform's crypto stack has no
secp256k1 — Linux with OpenSSL 1.1+ supports it, macOS and Windows may not.

## Signing and Verification

```csharp
using var key = AtProtoCrypto.GenerateP256Key();

// Sign data
byte[] data = "Hello, AT Protocol"u8.ToArray();
byte[] signature = key.Sign(data);

// Verify signature
bool isValid = key.Verify(data, signature);

// Verify with a public key only
using var publicKey = AtProtoCrypto.ImportCompressedPublicKey(
    key.GetCompressedPublicKey(), KeyCurve.P256);
bool verified = publicKey.Verify(data, signature);

// Or verify straight from a did:key
bool ok = AtProtoCrypto.VerifySignature(key.ToDidKey(), data, signature);
```

All signatures use SHA-256 with low-S normalization, as required by the AT Protocol specification. Signatures with S > half-order are automatically normalized during signing, and rejected during verification, as is anything but the 64-byte `r || s` form (DER included). Pass the raw message — these methods hash it for you.

`AtProtoCrypto.VerifySignature` keeps a bounded cache of parsed keys, keyed by the did:key string, so verifying repeatedly against the same signer skips the parse and key import. `FromDidKey` always returns a fresh key that you own and dispose.

## Multikey Encoding

AT Protocol uses [Multikey](https://www.w3.org/TR/controller-document/#multikey) format for public keys in DID documents:

```csharp continued
// Encode the public key as multikey (base58btc with multicodec prefix)
string multikey = key.ToMultikey();
// e.g., "zDnae..."

// Parse a multikey back into a (public-only) key
using var parsed = AtProtoCrypto.FromMultikey(multikey);
KeyCurve curve = parsed.Curve;
```

## did:key

Generate and parse `did:key` identifiers:

```csharp continued
// Derive a did:key from a key pair
string didKey = key.ToDidKey();
// e.g., "did:key:zDnae..."

// Parse a did:key back into a public key
using var fromDidKey = AtProtoCrypto.FromDidKey(didKey);
```

## Service Authentication

Generate inter-service authentication JWTs for Feed Generators, Labelers, and relay services:

```csharp
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;

using var key = AtProtoCrypto.GenerateP256Key();

using var generator = new ServiceAuthGenerator(
    serviceDid: Did.Parse("did:web:my-service.example.com"),
    signingKey: key);

// The audience is the target's DID with the `#fragment` of the service entry you are calling.
var token = generator.CreateToken(
    audience: "did:web:feed.example.com#bsky_fg",
    lxm: Nsid.Parse("app.bsky.feed.getFeedSkeleton"));  // the method the token is valid for

Console.WriteLine($"JWT: {token}");
```

### Token Properties

- Signed with ES256 (P-256) or ES256K (K-256)
- Contains `iss`, `aud`, `exp`, `iat`, `jti`, and `lxm` claims — all required by the
  [service auth spec](https://atproto.com/specs/xrpc#inter-service-authentication-jwt) since its
  2026 revision
- `aud` is `did#serviceId`; a bare DID is still accepted for receivers that only understand that
  form, but it is deprecated
- `iss` is a bare DID. To sign with a key other than `#atproto`, pass its fragment as the
  constructor's `keyId` (e.g. `"#atproto_label"`) and it is sent as the `kid` header
- Default expiry: 60 seconds
- Maximum allowed expiry: 5 minutes

To accept these tokens on your own service, see
[Serving XRPC to other services](xrpc-handlers.md#serving-xrpc-to-other-services).

## Next Steps

- [DID Resolution](did-resolution.md) — Resolve DIDs and verify signing keys
- [Firehose Streaming](firehose.md) — Commit signature verification
- [Low-Level Repo API](low-level-repo.md) — DAG-CBOR, CIDs, CAR files, the MST, commits and `did:plc` operations
