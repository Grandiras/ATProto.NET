namespace ATProtoNet.Tests.Syntax;

public class DidTests
{
    [Theory]
    [InlineData("did:plc:z72i7hdynmk6r22z27h6tvur", "plc", "z72i7hdynmk6r22z27h6tvur")]
    [InlineData("did:web:example.com", "web", "example.com")]
    [InlineData("did:web:localhost%3A3000", "web", "localhost%3A3000")]
    [Trait("Spec", "SYN-14")]
    public void Parse_ExtractsMethodAndSpecificId(string value, string method, string specificId)
    {
        var did = Did.Parse(value);

        Assert.Equal((method, specificId), (did.Method, did.MethodSpecificId.ToString()));
    }

    [Theory]
    [InlineData("did:method:val%")]
    [InlineData("did:method:val:")]
    [Trait("Spec", "SYN-11"), Trait("Spec", "SYN-12")]
    public void Parse_EndingInPercentOrColon_Throws(string value) => Assert.Throws<FormatException>(() => Did.Parse(value));

    [Fact]
    [Trait("Spec", "SYN-12")]
    public void Parse_PercentSequencesInside_AreNotFurtherValidated() => Assert.Equal("did:web:a%zz.b", Did.Parse("did:web:a%zz.b").Value);

    [Fact]
    [Trait("Spec", "SYN-13")]
    public void Parse_UnsupportedMethodAndShortPlcId_ParseAnyway()
    {
        Assert.Equal("fancy", Did.Parse("did:fancy:x").Method);
        Assert.Equal("abc", Did.Parse("did:plc:abc").MethodSpecificId.ToString());
    }

    [Fact]
    [Trait("Spec", "SYN-14")]
    public void Parse_IsCaseSensitiveAndNeverNormalized()
    {
        Assert.Equal("did:method:VAL", Did.Parse("did:method:VAL").Value);
        Assert.NotEqual(Did.Parse("did:method:val"), Did.Parse("did:method:VAL"));
        Assert.False(Did.TryParse("DID:method:val", out _));
    }

    [Fact]
    [Trait("Spec", "SYN-11"), Trait("Spec", "SYN-9")]
    public void Parse_LengthLimits_AreSevenAndTwoThousandFortyEight()
    {
        Assert.True(Did.TryParse("did:a:b", out _));
        Assert.False(Did.TryParse("did:a:", out _));
        Assert.True(Did.TryParse("did:plc:" + new string('a', Did.MaxLength - 8), out _));
        Assert.False(Did.TryParse("did:plc:" + new string('a', Did.MaxLength - 7), out _));
    }

    [Fact]
    [Trait("Spec", "SYN-1")]
    public void Parse_NonAscii_Throws()
    {
        Assert.False(Did.TryParse("did:plc:é", out _));
        Assert.False(Did.TryParse("did:plc:é"u8, out _));
    }
}
