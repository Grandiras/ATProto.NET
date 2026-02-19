using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

public class NsidTests
{
    [Theory]
    [InlineData("com.atproto.repo.createRecord")]
    [InlineData("app.bsky.feed.post")]
    [InlineData("app.bsky.actor.getProfile")]
    [InlineData("io.example.myLexicon")]
    public void Parse_ValidNsid_Succeeds(string value)
    {
        var nsid = Nsid.Parse(value);
        Assert.Equal(value, nsid.Value);
    }

    [Fact]
    public void Parse_ExtractsAuthorityAndName()
    {
        var nsid = Nsid.Parse("com.atproto.repo.createRecord");
        Assert.Equal("com.atproto.repo", nsid.Authority);
        Assert.Equal("createRecord", nsid.Name);
    }

    [Fact]
    public void Parse_ExtractsSegments()
    {
        var nsid = Nsid.Parse("com.atproto.repo.createRecord");
        Assert.Equal(["com", "atproto", "repo", "createRecord"], nsid.Segments);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("single")]           // Only one segment
    [InlineData("two.segments")]     // Only two segments
    [InlineData("com.example.123")]  // Name starts with digit
    public void Parse_InvalidNsid_Throws(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => Nsid.Parse(value));
    }

    [Fact]
    public void Parse_NullValue_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => Nsid.Parse(null!));
    }

    [Theory]
    [InlineData("com.atproto.repo.createRecord", true)]
    [InlineData("invalid", false)]
    [InlineData(null, false)]
    public void TryParse_ReturnsExpected(string? value, bool expected)
    {
        var result = Nsid.TryParse(value, out var nsid);
        Assert.Equal(expected, result);

        if (expected)
            Assert.NotNull(nsid);
        else
            Assert.Null(nsid);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var a = Nsid.Parse("com.atproto.repo.createRecord");
        var b = Nsid.Parse("com.atproto.repo.createRecord");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        var a = Nsid.Parse("com.atproto.repo.createRecord");
        var b = Nsid.Parse("com.atproto.repo.deleteRecord");

        Assert.NotEqual(a, b);
    }
}
