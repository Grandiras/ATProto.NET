using System.Formats.Cbor;
using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

/// <summary>
/// Pins <see cref="DagCborEncoder"/> and <see cref="DagCborDecoder"/> to the data-model fixtures
/// of bluesky-social/atproto-interop-tests (<c>data-model/data-model-fixtures.json</c>, CC0).
/// </summary>
public sealed class DagCborInteropTests
{
    public static TheoryData<string, string, string> DataModelFixtures => new()
    {
        {
            """{"string":"abc","unicode":"a~öñ©⽘☎𓋓😀👨‍👩‍👧‍👧","integer":123,"bool":true,"null":null,"array":["abc","def","ghi"],"object":{"string":"abc","number":123,"bool":true,"arr":["abc","def","ghi"]}}""",
            "a764626f6f6cf5646e756c6cf665617272617983636162636364656663676869666f626a656374a4636172728363616263636465666367686964626f6f6cf5666e756d626572187b66737472696e676361626366737472696e676361626367696e7465676572187b67756e69636f6465782f617ec3b6c3b1c2a9e2bd98e2988ef0938b93f09f9880f09f91a8e2808df09f91a9e2808df09f91a7e2808df09f91a7",
            "bafyreiclp443lavogvhj3d2ob2cxbfuscni2k5jk7bebjzg7khl3esabwq"
        },
        {
            """{"a":{"$link":"bafyreidfayvfuwqa7qlnopdjiqrxzs6blmoeu4rujcjtnci5beludirz2a"},"b":{"$bytes":"nFERjvLLiw9qm45JrqH9QTzyC2Lu1Xb4ne6+sBrCzI0"},"c":{"$type":"blob","ref":{"$link":"bafkreiccldh766hwcnuxnf2wh6jgzepf2nlu2lvcllt63eww5p6chi4ity"},"mimeType":"image/jpeg","size":10000}}""",
            "a36161d82a5825000171122065062a5a5a00fc16d73c6944237ccbc15b1c4a7234489336891d091741a239d0616258209c51118ef2cb8b0f6a9b8e49aea1fd413cf20b62eed576f89deebeb01ac2cc8d6163a463726566d82a582500015512204258cfff78f613697697563f926c91e5d3574d2ea25ae7ed92d6ebfc23a3889e6473697a6519271065247479706564626c6f62686d696d65547970656a696d6167652f6a706567",
            "bafyreihldkhcwijkde7gx4rpkkuw7pl6lbyu5gieunyc7ihactn5bkd2nm"
        },
        {
            """{"a":{"b":[{"d":[{"$link":"bafyreidfayvfuwqa7qlnopdjiqrxzs6blmoeu4rujcjtnci5beludirz2a"},{"$link":"bafyreidfayvfuwqa7qlnopdjiqrxzs6blmoeu4rujcjtnci5beludirz2a"}],"e":[{"$bytes":"nFERjvLLiw9qm45JrqH9QTzyC2Lu1Xb4ne6+sBrCzI0"},{"$bytes":"iE+sPoHobU9tSIqGI+309LLCcWQIRmEXwxcoDt19tas"}]}]}}""",
            "a16161a1616281a2616482d82a5825000171122065062a5a5a00fc16d73c6944237ccbc15b1c4a7234489336891d091741a239d0d82a5825000171122065062a5a5a00fc16d73c6944237ccbc15b1c4a7234489336891d091741a239d061658258209c51118ef2cb8b0f6a9b8e49aea1fd413cf20b62eed576f89deebeb01ac2cc8d5820884fac3e81e86d4f6d488a8623edf4f4b2c2716408466117c317280edd7db5ab",
            "bafyreid3imdulnhgeytpf6uk7zahjvrsqlofkmm5b5ub2maw4kqus6jp4i"
        },
    };

