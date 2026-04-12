using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

public sealed class MstKeyDepthTests
{
    /// <summary>
    /// Known test vectors from the AT Protocol specification.
    /// </summary>
    [Theory]
    [InlineData("2653ae71", 0)]
    [InlineData("blue", 1)]
    [InlineData("app.bsky.feed.post/454397e440ec", 4)]
    [InlineData("app.bsky.feed.post/9adeb165882c", 8)]
    public void ComputeDepth_SpecVectors_MatchExpectedDepth(string key, int expectedDepth)
    {
        var depth = MstKeyDepth.ComputeDepth(key);
        Assert.Equal(expectedDepth, depth);
    }

    [Fact]
    public void ComputeDepth_EmptyKey_ReturnsDepth()
    {
        // Empty key should produce some depth based on SHA-256 of empty bytes
        var depth = MstKeyDepth.ComputeDepth(ReadOnlySpan<byte>.Empty);
        Assert.True(depth >= 0);
    }

    [Fact]
    public void ComputeDepth_StringOverload_MatchesByteOverload()
    {
        var key = "app.bsky.feed.post/abc123";
        var depthFromString = MstKeyDepth.ComputeDepth(key);
        var depthFromBytes = MstKeyDepth.ComputeDepth(System.Text.Encoding.UTF8.GetBytes(key));
        Assert.Equal(depthFromBytes, depthFromString);
    }

    [Fact]
    public void ComputeDepth_DifferentKeys_DifferentDepths()
    {
        // "blue" is depth 1, "2653ae71" is depth 0 — confirming they differ
        Assert.NotEqual(
            MstKeyDepth.ComputeDepth("blue"),
            MstKeyDepth.ComputeDepth("2653ae71"));
    }
}
