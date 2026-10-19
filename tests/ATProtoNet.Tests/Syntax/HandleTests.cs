namespace ATProtoNet.Tests.Syntax;

public class HandleTests
{
    [Theory]
    [InlineData("Alice.Bsky.Social")]
    [InlineData("@alice.bsky.social")]
    [InlineData("@ALICE.bsky.social")]
    [Trait("Spec", "SYN-16")]
    public void Parse_NormalizesCaseAndALeadingAt(string value)
    {
        Assert.Equal("alice.bsky.social", Handle.Parse(value).Value);
        Assert.Equal("alice.bsky.social", Handle.Parse(value.AsSpan()).Value);
        Assert.Equal("alice.bsky.social", Handle.Parse(System.Text.Encoding.UTF8.GetBytes(value)).Value);
    }

    [Fact]
    [Trait("Spec", "SYN-16")]
    public void Parse_OnlyOneLeadingAtIsStripped() => Assert.False(Handle.TryParse("@@alice.bsky.social", out _));

    [Theory]
    [InlineData("alice", false)]
    [InlineData("alice.0", false)]
    [InlineData("alice.b0", true)]
    [InlineData("-a.test", false)]
    [InlineData("a-.test", false)]
    [InlineData("a..test", false)]
    [InlineData("a_b.test", false)]
    [Trait("Spec", "SYN-15")]
    public void TryParse_Structure_FollowsTheLabelRules(string value, bool valid) => Assert.Equal(valid, Handle.TryParse(value, out _));

    [Fact]
    [Trait("Spec", "SYN-15"), Trait("Spec", "SYN-9")]
    public void TryParse_LengthLimits_AreSixtyThreePerLabelAndTwoFiftyThreeInTotal()
    {
        Assert.True(Handle.TryParse(new string('a', Handle.MaxLabelLength) + ".com", out _));
        Assert.False(Handle.TryParse(new string('a', Handle.MaxLabelLength + 1) + ".com", out _));
        var longest = string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 61));
        Assert.Equal(Handle.MaxLength, longest.Length);
        Assert.True(Handle.TryParse(longest, out _));
        Assert.False(Handle.TryParse(longest + "d", out _));
    }

    [Fact]
    [Trait("Spec", "SYN-17")]
    public void Invalid_IsHandleInvalid()
    {
        Assert.Equal("handle.invalid", Handle.Invalid.Value);
        Assert.True(Handle.Invalid.IsInvalid);
        Assert.True(Handle.Parse("Handle.Invalid").IsInvalid);
        Assert.False(Handle.Parse("alice.test").IsInvalid);
    }

    [Theory]
    [InlineData("laptop.local")]
    [InlineData("hidden.onion")]
    [InlineData("alice.test")]
    [InlineData("xn--ls8h.test")]
    [Trait("Spec", "SYN-18")]
    public void Parse_ReservedAndSpecialUseTlds_AreSyntacticallyValid(string value) => Assert.Equal(value, Handle.Parse(value).Value);
}
