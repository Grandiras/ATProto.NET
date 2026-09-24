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
        Assert.Equal(Nsid.Parse("app.bsky.feed.post"), uri.Collection);
        Assert.Equal(RecordKey.Parse("3k2la"), uri.RecordKey);
    }

    [Fact]
    public void Parse_HandleAuthority_RepoIsLowerCasedHandle()
    {
        var uri = AtUri.Parse("at://Alice.Bsky.Social/app.bsky.actor.profile/self");

        Assert.Equal("Alice.Bsky.Social", uri.Authority);
        Assert.True(uri.Repo.IsHandle);
        Assert.Equal("alice.bsky.social", uri.Repo.Value);
        Assert.Equal("at://Alice.Bsky.Social/app.bsky.actor.profile/self", uri.Value);
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
        Assert.Equal("app.bsky.feed.post", uri.Collection?.Value);
        Assert.Null(uri.RecordKey);
    }

    [Fact]
    public void Repo_ReturnsParsedAtIdentifier()
    {
        var uri = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post/3k2la");
        var repo = uri.Repo;

        Assert.True(repo.IsDid);
        Assert.Equal("did:plc:abc123", repo.Value);
        Assert.Same(repo, uri.Repo);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("http://example.com")]
    [InlineData("at:missing-slashes")]
    [InlineData("at://name")]                                             // Authority is neither DID nor handle
    [InlineData("at://@alice.bsky.social")]                               // '@' is reserved for userinfo
    [InlineData("at://did:plc:abc123/")]                                  // Trailing slash
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/3k2la/")]
    [InlineData("at://did:plc:abc123#frag")]                              // Fragments and queries are unused
    [InlineData("at://did:plc:abc123/app.bsky.feed.post?x=1")]
    [InlineData("at://did:plc:abc123/app.bsky.feed_post")]                // Collection is not an NSID
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/a+b")]            // Record key character set
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/3k2la/extra")]    // Extra segment
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
        var uri = AtUri.Create(repo, Nsid.Parse("app.bsky.feed.post"), RecordKey.Parse("3k2la"));

        Assert.Equal("at://did:plc:abc123/app.bsky.feed.post/3k2la", uri.Value);
        Assert.Equal("did:plc:abc123", uri.Authority);
        Assert.Equal("app.bsky.feed.post", uri.Collection?.Value);
        Assert.Equal("3k2la", uri.RecordKey?.Value);
        Assert.Equal(AtUri.Parse(uri.Value), uri);
    }

    [Fact]
    public void Create_RepoOnly_ProducesAuthorityUri()
    {
        var uri = AtUri.Create(Handle.Parse("alice.bsky.social"));

        Assert.Equal("at://alice.bsky.social", uri.Value);
        Assert.Null(uri.Collection);
    }

    [Fact]
    public void Create_RkeyWithoutCollection_Throws()
    {
        var repo = AtIdentifier.Parse("did:plc:abc123");

        Assert.Throws<ArgumentException>(() => AtUri.Create(repo, rkey: RecordKey.Self));
    }

    [Fact]
    public void Create_WithoutRkey_ProducesValidUri()
    {
        var repo = AtIdentifier.Parse("did:plc:abc123");
        var uri = AtUri.Create(repo, Nsid.Parse("app.bsky.feed.post"));

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

    [Fact]
    public void ExplicitCast_FromString_Parses()
    {
        var uri = (AtUri)"at://did:plc:abc123/app.bsky.feed.post/3k2la";

        Assert.Equal("3k2la", uri.RecordKey?.Value);
        Assert.ThrowsAny<ArgumentException>(() => (AtUri)"at://did:plc:abc123/");
    }

    [Fact]
    public void CompareTo_IsOrdinal()
    {
        var a = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.like/3k2la");
        var b = AtUri.Parse("at://did:plc:abc123/app.bsky.feed.post/3k2la");

        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
        Assert.Equal(0, a.CompareTo(AtUri.Parse(a.Value)));
    }
}
