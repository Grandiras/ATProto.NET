using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// What <see cref="RecordKey"/> adds to the shared identifier contract (<see cref="IdentifierContractTests"/>)
/// and the interop syntax fixtures (<see cref="SyntaxInteropTests"/>).
/// </summary>
public class RecordKeyTests
{
    [Theory]
    // Only A-Z a-z 0-9 . - _ : ~ are allowed (regression: these were accepted).
    [InlineData("@handle")]
    [InlineData("any+space")]
    [InlineData("number(3)")]
    [InlineData("dHJ1ZQ==")]
    [InlineData("a$b")]
    public void Parse_CharacterOutsideTheAllowedSet_Throws(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => RecordKey.Parse(value));
    }

    [Fact]
    public void NewTid_SuccessiveCalls_AreStrictlyIncreasingTids()
    {
        var keys = Enumerable.Range(0, 1_000).Select(_ => RecordKey.NewTid()).ToList();

        Assert.All(keys, key => Assert.True(Tid.TryParse(key.Value, out _)));
        for (var i = 1; i < keys.Count; i++)
            Assert.True(keys[i].CompareTo(keys[i - 1]) > 0);
    }
}
