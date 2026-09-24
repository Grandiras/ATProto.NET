# Cryptography

ATProto.NET includes low-level cryptographic operations required by the AT Protocol, along with higher-level utilities for service authentication and key management.

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

```csharp
// Encode the public key as multikey (base58btc with multicodec prefix)
string multikey = key.ToMultikey();
// e.g., "zDnae..."

// Parse a multikey back into a (public-only) key
using var parsed = AtProtoCrypto.FromMultikey(multikey);
KeyCurve curve = parsed.Curve;
```

## did:key

Generate and parse `did:key` identifiers:

```csharp
// Derive a did:key from a key pair
string didKey = key.ToDidKey();
// e.g., "did:key:zDnae..."

// Parse a did:key back into a public key
using var publicKey = AtProtoCrypto.FromDidKey(didKey);
```

## Service Authentication

Generate inter-service authentication JWTs for Feed Generators, Labelers, and relay services:

```csharp
using ATProtoNet.Auth;
using ATProtoNet.Crypto;

using var key = AtProtoCrypto.GenerateP256Key();

using var generator = new ServiceAuthGenerator(
    serviceDid: "did:web:my-service.example.com",
    signingKey: key);

var token = generator.CreateToken(
    audience: "did:plc:target-service",
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
- Map keys are sorted canonically: shorter keys first, keys of equal length by their UTF-8 bytes
- A repeated map key is rejected
- `$link` properties are encoded as CID tag 42
- `$bytes` properties are encoded as CBOR byte strings
- Floats are rejected (AT Protocol doesn't use them)

### Decoding

```csharp
// Decode DAG-CBOR bytes back to JSON
JsonElement decoded = DagCborDecoder.Decode(cborBytes);
```

Anything that is not well-formed DAG-CBOR in the AT Protocol data model — malformed CBOR, a float,
a non-string map key, a malformed CID link, or nesting deeper than 64 levels — throws
`FormatException`. The depth limit keeps hostile input from overflowing the stack.

## CID Computation

Compute Content Identifiers (CIDv1) for AT Protocol data:

```csharp
using ATProtoNet.Repo;

// Compute a CIDv1 from DAG-CBOR encoded data
Cid cid = CidComputation.ComputeForDagCbor(dagCborBytes);

// …or for raw binary (blobs)
Cid blobCid = CidComputation.ComputeForRaw(blobBytes);

// The binary form, as CAR files and commit objects carry it
byte[] binaryCid = CidComputation.ComputeBinaryForDagCbor(dagCborBytes);

// Verify a CID matches its content
bool matches = CidComputation.Verify(cid, dagCborBytes);

// String ↔ binary conversion
byte[] decoded = CidComputation.DecodeCidString("bafyrei…");
string encoded = CidComputation.EncodeCidToString(decoded);

// Non-throwing variant
if (CidComputation.TryDecodeCidString(candidate, out var bytes))
    Console.WriteLine($"{bytes.Length} bytes");
```

CID computation uses SHA-256 with DAG-CBOR (0x71) or raw (0x55) codecs.

## CAR Files

Parse Content Addressable aRchive (CAR v1) files — used by `com.atproto.sync.getRepo`:

```csharp
using ATProtoNet.Repo;

// Parse from bytes
var car = CarReader.FromBytes(carData);

// Access root CIDs (binary form)
foreach (var root in car.Roots)
    Console.WriteLine($"Root: {CidComputation.EncodeCidToString(root)}");

// Enumerate blocks
foreach (var block in car.Blocks)
    Console.WriteLine($"Block {block.CidHex}: {block.Data.Length} bytes");

// Look up a specific block by binary CID, or grab the root block directly
CarBlock? block = car.FindBlock(binaryCid);
CarBlock? rootBlock = car.GetRootBlock();
```

Pass `verifyBlockCids: true` to `FromBytes` (or call `VerifyAllBlockCids()`) to check that every
block hashes to the CID it is filed under.

A malformed CAR throws `FormatException`: a truncated or oversized length prefix, a header that is
not a DAG-CBOR `{roots, version}` map, a root that is not a CID link, or a block addressed by a
CIDv0 (AT Protocol uses CIDv1 only). Every length is checked before it is used, so hostile input
cannot trigger huge allocations or overflow the stack.

### From Stream

```csharp
using var stream = File.OpenRead("repo.car");
var car = await CarReader.FromStreamAsync(stream);
```

### Writing CAR files

`CarWriter` is the producer counterpart — it takes the block map `MerkleSearchTree.Serialize()`
returns, or an explicit `CarBlock` sequence:

```csharp
var (rootCid, blocks) = mst.Serialize();

