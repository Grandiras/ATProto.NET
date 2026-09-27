using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// What <see cref="AtUri"/> adds to the shared identifier contract (<see cref="IdentifierContractTests"/>)
/// and the interop syntax fixtures (<see cref="SyntaxInteropTests"/>): its parts, the restricted
/// syntax it accepts, and <see cref="AtUri.Create"/>.
/// </summary>
public class AtUriTests
{
    [Theory]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/3k2la", "app.bsky.feed.post", "3k2la")]
    [InlineData("at://did:plc:abc123/app.bsky.feed.post", "app.bsky.feed.post", null)]
    [InlineData("at://did:plc:abc123", null, null)]
    public void Parse_ExtractsComponents(string value, string? collection, string? rkey)
    {
        var uri = AtUri.Parse(value);

        Assert.Equal("did:plc:abc123", uri.Authority);
        Assert.Equal(collection, uri.Collection?.Value);
        Assert.Equal(rkey, uri.RecordKey?.Value);
        Assert.True(uri.Repo.IsDid);
        Assert.Same(uri.Repo, uri.Repo);
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

    [Theory]
    [InlineData("at://name")]                                             // Authority is neither DID nor handle
    [InlineData("at://@alice.bsky.social")]                               // '@' is reserved for userinfo
    [InlineData("at://did:plc:abc123/")]                                  // Trailing slash
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/3k2la/")]
    [InlineData("at://did:plc:abc123#frag")]                              // Fragments and queries are unused
    [InlineData("at://did:plc:abc123/app.bsky.feed.post?x=1")]
    [InlineData("at://did:plc:abc123/app.bsky.feed_post")]                // Collection is not an NSID
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/a+b")]            // Record key character set
    [InlineData("at://did:plc:abc123/app.bsky.feed.post/3k2la/extra")]    // Extra segment
    public void Parse_OutsideTheRestrictedSyntax_Throws(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => AtUri.Parse(value));
    }

    [Theory]
    [InlineData(null, null, "at://did:plc:abc123")]
    [InlineData("app.bsky.feed.post", null, "at://did:plc:abc123/app.bsky.feed.post")]
    [InlineData("app.bsky.feed.post", "3k2la", "at://did:plc:abc123/app.bsky.feed.post/3k2la")]
    public void Create_FromComponents_ProducesTheUriParseReads(string? collection, string? rkey, string expected)
    {
        var uri = AtUri.Create(
            AtIdentifier.Parse("did:plc:abc123"),
            collection is null ? null : Nsid.Parse(collection),
            rkey is null ? null : RecordKey.Parse(rkey));

        Assert.Equal(expected, uri.Value);
        Assert.Equal(AtUri.Parse(expected), uri);
        Assert.Equal(rkey, uri.RecordKey?.Value);
    }

    [Fact]
    public void Create_RkeyWithoutCollection_Throws()
    {
        Assert.Throws<ArgumentException>(() => AtUri.Create(AtIdentifier.Parse("did:plc:abc123"), rkey: RecordKey.Self));
    }
}
