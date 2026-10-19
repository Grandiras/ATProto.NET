using System.Buffers;
using System.Text;
using System.Text.Json;
using ATProtoNet.Data;

namespace ATProtoNet.Tests.Data;

public class DataModelValueTests
{
    private const string LinkText = "bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku";

    [Fact]
    [Trait("Spec", "DAT-1")]
    [Trait("Spec", "DAT-4")]
    public void FromJson_EveryKind_IsTheMatchingCase()
    {
        var value = FromJson($$$"""{"n": null, "b": true, "i": -7, "s": "x", "y": {"$bytes": "AQI"}, "l": {"$link": "{{{LinkText}}}"}, "a": [], "m": {}}""");

        var map = Assert.IsType<DataModelMap>(value);
        Assert.Same(DataModelNull.Instance, map["n"]);
        Assert.Equal(new DataModelBoolean(true), map["b"]);
        Assert.Equal(new DataModelInteger(-7), map["i"]);
        Assert.Equal(new DataModelString("x"), map["s"]);
        Assert.Equal(new DataModelBytes(new byte[] { 1, 2 }), map["y"]);
        Assert.Equal(new DataModelLink(Cid.Parse(LinkText)), map["l"]);
        Assert.Empty(Assert.IsType<DataModelArray>(map["a"]));
        Assert.Empty(Assert.IsType<DataModelMap>(map["m"]));
    }

    [Theory]
    [Trait("Spec", "DAT-1")]
    [Trait("Spec", "DAT-2")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("9223372036854775808")]
    [InlineData("-9223372036854775809")]
    public void FromJson_NotAnInt64_ThrowsJsonException(string json) => Assert.ThrowsAny<JsonException>(() => FromJson(json));

    [Fact]
    [Trait("Spec", "DAT-2")]
    public void FromJson_IntegerLikeFloat_IsTheInteger() => Assert.Equal(new DataModelInteger(123), FromJson("123.0"));

    [Fact]
    [Trait("Spec", "DAT-2")]
    public void FromDrisl_IntegerBeyondInt64_SaysSo()
    {
        var error = Assert.Throws<FormatException>(() => DataModelValue.FromDrisl(Convert.FromHexString("1bffffffffffffffff")));
        Assert.Contains("64-bit", error.Message);
        Assert.DoesNotContain("loat", error.Message);
    }

    [Fact]
    [Trait("Spec", "DAT-3")]
    public void FromDrisl_IntegerKey_ThrowsFormatException() =>
        Assert.Throws<FormatException>(() => DataModelValue.FromDrisl(Convert.FromHexString("a10101")));

