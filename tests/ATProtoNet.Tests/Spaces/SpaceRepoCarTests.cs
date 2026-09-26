using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Repo;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

public class SpaceRepoCarTests
{
    private static readonly SpaceUri _space =
        SpaceUri.Parse("at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/space/com.atmoboards.forum/default");

    private static readonly Did Author = Did.Parse("did:plc:z72i7hdynmk6r22z27h6tvur");
    private static readonly Tid Rev = Tid.Parse("3l6oveex3ii2l");

    private static SpaceRepoRecord Record(string collection, string rkey, string text)
    {
        var value = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["$type"] = collection,
            ["text"] = text,
        });

        return SpaceRepoRecord.Create(Nsid.Parse(collection), RecordKey.Parse(rkey), value);
    }

    private static (byte[] Car, AtProtoKey Key, List<SpaceRepoRecord> Records) BuildRepo(
        params (string Collection, string Rkey, string Text)[] entries)
    {
        var key = AtProtoCrypto.GenerateP256Key();
        var records = entries.Select(e => Record(e.Collection, e.Rkey, e.Text)).ToList();

        var commit = SpaceRepoCommit
            .FromRecords(records.Select(r => (r.Collection, r.Rkey, r.Cid)))
            .Sign(new SpaceCommitContext(_space, Author, Rev), key);

        return (SpaceRepoCar.Serialize(commit, records), key, records);
    }

    [Fact]
    public void SerializeThenVerify_RoundTripsTheWholeRepo()
    {
        var (car, key, records) = BuildRepo(
            ("com.atmoboards.thread", "aaa", "first"),
            ("com.atmoboards.reply", "bbb", "second"),
            ("com.atmoboards.reply", "ccc", "third"));
        using var _ = key;

        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey());

        Assert.Equal(Rev, verified.Commit.Rev);
        Assert.Equal(records.Count, verified.Index.Count);
        Assert.Equal(records.Count, verified.Records.Count);
        Assert.Equal(
            records.Select(r => r.Path).OrderBy(p => p, StringComparer.Ordinal),
            verified.Records.Select(r => r.Path).OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void Serialize_DeclaresTheCommitAndIndexAsItsTwoRoots()
    {
        var (car, key, _) = BuildRepo(("com.atmoboards.thread", "aaa", "first"));
        using var _k = key;

        var reader = CarReader.FromBytes(car);

        Assert.Equal(2, reader.Roots.Count);
        Assert.Equal(reader.Roots[0], reader.Blocks[0].Cid);
        Assert.Equal(reader.Roots[1], reader.Blocks[1].Cid);
    }

    [Fact]
    public void Serialize_OrdersRecordBlocksToMatchTheIndex()
    {
        // A consumer walks the index and the blocks in lockstep, so the block order has to be
        // the canonical map-key order the index was encoded in — length-first, then bytewise.
        var (car, key, _) = BuildRepo(
            ("com.example.n", "z", "short path"),
            ("com.example.note", "aaa", "long path"),
            ("com.example.n", "a", "shortest path"));
        using var _k = key;

        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey());

        Assert.Equal(
            ["com.example.n/a", "com.example.n/z", "com.example.note/aaa"],
            verified.Records.Select(r => r.Path));
    }

    [Fact]
    public void Verify_IndexAuthenticatesAgainstTheCommitWithoutReadingRecords()
    {
        var (car, key, records) = BuildRepo(("com.atmoboards.thread", "aaa", "first"));
        using var _k = key;

        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey());

        Assert.True(SpaceRepoCommit.FromIndex(verified.Index).Matches(verified.Commit));
        Assert.Equal(records[0].Cid, verified.Index[0].Value);
    }

    [Fact]
    public void Verify_IndexOnlyCar_SucceedsWhenValuesAreNotExpected()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var records = new[] { Record("com.atmoboards.thread", "aaa", "first") };
        var commit = SpaceRepoCommit
            .FromRecords(records.Select(r => (r.Collection, r.Rkey, r.Cid)))
            .Sign(new SpaceCommitContext(_space, Author, Rev), key);

        var car = SpaceRepoCar.Serialize(commit, records, excludeValues: true);

        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey(), expectValues: false);

        Assert.Empty(verified.Records);
        // The index is still fully authenticated — that is what makes excludeValues useful for
        // diffing against a local copy.
        Assert.Single(verified.Index);
        Assert.True(SpaceRepoCommit.FromIndex(verified.Index).Matches(verified.Commit));
    }

    [Fact]
    public void Verify_IndexOnlyCar_FailsWhenValuesAreExpected()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var records = new[] { Record("com.atmoboards.thread", "aaa", "first") };
        var commit = SpaceRepoCommit
            .FromRecords(records.Select(r => (r.Collection, r.Rkey, r.Cid)))
            .Sign(new SpaceCommitContext(_space, Author, Rev), key);

        var car = SpaceRepoCar.Serialize(commit, records, excludeValues: true);

        Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey()));
    }

    [Fact]
    public void Verify_EmptyRepo_Succeeds()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var commit = new SpaceRepoCommit().Sign(new SpaceCommitContext(_space, Author, Rev), key);

        var car = SpaceRepoCar.Serialize(commit, []);
        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey());

        Assert.Empty(verified.Index);
        Assert.Empty(verified.Records);
    }

    [Fact]
    public void Verify_WithTheWrongSigningKey_Throws()
    {
        var (car, key, _) = BuildRepo(("com.atmoboards.thread", "aaa", "first"));
        using var _k = key;
        using var other = AtProtoCrypto.GenerateP256Key();

        var ex = Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, other.ToDidKey()));

        Assert.Contains("commit failed verification", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_ForTheWrongSpace_Throws()
    {
        // The space is bound into the commit context, so a repo served for one space cannot be
        // passed off as the same account's repo in another.
        var (car, key, _) = BuildRepo(("com.atmoboards.thread", "aaa", "first"));
        using var _k = key;
        var otherSpace = SpaceUri.Create(_space.Authority, _space.SpaceType, RecordKey.Parse("other"));

        Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, otherSpace, Author, key.ToDidKey()));
    }

    [Fact]
    public void Verify_ForTheWrongAuthor_Throws()
    {
        var (car, key, _) = BuildRepo(("com.atmoboards.thread", "aaa", "first"));
        using var _k = key;

        Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz"), key.ToDidKey()));
    }

    [Fact]
    public void Verify_IndexThatDoesNotMatchTheCommit_Throws()
    {
        // A commit signed over one set of records, served with an index describing another.
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var claimed = new[] { Record("com.atmoboards.thread", "aaa", "first") };
        var actual = new[] { Record("com.atmoboards.thread", "aaa", "tampered") };

        var commit = SpaceRepoCommit
            .FromRecords(claimed.Select(r => (r.Collection, r.Rkey, r.Cid)))
            .Sign(new SpaceCommitContext(_space, Author, Rev), key);

        var car = SpaceRepoCar.Serialize(commit, actual);

        var ex = Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey()));

        Assert.Contains("does not match the commit hash", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_CarWithASingleRoot_Throws()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var commit = new SpaceRepoCommit().Sign(new SpaceCommitContext(_space, Author, Rev), key);
        var commitBytes = commit.ToDagCbor();
        var commitCid = CidComputation.ComputeBinaryForDagCbor(commitBytes);

        var car = CarWriter.Write(commitCid, new[] { new CarBlock(commitCid, commitBytes) });

        var ex = Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey()));

        Assert.Contains("2 CAR roots", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_Garbage_ThrowsVerificationExceptionRatherThanLeakingTheDecoder()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(new byte[] { 0xff, 0x00, 0x12 }, _space, Author, key.ToDidKey()));
    }

    [Fact]
    public void Serialize_LastWriteForAPathWins()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var stale = Record("com.atmoboards.thread", "aaa", "stale");
        var current = Record("com.atmoboards.thread", "aaa", "current");

        var commit = SpaceRepoCommit
            .FromRecords([(current.Collection, current.Rkey, current.Cid)])
            .Sign(new SpaceCommitContext(_space, Author, Rev), key);

        var car = SpaceRepoCar.Serialize(commit, [stale, current]);
        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey());

        Assert.Single(verified.Records);
        Assert.Equal(current.Cid, verified.Records[0].Cid);
    }

    [Fact]
    public void Serialize_IndexBlock_IsByteIdenticalToTheEncodedJsonIndex()
    {
        // The index is written straight to CBOR; it must match what encoding the equivalent
        // {path: {"$link": cid}} object through DagCborEncoder produces, byte for byte. Paths
        // are ASCII (NSIDs and record keys both are), so the length-first order is the one to
        // pin here; DagCborInteropTests covers the encoder's non-ASCII key order.
        var (car, key, records) = BuildRepo(
            ("com.example.n", "z", "short path"),
            ("com.example.note", "aaa", "long path"),
            ("com.example.n", "a", "shortest path"),
            ("com.example.n", "a:b", "colon"),
            ("com.example.n", "A~", "upper case and tilde"));
        using var _k = key;

        var index = new System.Text.Json.Nodes.JsonObject();
        foreach (var record in records)
            index[record.Path] = new System.Text.Json.Nodes.JsonObject { ["$link"] = record.Cid.Value };

        var reader = CarReader.FromBytes(car);

        Assert.Equal(DagCborEncoder.Encode(JsonSerializer.SerializeToElement(index)), reader.Blocks[1].Data);
        Assert.Equal(
            reader.Blocks.Skip(2).Select(b => CidComputation.EncodeCidToString(b.Cid)),
            SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey()).Index.Select(e => e.Value.Value));
    }

    [Theory]
    [InlineData("not-an-nsid/aaa")]
    [InlineData("com.example.n/not a record key")]
    [InlineData("com.example.n/aaa/bbb")] // two separators: the reference refuses it, LastIndexOf would not
    [InlineData("/aaa")]
    [InlineData("com.example.n")]
    public void Verify_IndexPathThatIsNotACollectionAndRecordKey_Throws(string path)
    {
        // The commit vouches for the index as a set of strings, so a path that does not parse
        // passes the digest comparison and has to be refused when the record is decoded.
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var (recordBytes, recordCid) = DagCborEncoder.EncodeWithCid(
            JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["text"] = "x" }));

        var commit = SpaceRepoCommit
            .FromIndex([new KeyValuePair<string, Cid>(path, recordCid)])
            .Sign(new SpaceCommitContext(_space, Author, Rev), key);
        var commitBytes = commit.ToDagCbor();
        var commitCid = CidComputation.ComputeBinaryForDagCbor(commitBytes);

        var index = new System.Text.Json.Nodes.JsonObject
        {
            [path] = new System.Text.Json.Nodes.JsonObject { ["$link"] = recordCid.Value },
        };
        var indexBytes = DagCborEncoder.Encode(JsonSerializer.SerializeToElement(index));
        var indexCid = CidComputation.ComputeBinaryForDagCbor(indexBytes);

        var car = CarWriter.Write(
            [commitCid, indexCid],
            [
                new CarBlock(commitCid, commitBytes),
                new CarBlock(indexCid, indexBytes),
                new CarBlock(recordCid.ToBytes(), recordBytes),
            ]);

        var ex = Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey()));

        Assert.Contains("Invalid record path", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_IndexOnlyCarWithAnInvalidPath_Throws()
    {
        // An index-only CAR is diffed against a local copy by path, so its paths are checked even
        // though no record block comes with them.
        using var key = AtProtoCrypto.GenerateP256Key();
        var (_, recordCid) = RecordBlock();
        var car = CraftedCar(key, "com.example.n/aaa/bbb", recordCid, record: null);

        var ex = Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey(), expectValues: false));

        Assert.Contains("Invalid record path", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new byte[] { 0x65, 0x68, 0x65, 0x6c, 0x6c, 0x6f })] // "hello"
    [InlineData(new byte[] { 0x82, 0x01, 0x02 })]                    // [1, 2]
    [InlineData(new byte[] { 0xa1, 0x61, 0x61 })]                    // a map missing its value
    [InlineData(new byte[] { 0xbf, 0xff })]                          // indefinite-length map
    [InlineData(new byte[] { 0xa1, 0x61, 0x61, 0x9f, 0xff })]        // {"a": an indefinite-length array}
    [InlineData(new byte[] { 0xa1, 0x61, 0x61, 0x7f, 0xff })]        // {"a": an indefinite-length string}
    [InlineData(new byte[] { 0xa1, 0x61, 0x61, 0xc1, 0x00 })]        // {"a": tag 1, a timestamp}
    [InlineData(new byte[] { 0xa1, 0x61, 0x61, 0xd8, 0x2a, 0x43, 0x01, 0x71, 0x12 })] // tag 42 without the 0x00 prefix
    [InlineData(new byte[] { 0xa1, 0x61, 0x61, 0xd8, 0x2a, 0x61, 0x78 })] // tag 42 over a text string
    [InlineData(new byte[] { 0xa1, 0x61, 0x61, 0x18, 0x01 })]        // {"a": 1} with 1 in a longer form than it needs
    [InlineData(new byte[] { 0xa2, 0x62, 0x61, 0x61, 0x01, 0x61, 0x62, 0x02 })] // keys out of length-first order
    [InlineData(new byte[] { 0xa2, 0x61, 0x61, 0x01, 0x61, 0x61, 0x02 })] // a repeated key
    [InlineData(new byte[] { 0xa1, 0x01, 0x01 })]                    // a non-text key
    [InlineData(new byte[] { 0xa1, 0x61, 0x61, 0xfb, 0x3f, 0xf0, 0, 0, 0, 0, 0, 0 })] // {"a": 1.0}
    [InlineData(new byte[] { 0xa1, 0x61, 0x61, 0xf7 })]              // {"a": undefined}
    [InlineData(new byte[] { 0xa1, 0x61, 0x61, 0x1b, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff })] // past int64
    [InlineData(new byte[] { 0xa1, 0x61, 0x61, 0x61, 0xff })]        // invalid UTF-8
    public void Verify_RecordBlockThatIsNotADagCborMap_Throws(byte[] recordBytes)
    {
        // The block hashes to the CID the index names, so only decoding it tells it is no record.
        using var key = AtProtoCrypto.GenerateP256Key();
        var recordCid = CidComputation.ComputeForDagCbor(recordBytes);
        var car = CraftedCar(key, "com.example.n/aaa", recordCid, recordBytes);

        var ex = Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey()));

        Assert.Contains("not a DAG-CBOR map", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_IndexLinkingARawCid_Throws()
    {
        // A raw CID names a blob; a record in the index is always DAG-CBOR.
        using var key = AtProtoCrypto.GenerateP256Key();
        var (recordBytes, _) = RecordBlock();
        var rawCid = CidComputation.ComputeForRaw(recordBytes);
        var car = CraftedCar(key, "com.example.n/aaa", rawCid, recordBytes);

        var ex = Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey()));

        Assert.Contains("non-DAG-CBOR", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_RecordBlockThatIsAMap_IsAccepted()
    {
        // The positive control for the crafted-CAR tests above.
        using var key = AtProtoCrypto.GenerateP256Key();
        var (recordBytes, recordCid) = RecordBlock();
        var car = CraftedCar(key, "com.example.n/aaa", recordCid, recordBytes);

        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey());

        var record = Assert.Single(verified.Records);
        Assert.Equal("com.example.n/aaa", record.Path);
    }

    [Fact]
    public void Verify_RecordUsingEveryAllowedKind_IsAccepted()
    {
        // The strict check must not refuse what a conforming writer produces: links, bytes,
        // negative and multi-byte integers, nesting, booleans and null.
        using var key = AtProtoCrypto.GenerateP256Key();
        var (_, linked) = RecordBlock();
        var value = System.Text.Json.Nodes.JsonNode.Parse(
            """
            {"$type":"com.example.n","text":"x","n":-5,"big":1000000,"flag":true,"none":null,
             "list":[1,"a",{"b":false}],"ref":{"$link":"LINK"},"bytes":{"$bytes":"AQI"}}
            """.Replace("LINK", linked.Value, StringComparison.Ordinal))!;
        var (recordBytes, recordCid) = DagCborEncoder.EncodeWithCid(JsonSerializer.SerializeToElement(value));
        var car = CraftedCar(key, "com.example.n/aaa", recordCid, recordBytes);

        var verified = SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey());

        Assert.Single(verified.Records);
    }

    private static (byte[] Bytes, Cid Cid) RecordBlock() =>
        DagCborEncoder.EncodeWithCid(
            JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["text"] = "x" }));

    /// <summary>
    /// A CAR whose commit vouches for a one-entry index built by hand, so the index can hold what
    /// <see cref="SpaceRepoCar.Serialize"/> never would.
    /// </summary>
    private static byte[] CraftedCar(AtProtoKey key, string path, Cid recordCid, byte[]? record)
    {
        var commit = SpaceRepoCommit
            .FromIndex([new KeyValuePair<string, Cid>(path, recordCid)])
            .Sign(new SpaceCommitContext(_space, Author, Rev), key);
        var commitBytes = commit.ToDagCbor();
        var commitCid = CidComputation.ComputeBinaryForDagCbor(commitBytes);

        var index = new System.Text.Json.Nodes.JsonObject
        {
            [path] = new System.Text.Json.Nodes.JsonObject { ["$link"] = recordCid.Value },
        };
        var indexBytes = DagCborEncoder.Encode(JsonSerializer.SerializeToElement(index));
        var indexCid = CidComputation.ComputeBinaryForDagCbor(indexBytes);

        List<CarBlock> blocks = [new CarBlock(commitCid, commitBytes), new CarBlock(indexCid, indexBytes)];
        if (record is not null)
            blocks.Add(new CarBlock(recordCid.ToBytes(), record));

        return CarWriter.Write([commitCid, indexCid], blocks);
    }

    [Fact]
    public void Verify_MalformedIndexBlock_ThrowsVerificationExceptionRatherThanLeakingTheDecoder()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        using var _k = key;
        var commit = new SpaceRepoCommit().Sign(new SpaceCommitContext(_space, Author, Rev), key);
        var commitBytes = commit.ToDagCbor();
        var commitCid = CidComputation.ComputeBinaryForDagCbor(commitBytes);

        // A truncated array: CborReader reports it as CborContentException, which used to escape.
        byte[] index = [0x82, 0x01];
        var indexCid = CidComputation.ComputeBinaryForDagCbor(index);

        var car = CarWriter.Write([commitCid, indexCid], [new CarBlock(commitCid, commitBytes), new CarBlock(indexCid, index)]);

        Assert.Throws<SpaceRepoVerificationException>(
            () => SpaceRepoCar.Verify(car, _space, Author, key.ToDidKey()));
    }
}
