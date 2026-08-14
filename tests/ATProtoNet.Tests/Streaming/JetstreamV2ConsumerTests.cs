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
        Collection = "app.bsky.feed.post",
        RKey = "3l3qo2vuowo2b",
        Operation = JetstreamOperation.Create,
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
        IFirehoseCursorStore? store = null,
        int persistInterval = 100,
        int maxReconnects = 0) => new()
    {
        ServiceUrl = StreamId,
        Protocol = JetstreamProtocol.V2,
        CursorStore = store,
        CursorPersistInterval = persistInterval,
        MaxReconnectAttempts = maxReconnects,
        ReconnectDelay = TimeSpan.FromMilliseconds(1),
    };

    private static async Task<List<JetstreamEvent>> DrainAsync(JetstreamConsumer consumer, long? cursor = null)
    {
        var events = new List<JetstreamEvent>();
        await foreach (var evt in consumer.ConsumeAsync(cursor))
            events.Add(evt);
        return events;
    }

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
        var store = new InMemoryFirehoseCursorStore();
        var source = new ScriptedSource().Connection(Commit(100), Commit(200), Commit(300));
        var consumer = new JetstreamConsumer(Options(store, persistInterval: 2), source.Connect);

        await DrainAsync(consumer);

        Assert.Equal(300L, await store.GetCursorAsync(StreamId));
    }

    [Fact]
    public async Task ConsumeAsync_ResumesFromStoredSequenceNumber()
    {
        var store = new InMemoryFirehoseCursorStore();
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
        var store = new InMemoryFirehoseCursorStore();
        var seqless = new JetstreamCommitEvent
        {
            Did = Did.Parse(TestDid),
            TimeUs = 1_725_911_162_329_308,
            Collection = "app.bsky.feed.post",
            RKey = "3l3qo2vuowo2b",
            Operation = JetstreamOperation.Create,
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
            .FailingConnection(new JetstreamConnectException("rate limited", statusCode: 429))
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
            .FailingConnection(new JetstreamConnectException("cursor too old", statusCode: 400));
        var consumer = new JetstreamConsumer(Options(maxReconnects: -1), source.Connect);

        var ex = await Assert.ThrowsAsync<JetstreamConnectException>(() => DrainAsync(consumer));

        Assert.Equal(400, ex.StatusCode);
        Assert.Single(source.ObservedCursors);
    }

    [Fact]
    public async Task ConsumeAsync_RejectedSubscription_PersistsProgressBeforeThrowing()
    {
        var store = new InMemoryFirehoseCursorStore();
        var source = new ScriptedSource()
            .Connection(Commit(100))
            .FailingConnection(new JetstreamConnectException("cursor too old", statusCode: 400));
        var consumer = new JetstreamConsumer(Options(store, maxReconnects: -1), source.Connect);

        await Assert.ThrowsAsync<JetstreamConnectException>(() => DrainAsync(consumer));

        Assert.Equal(100L, await store.GetCursorAsync(StreamId));
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(404, false)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(null, true)]
    public void JetstreamConnectException_IsRetryable_FollowsStatusClass(int? statusCode, bool retryable)
    {
        Assert.Equal(retryable, new JetstreamConnectException("...", statusCode).IsRetryable);
    }
}