    [Theory]
    [Trait("Spec", "DAT-4")]
    [Trait("Spec", "DAT-45")]
    [InlineData("""{"text":"hello","count":42,"active":true,"value":null,"items":[[1,2],[3,4]],"empty":[],"neg":-12345}""")]
    [InlineData($$$"""{"$type":"app.bsky.embed.images","image":{"$type":"blob","ref":{"$link":"{{{LinkText}}}"},"mimeType":"image/png","size":5}}""")]
    [InlineData("""{"sig":{"$bytes":"SGVsbG8"}}""")]
    public void ToDrisl_ThenFromDrisl_RoundTripsThroughBothForms(string json)
    {
        var value = FromJson(json);
        var drisl = value.ToDrisl();

        Assert.Equal(drisl, DrislJson.ToDrisl(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(value, DataModelValue.FromDrisl(drisl));
        Assert.Equal(value, FromJson(value.ToString()));
        Assert.Equal(DataModelValue.FromDrisl(drisl).ToString(), Json(drisl));
    }

    [Fact]
    [Trait("Spec", "DAT-45")]
    public void ToString_MapReadFromDrisl_KeepsTheEncodedKeyOrder()
    {
        var value = DataModelValue.FromDrisl(Convert.FromHexString("a2616202616101"), new() { Mode = DrislMode.Lenient });

        Assert.Equal("""{"b":2,"a":1}""", value.ToString());
        Assert.Equal(FromJson("""{"a":1,"b":2}"""), value);
    }

    [Fact]
    [Trait("Spec", "DAT-46")]
    public void WriteJson_Bytes_UnpaddedStandardBase64()
    {
        Assert.Equal("""{"$bytes":"+/8"}""", new DataModelBytes(new byte[] { 0xfb, 0xff }).ToString());
        Assert.Equal(FromJson("""{"$bytes":"+/8="}"""), FromJson("""{"$bytes":"+/8"}"""));
    }

    [Theory]
    [Trait("Spec", "DAT-47")]
    [InlineData("""{"$link": 1234}""")]
    [InlineData("""{"$link": "."}""")]
    [InlineData($$"""{"$link": "{{LinkText}}", "other": 1}""")]
    [InlineData($$"""{"other": 1, "$link": "{{LinkText}}"}""")]
    [InlineData("""{"$bytes": [1, 2, 3]}""")]
    [InlineData("""{"$bytes": "AQI", "other": 1}""")]
    [InlineData("""{"$bytes": "A Q"}""")]
    public void FromJson_MalformedLinkOrBytes_ThrowsJsonException(string json)
    {
        Assert.ThrowsAny<JsonException>(() => FromJson(json));
        Assert.ThrowsAny<JsonException>(() => DrislJson.ToDrisl(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [Trait("Spec", "DAT-47")]
    [InlineData("""{"$type": null}""")]
    [InlineData("""{"$type": ""}""")]
    [InlineData("""{"$type": 1}""")]
    [InlineData($$"""{"$type": "blob", "mimeType": "image/png", "size": 1}""")]
    [InlineData($$"""{"$type": "blob", "ref": {"$link": "{{LinkText}}"}, "mimeType": "image/png", "size": "1"}""")]
    public void FromJson_InvalidTypeOrBlob_ThrowsJsonException(string json) => Assert.ThrowsAny<JsonException>(() => FromJson(json));

    [Fact]
    [Trait("Spec", "DAT-47")]
    public void FromDrisl_MapKeyedLink_ThrowsFormatException() =>
        Assert.Throws<FormatException>(() => DataModelValue.FromDrisl(Convert.FromHexString("a165246c696e6b01")));

    [Theory]
    [Trait("Spec", "DAT-48")]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void FromJson_NestedArrays_LimitedTo64Levels(int depth, bool accepted)
    {
        var json = new string('[', depth) + "1" + new string(']', depth);

        if (accepted) Assert.NotNull(FromJson(json).ToDrisl());
        else Assert.ThrowsAny<JsonException>(() => FromJson(json));
    }

    [Fact]
    [Trait("Spec", "DAT-48")]
    public void ToDrisl_TreeDeeperThan64Levels_ThrowsFormatException()
    {
        DataModelValue value = new DataModelInteger(1);
        for (var i = 0; i < 65; i++) value = new DataModelArray([value]);

        Assert.Throws<DataLimitExceededException>(value.ToDrisl);
    }

    [Fact]
    [Trait("Spec", "DAT-33")]
    public void FromDrisl_NestedArraysDeclaringTheWholeInput_AllocateNothingInProportion()
    {
        // 63 nested arrays, each declaring as many items as bytes remain in 1 MB, then a break: lists sized
        // from those declarations took 562 MB before the read failed.
        var input = new byte[1_000_000];
        for (var at = 0; at < 63 * 5; at += 5)
        {
            input[at] = 0x9a;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(at + 1), (uint)(input.Length - at - 5));
        }
        input[63 * 5] = 0xff;

        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<FormatException>(() => DataModelValue.FromDrisl(input));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1_000_000);
    }

    [Fact]
    [Trait("Spec", "DAT-48")]
    public void ToString_ValueNoEncodingAccepts_IsTheKindNotAnException()
    {
        DataModelValue deep = new DataModelInteger(1);
        for (var i = 0; i < 65; i++) deep = new DataModelArray([deep]);

        Assert.Equal(nameof(DataModelString), new DataModelString("\uD800").ToString());
        Assert.Equal(nameof(DataModelArray), deep.ToString());
    }

    [Fact]
    [Trait("Spec", "DAT-41")]
    public void FromJson_DuplicateKey_ThrowsJsonException()
    {
        Assert.ThrowsAny<JsonException>(() => FromJson("""{"a":1,"b":2,"a":3}"""));
        Assert.ThrowsAny<JsonException>(() => DrislJson.ToDrisl("""{"a":1,"b":2,"a":3}"""u8));
    }

    [Fact]
    [Trait("Spec", "DAT-44")]
    public void ToDrisl_RealNetworkRecord_HashesToThePublishedCid()
    {
        // The post bsky.app published on 2024-10-17, and the CID its PDS gave it.
        var json = """{"$type":"app.bsky.feed.post","createdAt":"2024-10-17T07:06:51.491Z","langs":["en"],"text":"\ud83d\udc4b  Bluesky is an open social network that gives creators independence from platforms, developers the freedom to build, and users a choice in their experience. We're so excited to have you here! \n\nWe share Bluesky updates & news from this account. A quick orientation thread: \ud83e\uddf5\u2728"}""";

        Assert.Equal("bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq", Cid.Compute(CidCodec.DagCbor, FromJson(json).ToDrisl()).Value);
    }

    [Fact]
    public void JsonSerializer_DataModelValue_UsesItsConverter()
    {
        var value = JsonSerializer.Deserialize<DataModelValue>($$$"""{"l":{"$link":"{{{LinkText}}}"}}""");

        Assert.IsType<DataModelLink>(Assert.IsType<DataModelMap>(value)["l"]);
        Assert.Equal($$$"""{"l":{"$link":"{{{LinkText}}}"}}""", JsonSerializer.Serialize(value));
    }

    [Fact]
    public void DataModelMap_RepeatedKey_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => new DataModelMap([new("a", DataModelNull.Instance), new("a", DataModelNull.Instance)]));

    private static DataModelValue FromJson(string json) => DataModelValue.FromJson(Encoding.UTF8.GetBytes(json));

    private static string Json(byte[] drisl)
    {
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output, new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            DrislJson.WriteJson(drisl, writer);
        return Encoding.UTF8.GetString(output.WrittenSpan);
    }
}
