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
    public void Decode_FloatValue_ThrowsFormatException()
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteDouble(3.14);
        var bytes = writer.Encode();

        Assert.Throws<FormatException>(() => DagCborDecoder.Decode(bytes));
    }

    // ── Untrusted input ──────────────────────────────────────

    /// <summary><paramref name="depth"/> nested one-element arrays around <paramref name="inner"/>.</summary>
    private static byte[] NestedArrays(int depth, params byte[] inner) =>
        [.. Enumerable.Repeat((byte)0x81, depth), .. inner];

    [Fact]
    public void Decode_NestingAtTheLimit_Succeeds()
    {
        var decoded = DagCborDecoder.Decode(NestedArrays(64, 0x01));

        var element = decoded;
        for (var i = 0; i < 64; i++)
            element = element[0];
        Assert.Equal(1, element.GetInt32());
    }

    [Theory]
    [InlineData(65)]
    [InlineData(100_000)] // overflowed the stack before the limit existed
    public void Decode_NestingBeyondTheLimit_ThrowsFormatException(int depth)
    {
        Assert.Throws<FormatException>(() => DagCborDecoder.Decode(NestedArrays(depth, 0x01)));
    }

    [Fact]
    public void Decode_DeeplyNestedMaps_ThrowsFormatException()
    {
        // {"a": {"a": … }} 100,000 levels deep.
        var bytes = Enumerable.Range(0, 100_000).SelectMany(_ => new byte[] { 0xA1, 0x61, 0x61 }).Append((byte)0x01).ToArray();

        Assert.Throws<FormatException>(() => DagCborDecoder.Decode(bytes));
    }

    [Fact]
    public void Decode_LinkWrapperCountsTowardsTheLimit()
    {
        // A CID at array depth 64 renders as {"$link": …} one level further in.
        byte[] link = [0xD8, 0x2A, 0x58, 0x25, 0x00, .. CidComputation.ComputeBinaryForDagCbor([0xA0])];

        Assert.Equal(JsonValueKind.Array, DagCborDecoder.Decode(NestedArrays(63, link)).ValueKind);
        Assert.Throws<FormatException>(() => DagCborDecoder.Decode(NestedArrays(64, link)));
    }

    [Fact]
    public void Decode_LongChainOfUnknownTags_DecodesTheInnerValue()
    {
        // Unknown tags are skipped; a chain of them must not cost a stack frame each.
        var bytes = Enumerable.Repeat((byte)0xC1, 100_000).Append((byte)0x07).ToArray();

        Assert.Equal(7, DagCborDecoder.Decode(bytes).GetInt32());
    }

    [Theory]
    [InlineData("d82a4101")]            // CID link without the 0x00 prefix
    [InlineData("d82a4100")]            // CID link that is only the prefix
    [InlineData("d82a6161")]            // tag 42 around a text string
    [InlineData("a10101")]              // integer map key
    [InlineData("62")]                  // truncated text string
    [InlineData("82")]                  // truncated array
    [InlineData("")]                    // nothing at all
    [InlineData("1bffffffffffffffff")]  // unsigned integer beyond Int64
    [InlineData("f7")]                  // undefined
    [InlineData("ff")]                  // a lone break
    public void Decode_MalformedInput_ThrowsFormatException(string hex)
    {
        Assert.Throws<FormatException>(() => DagCborDecoder.Decode(Convert.FromHexString(hex)));
    }

    [Fact]
    public void TryValidate_DeepNesting_ReturnsFalseRatherThanOverflowing()
    {
        Assert.False(DagCborDecoder.TryValidate(NestedArrays(100_000, 0x01), out var error));
        Assert.NotNull(error);
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
