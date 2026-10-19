using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Data;

namespace ATProtoNet.Tests.Syntax;

public class IdentifierJsonTests
{
    private sealed class Model
    {
        public Did? Did { get; set; }
        public Handle? Handle { get; set; }
        public AtUri? Uri { get; set; }
        public Tid Tid { get; set; }
        public Tid? OptionalTid { get; set; }
    }

    [Theory]
    [InlineData(typeof(Did), "did:plc:abc123")]
    [InlineData(typeof(Handle), "alice.bsky.social")]
    [InlineData(typeof(AtIdentifier), "did:plc:abc123")]
    [InlineData(typeof(AtIdentifier), "alice.bsky.social")]
    [InlineData(typeof(Nsid), "com.atproto.repo.createRecord")]
    [InlineData(typeof(RecordKey), "self")]
    [InlineData(typeof(Tid), "3jzfcijpj2z2a")]
    [InlineData(typeof(AtUri), "at://did:plc:abc123/app.bsky.feed.post/3k2la")]
    [InlineData(typeof(SpaceUri), "at://did:plc:abc123/space/com.example.forum/a")]
    [InlineData(typeof(Cid), "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm")]
    [InlineData(typeof(Language), "pt-BR")]
    [InlineData(typeof(DecimalString), "52.520")]
    [Trait("Spec", "SYN-70")]
    public void Json_Identifier_IsItsValueAsAString(Type type, string text)
    {
        var json = $"\"{text}\"";

        var value = JsonSerializer.Deserialize(json, type);

        Assert.Equal(text, value!.ToString());
        Assert.Equal(json, JsonSerializer.Serialize(value, type));
    }

    [Fact]
    [Trait("Spec", "SYN-70")]
    public void Json_Handle_ReadsNormalized() => Assert.Equal("alice.bsky.social", JsonSerializer.Deserialize<Handle>("\"Alice.Bsky.Social\"")!.Value);

    [Fact]
    [Trait("Spec", "SYN-71")]
    public void Json_InvalidValue_ThrowsJsonExceptionWithPathAndFormatException()
    {
        var json = """{"Did":"did:plc:abc123","Uri":"at://did:plc:abc123/"}""";

        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Model>(json));

        Assert.Equal("$.Uri", ex.Path);
        Assert.Contains("$.Uri", ex.Message);
        Assert.Contains("at://did:plc:abc123/", Assert.IsType<FormatException>(ex.InnerException).Message);
    }

    [Theory]
    [InlineData("\"not-a-did\"")]
    [InlineData("\"\"")]
    [InlineData("\"did:plc:abc\\n\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{}")]
    [Trait("Spec", "SYN-71")]
    public void Json_DidInvalidTokens_ThrowJsonException(string json) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Did>(json));

    [Fact]
    [Trait("Spec", "SYN-71")]
    public void Json_EscapedValidText_IsReadThroughTheStringPath() =>
        Assert.Equal("did:plc:abc123", JsonSerializer.Deserialize<Did>("\"did:plc:abc\\u0031\\u0032\\u0033\"")!.Value);

    [Fact]
    [Trait("Spec", "SYN-72")]
    public void Json_NullAndAbsent_ReadAsNullAndRoundTrip()
    {
        var model = JsonSerializer.Deserialize<Model>("""{"Did":null,"Uri":null,"OptionalTid":null}""")!;

        Assert.Null(model.Did);
        Assert.Null(model.Uri);
        Assert.Null(model.OptionalTid);
        Assert.Equal(default, model.Tid);
        Assert.Equal("""{"Did":null,"Handle":null,"Uri":null,"Tid":"2222222222222","OptionalTid":null}""", JsonSerializer.Serialize(model));
    }

    [Fact]
    [Trait("Spec", "SYN-70")]
    public void Json_Members_RoundTripInAnObject()
    {
        var model = new Model { Did = Did.Parse("did:plc:abc123"), Handle = Handle.Parse("alice.bsky.social"), Uri = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post/x"), Tid = Tid.Parse("3jzfcijpj2z2a") };

        var back = JsonSerializer.Deserialize<Model>(JsonSerializer.Serialize(model))!;

        Assert.Equal((model.Did, model.Handle, model.Uri, model.Tid), (back.Did, back.Handle, back.Uri, back.Tid));
    }

    [Fact]
    [Trait("Spec", "SYN-73")]
    public void Json_AsDictionaryKey_RoundTrips()
    {
        var map = new Dictionary<Did, int> { [Did.Parse("did:plc:abc123")] = 1, [Did.Parse("did:web:example.com")] = 2 };
        var nsids = new Dictionary<Nsid, string> { [Nsid.Parse("app.bsky.feed.post")] = "post" };

        var json = JsonSerializer.Serialize(map);

        Assert.Equal("""{"did:plc:abc123":1,"did:web:example.com":2}""", json);
        Assert.Equal(map, JsonSerializer.Deserialize<Dictionary<Did, int>>(json));
        Assert.Equal(nsids, JsonSerializer.Deserialize<Dictionary<Nsid, string>>(JsonSerializer.Serialize(nsids)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<Did, int>>("""{"nope":1}"""));
    }

    [Fact]
    [Trait("Spec", "SYN-70")]
    public void Json_SourceGeneratedContext_UsesTheAttachedConverters()
    {
        var entry = new SyntaxEntry(Did.Parse("did:plc:abc123"), Tid.Parse("3jzfcijpj2z2a"), AtDatetime.Parse("2024-01-01T00:00:00.000Z"), DecimalString.Parse("52.520"), AtAddress.Parse("at://did:plc:abc123/app.bsky.feed.post/x"));

        var json = JsonSerializer.Serialize(entry, SyntaxEntryContext.Default.SyntaxEntry);

        Assert.Equal("""{"Did":"did:plc:abc123","Tid":"3jzfcijpj2z2a","When":"2024-01-01T00:00:00.000Z","Amount":"52.520","Address":"at://did:plc:abc123/app.bsky.feed.post/x"}""", json);
        var back = JsonSerializer.Deserialize(json, SyntaxEntryContext.Default.SyntaxEntry)!;
        Assert.Equal((entry.Did, entry.Tid, entry.When, entry.Amount), (back.Did, back.Tid, back.When, back.Amount));
        Assert.Equal(entry.Address.ToString(), back.Address.ToString());
    }
}

internal sealed record SyntaxEntry(Did Did, Tid Tid, AtDatetime When, DecimalString Amount, AtAddress Address);

[JsonSerializable(typeof(SyntaxEntry))]
internal sealed partial class SyntaxEntryContext : JsonSerializerContext;
