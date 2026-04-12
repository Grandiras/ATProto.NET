using System.Formats.Cbor;
using System.Net.WebSockets;
using System.Text.Json;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
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

    private static FirehoseFrame MakeFrame(byte[] data) => new()
    {
        RawData = data,
        MessageType = WebSocketMessageType.Binary,
    };

    [Fact]
    public void Parse_CommitEvent_ReturnsCommitEvent()
    {
        var body = new Dictionary<string, object?>
        {
            ["repo"] = "did:plc:test123",
            ["commit"] = "bafyreiabc123",
            ["rev"] = "abc123",
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
        Assert.Equal("bafyreiabc123", commit.Commit);
        Assert.Equal("abc123", commit.Rev);
        Assert.Equal(42, commit.Seq);
        Assert.False(commit.TooBig);
        Assert.False(commit.Rebase);
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
            ["rev"] = "rev123",
            ["blocks"] = new byte[] { 1, 2, 3 },
            ["seq"] = 300L,
            ["time"] = "2024-01-15T12:00:00.000Z",
        };

        var frameData = EncodeCborFrame(1, "#sync", body);
        var result = FirehoseEventParser.Parse(MakeFrame(frameData));

        Assert.NotNull(result);
        var sync = Assert.IsType<SyncEvent>(result);
        Assert.Equal("did:plc:sync456", sync.Did);
        Assert.Equal("rev123", sync.Rev);
        Assert.Equal(300, sync.Seq);
    }

    [Fact]
    public void Parse_ErrorOp_ReturnsNull()
    {
        // op = -1 means error
        var body = new Dictionary<string, object?>
        {
            ["error"] = "FutureCursor",
            ["message"] = "Cursor is in the future",
        };

        var frameData = EncodeCborFrame(-1, "#info", body);
        var result = FirehoseEventParser.Parse(MakeFrame(frameData));

        Assert.Null(result);
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
                ["cid"] = "bafyreicid123",
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
            ["commit"] = "bafyreiabc123",
            ["rev"] = "abc123",
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
        Assert.Equal("create", commit.Ops[0].Action);
        Assert.Equal("app.bsky.feed.post/abc123", commit.Ops[0].Path);
        Assert.Equal("bafyreicid123", commit.Ops[0].Cid);
        Assert.Equal("delete", commit.Ops[1].Action);
    }

    [Fact]
    public void Parse_CommitWithSyncV1Fields_DeserializesPrevData()
    {
        var body = new Dictionary<string, object?>
        {
            ["repo"] = "did:plc:test123",
            ["commit"] = "bafyreiabc123",
            ["rev"] = "abc123",
            ["since"] = "prevrev",
            ["tooBig"] = false,
            ["rebase"] = false,
            ["blocks"] = Array.Empty<byte>(),
            ["ops"] = new List<object?>(),
            ["prevData"] = "bafyreiprevdata",
            ["blobs"] = new List<object?> { "bafyreiblob1" },
            ["seq"] = 42L,
            ["time"] = "2024-01-15T12:00:00.000Z",
        };

        var frameData = EncodeCborFrame(1, "#commit", body);
        var result = FirehoseEventParser.Parse(MakeFrame(frameData));

        Assert.NotNull(result);
        var commit = Assert.IsType<CommitEvent>(result);
        Assert.Equal("bafyreiprevdata", commit.PrevData);
        Assert.NotNull(commit.Blobs);
        Assert.Single(commit.Blobs);
    }

    [Fact]
    public void TryParse_ValidFrame_ReturnsTrue()
    {
        var body = new Dictionary<string, object?>
        {
            ["did"] = "did:plc:test",
            ["active"] = true,
            ["seq"] = 1L,
        };

        var frameData = EncodeCborFrame(1, "#account", body);
        var frame = MakeFrame(frameData);

        bool success = FirehoseEventParser.TryParse(frame, out var message, out var error);

        Assert.True(success);
        Assert.NotNull(message);
        Assert.Null(error);
    }

    [Fact]
    public void TryParse_EmptyFrame_ReturnsFalse()
    {
        var frame = MakeFrame(Array.Empty<byte>());

        bool success = FirehoseEventParser.TryParse(frame, out var message, out var error);

        Assert.False(success);
        Assert.Null(message);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_NullFrame_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FirehoseEventParser.Parse((FirehoseFrame)null!));
    }

    [Fact]
    public void Parse_HandleEvent_ReturnsHandleEvent()
    {
        var body = new Dictionary<string, object?>
        {
            ["did"] = "did:plc:handle123",
            ["handle"] = "newhandle.bsky.social",
            ["seq"] = 500L,
            ["time"] = "2024-01-15T12:00:00.000Z",
        };

        var frameData = EncodeCborFrame(1, "#handle", body);
        var result = FirehoseEventParser.Parse(MakeFrame(frameData));

        Assert.NotNull(result);
        var handle = Assert.IsType<HandleEvent>(result);
        Assert.Equal("did:plc:handle123", handle.Did);
        Assert.Equal("newhandle.bsky.social", handle.Handle);
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
}
