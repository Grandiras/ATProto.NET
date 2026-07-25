using System.Text;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Repo;

public sealed class RepoCommitTests
{
    private const string TestDid = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    private static byte[] DataCid(string label) =>
        CidComputation.ComputeBinaryForDagCbor(Encoding.UTF8.GetBytes(label));

    private static RepoCommit NewCommit(string rev = "3ku2ipumwvw2a") => new()
    {
        Did = TestDid,
        Data = DataCid("mst-root"),
        Rev = rev,
    };

    // ── Signing ──────────────────────────────────────────────

    [Fact]
    public void Sign_ProducesCommitThatVerifiesWithItsOwnKey()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = NewCommit().Sign(key);

        Assert.True(signed.Verify(key));
    }

    [Fact]
    public void Sign_CommitDoesNotVerifyWithADifferentKey()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var other = AtProtoCrypto.GenerateP256Key();

        Assert.False(NewCommit().Sign(key).Verify(other));
    }

    [Fact]
    public void Sign_SignatureIsOverTheUnsignedEncoding()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var commit = NewCommit();
        var signed = commit.Sign(key);

        Assert.True(key.Verify(commit.EncodeUnsigned(), signed.Signature));
    }

    [Fact]
    public void Sign_PopulatesCidFromTheSignedBytes()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = NewCommit().Sign(key);

        Assert.Equal(CidComputation.ComputeForDagCbor(signed.Bytes).Value, signed.Cid.Value);
        Assert.StartsWith("bafyrei", signed.Cid.Value);
    }

    // ── Interop with the firehose verifier ───────────────────

    [Fact]
    public void Sign_SignedViewExtractedByFirehoseVerifierMatchesTheUnsignedEncoding()
    {
        // This is the contract that makes a commit federatable: a relay recovers the signed
        // bytes by stripping `sig` from the encoded block, so that splice must reproduce
        // exactly what we signed.
        using var key = AtProtoCrypto.GenerateP256Key();
        var commit = NewCommit();
        var signed = commit.Sign(key);

        var view = FirehoseVerifier.ExtractSignedView(signed.Bytes);

        Assert.NotNull(view);
        Assert.Equal(commit.EncodeUnsigned(), view!.Value.UnsignedBytes);
        Assert.Equal(signed.Signature, view.Value.SigBytes);
        Assert.True(key.Verify(view.Value.UnsignedBytes, view.Value.SigBytes!));
    }

    [Fact]
    public void Sign_CommitBlockDecodesWithTheExpectedFields()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = NewCommit("3ku2ipumwvw2a").Sign(key);

        var json = DagCborDecoder.Decode(signed.Bytes);

        Assert.Equal(TestDid, json.GetProperty("did").GetString());
        Assert.Equal(3, json.GetProperty("version").GetInt32());
        Assert.Equal("3ku2ipumwvw2a", json.GetProperty("rev").GetString());
        Assert.Equal(
            CidComputation.EncodeCidToString(signed.Data),
            json.GetProperty("data").GetProperty("$link").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.GetProperty("prev").ValueKind);
    }

    [Fact]
    public void EncodeUnsigned_OmitsSigButKeepsEveryOtherField()
    {
        var json = DagCborDecoder.Decode(NewCommit().EncodeUnsigned());

        Assert.False(json.TryGetProperty("sig", out _));
        Assert.True(json.TryGetProperty("did", out _));
        Assert.True(json.TryGetProperty("data", out _));
        Assert.True(json.TryGetProperty("rev", out _));
        Assert.True(json.TryGetProperty("prev", out _));
        Assert.True(json.TryGetProperty("version", out _));
    }

    // ── Determinism ──────────────────────────────────────────

    [Fact]
    public void EncodeUnsigned_IsDeterministic()
    {
        Assert.Equal(NewCommit().EncodeUnsigned(), NewCommit().EncodeUnsigned());
    }

    [Fact]
    public void Sign_WithPrev_EncodesPrevAsCidLink()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var prev = DataCid("previous-commit");

        var signed = new RepoCommit
        {
            Did = TestDid,
            Data = DataCid("mst-root"),
            Rev = "3ku2ipumwvw2a",
            Prev = prev,
        }.Sign(key);

        var json = DagCborDecoder.Decode(signed.Bytes);
        Assert.Equal(
            CidComputation.EncodeCidToString(prev),
            json.GetProperty("prev").GetProperty("$link").GetString());
        Assert.True(signed.Verify(key));
    }

    [Fact]
    public void DataCid_MatchesTheBinaryRoot()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var signed = NewCommit().Sign(key);

        Assert.Equal(CidComputation.EncodeCidToString(signed.Data), signed.DataCid.Value);
    }

    [Fact]
    public void Sign_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => NewCommit().Sign(null!));
    }

}
