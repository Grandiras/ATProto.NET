namespace ATProtoNet.Tests.Syntax;

public class UriFormatTests
{
    [Theory]
    [InlineData("https://example.com/path?q=blah&yes=true#frag.123")]
    [InlineData("dns:example.com")]
    [InlineData("at://handle.example.com/nsid/rkey")]
    [InlineData("did:key:zQ3shZc2QzApp2oymGvQbzP8eKheVshBHbU4ZYjeXqwSKEn6N")]
    [InlineData("microsoft.windows.camera:thing")]
    [InlineData("go://?Mercedes%20Benz")]
    [InlineData("a+b-c.d:x")]
    [Trait("Spec", "SYN-40")]
    public void IsValid_UriWithAScheme_Accepts(string value) => Assert.True(UriFormat.IsValid(value));

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("://example.com")]
    [InlineData("//example.com")]
    [InlineData("http:")]
    [InlineData(".http://example.com")]
    [InlineData("1http://example.com")]
    [InlineData("ht_tp://example.com")]
    [InlineData("https://example.com/path gap")]
    [InlineData(" https://example.com")]
    [InlineData("https://example.com\t")]
    [InlineData("https://exämple.com")]
    [Trait("Spec", "SYN-40")]
    public void IsValid_NoSchemeEmptyRemainderOrWhitespace_Refuses(string value) => Assert.False(UriFormat.IsValid(value));

    [Fact]
    [Trait("Spec", "SYN-40"), Trait("Spec", "SYN-9")]
    public void IsValid_LengthLimit_IsEightKibibytes()
    {
        var prefix = "https://example.com/";

        Assert.True(UriFormat.IsValid(prefix + new string('x', UriFormat.MaxLength - prefix.Length)));
        Assert.False(UriFormat.IsValid(prefix + new string('x', UriFormat.MaxLength - prefix.Length + 1)));
    }
}
