# Cryptography

`ATProtoNet.Crypto` has the keys and primitives the protocol is built on: the two signing curves
and `did:key`, a verification-key cache, compact JWS and JSON Web Keys (`ATProtoNet.Crypto.Jose`),
BLAKE3, key encapsulation for the Spaces commitment, and RFC 9421 HTTP message signatures with RFC
8941 structured fields (`ATProtoNet.Crypto.HttpSignatures`). None of it does I/O.

## Keys and signatures

atproto signs with P-256 or K-256 (secp256k1): SHA-256, ECDSA, the 64-byte `r || s` form, always
low-S. A `SigningKey` is the private key; its `PublicKey` is a `VerificationKey`, which is
immutable and safe to share between threads. Whoever creates a `SigningKey` disposes it.

```csharp
using ATProtoNet.Crypto;

using var key = SigningKey.Generate(KeyCurve.P256);
byte[] signature = key.Sign("hello"u8);

DidKey didKey = key.PublicKey.DidKey;            // did:key:zDn…
bool valid = didKey.ToVerificationKey().Verify("hello"u8, signature);
```

Keys come in as PKCS#8 (`SigningKey.ImportPkcs8`, which checks the curve) or as the raw hex
scalars PDS configuration uses (`SigningKey.ImportRawPrivateScalar`). Public keys come from a
`did:key` or multikey, a compressed or uncompressed point, or a JWK. Every malformed key fails
when it is parsed, with `FormatException`; `Verify` never throws, it answers `false`.

```csharp continued
var fromDocument = DidKey.FromMultikey("zQ3shokFTS3brHcDQrn82RUDfCZESWL1ZdCEJwekUDPQiYBme");
var restored = SigningKey.ImportPkcs8(key.ExportPkcs8(), KeyCurve.P256);
restored.Dispose();
```

`Verify` accepts only low-S signatures by default, as commits, labels and PLC operations require.
`SignaturePolicy.AcceptHighS` is for bearer tokens and HTTP signatures, whose signers may emit
either form; `Jws.Verify` and `HttpMessageSignatureVerifier` apply it themselves. On a platform
whose ECDSA lacks secp256k1 (macOS), K-256 keys still verify, through a managed implementation;
signing with them throws `PlatformNotSupportedException`.

Parsing a `did:key` costs about as much as verifying with it, so share one `VerificationKeyCache`
between the components that verify the same signers. There is no global instance.

```csharp continued
var cache = new VerificationKeyCache();
bool cached = cache.Verify(didKey, "hello"u8, signature);
```

## JWS and JSON Web Keys

`Jws` is the compact JWS encoding with `ES256` (P-256) and `ES256K` (K-256). It knows nothing about
claims: each token type checks its own. `TryDecode` is strict and bounded (`Jws.MaxTokenLength`), and
`Verify` refuses any algorithm other than the key's own.

```csharp continued
using ATProtoNet.Crypto.Jose;

var token = Jws.Sign(new JwsHeader { Algorithm = Jws.AlgorithmFor(key.Curve), Type = "example+jwt" },
    """{"iss":"did:plc:example"}"""u8, key);

if (Jws.TryDecode(token, out var jws, out var error) && Jws.Verify(jws, key.PublicKey))
    Console.WriteLine(jws.Payload.GetProperty("iss").GetString());
```

`VerificationKey.ToJsonWebKey()` and `FromJsonWebKey` convert to and from EC JWKs, and
`JsonWebKey.Thumbprint()` is the RFC 7638 thumbprint DPoP binds tokens to. A `JsonWebKeySet`
holds public keys only.

## BLAKE3

`Blake3` hashes with any output length, plain or keyed, in one shot or incrementally:

```csharp continued
Span<byte> digest = stackalloc byte[Blake3.HashLength];
Blake3.Hash("data"u8, digest);

var hasher = Blake3.CreateHasher();
hasher.AppendData("da"u8);
hasher.AppendData("ta"u8);
var extended = new byte[2048];
hasher.GetCurrentHash(extended);                 // starts with digest
```

## Key encapsulation

A repository host publishes a key-encapsulation key next to its signing key. A requester
encapsulates a fresh shared secret to it; the host decapsulates the same secret. The secret is only
ever read through HKDF, bound to a context. The algorithm today is DHKEM(P-256, HKDF-SHA256) of
RFC 9180 (`KemAlgorithm.Default`); KEM keys are never signing keys.

```csharp continued
using var hostKey = KemPrivateKey.Generate(KemAlgorithm.Default);
var published = KemPublicKey.FromMultikey(hostKey.PublicKey.Multikey);

var (secret, ciphertext) = published.Encapsulate();
using var mine = secret;
using var theirs = hostKey.Decapsulate(ciphertext.Span);

var macKey = new byte[32];
mine.DeriveKey("example context"u8, macKey);
```

## HTTP message signatures

`HttpMessageSigner` signs exactly the fields and parameters it is given, and
`HttpMessageSignatureVerifier` checks a signature covers what the caller requires. Which fields a
protocol signs is the protocol's choice; Spaces signs its requests under the label
`atproto-space`. The request is a plain list of fields, so this works with any HTTP stack.

```csharp continued
using ATProtoNet.Crypto.HttpSignatures;

KeyValuePair<string, string>[] headers = [new("Authorization", "Bearer token")];
var signed = HttpMessageSigner.Sign(new HttpSignatureRequest(headers),
    new HttpSignatureParameters { Label = "example", Components = ["authorization"], KeyId = key.PublicKey.DidKey },
    key);

var received = new HttpSignatureRequest([.. headers, new("Signature-Input", signed.SignatureInput), new("Signature", signed.Signature)]);
var verdict = HttpMessageSignatureVerifier.Verify(received,
    new HttpSignatureRequirements { Label = "example", Components = ["authorization"] }, key.PublicKey);

if (verdict is HttpSignatureVerdict.Refused refused)
    Console.WriteLine(refused.Reason);
```

A signature with `expires` needs `HttpSignatureRequirements.Time` (your `TimeProvider`) and
optionally `ClockSkew`. `StructuredFields` parses and serializes RFC 8941 items, lists and
dictionaries strictly, for headers of your own.
