using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

public class AtUriTests
{
    [Theory]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/3k2la")]
    [InlineData("at://alice.bsky.social/app.bsky.actor.profile/self")]
    [InlineData("at://did:plc:abc123")]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post")]
    public void Parse_ValidAtUri_Succeeds(string value)
    {
        var uri = AtUri.Parse(value);
        Assert.Equal(value, uri.Value);
    }

    [Fact]
    public void Parse_FullUri_ExtractsComponents()
    {
        var uri = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post/3k2la");

        Assert.Equal("did:plc:abc123", uri.Authority);
        Assert.Equal("app.bsky.feed.post", uri.Collection);
        Assert.Equal("3k2la", uri.RecordKey);
    }

    [Fact]
    public void Parse_AuthorityOnly_HasNullCollectionAndRkey()
    {
        var uri = AtUri.Parse("at://did:plc:abc123");

        Assert.Equal("did:plc:abc123", uri.Authority);
        Assert.Null(uri.Collection);
        Assert.Null(uri.RecordKey);
    }

    [Fact]
    public void Parse_WithCollection_HasNullRkey()
    {
        var uri = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post");

        Assert.Equal("did:plc:abc123", uri.Authority);
        Assert.Equal("app.bsky.feed.post", uri.Collection);
        Assert.Null(uri.RecordKey);
    }

    [Fact]
    public void Repo_ReturnsParsedAtIdentifier()
    {
        var uri = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post/3k2la");
        var repo = uri.Repo;

        Assert.True(repo.IsDid);
        Assert.Equal("did:plc:abc123", repo.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("http://example.com")]
    [InlineData("at:missing-slashes")]
    public void Parse_InvalidAtUri_Throws(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => AtUri.Parse(value));
    }

    [Fact]
    public void Parse_ExceedsMaxLength_Throws()
    {
        var longUri = "at://did:plc:" + new string('a', 8200);
        Assert.Throws<ArgumentException>(() => AtUri.Parse(longUri));
    }

    [Theory]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/3k2la", true)]
    [InlineData("not-a-uri", false)]
    [InlineData(null, false)]
    public void TryParse_ReturnsExpected(string? value, bool expected)
    {
        var result = AtUri.TryParse(value, out var uri);
        Assert.Equal(expected, result);

        if (expected)
            Assert.NotNull(uri);
        else
            Assert.Null(uri);
    }

    [Fact]
    public void Create_FromComponents_ProducesValidUri()
    {
        var repo = AtIdentifier.Parse("did:plc:abc123");
        var uri = AtUri.Create(repo, "app.bsky.feed.post", "3k2la");

        Assert.Equal("at://did:plc:abc123/app.bsky.feed.post/3k2la", uri.Value);
        Assert.Equal("did:plc:abc123", uri.Authority);
        Assert.Equal("app.bsky.feed.post", uri.Collection);
        Assert.Equal("3k2la", uri.RecordKey);
    }

    [Fact]
    public void Create_WithoutRkey_ProducesValidUri()
    {
        var repo = AtIdentifier.Parse("did:plc:abc123");
        var uri = AtUri.Create(repo, "app.bsky.feed.post");

        Assert.Equal("at://did:plc:abc123/app.bsky.feed.post", uri.Value);
        Assert.Null(uri.RecordKey);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var a = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post/3k2la");
        var b = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post/3k2la");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ImplicitCast_ToString_Works()
    {
        var uri = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post/3k2la");
        string value = uri;
        Assert.Equal("at://did:plc:abc123/app.bsky.feed.post/3k2la", value);
    }
}