byte[] car = CarWriter.Write(rootCid, blocks);

// Or stream it out
await using var file = File.Create("repo.car");
await CarWriter.WriteToAsync(file, [rootCid], blocks.Select(
    kv => new CarBlock(CidComputation.DecodeCidString(kv.Key), kv.Value)));
```

## Merkle Search Tree (MST)

Full in-memory MST implementation for AT Protocol repository data structure:

Keys are repo paths (`collection/rkey`) and values are **binary** record CIDs. A key must be a valid
MST key — one `/` between two non-empty segments of `A-Z a-z 0-9 _ ~ - : .`, at most 1024
characters — or `Add` throws `ArgumentException`.

```csharp
using ATProtoNet.Repo;

// Create a new MST
var mst = MerkleSearchTree.Create();

// Add entries — values are binary CIDs
mst.Add("com.example.todo.item/3k2la7r", CidComputation.ComputeBinaryForDagCbor(record1));
mst.Add("com.example.todo.item/3k2lb8s", CidComputation.ComputeBinaryForDagCbor(record2));
mst.Add("app.bsky.feed.post/3k2lc9t", CidComputation.ComputeBinaryForDagCbor(record3));

// Look up an entry
byte[]? foundCid = mst.Get("com.example.todo.item/3k2la7r");

// Update an entry
mst.Update("com.example.todo.item/3k2la7r", newCid);

// Delete an entry
mst.Delete("com.example.todo.item/3k2la7r");

// Get all entries, or just the count
var entries = mst.GetEntries();
int count = mst.Count;

// Compute the root CID (binary)
byte[] rootCid = mst.ComputeRootCid();

// Serialize to a block store: root CID + blocks keyed by base32 CID string
var (root, blocks) = mst.Serialize();
var restored = MerkleSearchTree.Deserialize(root, cid => blocks.GetValueOrDefault(cid));

// Confirm the loaded blocks are the canonical tree for their entries
bool isValid = restored.Validate();
```

`MerkleSearchTree.Create(entries)` builds a tree from an existing key/value set in one call.

The tree keeps its entries as a sorted set and derives the node structure from them on demand, so
any sequence of `Add`/`Update`/`Delete` calls produces exactly the root a bulk build of the same
entries produces — the root every other AT Protocol implementation computes. Root CIDs are pinned
against the reference implementation's test vectors.

`Deserialize` reads untrusted blocks defensively and throws `FormatException` for a missing or
malformed node, an invalid or out-of-order key, a prefix length outside the previous key, or a tree
deeper than 64 layers. It does not re-hash the blocks: read them with `verifyBlockCids: true`, and
call `Validate()`, which rebuilds the tree from its entries and throws `InvalidOperationException`
if the result is not the root it was loaded from.

### Covering Proofs

`SerializeProof(keys)` emits the covering proof a firehose `#commit` carries for the keys it
touched: the root plus, for each key, the nodes on the path to it and to its immediate neighbours,
exactly as the reference implementation's `getCoveringProof` computes them. That is what lets a relay
replay the operations in reverse against the proof. Keys that are absent (deletions) contribute the
nodes around where they were, which is what proves the absence:

```csharp
var (proofRoot, proofBlocks) = mst.SerializeProof(["com.example.todo.item/3k2la7r"]);
byte[] car = CarWriter.Write(proofRoot, proofBlocks);
```

### Key Depth

Each key's layer in the tree is the number of leading zero 2-bit chunks of its SHA-256 hash
(fanout 4). The tree computes it internally.

## Next Steps

- [DID Resolution](did-resolution.md) — Resolve DIDs and verify signing keys
- [Firehose Streaming](firehose.md) — Commit signature verification
- [Low-Level Repo API](low-level-repo.md) — Repository operations
