using System.Text;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Pds;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Pds;

/// <summary>
/// Frames written by the PDS must be readable by the SDK's own firehose consumer — that
/// round-trip is the closest thing to a conformance check without a live relay.
/// </summary>
public sealed class PdsFirehoseFrameTests
{
    private const string Did = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    private static byte[] Cid(string label) =>
        CidComputation.ComputeBinaryForDagCbor(Encoding.UTF8.GetBytes(label));

    private static byte[] EmptyCar() => CarWriter.Write(Cid("commit"), Array.Empty<CarBlock>());

    // ── #commit ──────────────────────────────────────────────

    [Fact]
    public void Commit_ParsesAsACommitEvent()
    {
        var commitCid = Cid("commit");
        var recordCid = Cid("record");

        var frame = PdsFirehoseFrame.Commit(
            42, DateTimeOffset.UtcNow, Did, commitCid, "3ku2ipumwvw2a", "3ku2ipumwvw29",
            EmptyCar(), [PdsRepoOp.Create("app.bsky.feed.post/abc", recordCid)], prevData: null);

        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(frame));

        Assert.Equal(42, parsed.Seq);
        Assert.Equal(Did, parsed.Repo);
        Assert.Equal("3ku2ipumwvw2a", parsed.Rev);
        Assert.Equal("3ku2ipumwvw29", parsed.Since);
        Assert.Equal(CidComputation.EncodeCidToString(commitCid), parsed.Commit);
        Assert.False(parsed.TooBig);
        Assert.False(parsed.Rebase);
    }

    [Fact]
    public void Commit_RoundTripsOperations()
    {
        var recordCid = Cid("record");
        var previousCid = Cid("previous");

        var frame = PdsFirehoseFrame.Commit(
            1, DateTimeOffset.UtcNow, Did, Cid("commit"), "3ku2ipumwvw2a", null, EmptyCar(),
            [
                PdsRepoOp.Create("app.bsky.feed.post/aaa", recordCid),
                PdsRepoOp.Update("app.bsky.feed.post/bbb", recordCid, previousCid),
                PdsRepoOp.Delete("app.bsky.feed.post/ccc", previousCid),
            ],
            prevData: null);

        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(frame));
        var ops = parsed.Ops!;

        Assert.Equal(3, ops.Count);

        Assert.Equal("create", ops[0].Action);
        Assert.Equal("app.bsky.feed.post/aaa", ops[0].Path);
        Assert.Equal(CidComputation.EncodeCidToString(recordCid), ops[0].Cid);
        Assert.Null(ops[0].Prev);

        Assert.Equal("update", ops[1].Action);
        Assert.Equal(CidComputation.EncodeCidToString(previousCid), ops[1].Prev);

        Assert.Equal("delete", ops[2].Action);
        Assert.Null(ops[2].Cid);
        Assert.Equal(CidComputation.EncodeCidToString(previousCid), ops[2].Prev);
    }

    [Fact]
    public void Commit_FirstCommit_HasNullSince()
    {
        var frame = PdsFirehoseFrame.Commit(
            1, DateTimeOffset.UtcNow, Did, Cid("commit"), "3ku2ipumwvw2a", null,
            EmptyCar(), [], prevData: null);

        Assert.Null(Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(frame)).Since);
    }

    [Fact]
    public void Commit_WithPrevData_IncludesTheInductiveField()
    {
        var prevData = Cid("previous-mst-root");

        var frame = PdsFirehoseFrame.Commit(
            1, DateTimeOffset.UtcNow, Did, Cid("commit"), "3ku2ipumwvw2a", "3ku2ipumwvw29",
            EmptyCar(), [], prevData);

        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(frame));
        Assert.Equal(CidComputation.EncodeCidToString(prevData), parsed.PrevData);
    }

    [Fact]
    public void Commit_TooBig_OmitsBlocksButKeepsMetadata()
    {
        var frame = PdsFirehoseFrame.Commit(
            7, DateTimeOffset.UtcNow, Did, Cid("commit"), "3ku2ipumwvw2a", null,
            EmptyCar(), [], prevData: null, tooBig: true);

        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(frame));

        Assert.True(parsed.TooBig);
        Assert.Empty(parsed.Blocks!);
        Assert.Equal("3ku2ipumwvw2a", parsed.Rev);
    }

    [Fact]
    public void Commit_BlocksSurviveAsAReadableCar()
    {
        var blockData = Encoding.UTF8.GetBytes("block-contents");
        var blockCid = CidComputation.ComputeBinaryForDagCbor(blockData);
        var car = CarWriter.Write(blockCid, new[] { new CarBlock(blockCid, blockData) });

        var frame = PdsFirehoseFrame.Commit(
            1, DateTimeOffset.UtcNow, Did, blockCid, "3ku2ipumwvw2a", null, car, [], prevData: null);

        var parsed = Assert.IsType<CommitEvent>(FirehoseEventParser.Parse(frame));
        var reader = CarReader.FromBytes(parsed.Blocks!, verifyBlockCids: true);

        Assert.Single(reader.Blocks);
        Assert.Equal(blockData, reader.Blocks[0].Data);
    }

    // ── #identity / #account / #sync / #info ─────────────────

    [Fact]
    public void Identity_ParsesAsAnIdentityEvent()
    {
        var frame = PdsFirehoseFrame.Identity(9, DateTimeOffset.UtcNow, Did, "alice.example.com");
        var parsed = Assert.IsType<IdentityEvent>(FirehoseEventParser.Parse(frame));

        Assert.Equal(9, parsed.Seq);
        Assert.Equal(Did, parsed.Did);
        Assert.Equal("alice.example.com", parsed.Handle);
    }

    [Fact]
    public void Identity_WithoutHandle_OmitsTheField()
    {
        var frame = PdsFirehoseFrame.Identity(9, DateTimeOffset.UtcNow, Did, null);
        Assert.Null(Assert.IsType<IdentityEvent>(FirehoseEventParser.Parse(frame)).Handle);
    }

    [Fact]
    public void Account_ActiveAccount_ParsesWithoutAStatus()
    {
        var frame = PdsFirehoseFrame.Account(3, DateTimeOffset.UtcNow, Did, active: true, status: null);
        var parsed = Assert.IsType<AccountEvent>(FirehoseEventParser.Parse(frame));

        Assert.True(parsed.Active);
        Assert.Null(parsed.Status);
    }

    [Fact]
    public void Account_InactiveAccount_CarriesTheStatus()
    {
        var frame = PdsFirehoseFrame.Account(3, DateTimeOffset.UtcNow, Did, active: false, status: "deleted");
        var parsed = Assert.IsType<AccountEvent>(FirehoseEventParser.Parse(frame));

        Assert.False(parsed.Active);
        Assert.Equal("deleted", parsed.Status);
    }

    [Fact]
    public void Sync_ParsesAsASyncEvent()
    {
        var frame = PdsFirehoseFrame.Sync(11, DateTimeOffset.UtcNow, Did, EmptyCar(), "3ku2ipumwvw2a");
        var parsed = Assert.IsType<SyncEvent>(FirehoseEventParser.Parse(frame));

        Assert.Equal(11, parsed.Seq);
        Assert.Equal(Did, parsed.Did);
        Assert.Equal("3ku2ipumwvw2a", parsed.Rev);
    }

    [Fact]
    public void Info_ParsesAsAnInfoEvent()
    {
        var frame = PdsFirehoseFrame.Info("OutdatedCursor", "too old");
        var parsed = Assert.IsType<InfoEvent>(FirehoseEventParser.Parse(frame));

        Assert.Equal("OutdatedCursor", parsed.Name);
        Assert.Equal("too old", parsed.Message);
    }

    [Fact]
    public void Error_IsNotParsedAsARegularEvent()
    {
        // op: -1 frames terminate the stream; the parser deliberately returns null for them.
        Assert.Null(FirehoseEventParser.Parse(PdsFirehoseFrame.Error("FutureCursor", "ahead")));
    }

    // ── Timestamps ───────────────────────────────────────────

    [Fact]
    public void FormatTime_IsUtcIso8601WithMilliseconds()
    {
        var formatted = PdsFirehoseFrame.FormatTime(
            new DateTimeOffset(2026, 7, 25, 12, 30, 45, 123, TimeSpan.FromHours(2)));

        Assert.Equal("2026-07-25T10:30:45.123Z", formatted);
    }
}