    [Theory]
    [MemberData(nameof(DataModelFixtures))]
    public void Encode_DataModelFixture_MatchesReferenceBytesAndCid(string json, string cborHex, string cid)
    {
        var (bytes, computed) = DagCborEncoder.EncodeWithCid(JsonSerializer.Deserialize<JsonElement>(json));

        Assert.Equal(cborHex, Convert.ToHexStringLower(bytes));
        Assert.Equal(cid, computed.Value);
    }

    [Theory]
    [MemberData(nameof(DataModelFixtures))]
    public void Decode_DataModelFixture_MatchesTheReferenceJson(string json, string cborHex, string cid)
    {
        // The reference JSON writes $bytes unpadded, as the data model specifies.
        _ = cid;
        var decoded = DagCborDecoder.Decode(Convert.FromHexString(cborHex));

        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(json), JsonSerializer.SerializeToNode(decoded)),
            $"Decoded {decoded.GetRawText()}");
    }

    [Theory]
    [MemberData(nameof(DataModelFixtures))]
    public void Decode_DataModelFixture_RoundTripsToTheSameBytes(string json, string cborHex, string cid)
    {
        _ = cid;
        var decoded = DagCborDecoder.Decode(Convert.FromHexString(cborHex));

        // Re-encoding reproduces the block only if every link, byte string and text value came
        // through the decoder intact.
        Assert.Equal(cborHex, Convert.ToHexStringLower(DagCborEncoder.Encode(decoded)));
        Assert.Equal(
            JsonSerializer.Deserialize<JsonElement>(json).EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal),
            decoded.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Decode_DataModelFixture_RendersLinksAndBytesInTheJsonDataModel()
    {
        var decoded = DagCborDecoder.Decode(Convert.FromHexString(
            "a36161d82a5825000171122065062a5a5a00fc16d73c6944237ccbc15b1c4a7234489336891d091741a239d0616258209c51118ef2cb8b0f6a9b8e49aea1fd413cf20b62eed576f89deebeb01ac2cc8d6163a463726566d82a582500015512204258cfff78f613697697563f926c91e5d3574d2ea25ae7ed92d6ebfc23a3889e6473697a6519271065247479706564626c6f62686d696d65547970656a696d6167652f6a706567"));

        Assert.Equal("bafyreidfayvfuwqa7qlnopdjiqrxzs6blmoeu4rujcjtnci5beludirz2a", decoded.GetProperty("a").GetProperty("$link").GetString());
        Assert.Equal(
            "nFERjvLLiw9qm45JrqH9QTzyC2Lu1Xb4ne6+sBrCzI0",
            decoded.GetProperty("b").GetProperty("$bytes").GetString());
        Assert.Equal(
            "bafkreiccldh766hwcnuxnf2wh6jgzepf2nlu2lvcllt63eww5p6chi4ity",
            decoded.GetProperty("c").GetProperty("ref").GetProperty("$link").GetString());
        Assert.Equal(10000, decoded.GetProperty("c").GetProperty("size").GetInt32());
    }

    // ── Encoder output is unchanged by writing maps in order ──

    /// <summary>
    /// The encoder as it was before it sorted maps itself: properties in document order, with a
    /// Canonical-mode writer doing the sorting. The two must agree byte for byte.
    /// </summary>
    private static byte[] EncodeWithCanonicalWriter(JsonElement element)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        Write(element);
        return writer.Encode();

        void Write(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    var properties = value.EnumerateObject().ToList();
                    if (properties is [{ Name: "$link", Value.ValueKind: JsonValueKind.String } link])
                    {
                        writer.WriteTag((CborTag)42);
                        writer.WriteByteString([0x00, .. CidComputation.DecodeCidString(link.Value.GetString()!)]);
                        return;
                    }

                    if (properties is [{ Name: "$bytes", Value.ValueKind: JsonValueKind.String } bytes])
                    {
                        writer.WriteByteString(Convert.FromBase64String(bytes.Value.GetString()!));
                        return;
                    }

                    writer.WriteStartMap(properties.Count);
                    foreach (var property in properties)
                    {
                        writer.WriteTextString(property.Name);
                        Write(property.Value);
                    }
                    writer.WriteEndMap();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray(value.GetArrayLength());
                    foreach (var item in value.EnumerateArray())
                        Write(item);
                    writer.WriteEndArray();
                    break;
                case JsonValueKind.String:
                    writer.WriteTextString(value.GetString()!);
                    break;
                case JsonValueKind.Number:
                    writer.WriteInt64(value.GetInt64());
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    writer.WriteBoolean(value.GetBoolean());
                    break;
                default:
                    writer.WriteNull();
                    break;
            }
        }
    }

    /// <summary>
    /// Keys chosen to stress the canonical order: differing lengths, non-ASCII, and supplementary
    /// characters against U+E000–U+FFFF, where UTF-16 and UTF-8 order disagree.
    /// </summary>
    private static readonly string[] AwkwardKeys =
    [
        "a", "b", "z", "ab", "ba", "$type", "text", "langs", "createdAt", "é", "é", "a", "\U00010000",
        "￿", "￿a", "😀", "日本", "key with spaces", new string('k', 23), new string('k', 24), new string('k', 300),
    ];

    private static JsonNode? RandomValue(Random random, int depth)
    {
        switch (random.Next(depth >= 4 ? 6 : 9))
        {
            case 0: return JsonValue.Create(random.NextInt64(long.MinValue, long.MaxValue) >> random.Next(64));
            case 1: return JsonValue.Create(AwkwardKeys[random.Next(AwkwardKeys.Length)]);
            case 2: return JsonValue.Create(random.Next(2) == 0);
            case 3: return null;
            case 4:
                return new JsonObject { ["$link"] = CidComputation.EncodeCidToString(CidComputation.ComputeBinaryForRaw([(byte)random.Next(256)])) };
            case 5:
                var bytes = new byte[random.Next(40)];
                random.NextBytes(bytes);
                return new JsonObject { ["$bytes"] = Convert.ToBase64String(bytes) };
            case 6:
                var array = new JsonArray();
                for (var i = random.Next(5); i > 0; i--)
                    array.Add(RandomValue(random, depth + 1));
                return array;
            default:
                var obj = new JsonObject();
                foreach (var key in AwkwardKeys.OrderBy(_ => random.Next()).Take(random.Next(1, 10)))
                    obj[key] = RandomValue(random, depth + 1);
                return obj;
        }
    }

    [Fact]
    public void Encode_RandomDocuments_ByteIdenticalToCanonicalWriterSorting()
    {
        var random = new Random(111);
        for (var i = 0; i < 500; i++)
        {
            var obj = new JsonObject();
            foreach (var key in AwkwardKeys.OrderBy(_ => random.Next()).Take(random.Next(1, AwkwardKeys.Length)))
                obj[key] = RandomValue(random, 1);
            var element = JsonSerializer.SerializeToElement(obj);

            Assert.Equal(EncodeWithCanonicalWriter(element), DagCborEncoder.Encode(element));
        }
    }

    [Fact]
    public void Encode_SupplementaryVersusPrivateUseKey_SortsByUtf8Bytes()
    {
        // Both keys are four UTF-8 bytes. U+E000 encodes as EE 80 80 and U+10000 as F0 90 80 80,
        // so the U+E000 key sorts first — even though its UTF-16 code unit (0xE000) is above the
        // surrogate 0xD800 that starts U+10000.
        var element = JsonSerializer.SerializeToElement(new Dictionary<string, int>
        {
            ["\U00010000"] = 2,
            ["a"] = 1,
        });

        var cbor = DagCborEncoder.Encode(element);

        Assert.Equal("a264ee8080610164f090808002", Convert.ToHexStringLower(cbor));
        Assert.True(DagCborEncoder.CompareCanonical("a", "\U00010000") < 0);
    }

    [Fact]
    public void Encode_RepeatedPropertyName_Throws()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""{"a":1,"b":2,"a":3}""");

        Assert.Throws<InvalidOperationException>(() => DagCborEncoder.Encode(element));
    }
}
