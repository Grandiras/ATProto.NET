# Cryptography

ATProto.NET includes low-level cryptographic operations required by the AT Protocol, along with higher-level utilities for service authentication and key management.

## Key Generation

### P-256 (NIST secp256r1)

```csharp
using ATProtoNet.Crypto;

// Generate a P-256 key pair
var key = AtProtoKey.Generate(KeyCurve.P256);

// Export public key (compressed)
byte[] publicKey = key.ExportPublicKey();

// Export private key (PKCS#8)
byte[] privateKey = key.ExportPrivateKey();

// Import a private key
var imported = AtProtoKey.ImportPrivateKey(privateKey, KeyCurve.P256);
```

### K-256 (secp256k1)

```csharp
// Generate a K-256 key pair (used by some AT Protocol operations)
var key = AtProtoKey.Generate(KeyCurve.K256);
```

## Signing and Verification

```csharp
var key = AtProtoKey.Generate(KeyCurve.P256);

// Sign data
byte[] data = "Hello, AT Protocol"u8.ToArray();
byte[] signature = key.Sign(data);

// Verify signature
bool isValid = key.Verify(data, signature);

// Verify with a public key only
var publicKey = AtProtoKey.ImportPublicKey(key.ExportPublicKey(), KeyCurve.P256);
bool verified = publicKey.Verify(data, signature);
```

All signatures use SHA-256 with low-S normalization, as required by the AT Protocol specification. Signatures with S > half-order are automatically normalized during signing, and rejected during verification.

## Multikey Encoding

AT Protocol uses [Multikey](https://www.w3.org/TR/controller-document/#multikey) format for public keys in DID documents:

```csharp
// Encode a public key as multikey (base58btc with multicodec prefix)
string multikey = AtProtoCrypto.EncodeMultikey(publicKeyBytes, KeyCurve.P256);
// e.g., "zDnae..."

// Decode a multikey back to bytes
(byte[] keyBytes, KeyCurve curve) = AtProtoCrypto.DecodeMultikey(multikey);
```

## did:key

Generate and parse `did:key` identifiers:

```csharp
// Generate a did:key from a key pair
string didKey = AtProtoCrypto.GenerateDidKey(key);
// e.g., "did:key:zDnae..."

// Parse a did:key back to a public key
(byte[] publicKeyBytes, KeyCurve curve) = AtProtoCrypto.ParseDidKey(didKey);
```

## EC Point Decompression

Decompress compressed EC public keys:

```csharp
// Compressed public key (33 bytes for P-256)
byte[] compressed = key.ExportPublicKey();

// Decompress to full point (65 bytes)
byte[] uncompressed = AtProtoCrypto.DecompressPoint(compressed, KeyCurve.P256);
```

## Base58

AT Protocol uses Base58 Bitcoin encoding for certain identifiers:

```csharp
byte[] data = [1, 2, 3, 4, 5];
string encoded = AtProtoCrypto.Base58Encode(data);
byte[] decoded = AtProtoCrypto.Base58Decode(encoded);
```

## Service Authentication

Generate inter-service authentication JWTs for Feed Generators, Labelers, and relay services:

```csharp
using ATProtoNet.Auth;

var key = AtProtoKey.Generate(KeyCurve.P256);

var token = ServiceAuthGenerator.CreateToken(
    issuerDid: "did:web:my-service.example.com",
    audience: "did:plc:target-service",
    signingKey: key,
    lxm: "app.bsky.feed.getFeedSkeleton");  // Optional: Lexicon method

Console.WriteLine($"JWT: {token}");
```

### Token Properties

- Signed with ES256 (P-256) or ES256K (K-256)
- Contains `iss`, `aud`, `exp`, `iat`, `jti`, and optional `lxm` claims
- Default expiry: 60 seconds
- Maximum allowed expiry: 5 minutes

## DAG-CBOR

Deterministic CBOR encoding/decoding for AT Protocol data:

### Encoding

```csharp
using ATProtoNet.Repo;

// Encode a JSON element to DAG-CBOR
byte[] encoded = DagCborEncoder.Encode(jsonElement);
```

DAG-CBOR encoding rules:
- Map keys are sorted lexicographically
- `$link` properties are encoded as CID tag 42
- `$bytes` properties are encoded as CBOR byte strings
- Floats are rejected (AT Protocol doesn't use them)

### Decoding

```csharp
// Decode DAG-CBOR bytes back to JSON
JsonElement decoded = DagCborDecoder.Decode(cborBytes);
```

## CID Computation

Compute Content Identifiers (CIDv1) for AT Protocol data:

```csharp
using ATProtoNet.Repo;

// Compute a CIDv1 from DAG-CBOR encoded data
string cid = CidComputation.ComputeCid(dagCborBytes);

// Verify a CID matches its content
bool matches = CidComputation.VerifyCid(cid, dagCborBytes);

// Base32-lower encoding/decoding
string base32 = CidComputation.Base32LowerEncode(bytes);
byte[] decoded = CidComputation.Base32LowerDecode(base32);
```

CID computation uses SHA-256 with DAG-CBOR (0x71) or raw (0x55) codecs.

## CAR File Reader

Parse Content Addressable aRchive (CAR v1) files — used by `com.atproto.sync.getRepo`:

```csharp
using ATProtoNet.Repo;

// Parse from bytes
var car = CarReader.FromBytes(carData);

// Access root CID
Console.WriteLine($"Root: {car.Root}");

// Enumerate blocks
foreach (var (cid, blockData) in car.Blocks)
{
    Console.WriteLine($"Block {cid}: {blockData.Length} bytes");
}

// Look up a specific block by CID
byte[]? block = car.GetBlock(someCid);
```

### From Stream

```csharp
using var stream = File.OpenRead("repo.car");
var car = await CarReader.FromStreamAsync(stream);
```

## Merkle Search Tree (MST)

Full in-memory MST implementation for AT Protocol repository data structure:

```csharp
using ATProtoNet.Repo;

// Create a new MST
var mst = new MerkleSearchTree();

// Add entries
mst.Add("com.example.todo.item/3k2la7r", cid1);
mst.Add("com.example.todo.item/3k2lb8s", cid2);
mst.Add("app.bsky.feed.post/3k2lc9t", cid3);

// Look up an entry
string? foundCid = mst.Get("com.example.todo.item/3k2la7r");

// Update an entry
mst.Update("com.example.todo.item/3k2la7r", newCid);

// Delete an entry
mst.Delete("com.example.todo.item/3k2la7r");

// Get all entries
var entries = mst.GetEntries();

// Compute the root CID
string rootCid = mst.ComputeRootCid();

// Serialize/Deserialize
byte[] serialized = mst.Serialize();
var restored = MerkleSearchTree.Deserialize(serialized);

// Validate tree integrity
bool isValid = mst.Validate();
```

### Key Depth

MST key depth is computed via SHA-256 leading-zero counting with fanout 4:

```csharp
int depth = MstKeyDepth.Compute("com.example.todo.item/3k2la7r");
```

### Safety Limits

The MST implementation includes DoS protection:
- Maximum 256 entries per node
- Maximum 64 levels of depth

## Next Steps

- [DID Resolution](did-resolution.md) — Resolve DIDs and verify signing keys
- [Firehose Streaming](firehose.md) — Commit signature verification
- [Low-Level Repo API](low-level-repo.md) — Repository operations
