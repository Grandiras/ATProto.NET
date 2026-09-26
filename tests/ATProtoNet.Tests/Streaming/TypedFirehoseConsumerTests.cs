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
        FirehoseVerifier? verifier = null,
        List<DroppedStreamEvent>? dropped = null,
        List<EventStreamError>? errors = null) => new()
    {
        ServiceUrl = Relay,
        CursorStore = store,
        CursorPersistInterval = persistInterval,
        Reconnect = StreamTestExtensions.Immediate(maxReconnects),
        CollectionFilter = filter,
        VerifyCids = verifyCids,
        Verifier = verifier,
        OnEventDropped = dropped is null ? null : dropped.Add,
        OnStreamError = errors is null ? null : errors.Add,
    };

    private static HashSet<Nsid> Posts => [Nsid.Parse("app.bsky.feed.post")];

    [Fact]
    public void Options_Defaults()
    {
        var options = new TypedFirehoseConsumerOptions { ServiceUrl = Relay };

        Assert.False(options.VerifyCids);
        Assert.Null(options.CollectionFilter);
        Assert.Null(options.CursorStore);
        Assert.Null(options.Verifier);
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
    public async Task ConsumeAsync_Break_SavesTheCursorOfTheLastHandledEvent()
    {
        // The final save runs in finally, so leaving the loop early keeps the position. The event
        // being handled when the caller broke out is not recorded: at-least-once.
        var store = new InMemoryStreamCursorStore();
        var connector = new ScriptedConnector().Connection(IdentityFrame(10), IdentityFrame(11), IdentityFrame(12));
        var consumer = new TypedFirehoseConsumer(Options(store), connector.Connect);

        await foreach (var message in consumer.ConsumeAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            if (((IdentityEvent)message).Seq == 11)
                break;
        }

        Assert.Equal(10, await store.GetCursorAsync(Relay));
    }

    [Fact]
    public async Task ConsumeAsync_Cancellation_EndsNormallyAndSavesTheCursor()
    {
        var store = new InMemoryStreamCursorStore();
        var connector = new ScriptedConnector().Connection(IdentityFrame(10), IdentityFrame(11), IdentityFrame(12));
        var consumer = new TypedFirehoseConsumer(Options(store, maxReconnects: null), connector.Connect);
        using var cts = new CancellationTokenSource();

        var seen = new List<long>();
        await foreach (var message in consumer.ConsumeAsync(cancellationToken: cts.Token))
        {
            seen.Add(((IdentityEvent)message).Seq);
            if (seen.Count == 2)
                cts.Cancel();
        }

        Assert.Equal([10L, 11L], seen);
        Assert.Equal(11, await store.GetCursorAsync(Relay));
    }

    [Fact]
    public async Task ConsumeAsync_ResumesFromTheStoredCursor_AndAnExplicitCursorWins()
    {
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync(Relay, 41, TestContext.Current.CancellationToken);
        var connector = new ScriptedConnector().Connection().Connection();

        await new TypedFirehoseConsumer(Options(store), connector.Connect).ConsumeAsync().DrainAsync();
        await new TypedFirehoseConsumer(Options(store), connector.Connect).ConsumeAsync(cursor: 7).DrainAsync();

        Assert.Equal(["41", "7"], connector.Cursors);
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
    public async Task ConsumeAsync_ReconnectAttemptsExhausted_Throws()
    {
        var failure = new System.Net.WebSockets.WebSocketException("reset");
        var connector = new ScriptedConnector()
            .Connection(IdentityFrame(1))
            .Failing(failure)
            .Failing(failure);
        var consumer = new TypedFirehoseConsumer(Options(maxReconnects: 2), connector.Connect);

        var ex = await Assert.ThrowsAsync<EventStreamException>(async () =>
        {
            await foreach (var _ in consumer.ConsumeAsync(cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Same(failure, ex.InnerException);
        Assert.Equal(3, connector.Endpoints.Count);
    }

    [Fact]
    public async Task ConsumeAsync_FutureCursorErrorFrame_ThrowsAndIsReported()
    {
        var errors = new List<EventStreamError>();
        var connector = new ScriptedConnector().Connection(Error("FutureCursor", "cursor is ahead"));
        var consumer = new TypedFirehoseConsumer(Options(maxReconnects: null, errors: errors), connector.Connect);

        var ex = await Assert.ThrowsAsync<EventStreamException>(() => consumer.ConsumeAsync(cursor: 999).DrainAsync());

        Assert.Equal(EventStreamErrors.FutureCursor, ex.Error);
        Assert.False(ex.IsRetryable);
        Assert.Equal([new EventStreamError("FutureCursor", "cursor is ahead")], errors);
        Assert.Single(connector.Endpoints);
    }

    [Fact]
    public async Task ConsumeAsync_ConsumerTooSlowErrorFrame_ReconnectsFromTheLastCursor()
    {
        var errors = new List<EventStreamError>();
        var connector = new ScriptedConnector()
            .Connection(IdentityFrame(5), Error("ConsumerTooSlow"))
            .Connection(IdentityFrame(6));
        var consumer = new TypedFirehoseConsumer(Options(maxReconnects: 1, errors: errors), connector.Connect);

        var messages = await consumer.ConsumeAsync().DrainAsync();

        Assert.Equal([5L, 6L], messages.Cast<IdentityEvent>().Select(e => e.Seq));
        Assert.Equal("ConsumerTooSlow", Assert.Single(errors).Error);
        Assert.Equal("5", connector.Cursors.ElementAt(1));
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

    [Fact]
    public async Task ConsumeAsync_WithAVerifier_DropsACommitThatFailsVerification()
    {
        // A verifier verifies exactly when it is set: the blocks here are not a CAR, so the commit
        // cannot verify and must not be delivered.
        var dropped = new List<DroppedStreamEvent>();
        using var verifier = new FirehoseVerifier(new UnreachableResolver());
        var connector = new ScriptedConnector().Connection(
            Commit(1, blocks: [1, 2, 3], paths: "app.bsky.feed.post/3jzfcijpj2z2a"),
            IdentityFrame(2));
        var consumer = new TypedFirehoseConsumer(Options(verifier: verifier, dropped: dropped), connector.Connect);

        var messages = await consumer.ConsumeAsync().DrainAsync();

        Assert.IsType<IdentityEvent>(Assert.Single(messages));
        Assert.Equal(StreamDropReason.VerificationFailed, Assert.Single(dropped).Reason);
        Assert.Equal(1, dropped[0].Cursor);
    }

    [Fact]
    public async Task ConsumeAsync_VerifyCids_DropsACommitWhoseBlocksDoNotVerify()
    {
        var dropped = new List<DroppedStreamEvent>();
        var connector = new ScriptedConnector().Connection(
            Commit(1, blocks: [1, 2, 3], paths: "app.bsky.feed.post/3jzfcijpj2z2a"));
        var consumer = new TypedFirehoseConsumer(Options(verifyCids: true, dropped: dropped), connector.Connect);

        Assert.Empty(await consumer.ConsumeAsync().DrainAsync());
        Assert.Equal(StreamDropReason.VerificationFailed, Assert.Single(dropped).Reason);
    }

    [Fact]
    public async Task ConsumeAsync_NoVerification_DeliversUnverifiedCommits()
    {
        var connector = new ScriptedConnector().Connection(
            Commit(1, blocks: [1, 2, 3], paths: "app.bsky.feed.post/3jzfcijpj2z2a"));
        var consumer = new TypedFirehoseConsumer(Options(), connector.Connect);

        Assert.Single(await consumer.ConsumeAsync().DrainAsync());
    }

    private sealed class UnreachableResolver : IDidResolver
    {
        public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The CID check fails first.");

        public Task<DidDocument> RefreshAsync(Did did, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The CID check fails first.");

        public Task InvalidateAsync(Did did, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
