# Low-Level Repo API

For advanced scenarios, you can use the `RepoClient` directly instead of the higher-level `RecordCollection<T>`.

## Direct Record Operations

Every identifier is typed (`AtIdentifier`, `Nsid`, `RecordKey`, `Cid`, `AtUri`; see
[Identity Types](identity-types.md)). Parse literals once, where they enter your code:

```csharp
using ATProtoNet.Identity;

var collection = Nsid.Parse("com.example.myapp.record");
var alice = Did.Parse("did:plc:abc123");
```

### Create Record

<!-- snippet: Nsid collection; -->
```csharp
var response = await client.Repo.CreateRecordAsync(
    repo: client.Did!,
    collection: collection,
    record: new {
        foo = "bar",
        count = 42,
    },
    rkey: null,         // Server generates TID
    validate: true,      // Validate against Lexicon schema
    swapCommit: null     // Optional CAS
);

Console.WriteLine($"URI: {response.Uri}");               // AtUri
Console.WriteLine($"Key: {response.Uri.RecordKey}");     // RecordKey
Console.WriteLine($"CID: {response.Cid}");               // Cid
```

### Get Record (Untyped)

<!-- snippet: Nsid collection; Did alice; -->
```csharp
var response = await client.Repo.GetRecordAsync(
    repo: alice,
    collection: collection,
    rkey: RecordKey.Parse("3k2la7rxjgs2t"));

// response.Value is a JsonElement
Console.WriteLine(response.Value.GetProperty("foo").GetString());

// Or address the record by its AT URI
var same = await client.Repo.GetRecordAsync(
    AtUri.Parse("at://did:plc:abc123/com.example.myapp.record/3k2la7rxjgs2t"));
```

### Get Record (Typed)

<!-- snippet: Did alice; -->
```csharp
var response = await client.Repo.GetRecordAsync<TodoItem>(
    repo: alice,
    collection: Nsid.Parse("com.example.todo.item"),
    rkey: RecordKey.Parse("3k2la7rxjgs2t"));

Console.WriteLine(response.Value.Title);
```

### Put Record

<!-- snippet: Nsid collection; Cid existingCid; -->
```csharp
var response = await client.Repo.PutRecordAsync(
    repo: client.Did!,
    collection: collection,
    rkey: RecordKey.Parse("my-key"),
    record: new TodoItem { Title = "Updated" },
    validate: true,
    swapRecord: existingCid,  // CAS: fail if record changed
    swapCommit: null);
```

### Delete Record

<!-- snippet: Nsid collection; AtUri recordUri; -->
```csharp
var response = await client.Repo.DeleteRecordAsync(
    repo: client.Did!,
    collection: collection,
    rkey: RecordKey.Parse("3k2la7rxjgs2t"),
    swapRecord: null,
    swapCommit: null);

// Or by AT URI
await client.Repo.DeleteRecordAsync(recordUri);
```

### List Records

One page at a time. Filters come before `limit` and `cursor`:

<!-- snippet: Nsid collection; Did alice; -->
```csharp
var response = await client.Repo.ListRecordsAsync(
    repo: alice,
    collection: collection,
    reverse: false,
    limit: 100,
    cursor: null);

foreach (var entry in response.Records)
{
    Console.WriteLine($"{entry.Uri}: {entry.Value}");
}
```

### Enumerate All Records

`Enumerate*` methods fetch the pages for you. They stop when the server returns no cursor, an
empty one, or one it already returned:

<!-- snippet: Nsid collection; -->
```csharp
await foreach (var entry in client.Repo.EnumerateRecordsAsync(client.Did!, collection))
{
    Console.WriteLine(entry.Uri);
}
```

## Repository Info

<!-- snippet: Did alice; -->
```csharp
var info = await client.Repo.DescribeRepoAsync(alice);

Console.WriteLine($"Handle: {info.Handle}");
Console.WriteLine($"DID: {info.Did}");
Console.WriteLine($"Collections: {string.Join(", ", info.Collections)}");
```

