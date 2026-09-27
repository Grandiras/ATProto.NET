using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Identity;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class JetstreamV2ConsumerTests
{
    private const string TestDid = "did:plc:eygmaihciaxprqvxpfvl6flk";
    private const string StreamId = "wss://jetstream.test";

    /// <summary>A v2 commit event: identified by its sequence number, not its timestamp.</summary>
    private static JetstreamCommitEvent Commit(long seq, long? timeUs = null) => new()
    {
        Did = Did.Parse(TestDid),
        TimeUs = timeUs ?? 1_725_911_162_000_000 + seq,
        Cursor = seq,
        Collection = Nsid.Parse("app.bsky.feed.post"),
        Rkey = RecordKey.Parse("3l3qo2vuowo2b"),
        Operation = RepoOpAction.Create,
    };

    private static JetstreamConsumerOptions Options(
        IStreamCursorStore? store = null,
        int persistInterval = 100,
        int maxReconnects = 0,
        string serviceUrl = StreamId,
        Action<EventStreamError>? onStreamError = null,
        Action<JetstreamInfo>? onInfo = null,
        Action<DroppedStreamEvent>? onDropped = null,
        IJetstreamDecompressor? decompressor = null) => new()
    {
        ServiceUrl = serviceUrl,
        Protocol = JetstreamProtocol.V2,
        CursorStore = store,
        CursorPersistInterval = persistInterval,
        Reconnect = JetstreamConsumerTests.Reconnect(maxReconnects),
        OnStreamError = onStreamError,
        OnInfo = onInfo,
        OnEventDropped = onDropped,
        Decompressor = decompressor,
        ZstdDictionaryId = decompressor is null ? null : 3,
    };

    private static Task<List<JetstreamEvent>> DrainAsync(JetstreamConsumer consumer, long? cursor = null)
        => JetstreamConsumerTests.DrainAsync(consumer, cursor);

    [Fact]
    public async Task ConsumeAsync_TracksLastSequenceNumber()
    {
        var source = new ScriptedJetstream(JetstreamProtocol.V2).Connection(Commit(100), Commit(200), Commit(300));
        var consumer = new JetstreamConsumer(Options(), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Equal(3, events.Count);
        Assert.Equal(300L, consumer.LastCursor);
    }

    [Fact]
    public async Task ConsumeAsync_Reconnect_ResumesAtLastSequenceNumberWithoutRewind()
    {
        // The v2 cursor is replayed inclusively, so rewinding it would only widen the overlap.
        var source = new ScriptedJetstream(JetstreamProtocol.V2)
            .Connection(Commit(100))
            .Connection(Commit(101));
        var consumer = new JetstreamConsumer(Options(maxReconnects: 1), source.Connect);

        await DrainAsync(consumer);

        Assert.Equal(100L, source.ObservedCursors[1]);
    }

    [Fact]
    public async Task ConsumeAsync_Reconnect_SkipsInclusivelyReplayedEvent()
    {
        var source = new ScriptedJetstream(JetstreamProtocol.V2)
            .Connection(Commit(100), Commit(200))
            // Reconnecting at 200 replays 200 itself, per the inclusive cursor.
            .Connection(Commit(200), Commit(300));
        var consumer = new JetstreamConsumer(Options(maxReconnects: 1), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Equal([100L, 200L, 300L], events.Select(e => e.Cursor).ToArray());
    }

    [Fact]
    public async Task ConsumeAsync_PersistsSequenceNumberNotTimestamp()
    {
        var store = new InMemoryStreamCursorStore();
        var source = new ScriptedJetstream(JetstreamProtocol.V2).Connection(Commit(100), Commit(200), Commit(300));
        var consumer = new JetstreamConsumer(Options(store, persistInterval: 2), source.Connect);

        await DrainAsync(consumer);

        Assert.Equal(300L, await store.GetCursorAsync(StreamId));
    }

    [Fact]
    public async Task ConsumeAsync_ResumesFromStoredSequenceNumber()
    {
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync(StreamId, 24664288881, TestContext.Current.CancellationToken);
        var source = new ScriptedJetstream(JetstreamProtocol.V2).Connection(Commit(24664288881), Commit(24664288882));
        var consumer = new JetstreamConsumer(Options(store), source.Connect);

        var events = await DrainAsync(consumer);

        // The stored cursor is replayed inclusively, and dropped as already delivered.
        Assert.Equal([24664288882L], events.Select(e => e.Cursor!.Value));
        Assert.Equal([24664288881L], source.ObservedCursors);
    }

    [Fact]
    public async Task ConsumeAsync_RejectedSubscription_PersistsProgressBeforeThrowing()
    {
        var store = new InMemoryStreamCursorStore();
        var source = new ScriptedJetstream(JetstreamProtocol.V2)
            .Connection(Commit(100))
            .FailingConnection(new EventStreamException("cursor too old", statusCode: 400));
        var consumer = new JetstreamConsumer(Options(store, maxReconnects: -1), source.Connect);

        await Assert.ThrowsAsync<JetstreamException>(() => DrainAsync(consumer));

        Assert.Equal(100L, await store.GetCursorAsync(StreamId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConsumeAsync_EventWithoutSequenceNumber_NotPersisted()
    {
        // Storing a v2 event's timestamp would hand the server a resume position it reads as
        // a sequence number, silently jumping the stream forward.
        var store = new InMemoryStreamCursorStore();
        var seqless = new JetstreamCommitEvent
        {
            Did = Did.Parse(TestDid),
            TimeUs = 1_725_911_162_329_308,
            Collection = Nsid.Parse("app.bsky.feed.post"),
            Rkey = RecordKey.Parse("3l3qo2vuowo2b"),
            Operation = RepoOpAction.Create,
        };
        var source = new ScriptedJetstream(JetstreamProtocol.V2).Connection(seqless);
        var consumer = new JetstreamConsumer(Options(store, persistInterval: 1), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Single(events);
        Assert.Null(await store.GetCursorAsync(StreamId));
    }

    [Fact]
    public async Task ConsumeAsync_RejectedSubscription_ThrowsAJetstreamExceptionInsteadOfLooping()
    {
        // CursorTooOld: reconnecting with the same cursor can only fail the same way, and
        // dropping the cursor would silently skip the gap the caller must backfill.
        var source = new ScriptedJetstream(JetstreamProtocol.V2)
            .FailingConnection(new EventStreamException("refused", statusCode: 400));
        var consumer = new JetstreamConsumer(Options(maxReconnects: -1), source.Connect);

        var ex = await Assert.ThrowsAsync<JetstreamException>(() => DrainAsync(consumer, cursor: 1));

        Assert.Equal(400, ex.StatusCode);
        Assert.False(ex.IsRetryable);
        Assert.Single(source.ObservedCursors);
    }

    [Theory]
    [InlineData(EventStreamErrors.ConsumerTooSlow)]
    [InlineData(EventStreamErrors.FutureCursor)]
    public async Task ConsumeAsync_ErrorFrame_IsReportedAndThrownAsAJetstreamExceptionUnlessRetryable(string error)
    {
        var errors = new List<EventStreamError>();
        var source = new ScriptedJetstream(JetstreamProtocol.V2)
            .Frames(JetstreamFrames.Commit(Commit(100), JetstreamProtocol.V2), JetstreamFrames.Error(error, "stopping"))
            .Connection(Commit(101));
        var consumer = new JetstreamConsumer(Options(maxReconnects: 1, onStreamError: errors.Add), source.Connect);

        var events = new List<JetstreamEvent>();
        var ex = await Record.ExceptionAsync(async () =>
        {
            await foreach (var evt in consumer.ConsumeAsync(cancellationToken: TestContext.Current.CancellationToken))
                events.Add(evt);
        });

        Assert.Equal([new EventStreamError(error, "stopping")], errors);
        if (error == EventStreamErrors.FutureCursor)
        {
            Assert.Equal(error, Assert.IsType<JetstreamException>(ex).Error);
            Assert.Equal([100L], events.Select(e => e.Cursor!.Value));
        }
        else
        {
            // Reconnected from the last event, then gave up when the script ran out.
            Assert.IsType<EventStreamException>(ex);
            Assert.Equal([100L, 101L], events.Select(e => e.Cursor!.Value));
            Assert.Equal(100L, source.ObservedCursors[1]);
        }
    }

    [Fact]
    public async Task ConsumeAsync_InfoFrames_AreReportedOutOfBandOverTheV2Subprotocol()
    {
        var infos = new List<JetstreamInfo>();
        var source = new ScriptedJetstream(JetstreamProtocol.V2).Frames(
            JetstreamFrames.Commit(Commit(1), JetstreamProtocol.V2),
            JetstreamFrames.Info("OutdatedCursor"),
            JetstreamFrames.Commit(Commit(2), JetstreamProtocol.V2));
        var consumer = new JetstreamConsumer(Options(serviceUrl: "https://jetstream.test", onInfo: infos.Add), source.Connect);

        var events = await DrainAsync(consumer, cursor: 0);

        Assert.Equal([1L, 2L], events.Select(e => e.Cursor!.Value));
        Assert.Equal("OutdatedCursor", Assert.Single(infos).Name);
        Assert.Equal("wss://jetstream.test/xrpc/network.bsky.jetstream.subscribeEvents?cursor=0", source.Endpoints[0].ToString());
        Assert.Equal("xrpc.v1.json", source.Options[0].SubProtocol);
    }

    [Fact]
    public async Task ConsumeAsync_ReadsEachFrameStraightFromTheReceiveBuffer()
    {
        // The socket reuses its buffer for the next message, so an event must not keep a
        // reference to it.
        var buffer = JetstreamFrames.Commit(Commit(5), JetstreamProtocol.V2);
        var source = new ScriptedJetstream(JetstreamProtocol.V2).Frames(buffer);
        var consumer = new JetstreamConsumer(Options(), source.Connect);

        var events = await DrainAsync(consumer);
        Array.Clear(buffer);

        var commit = Assert.IsType<JetstreamCommitEvent>(Assert.Single(events));
        Assert.Equal("app.bsky.feed.post", commit.Collection.Value);
    }

    [Fact]
    public async Task ConsumeAsync_UnreadableFrame_IsSkippedAndReported()
    {
        var dropped = new List<DroppedStreamEvent>();
        var source = new ScriptedJetstream(JetstreamProtocol.V2)
            .Frames("{not json"u8.ToArray(), JetstreamFrames.Commit(Commit(3), JetstreamProtocol.V2));
        var consumer = new JetstreamConsumer(Options(onDropped: dropped.Add), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Single(events);
        Assert.Equal(StreamDropReason.Malformed, Assert.Single(dropped).Reason);
    }

    [Fact]
    public async Task ConsumeAsync_BinaryFrame_GoesThroughTheDecompressor()
    {
        var decompressor = new ReversingDecompressor();
        var source = new ScriptedJetstream(JetstreamProtocol.V2)
            .Frames(JetstreamFrames.Commit(Commit(9), JetstreamProtocol.V2).Reverse().ToArray());
        var consumer = new JetstreamConsumer(Options(decompressor: decompressor), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Equal(9, Assert.Single(events).Cursor);
        Assert.Equal(1, decompressor.Calls);
    }

    [Fact]
    public async Task ConsumeAsync_FrameThatDoesNotDecompress_FailsTheConnectionAndReconnects()
    {
        // A corrupt frame, or one made with another dictionary: reconnecting from the last cursor
        // fetches it again, where skipping it would lose it, or every frame after a dictionary change.
        var good = JetstreamFrames.Commit(Commit(9), JetstreamProtocol.V2).Reverse().ToArray();
        var source = new ScriptedJetstream(JetstreamProtocol.V2)
            .Frames(good, [0xFF, 0xFF])
            .Frames(good, JetstreamFrames.Commit(Commit(10), JetstreamProtocol.V2).Reverse().ToArray());
        var consumer = new JetstreamConsumer(Options(maxReconnects: 1, decompressor: new ReversingDecompressor()), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Equal([9L, 10L], events.Select(e => e.Cursor!.Value));
        Assert.Equal([null, 9L, 10L], source.ObservedCursors);
    }

    private sealed class ReversingDecompressor : IJetstreamDecompressor
    {
        public int Calls { get; private set; }

        public byte[] Decompress(ReadOnlySpan<byte> frame)
        {
            Calls++;
            if (frame.SequenceEqual((ReadOnlySpan<byte>)[0xFF, 0xFF]))
                throw new InvalidDataException("Not a zstd frame.");
            var copy = frame.ToArray();
            Array.Reverse(copy);
            return copy;
        }
    }

    [Fact]
    public async Task ConsumeAsync_SequenceCursor_SkipsTheInclusiveReplayOfIt()
    {
        // The server replays ?cursor=N inclusively, and N is the last event already handled.
        var source = new ScriptedJetstream(JetstreamProtocol.V2).Connection(Commit(100), Commit(101));
        var consumer = new JetstreamConsumer(Options(), source.Connect);

        var events = await DrainAsync(consumer, cursor: 100);

        Assert.Equal([101L], events.Select(e => e.Cursor).ToArray());
    }

    [Fact]
    public async Task ConsumeAsync_TimestampCursor_IsNotADedupFloorForSequenceNumbers()
    {
        // A cursor of 10^15 or more seeks by time. Treated as a sequence floor it would drop every
        // event, since sequence numbers are far below it.
        var seek = JetstreamCursor.FromTimestamp(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryStreamCursorStore();
        var source = new ScriptedJetstream(JetstreamProtocol.V2).Connection(Commit(100), Commit(101));
        var consumer = new JetstreamConsumer(Options(store), source.Connect);

        var events = await DrainAsync(consumer, cursor: seek);

        Assert.Equal([100L, 101L], events.Select(e => e.Cursor).ToArray());
        Assert.Equal([seek], source.ObservedCursors);
        Assert.Equal(101L, await store.GetCursorAsync(StreamId));
    }

    [Fact]
    public async Task ConsumeAsync_TimestampCursor_ReconnectsBySequenceNumber()
    {
        var seek = JetstreamCursor.FromTimestamp(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero));
        var source = new ScriptedJetstream(JetstreamProtocol.V2)
            .Connection(Commit(100))
            .Connection(Commit(100), Commit(101));
        var consumer = new JetstreamConsumer(Options(maxReconnects: 1), source.Connect);

        var events = await DrainAsync(consumer, cursor: seek);

        Assert.Equal([100L, 101L], events.Select(e => e.Cursor).ToArray());
        Assert.Equal(seek, source.ObservedCursors[0]);
        Assert.Equal(100L, source.ObservedCursors[1]);
    }

    [Fact]
    public async Task ConsumeAsync_TimestampSeekBeforeAnyEvent_ReconnectsWithTheTimestamp()
    {
        var seek = JetstreamCursor.FromTimestamp(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero));
        var source = new ScriptedJetstream(JetstreamProtocol.V2)
            .FailingConnection(new InvalidOperationException("reset"))
            .Connection(Commit(100));
        var consumer = new JetstreamConsumer(Options(maxReconnects: 1), source.Connect);

        await DrainAsync(consumer, cursor: seek);

        Assert.Equal(seek, source.ObservedCursors[1]);
    }

    [Fact]
    public async Task ConsumeAsync_StoredV1Cursor_MigratesToSequenceNumbers()
    {
        // A v1 consumer stored time_us; the same store read by a v2 consumer seeks by that time and
        // from then on persists sequence numbers.
        const long v1Cursor = 1_725_911_162_329_308;
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync(StreamId, v1Cursor);
        var source = new ScriptedJetstream(JetstreamProtocol.V2).Connection(Commit(24664288882));
        var consumer = new JetstreamConsumer(Options(store), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Single(events);
        Assert.Equal([v1Cursor], source.ObservedCursors);
        Assert.Equal(24664288882L, await store.GetCursorAsync(StreamId));
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(404, false)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(null, true)]
    public void JetstreamException_IsRetryable_FollowsStatusClass(int? statusCode, bool retryable)
    {
        Assert.Equal(retryable, new JetstreamException("...", statusCode).IsRetryable);
    }

    [Fact]
    public void JetstreamCursor_FromTimestamp_IsUnixMicroseconds()
    {
        var time = new DateTimeOffset(2024, 9, 9, 19, 46, 2, 329, TimeSpan.Zero).AddTicks(3080);

        Assert.Equal(1_725_911_162_329_308, JetstreamCursor.FromTimestamp(time));
        Assert.True(JetstreamCursor.IsTimestamp(1_725_911_162_329_308));
        Assert.False(JetstreamCursor.IsTimestamp(24664288882));
    }

    [Fact]
    public void JetstreamCursor_FromTimestamp_BeforeTheBoundary_Throws()
    {
        // 2001-09-09T01:46:40Z is 10^15 µs; anything earlier would read as a sequence number.
        var boundary = DateTimeOffset.UnixEpoch.AddTicks(JetstreamCursor.TimestampThreshold * 10);

        Assert.Equal(JetstreamCursor.TimestampThreshold, JetstreamCursor.FromTimestamp(boundary));
        Assert.Throws<ArgumentOutOfRangeException>(() => JetstreamCursor.FromTimestamp(boundary.AddTicks(-10)));
    }
}
