using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// What <see cref="Nsid"/> adds to the shared identifier contract (<see cref="IdentifierContractTests"/>)
/// and the interop syntax fixtures (<see cref="SyntaxInteropTests"/>).
/// </summary>
public class NsidTests
{
    [Fact]
    public void Parse_ExtractsAuthorityNameAndSegments()
    {
        var nsid = Nsid.Parse("com.atproto.repo.createRecord");

        Assert.Equal("com.atproto.repo", nsid.Authority);
        Assert.Equal("createRecord", nsid.Name);
        Assert.Equal(["com", "atproto", "repo", "createRecord"], nsid.Segments);
    }

    [Fact]
    public void Parse_NameLengthLimit_IsSixtyThreeCharacters()
    {
        // Regression: the name segment had no length limit.
        Assert.True(Nsid.TryParse("com.example." + new string('o', 63), out _));
        Assert.False(Nsid.TryParse("com.example." + new string('o', 64), out _));
    }
}