## Blob Operations

### Upload

<!-- snippet: Stream stream; byte[] bytes; -->
```csharp
using ATProtoNet.Models;   // BlobRef

// From file
BlobRef fromFile = await client.Repo.UploadBlobAsync("/path/to/file.jpg", "image/jpeg");

// From stream
BlobRef fromStream = await client.Repo.UploadBlobAsync(stream, "image/png");

// From bytes
BlobRef fromBytes = await client.Repo.UploadBlobAsync(bytes, "application/pdf");
```

### List Missing Blobs

```csharp
using ATProtoNet.Http;

var missing = await client.Repo.ListMissingBlobsAsync(limit: 100);

// Or every page
await foreach (var blob in Pagination.EnumerateAsync<ListMissingBlobsResponse, MissingBlob>(
    (cursor, ct) => client.Repo.ListMissingBlobsAsync(cursor: cursor, cancellationToken: ct)))
{
    Console.WriteLine(blob.Cid);
}
```

## Batch Operations

```csharp
using ATProtoNet.Lexicon.Com.AtProto.Repo;

var todos = Nsid.Parse("com.example.todo.item");

var response = await client.Repo.ApplyWritesAsync(
    client.Did!,
    [
        new ApplyWriteCreate
        {
            Collection = todos,
            Value = new TodoItem { Title = "Task 1" },
        },
        new ApplyWriteUpdate
        {
            Collection = todos,
            Rkey = RecordKey.Parse("existing-key"),
            Value = new TodoItem { Title = "Updated" },
        },
        new ApplyWriteDelete
        {
            Collection = todos,
            Rkey = RecordKey.Parse("old-key"),
        },
    ],
    validate: true,
    swapCommit: null);
```

## Verified Record Reads

`Repo.GetRecordAsync` returns whatever the server says. `Sync.GetVerifiedRecordAsync` instead fetches
the record with its proof (`com.atproto.sync.getRecord`: the signed commit, the tree nodes on the
path to the record, and the record) and checks it against the account's signing key, so the answer
holds whichever server delivered it:

<!-- snippet: DidDocument didDocument; Did did; -->
```csharp
using ATProtoNet.Repo;

// The #atproto key of the account's DID document, resolved from a source you trust.
string signingKey = didDocument.GetSigningKey()!;

VerifiedRecord record = await client.Sync.GetVerifiedRecordAsync(
    did, Nsid.Parse("app.bsky.feed.post"), RecordKey.Parse("3k2la…"), signingKey);

if (record.Exists)
    Console.WriteLine($"{record.Cid} at rev {record.Rev}: {record.Value}");
else
    Console.WriteLine($"No such record at rev {record.Rev}");   // the proof shows it is absent
```

A proof that does not verify (a block that does not match its CID, another repository's commit, a
signature from another key, a missing tree node) throws `RepoVerificationException`. To verify a
proof CAR you already hold, call `RecordProof.Verify(carBytes, did, collection, rkey, signingKey)`;
`Sync.GetRecordAsync` downloads one without verifying it.

`Sync.GetBlocksAsync(did, cids)` fetches arbitrary blocks (records or tree nodes) by CID as a CAR;
read it with `CarReader.FromStreamAsync` and call `VerifyAllBlockCids()` before trusting it.

## Importing a Repository

Moving an account to a new PDS includes loading its repository there. `Repo.ImportRepoAsync`
uploads a CAR (as `Sync.GetRepoAsync` exports it) into the signed-in account; blobs follow
separately, listed by `Repo.ListMissingBlobsAsync`:

