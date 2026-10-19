using System.Text.Json;

namespace ATProtoNet.Tests.Syntax;

public class SpaceAddressTests
{
    private const string Authority = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string Author = "did:plc:z72i7hdynmk6r22z27h6tvur";
    private const string SpaceRef = $"at://{Authority}/space/com.atmoboards.forum/default";
    private const string RecordRef = $"{SpaceRef}/{Author}/com.atmoboards.thread/3l6oveex3ii2l";
    private const string PublicRef = $"at://{Authority}/app.bsky.feed.post/3l6oveex3ii2l";

    [Fact]
    [Trait("Spec", "SYN-74")]
    public void Parse_SpaceRef_SplitsItsThreeComponents()
    {
        var space = SpaceUri.Parse(SpaceRef);

        Assert.Equal((Authority, "com.atmoboards.forum", "default"), (space.Authority.Value, space.SpaceType.Value, space.Skey.Value));
        Assert.Equal(SpaceRef, space.Value);
    }

    [Fact]
    [Trait("Spec", "SYN-74")]
    public void Parse_RecordUri_SplitsAllSixComponents()
    {
        var record = SpaceRecordUri.Parse(RecordRef);

        Assert.Equal(SpaceRef, record.Space.Value);
        Assert.Equal(Authority, record.Space.Authority.Value);
        Assert.Equal(Author, record.Author.Value);
        Assert.Equal("com.atmoboards.thread", record.Collection.Value);
        Assert.Equal("3l6oveex3ii2l", record.Rkey.Value);
        Assert.Equal("com.atmoboards.thread/3l6oveex3ii2l", record.Path);
        Assert.Equal(RecordRef, record.Value);
    }

    [Theory]
    [InlineData("at://alice.example.com/space/com.atmoboards.forum/default")]
    [InlineData($"at://{Authority}/space/forum/default")]
    [InlineData($"at://{Authority}/spaces/com.atmoboards.forum/default")]
    [InlineData($"at://{Authority}/space/com.atmoboards.forum")]
    [InlineData($"{Authority}/space/com.atmoboards.forum/default")]
    [InlineData($"{SpaceRef}?foo=bar")]
    [InlineData($"{SpaceRef}#/frag")]
    [InlineData($"{SpaceRef}/")]
    [InlineData($"{SpaceRef} ")]
    [InlineData($"{SpaceRef}\n")]
    [InlineData($"at://{Authority}/space")]
    [InlineData(PublicRef)]
    [InlineData(RecordRef)]
    [Trait("Spec", "SYN-74"), Trait("Spec", "SYN-76")]
    public void TryParse_InvalidSpaceRef_ReturnsFalse(string value) => Assert.False(SpaceUri.TryParse(value, out _));

    [Theory]
    [InlineData($"{SpaceRef}/alice.example.com/com.atmoboards.thread/3l6oveex3ii2l")]
    [InlineData($"{SpaceRef}/{Author}/thread/3l6oveex3ii2l")]
    [InlineData($"{SpaceRef}/{Author}/com.atmoboards.thread")]
    [InlineData($"{SpaceRef}/{Author}/com.atmoboards.thread/3l6oveex3ii2l/extra")]
    [InlineData($"{RecordRef}\n")]
    [InlineData(SpaceRef)]
    [InlineData(PublicRef)]
    [Trait("Spec", "SYN-74"), Trait("Spec", "SYN-76")]
    public void TryParse_InvalidRecordUri_ReturnsFalse(string value) => Assert.False(SpaceRecordUri.TryParse(value, out _));

    [Fact]
    [Trait("Spec", "SYN-75")]
    public void Parse_WrongKind_ThrowsNamingTheKindItIs()
    {
        Assert.Contains(nameof(SpaceRecordUri), Assert.Throws<FormatException>(() => SpaceUri.Parse(RecordRef)).Message);
        Assert.Contains(nameof(SpaceUri), Assert.Throws<FormatException>(() => SpaceRecordUri.Parse(SpaceRef)).Message);
        Assert.Contains(nameof(AtUri), Assert.Throws<FormatException>(() => SpaceUri.Parse(PublicRef)).Message);
        Assert.Contains(nameof(SpaceUri), Assert.Throws<FormatException>(() => AtUri.Parse(SpaceRef)).Message);
    }

