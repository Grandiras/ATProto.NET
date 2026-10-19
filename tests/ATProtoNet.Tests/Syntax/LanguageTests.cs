namespace ATProtoNet.Tests.Syntax;

public class LanguageTests
{
    [Theory]
    [InlineData("ja")]
    [InlineData("pt-BR")]
    [InlineData("pt-br")]
    [InlineData("zh-Hant-TW")]
    [InlineData("zh-yue-HK")]
    [InlineData("sl-rozaj-biske")]
    [InlineData("en-u-co-phonebk-t-en-US-x-private")]
    [InlineData("x-whatever")]
    [InlineData("X-fr-CH")]
    [InlineData("i-klingon")]
    [InlineData("EN-GB-OED")]
    [InlineData("sgn-BE-NL")]
    [InlineData("de-1996")]
    [Trait("Spec", "SYN-49")]
    public void Parse_WellFormedTags_AreAccepted(string value) => Assert.Equal(value, Language.Parse(value).Value);

    [Theory]
    [InlineData("")]
    [InlineData("e")]
    [InlineData("en_US")]
    [InlineData("en-")]
    [InlineData("-en")]
    [InlineData("en--US")]
    [InlineData("en-US-")]
    [InlineData("en-123456789")]
    [InlineData("x")]
    [InlineData("x-")]
    [InlineData("en-x")]
    [InlineData("en-a")]
    [InlineData("en-a-x-foo")]
    [InlineData("en-abc-def-ghi-jkl")]
    [InlineData("de-DE-1901-1901")]
    [InlineData("en-rozaj-ROZAJ")]
    [InlineData("en-a-foo-a-bar")]
    [InlineData("en-U-co-phonebk-u-ca-buddhist")]
    [InlineData("en-GB-US")]
    [InlineData("é")]
    [Trait("Spec", "SYN-49"), Trait("Spec", "SYN-1")]
    public void TryParse_MalformedOrRepeatedSubtags_AreRefused(string value)
    {
        Assert.False(Language.TryParse(value, out _));
        Assert.Throws<FormatException>(() => Language.Parse(value));
    }

    [Fact]
    [Trait("Spec", "SYN-49"), Trait("Spec", "SYN-9")]
    public void TryParse_LengthLimit_IsOneHundredTwentyEight()
    {
        var prefix = "x" + string.Concat(Enumerable.Repeat("-abcdefgh", 13));
        var longest = prefix + "-abcd-abcd";

        Assert.Equal(Language.MaxLength, longest.Length);
        Assert.True(Language.TryParse(longest, out _));
        Assert.False(Language.TryParse(longest + "e", out _));
    }

    [Fact]
    [Trait("Spec", "SYN-50")]
    public void Equality_IsOrdinalAndCaseInsensitive_ButTheTextIsKept()
    {
        var a = Language.Parse("pt-BR");
        var b = Language.Parse("pt-br");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(0, a.CompareTo(b));
        Assert.Equal("pt-br", b.Value);
        Assert.NotEqual(a, Language.Parse("pt-PT"));
    }
}
