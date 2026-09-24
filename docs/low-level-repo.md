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

```csharp
var response = await client.Repo.GetRecordAsync<TodoItem>(
    repo: alice,
    collection: Nsid.Parse("com.example.todo.item"),
    rkey: RecordKey.Parse("3k2la7rxjgs2t"));

Console.WriteLine(response.Value.Title);
```

### Put Record

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

```csharp
await foreach (var entry in client.Repo.EnumerateRecordsAsync(client.Did!, collection))
{
    Console.WriteLine(entry.Uri);
}
```

## Repository Info

```csharp
var info = await client.Repo.DescribeRepoAsync(alice);

Console.WriteLine($"Handle: {info.Handle}");
Console.WriteLine($"DID: {info.Did}");
Console.WriteLine($"Collections: {string.Join(", ", info.Collections)}");
```

## Blob Operations

### Upload

```csharp
// From file
var result = await client.Repo.UploadBlobAsync("/path/to/file.jpg", "image/jpeg");

// From stream
var result = await client.Repo.UploadBlobAsync(stream, "image/png");

// From bytes
var result = await client.Repo.UploadBlobAsync(bytes, "application/pdf");
```

### List Missing Blobs

```csharp
var missing = await client.Repo.ListMissingBlobsAsync(limit: 100);

// Or every page
await foreach (var blob in client.Repo.EnumerateMissingBlobsAsync())
    Console.WriteLine(blob.Cid);
```

## Batch Operations

```csharp
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

## Authoring Repository Data

The types above talk to a PDS. The `ATProtoNet.Repo` and `ATProtoNet.Identity` namespaces also let
you *produce* the structures a PDS serves — useful for tests, for a service that mints its own
`did:plc`, or for serving `com.atproto.sync.getRepo` yourself.

### Commit objects

`RepoCommit` builds and signs the commit block that sits at the root of a repository's CAR file, and
that relays verify:

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
[Cryptography → CAR Files](crypto.md#car-files)).

### did:plc genesis operations

`PlcOperationBuilder` builds, signs, and derives the DID from a `did:plc` genesis operation;
`PlcClient.SubmitOperationAsync` publishes it:

```csharp
using var rotationKey = AtProtoCrypto.GenerateK256Key();
using var repoSigningKey = AtProtoCrypto.GenerateP256Key();

var unsigned = PlcOperationBuilder.CreateGenesisOperation(
    rotationKeys: [rotationKey.ToDidKey()],
    signingKeyDidKey: repoSigningKey.ToDidKey(),
    handle: "alice.example.com",
    pdsEndpoint: "https://pds.example.com");

PlcSignedOperation signedOp = PlcOperationBuilder.Sign(unsigned, rotationKey);
Console.WriteLine(signedOp.Did);   // did:plc:… derived from the signed operation

using var plc = new PlcClient();
await plc.SubmitOperationAsync(signedOp);
```

A directory rejection surfaces as `PlcException` with `Kind == PlcErrorKind.InvalidOperation`.

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