    [Theory]
    [InlineData(PublicRef, typeof(AtUri))]
    [InlineData($"at://{Authority}", typeof(AtUri))]
    [InlineData(SpaceRef, typeof(SpaceUri))]
    [InlineData(RecordRef, typeof(SpaceRecordUri))]
    [Trait("Spec", "SYN-75"), Trait("Spec", "SYN-81")]
    public void TryParse_AtAddress_ClassifiesAnyAddress(string value, Type kind)
    {
        Assert.True(AtAddress.TryParse(value, out var address));
        Assert.IsType(kind, address.Value);
        Assert.Equal(value, address.ToString());
        Assert.Equal(value, AtAddress.Parse(value).ToString());
    }

    [Fact]
    [Trait("Spec", "SYN-81")]
    public void Switch_OverAtAddress_HandlesEachCase()
    {
        static string Describe(AtAddress address) => address switch
        {
            AtUri uri => $"public {uri.Authority}",
            SpaceUri space => $"space {space.SpaceType}",
            SpaceRecordUri record => $"record {record.Collection}",
        };

        Assert.Equal("public did:plc:ewvi7nxzyoun6zhxrhs64oiz", Describe(AtAddress.Parse(PublicRef)));
        Assert.Equal("space com.atmoboards.forum", Describe(AtAddress.Parse(SpaceRef)));
        Assert.Equal("record com.atmoboards.thread", Describe(AtAddress.Parse(RecordRef)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("at://alice.example.com/space/com.atmoboards.forum/default")]
    [Trait("Spec", "SYN-3"), Trait("Spec", "SYN-75")]
    public void TryParse_AtAddress_RefusesWhatIsNoAddress(string value)
    {
        Assert.False(AtAddress.TryParse(value, out _));
        Assert.Throws<FormatException>(() => AtAddress.Parse(value));
        Assert.False(AtAddress.TryParse(null, out _));
        Assert.Throws<ArgumentNullException>(() => AtAddress.Parse(null!));
    }

    [Fact]
    [Trait("Spec", "SYN-77")]
    public void Record_BuildsTheSameUriAsParsingOne()
    {
        var space = SpaceUri.Parse(SpaceRef);

        var record = space.Record(Did.Parse(Author), Nsid.Parse("com.atmoboards.thread"), RecordKey.Parse("3l6oveex3ii2l"));

        Assert.Equal(RecordRef, record.Value);
        Assert.Equal(SpaceRecordUri.Parse(RecordRef), record);
        Assert.Equal(space, record.Space);
    }

    [Fact]
    [Trait("Spec", "SYN-77")]
    public void Create_RoundTripsThroughParseAndAcceptsASpaceKeyAtTheLengthLimit()
    {
        var space = SpaceUri.Create(Did.Parse(Authority), Nsid.Parse("com.atmoboards.forum"), RecordKey.Parse("default"));
        var longKey = SpaceUri.Create(Did.Parse(Authority), Nsid.Parse("com.atmoboards.forum"), RecordKey.Parse(new string('a', 512)));

        Assert.Equal(SpaceRef, space.Value);
        Assert.Equal(space, SpaceUri.Parse(space.Value));
        Assert.Equal(longKey, SpaceUri.Parse(longKey.Value));
        Assert.False(SpaceUri.TryParse($"at://{Authority}/space/com.atmoboards.forum/{new string('a', 513)}", out _));
    }

    [Fact]
    [Trait("Spec", "SYN-77")]
    public void Equality_IsOrdinalOnTheValue()
    {
        var a = SpaceUri.Parse(SpaceRef);
        var b = SpaceUri.Create(Did.Parse(Authority), Nsid.Parse("com.atmoboards.forum"), RecordKey.Parse("default"));

        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(SpaceRef, (string)a);
    }

    [Fact]
    [Trait("Spec", "SYN-78")]
    public void Json_AddressesAreStringsAndInvalidTextFailsWithThePath()
    {
        var space = SpaceUri.Parse(SpaceRef);
        var record = SpaceRecordUri.Parse(RecordRef);
        var address = AtAddress.Parse(RecordRef);

        Assert.Equal($"\"{SpaceRef}\"", JsonSerializer.Serialize(space));
        Assert.Equal(record, JsonSerializer.Deserialize<SpaceRecordUri>(JsonSerializer.Serialize(record)));
        Assert.Equal($"\"{RecordRef}\"", JsonSerializer.Serialize(address));
        Assert.Equal(RecordRef, JsonSerializer.Deserialize<AtAddress>($"\"{RecordRef}\"").ToString());
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Holder>("""{"Address":"https://example.com"}"""));
        Assert.Equal("$.Address", ex.Path);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AtAddress>("42"));
    }

    private sealed class Holder
    {
        public AtAddress Address { get; set; }
    }
}
