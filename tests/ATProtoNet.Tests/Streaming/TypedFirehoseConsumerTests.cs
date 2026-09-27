using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Streaming;
using static ATProtoNet.Tests.Streaming.EventStreamFrames;

namespace ATProtoNet.Tests.Streaming;

public class TypedFirehoseConsumerTests
{
    private const string Relay = "wss://relay.test";

    private static TypedFirehoseConsumerOptions Options(
        IStreamCursorStore? store = null,
        int persistInterval = 100,
        int? maxReconnects = 0,
        IReadOnlySet<Nsid>? filter = null,
        bool verifyCids = false,
        List<DroppedStreamEvent>? dropped = null) => new()
    {
        ServiceUrl = Relay,
        CursorStore = store,
        CursorPersistInterval = persistInterval,
        Reconnect = StreamTestExtensions.Immediate(maxReconnects),
        CollectionFilter = filter,
        VerifyCids = verifyCids,
        OnEventDropped = dropped is null ? null : dropped.Add,
    };

    private static HashSet<Nsid> Posts => [Nsid.Parse("app.bsky.feed.post")];

    [Fact]
    public void Options_Defaults()
    {
        var options = new TypedFirehoseConsumerOptions { ServiceUrl = Relay };

        Assert.False(options.VerifyCids);
        Assert.Null(options.CollectionFilter);
        Assert.Null(options.CursorStore);
        Assert.Equal(100, options.CursorPersistInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Reconnect.InitialDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Reconnect.MaxDelay);
        Assert.Equal(10, options.Reconnect.MaxAttempts);
        Assert.Equal(Relay, options.ResolvedStreamId);
        Assert.Equal("custom", new TypedFirehoseConsumerOptions { ServiceUrl = Relay, StreamId = "custom" }.ResolvedStreamId);
    }

