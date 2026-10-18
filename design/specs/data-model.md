# Data model: DRISL-CBOR, JSON, CID, CAR, STAR — spec

**Status:** Accepted (maintainer, 2026-10-17) · **Package:** `ATProtoNet` (namespaces `ATProtoNet`,
`ATProtoNet.Data`, `ATProtoNet.Data.Car`, `ATProtoNet.Data.Star`; foundation layer) · **Phase:** 1

**Normative references:** [Data Model](https://atproto.com/specs/data-model) ·
[DRISL](https://dasl.ing/drisl.html) · [DASL CID](https://dasl.ing/cid.html) ·
[Repository: CAR files](https://atproto.com/specs/repository#car-file-serialization) ·
[CARv1](https://ipld.io/specs/transport/car/carv1/) ·
[STAR](https://next.tangled.org/microcosm.blue/star) (space repository serialization, via
[spaces.md](spaces.md) and proposal 0016) ·
[RFC 8949 (CBOR)](https://www.rfc-editor.org/rfc/rfc8949) ·
[RFC 4648 (base32/base64)](https://www.rfc-editor.org/rfc/rfc4648) ·
[unsigned-varint](https://github.com/multiformats/unsigned-varint)

## Scope

**In:** the atproto data model and its two encodings: DRISL-CBOR reading (strict and lenient)
and canonical writing, the JSON forms of links, bytes and blobs, the CBOR↔JSON bridge, the
format-neutral `DataModelValue` tree, the binary form, computation and verification of `Cid`,
`CidLink` and `BlobRef`, the multiformats codecs (base32, base58btc, unsigned varint), CAR v1
reading (bounded, whole-buffer and streaming) and writing for public repositories, STAR reading
and writing for space repositories, and the encoding side of `SignedObject` (deriving the bytes
a signature covers).

**Out:** CID string syntax ([syntax.md](syntax.md#cid-string-syntax)); signing and verifying
those bytes ([crypto.md](crypto.md#signed-objects)); the meaning of commit, MST and frame
structures, whose binders live with their consumers ([repo.md](repo.md),
[streaming.md](streaming.md)) but read through `DrislReader`; Lexicon-level validation such as
`maxLength` ([lexicon.md](lexicon.md)); System.Text.Json contracts for generated types
([D-10](../06-decisions.md#d-10-serialization-systemtextjson-with-source-generation-generated-union-converters)).

## Public API sketch

```csharp
namespace ATProtoNet;

public enum CidCodec : byte { DagCbor = 0x71, Raw = 0x55 }    // the blessed set, closed by protocol

public sealed class Cid : IAtIdentifier<Cid>                  // 36 bytes stored inline
{
    public const int BinaryLength = 36;
    public CidCodec Codec { get; }
    public ReadOnlySpan<byte> Digest { get; }                   // 32-byte SHA-256
    public static Cid Compute(CidCodec codec, ReadOnlySpan<byte> data);
    public static Cid FromBytes(ReadOnlySpan<byte> binary);     // FormatException unless a blessed CID
    public static bool TryFromBytes(ReadOnlySpan<byte> binary, [NotNullWhen(true)] out Cid? cid);
    public void WriteBytes(Span<byte> destination);
    public byte[] ToByteArray();
    public bool Matches(ReadOnlySpan<byte> data);               // recompute with this CID's codec
}

namespace ATProtoNet.Data;

public enum DrislMode { Strict, Lenient }

public sealed record DrislReaderOptions
{
    public DrislMode Mode { get; init; } = DrislMode.Strict;
    public int MaxDepth { get; init; } = DrislLimits.MaxDepth;
}

public sealed class DrislReader                                  // over System.Formats.Cbor
{
    public DrislReader(ReadOnlyMemory<byte> data, DrislReaderOptions? options = null);
    public DrislValueKind PeekKind();
    public int ReadStartMap();  public string ReadKey();  public void ReadEndMap();
    public int ReadStartArray();  public void ReadEndArray();
    public long ReadInt64();  public string ReadString();  public ReadOnlyMemory<byte> ReadBytes();
    public Cid ReadLink();  public bool ReadBoolean();  public void ReadNull();
    public void SkipValue();                                     // iterative, depth-counted
    public void ReadEnd();                                       // no trailing bytes
}

public sealed class DrislWriter                                  // output is always canonical
{
    public DrislWriter(IBufferWriter<byte> output);
    public void WriteStartMap(int count);  public void WriteKey(string key);  public void WriteEndMap();
    // … arrays, Int64, String, Bytes, Link(Cid), Boolean, Null
}

public closed record DataModelValue                              // closed hierarchy (C# 15 `closed`, AG-6a)
{
    public static DataModelValue FromDrisl(ReadOnlyMemory<byte> cbor, DrislReaderOptions? options = null);
    public static DataModelValue FromJson(ReadOnlySpan<byte> utf8Json);
    public byte[] ToDrisl();
    public void WriteJson(Utf8JsonWriter writer);
}
// Variants: DataModelNull, DataModelBoolean, DataModelInteger(long), DataModelString,
// DataModelBytes, DataModelLink(Cid), DataModelArray, DataModelMap (ordered string keys).

public sealed class CidLink { public required Cid Cid { get; init; } }       // {"$link": "…"}
public sealed class BlobRef                                                  // {"$type":"blob",…}
{
    public required CidLink Ref { get; init; }                  // codec must be Raw
    public required string MimeType { get; init; }              // non-empty
    public long? Size { get; init; }                            // null only for legacy blobs
    public bool IsLegacy { get; }
}

public static class Base32 { /* RFC 4648 lower-case, unpadded */ }
public static class Base58Btc { /* multibase 'z' alphabet */ }
public static class UnsignedVarint { /* minimal LEB128, ≤ 9 bytes */ }

// SignedObject lives in ATProtoNet.Crypto (crypto.md); its encoding side is specified here:
//   public static ReadOnlyMemory<byte> SignedObject.GetUnsignedBytes(ReadOnlyMemory<byte> signedDrisl, string signatureKey = "sig");

public sealed class DataLimitExceededException : FormatException { public string Limit { get; } }

namespace ATProtoNet.Data.Car;

public sealed record CarReaderOptions
{
    public long MaxTotalBytes { get; init; } = CarLimits.DefaultMaxTotalBytes;
    public int MaxBlocks { get; init; } = CarLimits.DefaultMaxBlocks;
    public int MaxBlockBytes { get; init; } = CarLimits.DefaultMaxBlockBytes;
    public int MaxHeaderBytes { get; init; } = CarLimits.MaxHeaderBytes;
    public bool VerifyBlockCids { get; init; }
}
public sealed record CarHeader(int Version, IReadOnlyList<Cid> Roots);
public sealed record CarBlock(Cid Cid, ReadOnlyMemory<byte> Data);
public sealed class CarFile                                      // whole-buffer result
{
    public CarHeader Header { get; }
    public IReadOnlyList<CarBlock> Blocks { get; }               // file order
    public bool TryGetBlock(Cid cid, out ReadOnlyMemory<byte> data);   // first occurrence wins
}
public static class CarReader
{
    public static CarFile Read(ReadOnlyMemory<byte> car, CarReaderOptions? options = null);
    public static ValueTask<CarHeader> ReadHeaderAsync(Stream car, CarReaderOptions? options = null, CancellationToken cancellationToken = default);
    public static IAsyncEnumerable<CarBlock> ReadBlocksAsync(Stream car, CarReaderOptions? options = null, CancellationToken cancellationToken = default);
}
public static class CarWriter
{
    public static ValueTask WriteAsync(Stream output, IEnumerable<Cid> roots, IAsyncEnumerable<CarBlock> blocks, CancellationToken cancellationToken = default);
    public static void Write(IBufferWriter<byte> output, IEnumerable<Cid> roots, IEnumerable<CarBlock> blocks);
}

namespace ATProtoNet.Data.Star;                                 // space repositories (DAT-72…DAT-79)

public sealed record StarReaderOptions
{
    public long MaxTotalBytes { get; init; } = StarLimits.DefaultMaxTotalBytes;
    public int MaxEntries { get; init; } = StarLimits.DefaultMaxEntries;
    public int MaxEntryBytes { get; init; } = StarLimits.DefaultMaxEntryBytes;
    public int MaxHeaderBytes { get; init; } = StarLimits.MaxHeaderBytes;
}
public sealed record StarHeader(ReadOnlyMemory<byte> Drisl);    // interpreted by its consumer (the space commit, spaces.md)
public sealed record StarEntry(string Path, Cid Cid, ReadOnlyMemory<byte> Record);   // Cid recomputed, never read
public static class StarReader
{
    public static ValueTask<StarHeader> ReadHeaderAsync(Stream star, StarReaderOptions? options = null, CancellationToken cancellationToken = default);
    public static IAsyncEnumerable<StarEntry> ReadEntriesAsync(Stream star, StarReaderOptions? options = null, CancellationToken cancellationToken = default);
}
public static class StarWriter
{
    public static ValueTask WriteAsync(Stream output, ReadOnlyMemory<byte> header, IAsyncEnumerable<(string Path, ReadOnlyMemory<byte> Record)> entries, CancellationToken cancellationToken = default);
}
```

## Requirements

### Data model values

- **DAT-1** (MUST) The data model has exactly these kinds: null, boolean, integer, string, bytes,
  link and array, plus map with string keys. Floats are not part of the atproto data model. Every
  reader and writer rejects them, in CBOR and in JSON-to-model conversion.
  *Source:* [specs/data-model](https://atproto.com/specs/data-model#data-types) · 0.8
  `DagCborDecoder`/`DagCborEncoder` · tests `DagCborDecoderTests.Decode_FloatValue_ThrowsFormatException`,
  `DagCborEncoderTests.Encode_Float_Throws`
- **DAT-2** (MUST) Integers are signed 64-bit. A CBOR integer outside `Int64`, or a JSON number
  with a fraction or exponent, is rejected. The error message names the actual problem (0.8
  reported out-of-range integers as "floating point", `DagCborEncoder.cs:195`).
  *Source:* specs/data-model · primitives audit, problem 8
- **DAT-3** (MUST) Map keys are strings. A non-string key is rejected.
  *Source:* specs/data-model · 0.8 `DagCborJson.cs:76`
- **DAT-4** (MUST) `DataModelValue` represents any data-model value without a schema. It
  converts losslessly to and from DRISL and JSON (DAT-45), and it is the bridge from CBOR
  record bodies to generated types (D-10).
  *Source:* **Decision** D-10 · [02 — Architecture](../02-architecture.md#1-identifiers-and-data-layer-0)

### CID

- **DAT-10** (MUST) A binary CID is exactly 36 bytes: version `0x01`, codec `0x71` (DRISL) or
  `0x55` (raw), multihash `0x12` (sha2-256), length `0x20`, then the 32-byte digest. Anything
  else is not a blessed CID and is rejected.
  *Source:* specs/data-model#link-and-cid-formats · 0.8 `Cid.cs:34-41`, `CidComputation.cs:15-27`
- **DAT-11** (MUST) `Cid.Compute(codec, data)` is the CID of SHA-256(`data`) with that codec.
  `Matches(data)` recomputes with the CID's own codec.
  *Source:* specs/data-model · test `CidTests.ComputeForDagCbor_ReturnsParsedCid`
- **DAT-12** (MUST) Binary input starting with `0x12` is reported as an unsupported CIDv0, not
  as generic garbage.
  *Source:* 0.8 `CarReader.cs` CID parsing · test `CarReaderTests.FromBytes_CidV0Block_ThrowsFormatException`
- **DAT-13** (MUST) `Cid` stores its 36 bytes inline, compares and hashes by those bytes, and its
  text form is SYN-60. A computed CID equals the same CID parsed from text.
  *Source:* 0.8 `Cid.cs` inline storage · test `CidTests.Equals_SameCidParsedAndComputed_IsEqualWithTheSameHashCode`
- **DAT-14** (MUST) No public API in the SDK accepts or returns a CID as `byte[]` or as a
  base32 `string`. `Cid` is the only representation.
  *Source:* **Decision** [AG-9a](../03-api-guidelines.md#types) · 0.8 used three
  representations (primitives audit, problem 4)
- **DAT-15** (MUST) `Cid.FromBytes` and `TryFromBytes` are public.
  *Source:* 0.8 kept `Cid.FromBytes` internal (`Cid.cs:114`), forcing `byte[]` APIs elsewhere

### Reading DRISL-CBOR: strict mode

Strict mode enforces every DRISL rule. It is the default (`DrislReaderOptions.Mode`).

- **DAT-20** (MUST) Strict reading rejects indefinite-length strings, arrays and maps.
  *Source:* DRISL · 0.8 #134 (commits, MST nodes, CAR headers) · test `CommitBlockTests`
- **DAT-21** (MUST) Strict reading rejects integers, lengths and tag numbers not in their
  shortest encoding.
  *Source:* DRISL
- **DAT-22** (MUST) Strict reading rejects map keys out of canonical order (shorter key first;
  equal-length keys by unsigned byte order of their UTF-8) and duplicate map keys.
  *Source:* DRISL · 0.8 `CommitBlock.cs:82` (Strict reader), `Spaces/SpaceRecordCbor.cs`
- **DAT-23** (MUST) Strict reading rejects every tag except 42. A tag 42 must wrap a byte string
  whose first byte is `0x00` (multibase identity prefix), followed by a blessed binary CID
  (DAT-10) and nothing else.
  *Source:* DRISL, specs/data-model · 0.8 `DagCborLink.cs`
- **DAT-24** (MUST) Strict reading rejects floats of every width, `undefined` and every simple
  value other than `false`, `true` and `null`.
  *Source:* DRISL, specs/data-model
- **DAT-25** (MUST) Strict reading rejects text strings that are not valid UTF-8, and bytes
  after the top-level value.
  *Source:* DRISL · 0.8 `CommitBlock` refuses trailing bytes

### Reading DRISL-CBOR: lenient mode

- **DAT-26** (MUST) Lenient mode accepts non-canonical key order, non-shortest encodings and
  indefinite lengths. It still rejects floats, non-string keys, malformed tag-42 links, nesting
  beyond the depth limit and **duplicate map keys** (DAT-60 lists this as a change). It skips
  unknown tags and reads the wrapped value in their place, without recursion. It replaces invalid
  UTF-8 with U+FFFD.
  *Source:* 0.8 Lax decoding (`DagCborDecoder.cs:27`, `DagCborJson.cs:85-94`) · tests
  `DagCborDecoderTests.Decode_InvalidUtf8Text_IsReplacedAsTheLaxReaderDoes`,
  `Decode_LongChainOfUnknownTags_DecodesTheInnerValue`
- **DAT-27** (MUST) Lenient mode is opt-in per call. No API that hashes, signs, verifies or
  compares content addresses accepts a mode parameter: those always read strictly.
  *Source:* **Decision** [02 — Architecture](../02-architecture.md#1-identifiers-and-data-layer-0)
  ("strict by default because anything hashed or signed must be")
- **DAT-28** (MUST) Which consumers use which mode:

  | Consumer | Mode |
  |---|---|
  | Commit blocks, MST nodes, CAR headers ([repo.md](repo.md)) | Strict |
  | Record blocks under verification: Sync 1.1, record proofs, snapshots, space repositories (STAR) | Strict |
  | Label signature verification input, PLC operations, space commits and records | Strict |
  | Anything written by the SDK, then re-read to compute or compare a CID | Strict |
  | Event-stream frame headers and envelope bodies (`#commit`, `#identity`, …, `#labels`) | Lenient (the signed content inside is verified strictly) |
  | Record bodies decoded for display when the caller has not enabled verification | Lenient, by explicit option |

  *Source:* **Decision** · primitives audit, problem 7 (0.8 applied strictness inconsistently,
  with key-order enforcement written four different ways)

### Limits and failure behaviour while reading

- **DAT-30** (MUST) Nesting depth is limited to `DrislLimits.MaxDepth` (64), counted by the SDK
  itself. Every map and array counts one level, and a tag-42 link counts as the level its JSON
  `{"$link"}` wrapper adds. The SDK MUST NOT rely on `System.Formats.Cbor`'s internal depth
  limit, whose value and behaviour have changed between versions of that library.
  *Source:* 0.8 `DagCborJson.cs:36` · tests `DagCborDecoderTests.Decode_NestingAtTheLimit_Succeeds`,
  `Decode_LinkWrapperCountsTowardsTheLimit` · regression: 0.8's
  `CarReaderTests.FromBytes_DeeplyNestedHeaderValue_IsSkippedWithoutOverflowingTheStack` fails
  on a newer `System.Formats.Cbor` that enforces its own limit of 64 inside `SkipToAncestor`
- **DAT-31** (MUST) `SkipValue` is iterative and counted against the same depth limit, so
  skipping an unknown value (for example an unknown CAR header key) behaves the same as reading
  it. A value nested beyond the limit is rejected, whether read or skipped.
  *Source:* **Decision** (settles the regression in DAT-30 one way, independent of library version)
- **DAT-32** (MUST) Hostile nesting (100,000 levels) fails with `FormatException`, never with a
  stack overflow.
  *Source:* tests `DagCborDecoderTests.Decode_DeeplyNestedMaps_ThrowsFormatException`,
  `FirehoseEventParserTests.Parse_DeeplyNestedBody_ReturnsNullRatherThanOverflowingTheStack`
- **DAT-33** (MUST) A declared length of a string, byte string, array or map is checked against
  the bytes remaining before anything is allocated for it. No allocation is proportional to a
  declared length larger than the input.
  *Source:* principle 2 · regressions `RepoFetcherTests.HostRepoFetcher_HugeDeclaredLength_DoesNotReserveIt`
  (a length over 2 GiB was cast to `int`), indigo SEC-4 ([repo.md](repo.md))
- **DAT-34** (MUST) Every decoding failure surfaces as `FormatException` (or
  `DataLimitExceededException`, a subtype, for exceeded limits). `CborContentException`,
  `InvalidOperationException` and `OverflowException` from `System.Formats.Cbor` never escape
  the SDK.
  *Source:* 0.8 regression `SpaceRepoCarTests.Verify_MalformedIndexBlock_…` (a
  `CborContentException` leaked) · test `DagCborDecoderTests.Decode_MalformedInput_ThrowsFormatException`
- **DAT-35** (MUST) A failed decode leaves no state behind that affects the next decode on the
  same thread or instance.
  *Source:* test `DagCborDecoderTests.Decode_AfterAFailedDecode_ReturnsOnlyItsOwnValue` (0.8
  used a thread-static buffer)

### Writing DRISL-CBOR

- **DAT-40** (MUST) `DrislWriter` output is canonical DRISL whatever order the caller writes map
  entries in. That means definite lengths, shortest encodings, and keys sorted shorter-first,
  then by UTF-8 bytes, comparing supplementary characters correctly.
  *Source:* DRISL · #111 · tests `DagCborEncoderTests.Encode_SortsMapKeysLengthFirst`,
  `Encode_SupplementaryVersusPrivateUseKey_SortsByUtf8Bytes`,
  `Encode_RandomDocuments_ByteIdenticalToCanonicalWriterSorting`
- **DAT-41** (MUST) Writing a duplicate key, a float, or an integer outside `Int64` throws.
  *Source:* test `DagCborEncoderTests.Encode_RepeatedPropertyName_Throws`
- **DAT-42** (MUST) Links are written as tag 42 over `0x00` followed by the 36-byte CID. Bytes
  are written as a CBOR byte string.
  *Source:* specs/data-model
- **DAT-43** (MUST) Encoding a typed object to DRISL uses the SDK's serializer contracts (the
  generated `JsonSerializerContext`s and `AtProtoJson` options). No overload silently uses
  `JsonSerializerOptions.Default`, which in 0.8 produced different bytes, and so different CIDs,
  from what the SDK writes elsewhere.
  *Source:* primitives audit, problem 8 (`DagCborEncoder.cs:39-41`)
- **DAT-44** (MUST) Encoding is deterministic. The CID of a re-encoded real network record
  equals the CID the network published for it.
  *Source:* test `DagCborEncoderTests.ComputeCid_RealNetworkRecord_MatchesTheCidTheNetworkPublished`

### JSON forms and the CBOR↔JSON bridge

- **DAT-45** (MUST) The bridge maps:
  - CBOR maps ↔ JSON objects, keeping key order as encoded;
  - arrays ↔ arrays;
  - integers ↔ JSON integers;
  - text ↔ strings;
  - `true`/`false`/`null` ↔ the same;
  - byte strings ↔ `{"$bytes": "<base64>"}`;
  - links ↔ `{"$link": "<cid>"}`.

  JSON → CBOR is the exact inverse, with canonical key order. Each interop data-model fixture
  round-trips byte for byte.
  *Source:* specs/data-model · tests `DagCborInteropTests.Encode_DataModelFixture_MatchesReferenceBytesAndCid`,
  `Decode_DataModelFixture_RoundTripsToTheSameBytes`, `Decode_DataModelFixture_RendersLinksAndBytesInTheJsonDataModel`
- **DAT-46** (MUST) `$bytes` is written in the standard base64 alphabet without padding.
  Reading accepts it with or without padding.
  *Source:* specs/data-model · #115 · tests `DagCborDecoderTests.Decode_ByteString_WritesUnpaddedBase64`,
  `Label_Signature_SerializesAsUnpaddedBytesObject`
- **DAT-47** (MUST) A JSON object is a link (or bytes) object only when `$link` (or `$bytes`) is
  its sole property. Otherwise it is an ordinary map.
  *Source:* 0.8 `DagCborEncoder` behaviour (primitives audit §3)
- **DAT-48** (MUST) JSON-side depth for data-model values is limited to the same 64 levels as
  DAT-30, so any value one encoding accepts, the other also accepts.
  *Source:* 0.8 `DagCborDecoder.cs:37` (`MaxDepth = DagCborJson.MaxDepth`)
- **DAT-49** (MUST) Hot protocol envelopes (frame headers, `#commit`, `#sync`, `#identity`,
  `#account`, `#info`, label frames, commits, MST nodes) are bound directly from `DrislReader` by
  hand-written binders, with no JSON step. Record bodies go CBOR → `DataModelValue` → generated
  contract.
  *Source:* **Decision** D-10 · streaming audit, problem 6 (0.8 base64-encodes up to 2 MB of
  blocks per commit)

### Links and blobs

- **DAT-50** (MUST) `CidLink` is the JSON/CBOR link form of a `Cid` and holds exactly one. It is
  used for Lexicon `cid-link` fields. Lexicon `format: cid` string fields use `Cid` directly.
  *Source:* specs/data-model · 0.8 `Models/CommonModels.cs:34`
- **DAT-51** (MUST) A blob is the map `{"$type": "blob", "ref": <link>, "mimeType": <non-empty
  string>, "size": <integer ≥ 0>}`. The `ref` codec MUST be `raw`. `BlobRef` reads and writes
  exactly this form and rejects a blob that violates it.
  *Source:* specs/data-model#blob-type · new (0.8 did not check the codec, and its `BlobRef`
  had a settable `Type` and defaulted `MimeType` to empty)
- **DAT-52** (SHOULD) The legacy blob form `{"cid": "<cid string>", "mimeType": "<string>"}` is
  read without error into a `BlobRef` with `IsLegacy == true` and `Size == null`. A legacy
  `BlobRef` is written back in the legacy form, so read-modify-write keeps the record intact. No
  SDK API ever creates a legacy blob.
  *Source:* specs/data-model#blob-type ("should not throw errors … should never write them") ·
  new in the rewrite (0.8 has no legacy support)

### CAR v1

- **DAT-55** (MUST) The header is an unsigned varint length (non-zero, at most what remains, at
  most `MaxHeaderBytes`) followed by a strict DRISL map with `version == 1` and `roots`, an
  array of links. Unknown header keys are skipped under DAT-31. Bytes after the header map
  within its declared length are rejected. Any version other than 1 is rejected.
  *Source:* CARv1 · specs/repository · 0.8 `CarReader.cs:59-66,241-290` · tests
  `CarReaderTests.FromBytes_MalformedHeader_ThrowsFormatException`,
  `FromBytes_UnsupportedVersion_ThrowsFormatException`, `FromBytes_TruncatedDeeplyNestedHeader_ThrowsFormatException`
- **DAT-56** (MUST) Varints are unsigned LEB128, at most 9 bytes, and minimally encoded. A
  truncated, over-long or non-minimal varint is rejected.
  *Source:* unsigned-varint · 0.8 `CarReader.cs:324-345` · tests
  `CarReaderTests.ReadUvarint_Truncated_ThrowsFormatException`,
  `ReadUvarint_LongerThanNineBytes_ThrowsFormatException`, `ReadUvarint_NotMinimallyEncoded_ThrowsFormatException`
- **DAT-57** (MUST) A block is a varint length (non-zero, at most the bytes remaining, at most
  `MaxBlockBytes`), a blessed binary CID that lies wholly inside the block, then the block data.
  *Source:* 0.8 `CarReader.cs:69-84` · tests `FromBytes_CidDigestPastTheEndOfItsBlock_ThrowsFormatException`,
  `FromBytes_BlockLongerThanTheFile_ThrowsFormatException`, `FromBytes_HugeLengthPrefixes_ThrowFormatException`,
  `FromBytes_UnsupportedCidVersion_ThrowsFormatException`
- **DAT-58** (MUST) With `VerifyBlockCids`, each block's CID is recomputed from its data. A
  mismatch or an unknown codec fails the read (fail closed).
  *Source:* 0.8 `CarReader.VerifyAllBlockCids` · test `CarReaderTests.VerifyBlockCid_CidV0Block_ReportsUnknownCodec`
- **DAT-59** (MUST) `MaxTotalBytes`, `MaxBlocks`, `MaxBlockBytes` and `MaxHeaderBytes` are
  enforced while reading, before buffering past them. Exceeding one throws
  `DataLimitExceededException` naming the limit.
  *Source:* principle 2 · primitives audit, problem 10 (0.8 `FromStreamAsync` had no size
  limit, `CarReader.cs:161`, and left bounds to each caller)
- **DAT-60** (MUST) `ReadBlocksAsync` streams. It holds at most one block plus a bounded read
  buffer in memory and never copies the whole input. `Read` over a buffer slices the input
  rather than copying each block.
  *Source:* primitives audit, problem 10 (0.8 copied every block, `CarReader.cs:87`)
- **DAT-61** (MUST) When a CID occurs more than once, lookups return the first occurrence.
  `Blocks` keeps every block in file order.
  *Source:* test `CarReaderTests.FindBlock_DuplicateCid_ReturnsFirstOccurrence`
- **DAT-62** (MUST) `CarWriter` writes the header as canonical DRISL `{roots, version: 1}` and
  each block as `varint(len(cid) + len(data))`, the binary CID and the data. Its output reads
  back identically, passes `VerifyBlockCids` when the blocks carry computed CIDs, and may have no
  roots or no blocks.
  *Source:* 0.8 `CarWriter.cs:108-141` · tests `CarWriterTests.Write_SingleRootAndBlocks_RoundTripsThroughCarReader`,
  `Write_BlocksCarryComputedCids_VerifyAllBlockCidsPasses`, `Write_NoRoots_ProducesRootlessCar`,
  `Write_NoBlocks_ProducesHeaderOnlyCar`

### STAR (space repositories)

Space repositories have no MST; their state is a set of records committed to by a set hash
([spaces.md](spaces.md)). Upstream ships their serialization as STAR rather than CAR (forum
"Post-alpha plans", 2026-10-08: "expect to ship with STAR files not CAR files").

- **DAT-72** (MUST) Space repositories are read and written as STAR; public repositories stay CAR
  (DAT-55…DAT-62). The format is chosen by the endpoint and its declared response encoding, never
  by sniffing bytes.
  *Source:* forum "Post-alpha plans" (repo serialization) · **Decision** (no sniffing)
- **DAT-73** (MUST) `ReadEntriesAsync` streams in file order, holding at most one entry plus a
  bounded read buffer, and enforces `MaxTotalBytes`, `MaxEntries`, `MaxEntryBytes` and
  `MaxHeaderBytes` before buffering past them (`DataLimitExceededException` naming the limit).
  *Source:* principle 2 · STAR design goal (streaming, constant memory)
- **DAT-74** (MUST) STAR is deterministic: entries are strictly increasing by path (duplicates and
  out-of-order entries are rejected), CIDs are recomputed from the record bytes (a CID on the wire,
  where the variant carries one, must match; the SDK never trusts it), and trailing bytes after
  the last entry are rejected.
  *Source:* STAR design ("rejects extra data that CAR files tolerate"; implicit, recomputed CIDs)
- **DAT-75** (MUST) Every record is read with the strict DRISL reader (DAT-28) and must be a map.
  The header is returned as raw strict DRISL for its consumer (the space commit, spaces.md) to
  interpret.
  *Source:* 0.8 `Spaces/SpaceRecordCbor.cs`, `SpaceRepoCar.cs` (strict record rules carried over)
- **DAT-76** (MUST) `StarWriter` output reads back identically, with entries in path order and
  canonical DRISL records, and two writers given the same header and records produce identical
  bytes.
  *Source:* **Decision** (determinism, DAT-74)
- **DAT-77** (MUST) Paths are validated as space record paths (`{author}/{collection}/{rkey}` or
  the variant's form, per spaces.md) before a record is handed to the caller.
  *Source:* **Decision** (mirrors DAT-61's role for CAR consumers)
- **DAT-78** (MUST) The STAR variant, framing and header contents implemented are the ones the
  pinned reference implementation produces ([spaces.md](spaces.md)); vectors generated from it
  pin the behaviour. A change in the GA specification is absorbed here and in its vectors.
  *Source:* forum "Post-alpha plans" · "Space commitment changes" (2026-10-12/13: the commit and
  its MAC move, which affects what the header carries)
- **DAT-79** (SHOULD) STAR reading shares the varint, CID-computation and limit machinery of CAR
  reading; there is no second varint or CID implementation.
  *Source:* G1 (one home per mechanism)

### Signed objects: encoding side

- **DAT-65** (MUST) The bytes a signature covers are the canonical DRISL encoding of the map
  without its signature entry (default key `sig`).
  *Source:* [specs/repository](https://atproto.com/specs/repository#commit-objects),
  [specs/label](https://atproto.com/specs/label#signature-and-validation), PLC
- **DAT-66** (MUST) When verifying received bytes, `SignedObject.GetUnsignedBytes` derives them from the
  received bytes: it removes the signature key/value pair and decrements the map header, without
  re-encoding the other entries. The received map must itself pass strict reading (DAT-20…25).
  *Source:* 0.8 `CommitBlock.cs:70,155` (sig splice) · primitives audit "keep nearly as-is"
- **DAT-67** (MUST) This is the one implementation of "encode without `sig`" in the SDK. Repo
  commits, labels, PLC operations and space commits all use it. In 0.8 it was written five times.
  *Source:* **Decision** [02 — Architecture](../02-architecture.md#1-identifiers-and-data-layer-0) ·
  primitives audit, problem 15

### Multiformats codecs

- **DAT-70** (MUST) `Base32` (RFC 4648, lower-case alphabet `a–z2–7`, no padding),
  `Base58Btc` and `UnsignedVarint` each exist once, are public, and are used everywhere they
  are needed (CID text, PLC DID derivation in the `ATProtoNet.Identity.Plc` namespace, multikey encoding, CAR).
  The TID base32-sortable alphabet is a separate codec inside `Tid` (SYN-27).
  *Source:* primitives audit, problem 6 (0.8 scattered these across five files) · the
  extension packages (`ATProtoNet.AspNetCore`, `ATProtoNet.EntityFrameworkCore`) get no
  `InternalsVisibleTo`, so helpers they need are public
- **DAT-71** (MUST) Base32 decoding rejects non-alphabet characters, upper case, padding and
  non-zero spare bits. Base58 decoding rejects non-alphabet characters, preserves leading zero
  bytes, and reports a value longer than the destination as `FormatException`.
  *Source:* tests `AtProtoCryptoTests.Base58Decode_RejectsInvalidCharacters`,
  `Base58_LeadingZeros_Preserved`, `Base58Decode_ValueLongerThanDestination_ThrowsFormatException`

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `DrislLimits.MaxDepth` | 64 | DAT-30, DAT-31, DAT-48 |
| `CarLimits.MaxHeaderBytes` | 64 KiB | DAT-55 (new; 0.8 bounded the header only by the input) |
| `CarLimits.MaxVarintBytes` | 9 | DAT-56 |
| `CarLimits.DefaultMaxBlockBytes` | 2 MiB | DAT-57 (records are at most 1 MB under the sync spec; MST nodes are small) |
| `CarLimits.DefaultMaxTotalBytes` | 256 MiB | DAT-59 (matches 0.8's resync and Spaces repository bounds) |
| `CarLimits.DefaultMaxBlocks` | 4,194,304 | DAT-59 (raise together with `MaxTotalBytes` for very large repositories) |
| `Cid.BinaryLength` | 36 | DAT-10 |
| `StarLimits.MaxHeaderBytes` | 64 KiB | DAT-73 |
| `StarLimits.DefaultMaxEntryBytes` | 1 MiB | DAT-73 (records are at most 1 MB) |
| `StarLimits.DefaultMaxTotalBytes` | 256 MiB | DAT-73 (matches 0.8's `SpaceSyncer.MaxRepoSize`) |
| `StarLimits.DefaultMaxEntries` | 1,048,576 | DAT-73 |

## Errors

| Failure | Result |
|---|---|
| Malformed or non-data-model CBOR, DRISL rule violated in strict mode | `FormatException` |
| A configured or built-in limit exceeded | `DataLimitExceededException : FormatException` (names the limit) |
| Not a blessed binary CID | `FormatException` (CIDv0 named as such) |
| CAR block CID mismatch with `VerifyBlockCids` | `FormatException` |
| STAR entry out of order, duplicated, CID mismatch, trailing bytes, non-map record | `FormatException` |
| Writing a float, duplicate key or out-of-range integer | `ArgumentException` |
| Invalid blob in JSON | `JsonException` with path |

## Conformance and tests

**Vectors** (conformance areas *Data model* and *CID and CAR*):
- `data-model-fixtures.json` from atproto-interop-tests: today pasted into `DagCborInteropTests`,
  to be extracted to a file.
- **New:** the interop-tests data-model valid/invalid suites, which 0.8 does not vendor.
- The CAR cases of `CarReaderTests`: malformed headers, varints, CIDv0, huge lengths, depth.
- The hostile-input corpus of [04 — Testing](../04-testing.md#regression-corpus): deep nesting,
  2 GiB declared lengths, a CAR header nested past the limit.
- **New:** STAR vectors generated from the pinned reference implementation (valid repositories,
  out-of-order and duplicate entries, trailing bytes, oversized entries), plus the strict-record
  cases of 0.8's `SpaceRepoCarTests` re-expressed against STAR.

**0.8 test classes whose scenarios are ported:** `DagCborEncoderTests`, `DagCborDecoderTests`,
`DagCborInteropTests`, `DagCborRoundtripTests`, `CidComputationTests`, `CidTests` (binary
scenarios), `CarReaderTests`, `CarWriterTests`, `CommitBlockTests` (the splice scenarios),
`LexBase64Tests`.

## Changes from 0.8

| Change | Reason |
|---|---|
| Strict DRISL is the default; lenient is opt-in and never used for hashed or signed content. | 0.8's general decoder was lax, and strictness was re-implemented per consumer. |
| Lenient mode rejects duplicate map keys. | Duplicates make the JSON bridge ambiguous; no conforming producer emits them. |
| A CAR header nested beyond 64 levels is rejected by the SDK's own counter. | 0.8's behaviour depended on the `System.Formats.Cbor` version (its test now fails). |
| A zero-length CAR block is an error (0.8 silently stopped parsing, `CarReader.cs:77`). | A block must contain at least a CID; silent truncation hides corruption. |
| Bounded, streaming CAR reader; no unbounded `FromStreamAsync`. | Principle 2. |
| No `DagCborEncoder.Encode(object)` with default serializer options. | It produced CIDs that disagreed with the SDK's own encoding. |
| `CarWriter` takes `Cid`-keyed blocks only (no base32-string dictionary). | One CID representation (DAT-14). |
| Blob `ref` must be a raw-codec link; legacy blobs are read. | Data-model spec. |
| One `SignedObject` splice implementation. | Five copies in 0.8. |
| Space repositories are STAR, read by a streaming, bounded reader in `ATProtoNet.Data.Star`. | Upstream ships STAR for space repos; 0.8 used a two-root CAR read whole (`SpaceRepoCar`). |

## Open points

Defaults accepted (maintainer, 2026-10-17). The implementing phase applies them without further
sign-off, settles any point left to it, follows upstream where a point waits on upstream, and
records the outcome here in the same PR.

1. **Bare base64 for `bytes` fields.** 0.8's `LexBytesJsonConverter` also accepts a bare base64
   string where a `{"$bytes"}` object is expected. *Default:* drop it (wire fidelity,
   D-20), unless the conformance suite finds a real producer that relies on it.
2. **Strict reading of record bodies when verification is off.** *Default:* lenient
   only by explicit stream option (DAT-28). Strict is the default even for display, because
   upstream PDSs validate records on write and canonical encoding is required by the repository
   spec.
3. **`DataModelValue` key order.** Kept as encoded for display. Writing always re-sorts. *Default:*
   keep. Revisit if users need ordered round trips of non-canonical JSON (they should
   not, since canonical order is the protocol's).
4. **STAR variant for spaces.** STAR has a flat key-record variant (STAR-lite) and MST variants;
   upstream has not published which framing and header the GA space repositories use, nor
   whether a multi-repository bundle (for account migration) follows. *Default:*
   implement the flat variant as the pinned reference implementation emits it (DAT-78); add a
   multi-repository reader only when the GA specification defines one.
5. **Floating point numbers** (added 2026-10-18). Upstream still has
   [no float](https://atproto.com/specs/data-model#why-no-floats): the IETF ATP working group heard a
   binary64 proposal at IETF 126 but decided nothing
   ([minutes](http://ietfminutes.org/minutes/ietf126/atp.html)). *Default:* (1) numbers carried in
   Lexicon strings use `DecimalString` with opt-in codegen per field
   ([SYN-82–87](syntax.md#numbers-carried-in-strings), LEX-92), so an untouched record keeps its
   CID; (2) scaled integers get a small static helper and a docs page, no codegen (Lexicon cannot
   express the scale); (3) the data model stays strict (DAT-1, DAT-24, DAT-41), with number handling
   behind one internal seam so a float kind can be added later. Options and reasons in
   [D-26](../06-decisions.md#d-26-floating-point-numbers-string-encoded-decimals-strict-data-model).
   Revisit when the ATP working group publishes a float mapping draft.
