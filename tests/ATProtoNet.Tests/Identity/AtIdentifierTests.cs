using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// What <see cref="AtIdentifier"/> adds to the shared identifier contract
/// (<see cref="IdentifierContractTests"/>) and the interop syntax fixtures
/// (<see cref="SyntaxInteropTests"/>): which of the two it holds.
/// </summary>
public class AtIdentifierTests
{
    [Theory]
    [InlineData("did:plc:abc123", true)]
    [InlineData("alice.bsky.social", false)]
    public void Parse_KnowsWhetherItIsADidOrAHandle(string value, bool isDid)
    {
        var id = AtIdentifier.Parse(value);

        Assert.Equal((isDid, !isDid), (id.IsDid, id.IsHandle));
        Assert.Equal(value, isDid ? id.Did!.Value : id.Handle!.Value);
        Assert.Equal(value, id.Value);
    }

    [Fact]
    public void FromDidAndFromHandle_WrapTheirIdentifier()
    {
        var did = Did.Parse("did:plc:abc123");
        var handle = Handle.Parse("alice.bsky.social");

        Assert.Equal(did, AtIdentifier.FromDid(did).Did);
        Assert.Equal(handle, AtIdentifier.FromHandle(handle).Handle);
    }
}
