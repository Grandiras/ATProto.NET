using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

public class SpaceUriTests
{
    private const string Authority = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string Author = "did:plc:z72i7hdynmk6r22z27h6tvur";
    private const string SpaceRef = $"at://{Authority}/space/com.atmoboards.forum/default";
    private const string RecordRef = $"{SpaceRef}/{Author}/com.atmoboards.thread/3l6oveex3ii2l";

    [Fact]
    public void Parse_SpaceRef_SplitsItsThreeComponents()
    {
        var space = SpaceUri.Parse(SpaceRef);

        Assert.Equal(Authority, space.Authority);
        Assert.Equal("com.atmoboards.forum", space.SpaceType);
        Assert.Equal("default", space.Skey);
        Assert.Equal(SpaceRef, space.Value);
    }

    [Fact]
    public void Create_RoundTripsThroughParse()
    {
        var space = SpaceUri.Create(Authority, "com.atmoboards.forum", "default");

        Assert.Equal(SpaceRef, space.Value);
        Assert.Equal(space, SpaceUri.Parse(space.Value));
    }

    [Fact]
    public void Parse_RecordUri_IsRejectedAsASpaceRef()
    {
        // The two forms address different things; a space ref is the three-segment form only.
        Assert.False(SpaceUri.TryParse(RecordRef, out _));
        Assert.Throws<ArgumentException>(() => SpaceUri.Parse(RecordRef));
    }

    [Fact]
    public void Parse_SpaceRef_IsRejectedAsARecordUri()
    {
        Assert.False(SpaceRecordUri.TryParse(SpaceRef, out _));
    }

    [Fact]
    public void Parse_RecordUri_SplitsAllSixComponents()
    {
        var record = SpaceRecordUri.Parse(RecordRef);

        Assert.Equal(SpaceRef, record.Space.Value);
        Assert.Equal(Author, record.Author);
        Assert.Equal("com.atmoboards.thread", record.Collection);
        Assert.Equal("3l6oveex3ii2l", record.Rkey);
        Assert.Equal("com.atmoboards.thread/3l6oveex3ii2l", record.Path);
    }

    [Fact]
    public void Record_BuildsTheSameUriAsParsingOne()
    {
        var space = SpaceUri.Parse(SpaceRef);

        var record = space.Record(Author, "com.atmoboards.thread", "3l6oveex3ii2l");

        Assert.Equal(RecordRef, record.Value);
        Assert.Equal(SpaceRecordUri.Parse(RecordRef), record);
    }

    [Theory]
    // The authority and the author are DIDs, never handles: a space's identity and its
    // membership are keyed on DIDs.
    [InlineData("at://alice.example.com/space/com.atmoboards.forum/default")]
    // A space type is an NSID.
    [InlineData($"at://{Authority}/space/forum/default")]
    // The marker is literal.
    [InlineData($"at://{Authority}/spaces/com.atmoboards.forum/default")]
    // Too few segments.
    [InlineData($"at://{Authority}/space/com.atmoboards.forum")]
    // No scheme.
    [InlineData($"{Authority}/space/com.atmoboards.forum/default")]
    // A public AT-URI is not a space URI.
    [InlineData($"at://{Authority}/app.bsky.feed.post/3l6oveex3ii2l")]
    // Query and fragment are not part of the grammar and would be ambiguous against an rkey.
    [InlineData($"{SpaceRef}?foo=bar")]
    [InlineData($"{SpaceRef}#/frag")]
    // A trailing slash is an empty segment, not a valid key.
    [InlineData($"{SpaceRef}/")]
    public void TryParse_InvalidSpaceRef_ReturnsFalse(string value)
    {
        Assert.False(SpaceUri.TryParse(value, out _));
    }

    [Theory]
    [InlineData($"{SpaceRef}/alice.example.com/com.atmoboards.thread/3l6oveex3ii2l")]
    [InlineData($"{SpaceRef}/{Author}/thread/3l6oveex3ii2l")]
    [InlineData($"{SpaceRef}/{Author}/com.atmoboards.thread")]
    [InlineData($"{SpaceRef}/{Author}/com.atmoboards.thread/3l6oveex3ii2l/extra")]
    public void TryParse_InvalidRecordUri_ReturnsFalse(string value)
    {
        Assert.False(SpaceRecordUri.TryParse(value, out _));
    }

    [Theory]
    [InlineData(SpaceRef, true)]
    [InlineData(RecordRef, true)]
    [InlineData($"at://{Authority}/space", true)]
    [InlineData($"at://{Authority}/app.bsky.feed.post/3l6oveex3ii2l", false)]
    [InlineData($"at://{Authority}", false)]
    [InlineData("https://example.com/space/a/b", false)]
    [InlineData(null, false)]
    public void IsSpaceUri_DistinguishesPermissionedFromPublic(string? value, bool expected)
    {
        // The marker and a collection NSID can never collide: an NSID always contains at least
        // two dots and "space" contains none.
        Assert.Equal(expected, SpaceUri.IsSpaceUri(value));
    }

    [Fact]
    public void Create_RejectsAHandleAuthority()
    {
        Assert.Throws<ArgumentException>(
            () => SpaceUri.Create("alice.example.com", "com.atmoboards.forum", "default"));
    }

    [Fact]
    public void Create_RejectsASpaceKeyOverTheLengthLimit()
    {
        Assert.Throws<ArgumentException>(
            () => SpaceUri.Create(Authority, "com.atmoboards.forum", new string('a', 513)));
    }

    [Fact]
    public void Create_AcceptsASpaceKeyAtTheLengthLimit()
    {
        var space = SpaceUri.Create(Authority, "com.atmoboards.forum", new string('a', 512));

        Assert.Equal(512, space.Skey.Length);
    }

    [Fact]
    public void HostAudience_NamesTheAuthoritysSpaceHostServiceEntry()
    {
        Assert.Equal($"{Authority}#atproto_space_host", SpaceUri.Parse(SpaceRef).HostAudience);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        var a = SpaceUri.Parse(SpaceRef);
        var b = SpaceUri.Create(Authority, "com.atmoboards.forum", "default");

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ImplicitConversion_YieldsTheUriString()
    {
        string value = SpaceUri.Parse(SpaceRef);

        Assert.Equal(SpaceRef, value);
    }
}
