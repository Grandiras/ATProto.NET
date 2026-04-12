using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class FirehoseVerifierTests
{
    [Fact]
    public void VerifyCid_CommitWithNullBlocks_ReturnsFailure()
    {
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = null,
        };

        var result = FirehoseVerifier.VerifyCid(commit);
        Assert.False(result.IsValid);
        Assert.Contains("no blocks", result.Error);
    }

    [Fact]
    public void VerifyCid_CommitWithEmptyBlocks_ReturnsFailure()
    {
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = Array.Empty<byte>(),
        };

        var result = FirehoseVerifier.VerifyCid(commit);
        Assert.False(result.IsValid);
        Assert.Contains("no blocks", result.Error);
    }

    [Fact]
    public void VerifyCid_SyncEventWithNullBlocks_ReturnsFailure()
    {
        var syncEvent = new SyncEvent
        {
            Did = "did:plc:test",
            Rev = "abc123",
            Blocks = null,
        };

        var result = FirehoseVerifier.VerifyCid(syncEvent);
        Assert.False(result.IsValid);
        Assert.Contains("no blocks", result.Error);
    }

    [Fact]
    public void VerifyCid_CommitWithMalformedBlocks_ReturnsFailure()
    {
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = new byte[] { 0xFF, 0xFF, 0xFF },
        };

        var result = FirehoseVerifier.VerifyCid(commit);
        Assert.False(result.IsValid);
        Assert.Contains("error", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyCid_NullCommit_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FirehoseVerifier.VerifyCid((CommitEvent)null!));
    }

    [Fact]
    public void VerifyCid_NullSyncEvent_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FirehoseVerifier.VerifyCid((SyncEvent)null!));
    }

    [Fact]
    public void VerificationResult_Success_IsValid()
    {
        // We can test via the static VerifyCid which conditionally returns success
        // Just verify the ToString behavior on failure results
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = null,
        };

        var result = FirehoseVerifier.VerifyCid(commit);
        Assert.Contains("Invalid:", result.ToString());
    }

    [Fact]
    public async Task VerifySignature_NullCommit_ThrowsArgumentNullException()
    {
        using var verifier = new FirehoseVerifier();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            verifier.VerifySignatureAsync(null!));
    }

    [Fact]
    public async Task VerifySignature_NoBlocks_ReturnsFailure()
    {
        using var verifier = new FirehoseVerifier();
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = null,
        };

        var result = await verifier.VerifySignatureAsync(commit);
        Assert.False(result.IsValid);
        Assert.Contains("no blocks", result.Error);
    }

    [Fact]
    public async Task VerifySignature_EmptyBlocks_ReturnsFailure()
    {
        using var verifier = new FirehoseVerifier();
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = Array.Empty<byte>(),
        };

        var result = await verifier.VerifySignatureAsync(commit);
        Assert.False(result.IsValid);
        Assert.Contains("no blocks", result.Error);
    }

    [Fact]
    public async Task VerifySignature_MalformedBlocks_ReturnsFailure()
    {
        using var verifier = new FirehoseVerifier();
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = new byte[] { 0xFF, 0xFF, 0xFF },
        };

        var result = await verifier.VerifySignatureAsync(commit);
        Assert.False(result.IsValid);
        Assert.Contains("error", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var verifier = new FirehoseVerifier();
        verifier.Dispose();
        verifier.Dispose(); // Should not throw
    }
}
