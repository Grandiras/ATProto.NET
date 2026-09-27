using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// What <see cref="Handle"/> adds to the shared identifier contract (<see cref="IdentifierContractTests"/>)
/// and the interop syntax fixtures (<see cref="SyntaxInteropTests"/>): normalization.
/// </summary>
public class HandleTests
{
    [Theory]
    [InlineData("Alice.Bsky.Social")]
    [InlineData("@alice.bsky.social")]
    public void Parse_NormalizesCaseAndALeadingAt(string value)
    {
        Assert.Equal("alice.bsky.social", Handle.Parse(value).Value);
    }
}
