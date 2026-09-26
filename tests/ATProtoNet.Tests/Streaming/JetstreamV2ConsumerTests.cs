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

    /// <summary>
    /// Scripted connection factory: each call pops the next "connection", records the cursor
    /// it was asked to resume from, and yields that connection's events.
    /// </summary>
    private sealed class ScriptedSource
    {
        private readonly Queue<Func<IAsyncEnumerable<JetstreamEvent>>> _connections = new();

        public List<long?> ObservedCursors { get; } = [];

        public ScriptedSource Connection(params JetstreamEvent[] events)
        {
            _connections.Enqueue(() => Yield(events));
            return this;
        }

        public ScriptedSource FailingConnection(Exception exception)
        {
            _connections.Enqueue(() => Throw(exception));
            return this;
        }

        public IAsyncEnumerable<JetstreamEvent> Connect(long? cursor, CancellationToken ct)
        {
            ObservedCursors.Add(cursor);
            return _connections.Count > 0 ? _connections.Dequeue()() : Yield([]);
        }

        private static async IAsyncEnumerable<JetstreamEvent> Yield(JetstreamEvent[] events)
        {
            foreach (var evt in events)
            {
                await Task.Yield();
                yield return evt;
            }
        }

        private static async IAsyncEnumerable<JetstreamEvent> Throw(Exception exception)
        {
            await Task.Yield();
            throw exception;
#pragma warning disable CS0162 // Unreachable, but required to make this an iterator.
            yield break;
#pragma warning restore CS0162
        }
    }

    private static JetstreamConsumerOptions Options(
        IStreamCursorStore? store = null,
        int persistInterval = 100,
        int maxReconnects = 0) => new()
    {
        ServiceUrl = StreamId,
        Protocol = JetstreamProtocol.V2,
        CursorStore = store,
        CursorPersistInterval = persistInterval,
        Reconnect = JetstreamConsumerTests.Reconnect(maxReconnects),
    };

    private static Task<List<JetstreamEvent>> DrainAsync(JetstreamConsumer consumer, long? cursor = null)
        => JetstreamConsumerTests.DrainAsync(consumer, cursor);

    [Fact]
    public async Task ConsumeAsync_TracksLastSequenceNumber()
    {
        var source = new ScriptedSource().Connection(Commit(100), Commit(200), Commit(300));
        var consumer = new JetstreamConsumer(Options(), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Equal(3, events.Count);
        Assert.Equal(300L, consumer.LastCursor);
    }

    [Fact]
    public async Task ConsumeAsync_Reconnect_ResumesAtLastSequenceNumberWithoutRewind()
    {
        // The v2 cursor is replayed inclusively, so rewinding it would only widen the overlap.
        var source = new ScriptedSource()
            .Connection(Commit(100))
            .Connection(Commit(101));
        var consumer = new JetstreamConsumer(Options(maxReconnects: 1), source.Connect);

        await DrainAsync(consumer);

        Assert.Equal(100L, source.ObservedCursors[1]);
    }

    [Fact]
    public async Task ConsumeAsync_Reconnect_SkipsInclusivelyReplayedEvent()
    {
        var source = new ScriptedSource()
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
        var source = new ScriptedSource().Connection(Commit(100), Commit(200), Commit(300));
        var consumer = new JetstreamConsumer(Options(store, persistInterval: 2), source.Connect);

        await DrainAsync(consumer);

        Assert.Equal(300L, await store.GetCursorAsync(StreamId));
    }

    [Fact]
    public async Task ConsumeAsync_ResumesFromStoredSequenceNumber()
    {
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync(StreamId, 24664288881);
        var source = new ScriptedSource().Connection(Commit(24664288882));
        var consumer = new JetstreamConsumer(Options(store), source.Connect);

        await DrainAsync(consumer);

        Assert.Equal([24664288881L], source.ObservedCursors);
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
        var source = new ScriptedSource().Connection(seqless);
        var consumer = new JetstreamConsumer(Options(store, persistInterval: 1), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Single(events);
        Assert.Null(await store.GetCursorAsync(StreamId));
    }

    [Fact]
    public async Task ConsumeAsync_RetryableConnectFailure_Reconnects()
    {
        var source = new ScriptedSource()
            .FailingConnection(new JetstreamException("rate limited", statusCode: 429))
            .Connection(Commit(100));
        var consumer = new JetstreamConsumer(Options(maxReconnects: 1), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Single(events);
    }

    [Fact]
    public async Task ConsumeAsync_RejectedSubscription_ThrowsInsteadOfLooping()
    {
        // CursorTooOld: reconnecting with the same cursor can only fail the same way, and
        // dropping the cursor would silently skip the gap the caller must backfill.
        var source = new ScriptedSource()
            .FailingConnection(new JetstreamException("cursor too old", statusCode: 400));
        var consumer = new JetstreamConsumer(Options(maxReconnects: -1), source.Connect);

        var ex = await Assert.ThrowsAsync<JetstreamException>(() => DrainAsync(consumer));

        Assert.Equal(400, ex.StatusCode);
        Assert.Single(source.ObservedCursors);
    }

    [Fact]
    public async Task ConsumeAsync_RejectedSubscription_PersistsProgressBeforeThrowing()
    {
        var store = new InMemoryStreamCursorStore();
        var source = new ScriptedSource()
            .Connection(Commit(100))
            .FailingConnection(new JetstreamException("cursor too old", statusCode: 400));
        var consumer = new JetstreamConsumer(Options(store, maxReconnects: -1), source.Connect);

        await Assert.ThrowsAsync<JetstreamException>(() => DrainAsync(consumer));

        Assert.Equal(100L, await store.GetCursorAsync(StreamId));
    }

    [Fact]
    public async Task ConsumeAsync_SequenceCursor_SkipsTheInclusiveReplayOfIt()
    {
        // The server replays ?cursor=N inclusively, and N is the last event already handled.
        var source = new ScriptedSource().Connection(Commit(100), Commit(101));
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
        var source = new ScriptedSource().Connection(Commit(100), Commit(101));
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
        var source = new ScriptedSource()
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
        var source = new ScriptedSource()
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
        var source = new ScriptedSource().Connection(Commit(24664288882));
        var consumer = new JetstreamConsumer(Options(store), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Single(events);
        Assert.Equal([v1Cursor], source.ObservedCursors);
        Assert.Equal(24664288882L, await store.GetCursorAsync(StreamId));
    }

    [Fact]
    public async Task ConsumeAsync_RetryableErrorFrame_Reconnects()
    {
        var source = new ScriptedSource()
            .FailingConnection(new JetstreamException("too slow", error: EventStreamErrors.ConsumerTooSlow))
            .Connection(Commit(100));
        var consumer = new JetstreamConsumer(Options(maxReconnects: 1), source.Connect);

        var events = await DrainAsync(consumer);

        // The connection the error ended, the reconnect that delivered, and one more that the
        // scripted source leaves empty.
        Assert.Single(events);
        Assert.Equal(3, source.ObservedCursors.Count);
    }

    [Fact]
    public async Task ConsumeAsync_FutureCursorErrorFrame_Throws()
    {
        var source = new ScriptedSource()
            .FailingConnection(new JetstreamException("ahead", error: EventStreamErrors.FutureCursor));
        var consumer = new JetstreamConsumer(Options(maxReconnects: -1), source.Connect);

        var ex = await Assert.ThrowsAsync<JetstreamException>(() => DrainAsync(consumer));

        Assert.Equal(EventStreamErrors.FutureCursor, ex.Error);
        Assert.Single(source.ObservedCursors);
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
