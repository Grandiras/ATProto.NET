namespace ATProtoNet.Tests.Syntax;

public class AtUriTests
{
    [Theory]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/3k2la", "app.bsky.feed.post", "3k2la")]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post", "app.bsky.feed.post", null)]
    [InlineData("at://did:plc:abc123", null, null)]
    [Trait("Spec", "SYN-36")]
    public void Parse_ExtractsComponents(string value, string? collection, string? rkey)
    {
        var uri = AtUri.Parse(value);

        Assert.Equal("did:plc:abc123", uri.Authority.Value);
        Assert.True(uri.Authority.IsDid);
        Assert.Equal(collection, uri.Collection?.Value);
        Assert.Equal(rkey, uri.RecordKey?.Value);
        Assert.Equal(value, uri.Value);
    }

    [Fact]
    [Trait("Spec", "SYN-37")]
    public void Parse_HandleAuthority_ReadsLowerCasedInAuthorityButKeepsTheText()
    {
        var uri = AtUri.Parse("at://Alice.Bsky.Social/app.bsky.actor.profile/self");

        Assert.True(uri.Authority.IsHandle);
        Assert.Equal("alice.bsky.social", uri.Authority.Value);
        Assert.Equal("at://Alice.Bsky.Social/app.bsky.actor.profile/self", uri.Value);
        Assert.NotEqual(AtUri.Parse("at://alice.bsky.social/app.bsky.actor.profile/self"), uri);
    }

    [Fact]
    [Trait("Spec", "SYN-39")]
    public void Parts_AreComputedOnceAndShared()
    {
        var uri = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post/3k2la");

        Assert.Same(uri.Authority, uri.Authority);
        Assert.Same(uri.Collection, uri.Collection);
        Assert.Same(uri.RecordKey, uri.RecordKey);
    }

    [Theory]
    [InlineData("at://name")]
    [InlineData("at://@alice.bsky.social")]
    [InlineData("at://did:plc:abc123/")]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/3k2la/")]
    [InlineData("at://did:plc:abc123#frag")]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post?x=1")]
    [InlineData("at://did:plc:abc123/app.bsky.feed_post")]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/a+b")]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/3k2la/extra")]
    [InlineData("at://did:plc:abc123//3k2la")]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/3k2la ")]
    [InlineData("AT://did:plc:abc123")]
    [InlineData("at:/did:plc:abc123")]
    [InlineData("at://")]
    [Trait("Spec", "SYN-36")]
    public void Parse_OutsideTheRestrictedSyntax_Throws(string value) => Assert.Throws<FormatException>(() => AtUri.Parse(value));

    [Fact]
    [Trait("Spec", "SYN-36"), Trait("Spec", "SYN-9")]
    public void TryParse_LengthLimit_IsEightKibibytes()
    {
        // 5 + 253 + 1 + 317 + 1 + 512 is far below it, so the limit is reached with a long authority only through the scanner's own check.
        Assert.False(AtUri.TryParse("at://did:plc:" + new string('a', AtUri.MaxLength), out _));
        Assert.True(AtUri.TryParse("at://did:plc:" + new string('a', Did.MaxLength - 8), out _));
    }

    [Theory]
    [InlineData(null, null, "at://did:plc:abc123")]
    [InlineData("app.bsky.feed.post", null, "at://did:plc:abc123/app.bsky.feed.post")]
    [InlineData("app.bsky.feed.post", "3k2la", "at://did:plc:abc123/app.bsky.feed.post/3k2la")]
    [Trait("Spec", "SYN-38")]
    public void Create_FromComponents_ProducesTheUriParseReads(string? collection, string? rkey, string expected)
    {
        var uri = AtUri.Create(Did.Parse("did:plc:abc123"), collection is null ? null : Nsid.Parse(collection), rkey is null ? null : RecordKey.Parse(rkey));

        Assert.Equal(expected, uri.Value);
        Assert.Equal(AtUri.Parse(expected), uri);
        Assert.Equal(rkey, uri.RecordKey?.Value);
        Assert.Equal(collection, uri.Collection?.Value);
    }

    [Fact]
    [Trait("Spec", "SYN-38")]
    public void Create_RkeyWithoutCollection_Throws() =>
        Assert.Throws<ArgumentException>(() => AtUri.Create(Did.Parse("did:plc:abc123"), recordKey: RecordKey.Self));

    [Fact]
    [Trait("Spec", "SYN-75")]
    public void Parse_SpaceAddress_ThrowsNamingTheSpaceType()
    {
        var ex = Assert.Throws<FormatException>(() => AtUri.Parse("at://did:plc:abc123/space/com.example.forum/a"));

        Assert.Contains(nameof(SpaceUri), ex.Message);
    }
}
