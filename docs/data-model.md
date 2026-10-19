# The data model: DRISL, JSON, CIDs, CAR and STAR

*Draft for 0.9 (phase 1).* Everything here is in `ATProtoNet.Data` (and `.Data.Car`, `.Data.Star`) and
does no I/O beyond the `Stream` you pass in.

atproto data has one shape and two encodings: **DRISL** (canonical DAG-CBOR), which is what gets
hashed and signed, and **JSON**, where links are `{"$link": "b…"}` and bytes `{"$bytes": "<base64>"}`.
The kinds are null, boolean, 64-bit integer, string, bytes, link, array and map. There are no floats:
`1.5` is refused, `123.0` is the integer 123.

## Encode a record and compute its CID

```csharp
using ATProtoNet;
using ATProtoNet.Data;

var drisl = DrislJson.ToDrisl("""{"$type":"app.bsky.feed.post","text":"hello","createdAt":"2026-10-19T00:00:00Z"}"""u8);
var cid = Cid.Compute(CidCodec.DagCbor, drisl);
Console.WriteLine(cid);           // bafyrei…
Console.WriteLine(cid.Matches(drisl));
```

The output is canonical whatever order the JSON has: keys sorted shorter first, then by their UTF-8
bytes, integers in their shortest form. The same record always hashes to the same CID.

## Work with a value without a schema

`DataModelValue` holds any value; switch over its cases, or use the map and array as collections.

<!-- snippet: byte[] recordBytes; -->
```csharp
var value = DataModelValue.FromDrisl(recordBytes);
if (value is DataModelMap map && map.TryGetValue("text", out var text) && text is DataModelString { Value: var s })
    Console.WriteLine(s);

Console.WriteLine(value);              // its JSON form
byte[] again = value.ToDrisl();        // canonical DRISL
```

`DataModelValue` converts with System.Text.Json too (`JsonSerializer.Deserialize<DataModelValue>(json)`),
and `DrislJson.WriteJson(drisl, writer)` turns DRISL into JSON without building a tree.

## Strict and lenient reading

Reading is strict by default: canonical key order, no duplicate keys, shortest integers, definite
lengths, valid UTF-8, only tag 42, nothing after the value. Anything hashed, signed or compared by CID
must be read this way, so `CarReader`, `StarReader` and `SignedDrisl` take no mode at all. To display
foreign data that is well-formed but not canonical, opt in per call:

<!-- snippet: byte[] foreignBytes; -->
```csharp
var shown = DataModelValue.FromDrisl(foreignBytes, new DrislReaderOptions { Mode = DrislMode.Lenient });
```

Lenient mode still refuses floats, non-string keys, duplicate keys and malformed links. Every failure,
in either mode, is a `FormatException`; an exceeded limit (64 levels of nesting, `DrislLimits.MaxDepth`)
is a `DataLimitExceededException` whose `Limit` names it.

For a hand-written binder, `DrislReader` reads item by item:

<!-- snippet: byte[] headerBytes; -->
```csharp
var reader = new DrislReader(headerBytes);
reader.ReadStartMap();
while (reader.PeekKind() != DrislValueKind.End)
    switch (reader.ReadKey())
    {
        case "version": Console.WriteLine(reader.ReadInt64()); break;
        default: reader.SkipValue(); break;
    }
reader.ReadEndMap();
reader.ReadEnd();
```

## Links and blobs

In generated models, a Lexicon `cid-link` is a `CidLink` and a blob a `BlobRef`; both read and write
their JSON form. A blob's `ref` must be a raw-codec CID.

<!-- snippet: Cid imageCid; -->
```csharp
var blob = new BlobRef { Ref = new CidLink { Cid = imageCid }, MimeType = "image/png", Size = 48_213 };
var json = JsonSerializer.Serialize(blob);   // {"$type":"blob","ref":{"$link":"bafkrei…"},"mimeType":"image/png","size":48213}
```

Old records may hold legacy blobs, `{"cid": "…", "mimeType": "…"}`. They read without error as a
`BlobRef` with `IsLegacy` set and no `Size`, and are written back in the legacy form, so editing such a
record does not rewrite its blob.

## Read a repository export (CAR)

<!-- snippet: byte[] carBytes; Stream carStream; -->
```csharp
using ATProtoNet.Data.Car;

// The whole file at once: the blocks slice the buffer.
var car = CarReader.Read(carBytes, new CarReaderOptions { VerifyBlockCids = true });
if (car.TryGetBlock(car.Header.Roots[0], out var commit))
    Console.WriteLine(commit.Length);

// Or streaming, one block at a time.
var header = await CarReader.ReadHeaderAsync(carStream, cancellationToken: ct);
await foreach (var block in CarReader.ReadBlocksAsync(carStream, cancellationToken: ct))
    Console.WriteLine($"{block.Cid}: {block.Data.Length} bytes");
```

Every read is bounded: 256 MiB in total, 4,194,304 blocks, 2 MiB per block and 64 KiB of header by
default (`CarLimits`), each checked before anything is buffered past it. `CarWriter.Write` and
`WriteAsync` write a CAR back.

## Space repositories (STAR)

Space repositories are exported as STAR archives instead of CAR: a header map, then each record under
its path, in path order. Record CIDs are computed from the bytes, never read.

<!-- snippet: Stream starStream; -->
```csharp
using ATProtoNet.Data.Star;

var star = await StarReader.ReadHeaderAsync(starStream, cancellationToken: ct);
await foreach (var entry in StarReader.ReadEntriesAsync(starStream, cancellationToken: ct))
    Console.WriteLine($"{entry.Path} {entry.Cid}");
```

`StarWriter.WriteAsync` writes one; the same header and records always give the same bytes.

## Signed objects

Commits, labels and PLC operations are signed over their DRISL map without the `sig` entry.
`SignedDrisl` derives those bytes from what was received, byte for byte; signing and verifying go
through `SignedObject` in `ATProtoNet.Crypto` ([Cryptography](crypto.md)):

<!-- snippet: byte[] signedCommit; ATProtoNet.Crypto.VerificationKey key; -->
```csharp
var unsigned = SignedDrisl.GetUnsignedBytes(signedCommit, out var signature);
var valid = ATProtoNet.Crypto.SignedObject.Verify(signedCommit, key);
```

## Codecs

`Base32` (the CID text alphabet), `Base58Btc` (multikeys and `did:key`) and `UnsignedVarint` (CAR and
STAR framing) are public and are the SDK's only implementations of each.