    [Fact]
    public void Constructor_RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentNullException>(() => new TypedFirehoseConsumer(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypedFirehoseConsumer(
            new TypedFirehoseConsumerOptions { ServiceUrl = Relay, CursorPersistInterval = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypedFirehoseConsumer(
            new TypedFirehoseConsumerOptions { ServiceUrl = Relay, Reconnect = new() { MaxAttempts = -1 } }));
    }

    [Fact]
    public async Task ConsumeAsync_ParsesEachFrameIntoATypedMessage()
    {
        var connector = new ScriptedConnector().Connection(
            Commit(1, paths: "app.bsky.feed.post/3jzfcijpj2z2a"),
            IdentityFrame(2),
            Info("OutdatedCursor"));
        var consumer = new TypedFirehoseConsumer(Options(), connector.Connect);

        var messages = await consumer.ConsumeAsync().DrainAsync();

        Assert.IsType<CommitEvent>(messages[0]);
        Assert.IsType<IdentityEvent>(messages[1]);
        Assert.Equal("OutdatedCursor", Assert.IsType<InfoEvent>(messages[2]).Name);
        Assert.Equal(2, consumer.LastSeq);
        Assert.Equal("wss://relay.test/xrpc/com.atproto.sync.subscribeRepos", connector.Endpoints[0].ToString());
    }

    [Fact]
    public async Task ConsumeAsync_CollectionFilter_SkippedCommitsStillAdvanceTheStoredCursor()
    {
        // A rarely matching filter must not leave the stored cursor at its last match, or a
        // restart replays everything since.
        var store = new InMemoryStreamCursorStore();
        var connector = new ScriptedConnector().Connection(
            Commit(1, paths: "app.bsky.feed.post/3jzfcijpj2z2a"),
            Commit(2, paths: "app.bsky.feed.like/3jzfcijpj2z2a"),
            Commit(3, paths: "app.bsky.graph.follow/3jzfcijpj2z2a"));
        var consumer = new TypedFirehoseConsumer(Options(store, filter: Posts), connector.Connect);

        var messages = await consumer.ConsumeAsync().DrainAsync();

        Assert.Equal([1L], messages.Cast<CommitEvent>().Select(c => c.Seq));
        Assert.Equal(3, await store.GetCursorAsync(Relay));
    }

    [Fact]
    public async Task ConsumeAsync_CollectionFilter_ReadsPathsBeforeParsing()
    {
        // The repo is not a DID, so parsing this commit would fail and report a drop. Filtered out
        // by its path first, it is never parsed.
        var dropped = new List<DroppedStreamEvent>();
        var connector = new ScriptedConnector().Connection(
            Commit(1, repo: "not-a-did", paths: "app.bsky.feed.like/3jzfcijpj2z2a"));
        var consumer = new TypedFirehoseConsumer(Options(filter: Posts, dropped: dropped), connector.Connect);

        var messages = await consumer.ConsumeAsync().DrainAsync();

        Assert.Empty(messages);
        Assert.Empty(dropped);
        Assert.Equal(1, consumer.LastSeq);
    }

    [Fact]
    public async Task ConsumeAsync_CollectionFilter_PassesACommitWithAnyMatchingOperation()
    {
        var connector = new ScriptedConnector().Connection(
            Commit(1, paths: ["app.bsky.feed.like/3jzfcijpj2z2a", "app.bsky.feed.post/3jzfcijpj2z2b"]),
            Commit(2));
        var consumer = new TypedFirehoseConsumer(Options(filter: Posts), connector.Connect);

        var messages = await consumer.ConsumeAsync().DrainAsync();

        // The second has no operations, which passes as it does unfiltered.
        Assert.Equal([1L, 2L], messages.Cast<CommitEvent>().Select(c => c.Seq));
    }

    [Fact]
    public async Task ConsumeAsync_ResumesFromTheStoredCursor_AndAnExplicitCursorWins()
    {
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync(Relay, 41, TestContext.Current.CancellationToken);
        var connector = new ScriptedConnector().Connection().Connection();

        await new TypedFirehoseConsumer(Options(store), connector.Connect).ConsumeAsync().DrainAsync();
        await new TypedFirehoseConsumer(Options(store), connector.Connect).ConsumeAsync(cursor: 0).DrainAsync();

        Assert.Equal(["41", "0"], connector.Cursors);
    }

    [Fact]
    public async Task ConsumeAsync_Reconnect_ResumesAfterTheLastEventAndSkipsReplays()
    {
        var connector = new ScriptedConnector()
            .Connection(IdentityFrame(1), IdentityFrame(2))
            .Connection(IdentityFrame(2), IdentityFrame(3));
        var consumer = new TypedFirehoseConsumer(Options(maxReconnects: 1), connector.Connect);

        var messages = await consumer.ConsumeAsync().DrainAsync();

        Assert.Equal([1L, 2L, 3L], messages.Cast<IdentityEvent>().Select(e => e.Seq));
        Assert.Equal([null, "2"], connector.Cursors.Take(2));
    }

    [Fact]
    public async Task ConsumeAsync_UnparseableIdentifier_IsReportedAndStillAdvancesTheCursor()
    {
        var dropped = new List<DroppedStreamEvent>();
        var connector = new ScriptedConnector().Connection(
            Commit(1, repo: "not-a-did", paths: "app.bsky.feed.post/3jzfcijpj2z2a"),
            Unknown("#somethingNew", 2));
        var consumer = new TypedFirehoseConsumer(Options(dropped: dropped), connector.Connect);

        var messages = await consumer.ConsumeAsync().DrainAsync();

        Assert.Empty(messages);
        Assert.Equal(
            [new DroppedStreamEvent(StreamDropReason.Malformed, 1, "#commit"),
             new DroppedStreamEvent(StreamDropReason.UnknownType, 2, "#somethingNew")],
            dropped);
        Assert.Equal(2, consumer.LastSeq);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 }, "do not verify")]
    [InlineData(new byte[0], "no blocks")]
    public async Task ConsumeAsync_VerifyCids_DropsACommitWhoseBlocksDoNotVerify(byte[] blocks, string reason)
    {
        var dropped = new List<DroppedStreamEvent>();
        var connector = new ScriptedConnector().Connection(
            Commit(1, blocks: blocks, paths: "app.bsky.feed.post/3jzfcijpj2z2a"),
            IdentityFrame(2));
        var consumer = new TypedFirehoseConsumer(Options(verifyCids: true, dropped: dropped), connector.Connect);

        Assert.IsType<IdentityEvent>(Assert.Single(await consumer.ConsumeAsync().DrainAsync()));
        Assert.Equal(StreamDropReason.VerificationFailed, Assert.Single(dropped).Reason);
        Assert.Equal(1, dropped[0].Cursor);
        Assert.Contains(reason, dropped[0].Detail);
    }

    [Fact]
    public async Task ConsumeAsync_NoVerification_DeliversUnverifiedCommits()
    {
        var connector = new ScriptedConnector().Connection(
            Commit(1, blocks: [1, 2, 3], paths: "app.bsky.feed.post/3jzfcijpj2z2a"));
        var consumer = new TypedFirehoseConsumer(Options(), connector.Connect);

        Assert.Single(await consumer.ConsumeAsync().DrainAsync());
    }
}
