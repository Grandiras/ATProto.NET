using System.Formats.Cbor;
using System.Text.Json;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class FirehoseEventParserTests
{
    /// <summary>
    /// Helper to encode a header+body CBOR frame like the AT Protocol firehose produces.
    /// </summary>
    private static byte[] EncodeCborFrame(int op, string type, Dictionary<string, object?> body)
    {
        using var ms = new MemoryStream();

        // Write header map: { "op": op, "t": type }
        var headerWriter = new CborWriter(CborConformanceMode.Lax);
        headerWriter.WriteStartMap(2);
        headerWriter.WriteTextString("op");
        headerWriter.WriteInt32(op);
        headerWriter.WriteTextString("t");
        headerWriter.WriteTextString(type);
        headerWriter.WriteEndMap();
        ms.Write(headerWriter.Encode());

        // Write body map
        var bodyWriter = new CborWriter(CborConformanceMode.Lax);
        WriteCborMap(bodyWriter, body);
        ms.Write(bodyWriter.Encode());

        return ms.ToArray();
    }

    private static void WriteCborMap(CborWriter writer, Dictionary<string, object?> map)
    {
        writer.WriteStartMap(map.Count);
        foreach (var kvp in map)
        {
            writer.WriteTextString(kvp.Key);
            WriteCborValue(writer, kvp.Value);
        }
        writer.WriteEndMap();
    }

    private static void WriteCborValue(CborWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull();
                break;
            case string s:
                writer.WriteTextString(s);
                break;
            case int i:
                writer.WriteInt32(i);
                break;
            case long l:
                writer.WriteInt64(l);
                break;
            case bool b:
                writer.WriteBoolean(b);
                break;
            case byte[] bytes:
                writer.WriteByteString(bytes);
                break;
            case Dictionary<string, object?> nested:
                WriteCborMap(writer, nested);
                break;
            case List<object?> list:
                writer.WriteStartArray(list.Count);
                foreach (var item in list)
                    WriteCborValue(writer, item);
                writer.WriteEndArray();
                break;
            default:
                writer.WriteTextString(value.ToString()!);
                break;
        }
    }

    private static ReadOnlyMemory<byte> MakeFrame(byte[] data) => data;

    [Fact]
    public void Parse_CommitEvent_ReturnsCommitEvent()
    {
        var body = new Dictionary<string, object?>
        {
            ["repo"] = "did:plc:test123",
            ["commit"] = "bafyreievaxfmw7drb3ixcjp4y3ftm2pi3xfgzdgyv5vdd5vtzvsgatbqta",
            ["rev"] = "3jzfcijpj2z2a",
            ["since"] = null,
            ["tooBig"] = false,
            ["rebase"] = false,
            ["blocks"] = Array.Empty<byte>(),
            ["ops"] = new List<object?>(),
            ["seq"] = 42L,
            ["time"] = "2024-01-15T12:00:00.000Z",
        };

        var frameData = EncodeCborFrame(1, "#commit", body);
        var result = FirehoseEventParser.Parse(MakeFrame(frameData));

        Assert.NotNull(result);
        var commit = Assert.IsType<CommitEvent>(result);
        Assert.Equal("did:plc:test123", commit.Repo);
        Assert.Equal("bafyreievaxfmw7drb3ixcjp4y3ftm2pi3xfgzdgyv5vdd5vtzvsgatbqta", commit.Commit);
        Assert.Equal("3jzfcijpj2z2a", commit.Rev);
        Assert.Equal(42, commit.Seq);
        Assert.Equal("2024-01-15T12:00:00.000Z", commit.Time?.ToString());
    }

    [Theory]
    [InlineData("repo", "not-a-did")]
    [InlineData("commit", "bafyreinotacid")]
    [InlineData("rev", "not-a-tid")]
    public void Parse_CommitWithAMalformedIdentifier_IsDroppedLikeAnyMalformedFrame(string field, string value)
    {
        var body = new Dictionary<string, object?>
        {
            ["repo"] = "did:plc:test123",
            ["commit"] = "bafyreievaxfmw7drb3ixcjp4y3ftm2pi3xfgzdgyv5vdd5vtzvsgatbqta",
            ["rev"] = "3jzfcijpj2z2a",
            ["seq"] = 42L,
        };
        body[field] = value;

        Assert.Null(FirehoseEventParser.Parse(MakeFrame(EncodeCborFrame(1, "#commit", body))));
    }

    [Fact]
    public void Parse_IdentityEvent_ReturnsIdentityEvent()
    {
        var body = new Dictionary<string, object?>
        {
            ["did"] = "did:plc:identity123",
            ["handle"] = "test.bsky.social",
            ["seq"] = 100L,
            ["time"] = "2024-01-15T12:00:00.000Z",
        };

        var frameData = EncodeCborFrame(1, "#identity", body);
        var result = FirehoseEventParser.Parse(MakeFrame(frameData));

        Assert.NotNull(result);
        var identity = Assert.IsType<IdentityEvent>(result);
        Assert.Equal("did:plc:identity123", identity.Did);
        Assert.Equal("test.bsky.social", identity.Handle);
        Assert.Equal(100, identity.Seq);
    }

    [Fact]
    public void Parse_AccountEvent_ReturnsAccountEvent()
    {
        var body = new Dictionary<string, object?>
        {
            ["did"] = "did:plc:account123",
            ["active"] = true,
            ["seq"] = 200L,
            ["time"] = "2024-01-15T12:00:00.000Z",
        };

        var frameData = EncodeCborFrame(1, "#account", body);
        var result = FirehoseEventParser.Parse(MakeFrame(frameData));

        Assert.NotNull(result);
        var account = Assert.IsType<AccountEvent>(result);
        Assert.Equal("did:plc:account123", account.Did);
        Assert.True(account.Active);
        Assert.Equal(200, account.Seq);
    }

    [Fact]
    public void Parse_SyncEvent_ReturnsSyncEvent()
    {
        var body = new Dictionary<string, object?>
        {
            ["did"] = "did:plc:sync456",
            ["rev"] = "3jzfcijpj2z2b",
            ["blocks"] = new byte[] { 1, 2, 3 },
            ["seq"] = 300L,
            ["time"] = "2024-01-15T12:00:00.000Z",
        };

        var frameData = EncodeCborFrame(1, "#sync", body);
        var result = FirehoseEventParser.Parse(MakeFrame(frameData));

        Assert.NotNull(result);
        var sync = Assert.IsType<SyncEvent>(result);
        Assert.Equal("did:plc:sync456", sync.Did);
        Assert.Equal("3jzfcijpj2z2b", sync.Rev);
        Assert.Equal(300, sync.Seq);
    }

    [Fact]
    public void Parse_ErrorFrame_ThrowsTheTypedError()
    {
        // op = -1 is an error frame; the relay closes the stream after it.
        var body = new Dictionary<string, object?>
        {
            ["error"] = "FutureCursor",
            ["message"] = "Cursor is in the future",
        };

        var ex = Assert.Throws<EventStreamException>(() => FirehoseEventParser.Parse(EncodeCborFrame(-1, "#info", body)));

        Assert.Equal(EventStreamErrors.FutureCursor, ex.Error);
        Assert.Contains("Cursor is in the future", ex.Message);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public void Parse_InfoEvent_HasNoSequence()
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = "OutdatedCursor",
            ["message"] = "Cursor is older than the retention window",
        };

        var info = Assert.IsType<InfoEvent>(FirehoseEventParser.Parse(EncodeCborFrame(1, "#info", body)));

        Assert.Equal("OutdatedCursor", info.Name);
        Assert.Equal("Cursor is older than the retention window", info.Message);
        Assert.IsNotAssignableFrom<FirehoseEvent>(info);
    }

    [Fact]
    public void Parse_EmptyFrame_ReturnsNull()
    {
        var result = FirehoseEventParser.Parse(MakeFrame(Array.Empty<byte>()));
        Assert.Null(result);
    }

    [Fact]
    public void Parse_MalformedData_ReturnsNull()
    {
        var result = FirehoseEventParser.Parse(MakeFrame(new byte[] { 0xFF, 0xFF }));
        Assert.Null(result);
    }

    [Fact]
    public void Parse_CommitWithOps_DeserializesOps()
    {
        var ops = new List<object?>
        {
            new Dictionary<string, object?>
            {
                ["action"] = "create",
                ["path"] = "app.bsky.feed.post/abc123",
                ["cid"] = "bafyreicuerrgxezkf745depqtasepklz7xoyws7ckhusinymwzuqbxybry",
            },
            new Dictionary<string, object?>
            {
                ["action"] = "delete",
                ["path"] = "app.bsky.feed.like/def456",
                ["cid"] = null,
            },
        };

        var body = new Dictionary<string, object?>
        {
            ["repo"] = "did:plc:test123",
            ["commit"] = "bafyreievaxfmw7drb3ixcjp4y3ftm2pi3xfgzdgyv5vdd5vtzvsgatbqta",
            ["rev"] = "3jzfcijpj2z2a",
            ["since"] = null,
            ["tooBig"] = false,
            ["rebase"] = false,
            ["blocks"] = Array.Empty<byte>(),
            ["ops"] = ops,
            ["seq"] = 42L,
            ["time"] = "2024-01-15T12:00:00.000Z",
        };

        var frameData = EncodeCborFrame(1, "#commit", body);
        var result = FirehoseEventParser.Parse(MakeFrame(frameData));

        Assert.NotNull(result);
        var commit = Assert.IsType<CommitEvent>(result);
        Assert.NotNull(commit.Ops);
        Assert.Equal(2, commit.Ops.Count);
        Assert.Equal(RepoOpAction.Create, commit.Ops[0].Action);
        Assert.Equal("app.bsky.feed.post/abc123", commit.Ops[0].Path);
        Assert.Equal("bafyreicuerrgxezkf745depqtasepklz7xoyws7ckhusinymwzuqbxybry", commit.Ops[0].Cid);
        Assert.Equal(RepoOpAction.Delete, commit.Ops[1].Action);
    }

    [Fact]
    public void Parse_CommitWithSyncV1Fields_DeserializesPrevData()
    {
        var body = new Dictionary<string, object?>
        {
            ["repo"] = "did:plc:test123",
            ["commit"] = "bafyreievaxfmw7drb3ixcjp4y3ftm2pi3xfgzdgyv5vdd5vtzvsgatbqta",
            ["rev"] = "3jzfcijpj2z2a",
            ["since"] = "3jzfcijpj2z22",
            ["tooBig"] = false,
            ["rebase"] = false,
            ["blocks"] = Array.Empty<byte>(),
            ["ops"] = new List<object?>(),
            ["prevData"] = "bafyreihlzn2lwoicy7x46zrj4ysc3eqhmpvghca5vbhcwtubpytilc6xsi",
            ["blobs"] = new List<object?> { "bafyreieludigxrnirftld5ub3hflfbyjpannprcqqawq4r3rglmjdhqmx4" },
            ["seq"] = 42L,
            ["time"] = "2024-01-15T12:00:00.000Z",
        };

        var frameData = EncodeCborFrame(1, "#commit", body);
        var result = FirehoseEventParser.Parse(MakeFrame(frameData));

        Assert.NotNull(result);
        var commit = Assert.IsType<CommitEvent>(result);
        Assert.Equal("bafyreihlzn2lwoicy7x46zrj4ysc3eqhmpvghca5vbhcwtubpytilc6xsi", commit.PrevData);

        // Deprecated upstream, but still read when a relay sends it.
#pragma warning disable CS0618
        Assert.NotNull(commit.Blobs);
        Assert.Single(commit.Blobs);
#pragma warning restore CS0618
    }

    [Theory]
    [InlineData("#handle")]
    [InlineData("#tombstone")]
    public void Parse_EventRemovedUpstream_ReturnsNull(string type)
    {
        var body = new Dictionary<string, object?>
        {
            ["did"] = "did:plc:handle123",
            ["handle"] = "newhandle.bsky.social",
            ["seq"] = 500L,
            ["time"] = "2024-01-15T12:00:00.000Z",
        };

        Assert.Null(FirehoseEventParser.Parse(EncodeCborFrame(1, type, body)));
    }

    [Fact]
    public void Parse_UnknownType_ReturnsNull()
    {
        // Unknown type should not be deserializable
        var body = new Dictionary<string, object?>
        {
            ["foo"] = "bar",
            ["seq"] = 1L,
        };

        var frameData = EncodeCborFrame(1, "#unknownFutureType", body);
        var result = FirehoseEventParser.Parse(MakeFrame(frameData));

        // Should return null since the type discriminator won't match
        Assert.Null(result);
    }

    // ── The shared DAG-CBOR transcoder ───────────────────────

    private static byte[] Header(string type)
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(2);
        writer.WriteTextString("op");
        writer.WriteInt32(1);
        writer.WriteTextString("t");
        writer.WriteTextString(type);
        writer.WriteEndMap();
        return writer.Encode();
    }

    [Fact]
    public void Parse_CidLinksAndByteStrings_AreFlattenedForTheModels()
    {
        var commitCid = CidComputation.ComputeBinaryForDagCbor([0xA0]);
        byte[] blocks = [1, 2, 3, 250];

        var body = new CborWriter(CborConformanceMode.Lax);
        body.WriteStartMap(5);
        body.WriteTextString("repo");
        body.WriteTextString("did:plc:test123");
        body.WriteTextString("commit");
        body.WriteTag((CborTag)42);
        body.WriteByteString([0x00, .. commitCid]);
        body.WriteTextString("rev");
        body.WriteTextString("3jzfcijpj2z2a");
        body.WriteTextString("blocks");
        body.WriteByteString(blocks);
        body.WriteTextString("seq");
        body.WriteInt64(7);
        body.WriteEndMap();

        var result = FirehoseEventParser.Parse(MakeFrame([.. Header("#commit"), .. body.Encode()]));

        var commit = Assert.IsType<CommitEvent>(result);
        Assert.Equal(CidComputation.EncodeCidToString(commitCid), commit.Commit);
        Assert.Equal(blocks, commit.Blocks);
    }

    [Fact]
    public void Parse_DeeplyNestedBody_ReturnsNullRatherThanOverflowingTheStack()
    {
        // {"junk": [[[… 100,000 levels …]]]}: a relay-supplied frame that used to crash the process.
        byte[] body = [0xA1, 0x64, .. "junk"u8, .. Enumerable.Repeat((byte)0x81, 100_000), 0x01];

        Assert.Null(FirehoseEventParser.Parse(MakeFrame([.. Header("#identity"), .. body])));
    }

    [Fact]
    public void Parse_MalformedCidLink_ReturnsNull()
    {
        // tag 42 around a byte string without the 0x00 multibase prefix.
        byte[] body = [0xA1, 0x66, .. "commit"u8, 0xD8, 0x2A, 0x42, 0x01, 0x71];

        Assert.Null(FirehoseEventParser.Parse(MakeFrame([.. Header("#commit"), .. body])));
    }
}
