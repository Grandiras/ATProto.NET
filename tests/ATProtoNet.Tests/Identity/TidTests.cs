using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// What <see cref="Tid"/> adds to the shared identifier contract (<see cref="IdentifierContractTests"/>)
/// and the interop syntax fixtures (<see cref="SyntaxInteropTests"/>).
/// </summary>
public class TidTests
{
    [Theory]
    [InlineData("jzzzzzzzzzzzz", true)]  // Largest TID: the high bit stays clear
    [InlineData("kjzfcijpj2z2a", false)] // First character sets the high bit
    public void TryParse_HighBit_MustBeClear(string value, bool valid)
    {
        Assert.Equal(valid, Tid.TryParse(value, out _));
    }

    [Fact]
    public void Next_RapidCalls_StrictlyIncreasing()
    {
        // Regression: Next() used to add a random clock id to a millisecond timestamp per call,
        // so values minted within one millisecond came out of order and could repeat.
        var tids = Enumerable.Range(0, 20_000).Select(_ => Tid.Next()).ToList();

        for (var i = 1; i < tids.Count; i++)
            Assert.True(tids[i].CompareTo(tids[i - 1]) > 0, $"{tids[i]} did not follow {tids[i - 1]}");
        Assert.True(Tid.TryParse(tids[0].Value, out _));
        Assert.True(Tid.TryParse(Tid.NextString(), out _));
    }

    [Fact]
    public void FromInt64_ToInt64_RoundTrip()
    {
        var tid = Tid.Parse("3jzfcijpj2z2a");

        Assert.Equal(1_728_652_679_052_295_174, tid.ToInt64());
        Assert.Equal(tid, Tid.FromInt64(tid.ToInt64()));
    }
}
