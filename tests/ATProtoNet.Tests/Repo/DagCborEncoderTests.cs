using System.Text.Json;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

public class DagCborEncoderTests
{
    [Fact]
    public void Encode_SimpleObject_ProducesDeterministicOutput()
    {
        var json = JsonSerializer.SerializeToElement(new { b = 2, a = 1 });
        var bytes = DagCborEncoder.Encode(json);

        // Re-encoding should produce identical bytes
        var bytes2 = DagCborEncoder.Encode(json);
        Assert.Equal(bytes, bytes2);
    }

    [Fact]
    public void Encode_SortKeys_ByByteValue()
    {
        // Keys "b" and "a" should be sorted as "a", "b" in CBOR
        var json = JsonSerializer.SerializeToElement(new { b = 2, a = 1 });
        var bytes = DagCborEncoder.Encode(json);

        // Decode back and verify key order
        var decoded = DagCborDecoder.Decode(bytes);
        var properties = decoded.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["a", "b"], properties);
    }

    [Fact]
    public void Encode_String_WritesTextString()
    {
        var json = JsonSerializer.SerializeToElement("hello");
        var bytes = DagCborEncoder.Encode(json);
        var decoded = DagCborDecoder.Decode(bytes);
        Assert.Equal("hello", decoded.GetString());
    }

    [Fact]
    public void Encode_Integer_WritesInteger()
    {
        var json = JsonSerializer.SerializeToElement(42);
        var bytes = DagCborEncoder.Encode(json);
        var decoded = DagCborDecoder.Decode(bytes);
        Assert.Equal(42, decoded.GetInt32());
    }

    [Fact]
    public void Encode_NegativeInteger_WritesNegativeInteger()
    {
        var json = JsonSerializer.SerializeToElement(-1);
        var bytes = DagCborEncoder.Encode(json);
        var decoded = DagCborDecoder.Decode(bytes);
        Assert.Equal(-1, decoded.GetInt32());
    }

    [Fact]
    public void Encode_Boolean_WritesBoolean()
    {
        var trueJson = JsonSerializer.SerializeToElement(true);
        var trueBytes = DagCborEncoder.Encode(trueJson);
        Assert.True(DagCborDecoder.Decode(trueBytes).GetBoolean());

        var falseJson = JsonSerializer.SerializeToElement(false);
        var falseBytes = DagCborEncoder.Encode(falseJson);
        Assert.False(DagCborDecoder.Decode(falseBytes).GetBoolean());
    }

    [Fact]
    public void Encode_Null_WritesNull()
    {
        var json = JsonDocument.Parse("null").RootElement;
        var bytes = DagCborEncoder.Encode(json);
        var decoded = DagCborDecoder.Decode(bytes);
        Assert.Equal(JsonValueKind.Null, decoded.ValueKind);
    }

    [Fact]
    public void Encode_Array_WritesArray()
    {
        var json = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 });
        var bytes = DagCborEncoder.Encode(json);
        var decoded = DagCborDecoder.Decode(bytes);
        Assert.Equal(JsonValueKind.Array, decoded.ValueKind);
        Assert.Equal(3, decoded.GetArrayLength());
        Assert.Equal(1, decoded[0].GetInt32());
        Assert.Equal(2, decoded[1].GetInt32());
        Assert.Equal(3, decoded[2].GetInt32());
    }

    [Fact]
    public void Encode_EmptyObject_WritesEmptyMap()
    {
        var json = JsonDocument.Parse("{}").RootElement;
        var bytes = DagCborEncoder.Encode(json);
        var decoded = DagCborDecoder.Decode(bytes);
        Assert.Equal(JsonValueKind.Object, decoded.ValueKind);
        Assert.Empty(decoded.EnumerateObject().ToList());
    }

    [Fact]
    public void Encode_BytesObject_WritesByteString()
    {
        // { "$bytes": "SGVsbG8=" } should encode as CBOR byte string
        var json = JsonDocument.Parse("{\"$bytes\":\"SGVsbG8=\"}").RootElement;
        var bytes = DagCborEncoder.Encode(json);
        var decoded = DagCborDecoder.Decode(bytes);

        // Decoded back should be a $bytes object
        Assert.True(decoded.TryGetProperty("$bytes", out var bytesValue));
        var decodedBytes = Convert.FromBase64String(bytesValue.GetString()!);
        Assert.Equal("Hello"u8.ToArray(), decodedBytes);
    }

    [Fact]
    public void Encode_Float_Throws()
    {
        var json = JsonDocument.Parse("3.14").RootElement;
        Assert.Throws<InvalidOperationException>(() => DagCborEncoder.Encode(json));
    }

    [Fact]
    public void Encode_NestedObject_ProducesCorrectOutput()
    {
        var json = JsonDocument.Parse("{\"outer\":{\"z\":3,\"a\":1},\"inner\":[true,null]}").RootElement;
        var bytes = DagCborEncoder.Encode(json);
        var decoded = DagCborDecoder.Decode(bytes);

        // Outer keys should be sorted
        var outerKeys = decoded.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["inner", "outer"], outerKeys);

        // Inner object keys should be sorted too
        var innerObjKeys = decoded.GetProperty("outer").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["a", "z"], innerObjKeys);
    }

    [Fact]
    public void Encode_TypeDiscriminator_Preserved()
    {
        // $type field should round-trip correctly
        var json = JsonDocument.Parse("{\"$type\":\"app.bsky.feed.post\",\"text\":\"hello\"}").RootElement;
        var bytes = DagCborEncoder.Encode(json);
        var decoded = DagCborDecoder.Decode(bytes);

        Assert.Equal("app.bsky.feed.post", decoded.GetProperty("$type").GetString());
        Assert.Equal("hello", decoded.GetProperty("text").GetString());
    }
}
