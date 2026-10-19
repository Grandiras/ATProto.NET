using System.Buffers;
using ATProtoNet.Data;

namespace ATProtoNet.Tests.Data;

public class DrislWriterTests
{
    [Fact]
    [Trait("Spec", "DAT-40")]
    public void WriteEndMap_KeysInAnyOrder_SortsShorterFirstThenByUtf8()
    {
        var hex = Write(w =>
        {
            w.WriteStartMap(4);
            foreach (var key in new[] { "a", "b", "ab", "\U00010000" })
            {
                w.WriteKey(key);
                w.WriteInt64(key.Length);
            }
            w.WriteEndMap();
        });

        // b, ab, then the two 4-byte keys by UTF-8: ee 80 80 61 before f0 90 80 80 (UTF-16 would order them the other way).
        Assert.Equal("a4616201626162026" + "4ee8080610264f090808002", hex);
    }

    [Fact]
    [Trait("Spec", "DAT-40")]
    public void WriteEndMap_NestedUnsortedMaps_EachSortedAndLengthsShortest()
    {
        var hex = Write(w =>
        {
            w.WriteStartMap(2);
            w.WriteKey("outer");
            w.WriteStartMap(2);
            w.WriteKey("z");
            w.WriteInt64(3);
            w.WriteKey("a");
            w.WriteInt64(1);
            w.WriteEndMap();
            w.WriteKey("inner");
            w.WriteStartArray(2);
            w.WriteBoolean(true);
            w.WriteNull();
            w.WriteEndArray();
            w.WriteEndMap();
        });

        Assert.Equal("a265696e6e657282f5f6656f75746572a2616101617a03", hex);
    }

    [Theory]
    [Trait("Spec", "DAT-40")]
    [InlineData(23L, "17")]
    [InlineData(24L, "1818")]
    [InlineData(256L, "190100")]
    [InlineData(-1L, "20")]
    [InlineData(long.MinValue, "3b7fffffffffffffff")]
    [InlineData(long.MaxValue, "1b7fffffffffffffff")]
    public void WriteInt64_Value_ShortestEncoding(long value, string hex) => Assert.Equal(hex, Write(w => w.WriteInt64(value)));

    [Fact]
    [Trait("Spec", "DAT-40")]
    public void WriteEndArray_24Items_UsesTheTwoByteHeader()
    {
        var hex = Write(w =>
        {
            w.WriteStartArray(24);
            for (var i = 0; i < 24; i++) w.WriteNull();
            w.WriteEndArray();
        });

        Assert.Equal("9818" + string.Concat(Enumerable.Repeat("f6", 24)), hex);
    }

    [Fact]
    [Trait("Spec", "DAT-40")]
    public void ToDrisl_JsonObjectOf300UnsortedKeys_GrowsItsHeaderAndSortsEverything()
    {
        var keys = Enumerable.Range(0, 300).Select(i => $"k{(i * 7919) % 300}").ToList();
        var json = "{" + string.Join(",", keys.Select(k => $"\"{k}\":[{{\"z\":1,\"a\":2}}]")) + "}";

        var drisl = DrislJson.ToDrisl(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Equal("b9012c", Convert.ToHexStringLower(drisl[..3]));
        var reader = new DrislReader(drisl);
        reader.SkipValue();
        reader.ReadEnd();
        Assert.Equal(DataModelValue.FromJson(System.Text.Encoding.UTF8.GetBytes(json)).ToDrisl(), drisl);
    }

    [Theory]
    [Trait("Spec", "DAT-41")]
    [InlineData("a", "a", "b")] // found as it is written
    [InlineData("a", "b", "a")] // found when the map is sorted
    public void WriteEndMap_RepeatedKey_ThrowsArgumentException(params string[] keys) =>
        Assert.Throws<ArgumentException>(() => Write(w =>
        {
            w.WriteStartMap(keys.Length);
            foreach (var key in keys)
            {
                w.WriteKey(key);
                w.WriteNull();
            }
            w.WriteEndMap();
        }));

    [Theory]
    [Trait("Spec", "DAT-41")]
    [InlineData(true)] // a lone surrogate
    [InlineData(false)] // a repeated key
    public void WriteKey_Refused_LeavesTheWriterUsable(bool loneSurrogate)
    {
        var refused = loneSurrogate ? "\uD800" : "a";
        var output = new ArrayBufferWriter<byte>();
        var writer = new DrislWriter(output);
        writer.WriteStartMap(1);
        writer.WriteKey("a");
        writer.WriteNull();

        Assert.Throws<ArgumentException>(() => writer.WriteKey(refused));
        writer.WriteEndMap();

        Assert.Equal("a16161f6", Convert.ToHexStringLower(output.WrittenSpan));
    }

    [Fact]
    [Trait("Spec", "DAT-41")]
    public void WriteString_LoneSurrogate_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => Write(w => w.WriteString("\uD800")));

    [Fact]
    [Trait("Spec", "DAT-41")]
    public void Writer_NoFloatMethod_FloatsCannotBeWritten() =>
        Assert.DoesNotContain(typeof(DrislWriter).GetMethods(), m => m.GetParameters().Any(p => p.ParameterType == typeof(double) || p.ParameterType == typeof(float)));

    [Fact]
    [Trait("Spec", "DAT-42")]
    public void WriteLink_AndWriteBytes_TagFortyTwoOverZeroAndTheCid()
    {
        var cid = Cid.Compute(CidCodec.Raw, []);

        Assert.Equal("d82a58250001551220e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Write(w => w.WriteLink(cid)));
        Assert.Equal("4548656c6c6f", Write(w => w.WriteBytes("Hello"u8)));
    }

    [Fact]
    public void WriteEndMap_FewerEntriesThanDeclared_ThrowsInvalidOperationException()
    {
        var writer = new DrislWriter(new ArrayBufferWriter<byte>());
        writer.WriteStartMap(2);
        writer.WriteKey("a");
        writer.WriteNull();

        Assert.Throws<InvalidOperationException>(writer.WriteEndMap);
    }

    [Fact]
    public void Write_TwoTopLevelValues_EachReachesTheOutputWhenComplete()
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new DrislWriter(output);
        writer.WriteStartArray(1);
        Assert.Equal(0, output.WrittenCount);
        writer.WriteInt64(1);
        writer.WriteEndArray();
        writer.WriteNull();

        Assert.Equal("8101f6", Convert.ToHexStringLower(output.WrittenSpan));
    }

    private static string Write(Action<DrislWriter> write)
    {
        var output = new ArrayBufferWriter<byte>();
        write(new DrislWriter(output));
        return Convert.ToHexStringLower(output.WrittenSpan);
    }
}
