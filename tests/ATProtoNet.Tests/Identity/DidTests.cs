using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

public class DidTests
{
    [Theory]
    [InlineData("did:plc:z72i7hdynmk6r22z27h6tvur")]
    [InlineData("did:web:example.com")]
    [InlineData("did:plc:abcdefg")]
    [InlineData("did:key:z6MkhaXgBZDvotDkL5257faiztiGiC2QtKLGpbnnEGta2doK")]
    public void Parse_ValidDid_Succeeds(string value)
    {
        var did = Did.Parse(value);
        Assert.Equal(value, did.Value);
        Assert.Equal(value, did.ToString());
        Assert.Equal(value, (string)did);
    }

    [Fact]
    public void Parse_ExtractsMethodAndSpecificId()
    {
        var did = Did.Parse("did:plc:z72i7hdynmk6r22z27h6tvur");
        Assert.Equal("plc", did.Method);
        Assert.Equal("z72i7hdynmk6r22z27h6tvur", did.MethodSpecificId);
    }

    [Fact]
    public void Parse_WebMethod_ExtractsCorrectly()
    {
        var did = Did.Parse("did:web:example.com");
        Assert.Equal("web", did.Method);
        Assert.Equal("example.com", did.MethodSpecificId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-did")]
    [InlineData("did:")]
    [InlineData("did:plc:")]    // Trailing colon makes method-specific-id empty
    [InlineData("DID:plc:abc")] // Must start lowercase
    [InlineData("did:PLC:abc")] // Method must be lowercase
    public void Parse_InvalidDid_Throws(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => Did.Parse(value));
    }

    [Fact]
    public void Parse_NullValue_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => Did.Parse(null!));
    }

    [Fact]
    public void Parse_ExceedsMaxLength_Throws()
    {
        var longDid = "did:plc:" + new string('a', 2050);
        Assert.Throws<ArgumentException>(() => Did.Parse(longDid));
    }

    [Theory]
    [InlineData("did:plc:abc123", true)]
    [InlineData("did:web:example.com", true)]
    [InlineData("not-a-did", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_ReturnsExpected(string? value, bool expected)
    {
        var result = Did.TryParse(value, out var did);
        Assert.Equal(expected, result);

        if (expected)
            Assert.NotNull(did);
        else
            Assert.Null(did);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var a = Did.Parse("did:plc:abc123");
        var b = Did.Parse("did:plc:abc123");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        var a = Did.Parse("did:plc:abc123");
        var b = Did.Parse("did:web:example.com");

        Assert.NotEqual(a, b);
        Assert.True(a != b);
        Assert.False(a == b);
    }

    [Fact]
    public void CompareTo_Orders_Correctly()
    {
        var a = Did.Parse("did:plc:aaa");
        var b = Did.Parse("did:plc:bbb");

        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
        Assert.Equal(0, a.CompareTo(Did.Parse("did:plc:aaa")));
    }

    [Fact]
    public void ExplicitCast_FromString_Works()
    {
        var did = (Did)"did:plc:abc123";
        Assert.Equal("did:plc:abc123", did.Value);
    }

    [Fact]
    public void ImplicitCast_ToString_Works()
    {
        var did = Did.Parse("did:plc:abc123");
        string value = did;
        Assert.Equal("did:plc:abc123", value);
    }
}