<!-- snippet: AtProtoClient oldClient, newClient; Did did; string path; -->
```csharp
// On the old PDS: export to a file, so the upload can send a Content-Length and be retried.
await using (var export = await oldClient.Sync.GetRepoAsync(did))
await using (var file = File.Create(path))
    await export.Content.CopyToAsync(file);

// On the new PDS, signed in as the migrating account:
await using var car = File.OpenRead(path);
await newClient.Repo.ImportRepoAsync(car);
```

## Repository Data Structures

A repository is a Merkle Search Tree of DAG-CBOR records, addressed by CID, signed by a commit, and
shipped as a CAR file. `ATProtoNet.Repo` reads and writes each layer.

### DAG-CBOR

Deterministic CBOR encoding/decoding for AT Protocol data:

#### Encoding

<!-- snippet: JsonElement jsonElement; -->
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

#### Decoding

<!-- snippet: byte[] cborBytes; -->
```csharp
// Decode DAG-CBOR bytes back to JSON
JsonElement decoded = DagCborDecoder.Decode(cborBytes);
```

Anything that is not well-formed DAG-CBOR in the AT Protocol data model — malformed CBOR, a float,
a non-string map key, a malformed CID link, or nesting deeper than 64 levels — throws
`FormatException`. The depth limit keeps hostile input from overflowing the stack.

### CID Computation

Compute Content Identifiers (CIDv1) for AT Protocol data:

<!-- snippet: byte[] dagCborBytes, blobBytes; string candidate; -->
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

### CAR Files

Parse Content Addressable aRchive (CAR v1) files — used by `com.atproto.sync.getRepo`:

<!-- snippet: byte[] carData, binaryCid; -->
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
CarBlock? found = car.FindBlock(binaryCid);
CarBlock? rootBlock = car.GetRootBlock();
```

Pass `verifyBlockCids: true` to `FromBytes` (or call `VerifyAllBlockCids()`) to check that every
block hashes to the CID it is filed under.

A malformed CAR throws `FormatException`: a truncated or oversized length prefix, a header that is
not a DAG-CBOR `{roots, version}` map, a root that is not a CID link, or a block addressed by a
CIDv0 (AT Protocol uses CIDv1 only). Every length is checked before it is used, so hostile input
cannot trigger huge allocations or overflow the stack.

#### From Stream

```csharp
using var stream = File.OpenRead("repo.car");
var car = await CarReader.FromStreamAsync(stream);
```

#### Writing CAR files

`CarWriter` is the producer counterpart — it takes the block map `MerkleSearchTree.Serialize()`
returns, or an explicit `CarBlock` sequence:

<!-- snippet: MerkleSearchTree mst; -->
```csharp
var (rootCid, blocks) = mst.Serialize();

byte[] car = CarWriter.Write(rootCid, blocks);

// Or stream it out
await using var file = File.Create("repo.car");
await CarWriter.WriteToAsync(file, [rootCid], blocks.Select(
    kv => new CarBlock(CidComputation.DecodeCidString(kv.Key), kv.Value)));
```

### Merkle Search Tree (MST)

Full in-memory MST implementation for AT Protocol repository data structure:

Keys are repo paths (`collection/rkey`) and values are **binary** record CIDs. A key must be a valid
MST key — one `/` between two non-empty segments of `A-Z a-z 0-9 _ ~ - : .`, at most 1024
characters — or `Add` throws `ArgumentException`.

<!-- snippet: byte[] record1, record2, record3, newCid; -->
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

#### Covering Proofs

`SerializeProof(keys)` emits the covering proof a firehose `#commit` carries for the keys it
touched: the root plus, for each key, the nodes on the path to it and to its immediate neighbours,
exactly as the reference implementation's `getCoveringProof` computes them. That is what lets a relay
replay the operations in reverse against the proof. Keys that are absent (deletions) contribute the
nodes around where they were, which is what proves the absence:

<!-- snippet: MerkleSearchTree mst; -->
```csharp
var (proofRoot, proofBlocks) = mst.SerializeProof(["com.example.todo.item/3k2la7r"]);
byte[] car = CarWriter.Write(proofRoot, proofBlocks);
```

