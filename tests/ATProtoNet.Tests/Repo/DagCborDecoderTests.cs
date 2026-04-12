using System.Formats.Cbor;
using System.Text.Json;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

public class DagCborDecoderTests
{
    [Fact]
    public void Decode_Map_ReturnsObject()
    {
        // Encode a map { "key": "value" } manually via CborWriter
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(1);
        writer.WriteTextString("key");
        writer.WriteTextString("value");
        writer.WriteEndMap();
        var bytes = writer.Encode();

        var decoded = DagCborDecoder.Decode(bytes);
        Assert.Equal(JsonValueKind.Object, decoded.ValueKind);
        Assert.Equal("value", decoded.GetProperty("key").GetString());
    }

    [Fact]
    public void Decode_CidTag42_ReturnsLinkObject()
    {
        // Encode a CID with tag 42
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteTag((CborTag)42);
        // CID binary: 0x00 prefix + CIDv1 bytes
        var fakeCidBytes = new byte[] { 0x00, 0x01, 0x71, 0x12, 0x20 };
        var hash = new byte[32]; // all zeros for simplicity
        var cidTagBytes = new byte[fakeCidBytes.Length + hash.Length];
        fakeCidBytes.CopyTo(cidTagBytes, 0);
        hash.CopyTo(cidTagBytes, fakeCidBytes.Length);
        writer.WriteByteString(cidTagBytes);
        var bytes = writer.Encode();

        var decoded = DagCborDecoder.Decode(bytes);
        Assert.Equal(JsonValueKind.Object, decoded.ValueKind);
        Assert.True(decoded.TryGetProperty("$link", out var linkValue));
        Assert.StartsWith("b", linkValue.GetString()!);
    }

    [Fact]
    public void Decode_ByteString_ReturnsBytesObject()
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteByteString("Hello"u8);
        var bytes = writer.Encode();

        var decoded = DagCborDecoder.Decode(bytes);
        Assert.Equal(JsonValueKind.Object, decoded.ValueKind);
        Assert.True(decoded.TryGetProperty("$bytes", out var bytesValue));
        var decodedData = Convert.FromBase64String(bytesValue.GetString()!);
        Assert.Equal("Hello"u8.ToArray(), decodedData);
    }

    [Fact]
    public void Decode_FloatValue_Throws()
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteDouble(3.14);
        var bytes = writer.Encode();

        Assert.Throws<InvalidOperationException>(() => DagCborDecoder.Decode(bytes));
    }

    [Fact]
    public void TryValidate_ValidCbor_ReturnsTrue()
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(1);
        writer.WriteTextString("a");
        writer.WriteInt32(1);
        writer.WriteEndMap();
        var bytes = writer.Encode();

        Assert.True(DagCborDecoder.TryValidate(bytes, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void TryValidate_UnsortedKeys_ReturnsFalse()
    {
        // Use Lax mode to write unsorted keys
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(2);
        writer.WriteTextString("b");
        writer.WriteInt32(2);
        writer.WriteTextString("a");
        writer.WriteInt32(1);
        writer.WriteEndMap();
        var bytes = writer.Encode();

        Assert.False(DagCborDecoder.TryValidate(bytes, out var error));
        Assert.Contains("sorted", error!);
    }

    [Fact]
    public void TryValidate_FloatValue_ReturnsFalse()
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteDouble(1.5);
        var bytes = writer.Encode();

        Assert.False(DagCborDecoder.TryValidate(bytes, out var error));
        Assert.Contains("float", error!, StringComparison.OrdinalIgnoreCase);
    }
}
