using System.Text.Json;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

public class DagCborRoundtripTests
{
    [Fact]
    public void Roundtrip_SimpleObject()
    {
        var original = JsonDocument.Parse("{\"text\":\"hello\",\"count\":42,\"active\":true}").RootElement;
        var bytes = DagCborEncoder.Encode(original);
        var decoded = DagCborDecoder.Decode(bytes);

        Assert.Equal("hello", decoded.GetProperty("text").GetString());
        Assert.Equal(42, decoded.GetProperty("count").GetInt32());
        Assert.True(decoded.GetProperty("active").GetBoolean());
    }

    [Fact]
    public void Roundtrip_BytesField()
    {
        var original = JsonDocument.Parse("{\"data\":{\"$bytes\":\"SGVsbG8gV29ybGQ=\"}}").RootElement;
        var bytes = DagCborEncoder.Encode(original);
        var decoded = DagCborDecoder.Decode(bytes);

        var dataBytes = Convert.FromBase64String(decoded.GetProperty("data").GetProperty("$bytes").GetString()!);
        Assert.Equal("Hello World"u8.ToArray(), dataBytes);
    }

    [Fact]
    public void Roundtrip_NullValue()
    {
        var original = JsonDocument.Parse("{\"value\":null}").RootElement;
        var bytes = DagCborEncoder.Encode(original);
        var decoded = DagCborDecoder.Decode(bytes);

        Assert.Equal(JsonValueKind.Null, decoded.GetProperty("value").ValueKind);
    }

    [Fact]
    public void Roundtrip_NestedArrays()
    {
        var original = JsonDocument.Parse("{\"items\":[[1,2],[3,4]]}").RootElement;
        var bytes = DagCborEncoder.Encode(original);
        var decoded = DagCborDecoder.Decode(bytes);

        var items = decoded.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(1, items[0][0].GetInt32());
        Assert.Equal(4, items[1][1].GetInt32());
    }

    [Fact]
    public void Roundtrip_TypeField_Preserved()
    {
        var original = JsonDocument.Parse("{\"$type\":\"app.bsky.feed.post\",\"text\":\"hello\",\"createdAt\":\"2024-01-01T00:00:00Z\"}").RootElement;
        var bytes = DagCborEncoder.Encode(original);
        var decoded = DagCborDecoder.Decode(bytes);

        Assert.Equal("app.bsky.feed.post", decoded.GetProperty("$type").GetString());
        Assert.Equal("hello", decoded.GetProperty("text").GetString());
    }

    [Fact]
    public void Roundtrip_EmptyArray()
    {
        var original = JsonDocument.Parse("{\"items\":[]}").RootElement;
        var bytes = DagCborEncoder.Encode(original);
        var decoded = DagCborDecoder.Decode(bytes);

        Assert.Equal(0, decoded.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Roundtrip_LargeInteger()
    {
        long value = long.MaxValue;
        var original = JsonDocument.Parse($"{{\"big\":{value}}}").RootElement;
        var bytes = DagCborEncoder.Encode(original);
        var decoded = DagCborDecoder.Decode(bytes);

        Assert.Equal(value, decoded.GetProperty("big").GetInt64());
    }

    [Fact]
    public void Roundtrip_NegativeInteger()
    {
        var original = JsonDocument.Parse("{\"neg\":-12345}").RootElement;
        var bytes = DagCborEncoder.Encode(original);
        var decoded = DagCborDecoder.Decode(bytes);

        Assert.Equal(-12345, decoded.GetProperty("neg").GetInt32());
    }

    [Fact]
    public void EncodeThenComputeCid_IsConsistent()
    {
        var json = JsonDocument.Parse("{\"text\":\"hello\",\"count\":42}").RootElement;

        var (bytes1, cid1) = DagCborEncoder.EncodeWithCid(json);
        var (bytes2, cid2) = DagCborEncoder.EncodeWithCid(json);

        Assert.Equal(bytes1, bytes2);
        Assert.Equal(cid1, cid2);
    }
}
