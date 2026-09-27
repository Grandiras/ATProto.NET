using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// What <see cref="Did"/> adds to the shared identifier contract (<see cref="IdentifierContractTests"/>)
/// and the interop syntax fixtures (<see cref="SyntaxInteropTests"/>).
/// </summary>
public class DidTests
{
    [Theory]
    [InlineData("did:plc:z72i7hdynmk6r22z27h6tvur", "plc", "z72i7hdynmk6r22z27h6tvur")]
    [InlineData("did:web:example.com", "web", "example.com")]
    public void Parse_ExtractsMethodAndSpecificId(string value, string method, string specificId)
    {
        var did = Did.Parse(value);

        Assert.Equal((method, specificId), (did.Method, did.MethodSpecificId));
    }

    [Fact]
    public void Parse_EndingInPercent_Throws()
    {
        // Regression: accepted before. A DID may not end in '%'.
        Assert.ThrowsAny<ArgumentException>(() => Did.Parse("did:method:val%"));
    }
}
