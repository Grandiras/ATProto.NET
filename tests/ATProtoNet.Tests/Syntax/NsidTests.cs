namespace ATProtoNet.Tests.Syntax;

public class NsidTests
{
    [Fact]
    [Trait("Spec", "SYN-24")]
    public void Parse_ExtractsAuthorityAndName()
    {
        var nsid = Nsid.Parse("com.atproto.repo.createRecord");

        Assert.Equal("com.atproto.repo", nsid.Authority);
        Assert.Equal("createRecord", nsid.Name);
        Assert.Same(nsid.Authority, nsid.Authority);
        Assert.Same(nsid.Name, nsid.Name);
    }

    [Fact]
    [Trait("Spec", "SYN-22"), Trait("Spec", "SYN-9")]
    public void Parse_NameLengthLimit_IsSixtyThreeCharacters()
    {
        Assert.True(Nsid.TryParse("com.example." + new string('o', Nsid.MaxNameLength), out _));
        Assert.False(Nsid.TryParse("com.example." + new string('o', Nsid.MaxNameLength + 1), out _));
    }

    [Theory]
    [InlineData("com.example.3post", false)]
    [InlineData("com.example.po-st", false)]
    [InlineData("com.example.", false)]
    [InlineData("3com.example.post", false)]
    [InlineData("com.3example.post", true)]
    [InlineData("com.ex-ample.post", true)]
    [InlineData("com.-example.post", false)]
    [InlineData("com.example", false)]
    [Trait("Spec", "SYN-21"), Trait("Spec", "SYN-22")]
    public void TryParse_Structure_FollowsTheSegmentRules(string value, bool valid) => Assert.Equal(valid, Nsid.TryParse(value, out _));

    [Fact]
    [Trait("Spec", "SYN-21"), Trait("Spec", "SYN-9")]
    public void TryParse_TotalLength_IsThreeHundredSeventeen()
    {
        var longest = string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 63), new string('e', 61));
        Assert.Equal(Nsid.MaxLength, longest.Length);
        Assert.True(Nsid.TryParse(longest, out _));
        Assert.False(Nsid.TryParse(longest + "e", out _));
    }

    [Fact]
    [Trait("Spec", "SYN-23")]
    public void Equality_IsOrdinalOnTheTextAsWritten()
    {
        Assert.Equal("App.Bsky.Feed.Post", Nsid.Parse("App.Bsky.Feed.Post").Value);
        Assert.NotEqual(Nsid.Parse("app.bsky.feed.post"), Nsid.Parse("App.Bsky.Feed.Post"));
    }
}