#### Key Depth

Each key's layer in the tree is the number of leading zero 2-bit chunks of its SHA-256 hash
(fanout 4). The tree computes it internally.

## Authoring Repository Data

The types above talk to a PDS. The `ATProtoNet.Repo` and `ATProtoNet.Identity` namespaces also let
you *produce* the structures a PDS serves — useful for tests, for a service that mints its own
`did:plc`, or for serving `com.atproto.sync.getRepo` yourself.

### Commit objects

`RepoCommit` builds and signs the commit block that sits at the root of a repository's CAR file, and
that relays verify:

<!-- snippet: MerkleSearchTree mst; -->
```csharp
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Repo;

var (mstRoot, blocks) = mst.Serialize();

var commit = new RepoCommit
{
    Did = "did:plc:abc123",
    Data = mstRoot,          // binary CID of the MST root
    Rev = Tid.NextString(),  // monotonically increasing revision
    Prev = null,             // deprecated by the spec, but the field must be present
};

using var signingKey = AtProtoCrypto.GenerateP256Key();
SignedRepoCommit signed = commit.Sign(signingKey);

Console.WriteLine(signed.Cid);        // commit CID
bool ok = signed.Verify(signingKey);  // check the signature round-trips
```

`EncodeUnsigned()` returns exactly the bytes that get signed — a byte-for-byte prefix of the signed
encoding, which is what makes `FirehoseVerifier.ExtractSignedView` able to recover them.

Write the commit and its blocks out as a CAR file with `CarWriter` (see
[CAR Files](#car-files)).

### did:plc genesis operations

`PlcOperationBuilder` builds, signs, and derives the DID from a `did:plc` genesis operation;
`PlcClient.SubmitOperationAsync` publishes it:

```csharp
using var rotationKey = AtProtoCrypto.GenerateK256Key();
using var repoSigningKey = AtProtoCrypto.GenerateP256Key();

var unsigned = PlcOperationBuilder.CreateGenesisOperation(
    rotationKeys: [rotationKey.ToDidKey()],
    signingKeyDidKey: repoSigningKey.ToDidKey(),
    handle: Handle.Parse("alice.example.com"),
    pdsEndpoint: "https://pds.example.com");

PlcSignedOperation signedOp = PlcOperationBuilder.Sign(unsigned, rotationKey);
Console.WriteLine(signedOp.Did);   // did:plc:… derived from the signed operation

using var plc = new PlcClient();
await plc.SubmitOperationAsync(signedOp);
```

A directory rejection surfaces as `DidResolutionException` with `Kind == DidResolutionErrorKind.OperationRejected`.

### Record keys from a sequence

`Tid.Next()` is already strictly increasing within a process. For an independent sequence with its
own clock identifier or clock, use a `TidGenerator`; `Tid.FromInt64` / `Tid.ToInt64` convert between a
TID and its raw 64-bit value:

```csharp
var generator = new TidGenerator(clockId: 7);
var rev = generator.Next();
long raw = rev.ToInt64();   // microseconds << 10 | clock id
```

## When to Use Low-Level API

Use `RepoClient` directly when you need:
- Untyped access to `JsonElement` record values
- `swapCommit` for repo-level CAS
- Direct control over validation
- Operations on behalf of other users (admin)
- Access to response metadata beyond what `RecordCollection<T>` exposes

For most custom app scenarios, prefer `RecordCollection<T>` — see [Custom Records](custom-records.md).

## The record type on this page

`TodoItem` is a custom record type (see [Custom Lexicon Records](custom-records.md)):

```csharp
public class TodoItem : AtProtoRecord, IAtProtoRecord
{
    public static Nsid Collection { get; } = Nsid.Parse("com.example.todo.item");
    public override string Type => Collection;

    [JsonPropertyName("title")] public string Title { get; set; } = "";
}
```
