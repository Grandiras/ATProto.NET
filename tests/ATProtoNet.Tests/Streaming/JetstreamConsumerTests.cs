using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Identity;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class JetstreamConsumerTests
{
    private const string TestDid = "did:plc:eygmaihciaxprqvxpfvl6flk";

    private static JetstreamCommitEvent Commit(long timeUs, string rkey = "3l3qo2vuowo2b") => new()
    {
        Did = Did.Parse(TestDid),
        TimeUs = timeUs,
        Collection = Nsid.Parse("exchange.recipe.recipe"),
        Rkey = RecordKey.Parse(rkey),
        Operation = RepoOpAction.Create,
    };

    /// <summary>
    /// Scripted connection factory: each call to the factory pops the next "connection",
    /// records the cursor it was asked to resume from, and yields that connection's events.
    /// </summary>
    private sealed class ScriptedSource
    {
        private readonly Queue<Func<long?, IAsyncEnumerable<JetstreamEvent>>> _connections = new();

        public List<long?> ObservedCursors { get; } = [];

        public int ConnectionCount { get; private set; }

        public ScriptedSource Connection(params JetstreamEvent[] events)
        {
            _connections.Enqueue(_ => Yield(events));
            return this;
        }

        public ScriptedSource FailingConnection(Exception exception)
        {
            _connections.Enqueue(_ => Throw(exception));
            return this;
        }

        public IAsyncEnumerable<JetstreamEvent> Connect(long? cursor, CancellationToken ct)
        {
            ObservedCursors.Add(cursor);
            ConnectionCount++;
            return _connections.Count > 0
                ? _connections.Dequeue()(cursor)
                : Yield([]);
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
            if (exception is not null)
                throw exception;
            yield break;
        }
    }

    private static JetstreamConsumerOptions Options(
        IStreamCursorStore? store = null,
        int persistInterval = 100,
        int maxReconnects = 0,
        TimeSpan? rewind = null) => new()
    {
        ServiceUrl = "wss://jetstream.test",
        CursorStore = store,
        CursorPersistInterval = persistInterval,
        Reconnect = Reconnect(maxReconnects),
        ReconnectRewind = rewind ?? TimeSpan.FromSeconds(5),
    };

    internal static StreamReconnectPolicy Reconnect(int maxReconnects) => new()
    {
        InitialDelay = TimeSpan.FromMilliseconds(1),
        MaxDelay = TimeSpan.FromMilliseconds(1),
        MaxAttempts = maxReconnects < 0 ? null : maxReconnects,
    };

    /// <summary>
    /// Collects every event until the scripted connections run out and the reconnect policy gives
    /// up, which ends the enumeration with an <see cref="EventStreamException"/>.
    /// </summary>
    internal static async Task<List<JetstreamEvent>> DrainAsync(
        JetstreamConsumer consumer, long? cursor = null, CancellationToken ct = default)
    {
        var events = new List<JetstreamEvent>();
        try
        {
            await foreach (var evt in consumer.ConsumeAsync(cursor, ct))
                events.Add(evt);
        }
        catch (EventStreamException ex) when (ex is not JetstreamException)
        {
        }

        return events;
    }

    [Fact]
    public async Task ConsumeAsync_YieldsEventsFromSource()
    {
        var source = new ScriptedSource().Connection(Commit(100), Commit(200), Commit(300));
        var consumer = new JetstreamConsumer(Options(), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Equal(3, events.Count);
        Assert.Equal(300L, consumer.LastTimeUs);
    }

    [Fact]
    public async Task ConsumeAsync_NoCursorNoStore_ConnectsLive()
    {
        var source = new ScriptedSource().Connection(Commit(100));
        var consumer = new JetstreamConsumer(Options(), source.Connect);

        await DrainAsync(consumer);

        Assert.Equal([null], source.ObservedCursors);
    }

    [Fact]
    public async Task ConsumeAsync_ResumesFromStoredCursor()
    {
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync("wss://jetstream.test", 12345);
        var source = new ScriptedSource().Connection(Commit(100_000_000));
        var consumer = new JetstreamConsumer(Options(store), source.Connect);

        await DrainAsync(consumer);

        Assert.Equal([12345L], source.ObservedCursors);
    }

    [Fact]
    public async Task ConsumeAsync_ExplicitCursor_OverridesStore()
    {
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync("wss://jetstream.test", 12345);
        var source = new ScriptedSource().Connection(Commit(100_000_000));
        var consumer = new JetstreamConsumer(Options(store), source.Connect);

        await DrainAsync(consumer, cursor: 99999);

        Assert.Equal([99999L], source.ObservedCursors);
    }

    [Fact]
    public async Task ConsumeAsync_PersistsCursorAtIntervalAndOnShutdown()
    {
        var store = new InMemoryStreamCursorStore();
        var source = new ScriptedSource().Connection(
            Commit(100), Commit(200), Commit(300), Commit(400), Commit(500));
        var consumer = new JetstreamConsumer(Options(store, persistInterval: 2), source.Connect);

        await DrainAsync(consumer);

        // Interval persists at events 2 and 4; final persist on shutdown stores the last event.
        Assert.Equal(500L, await store.GetCursorAsync("wss://jetstream.test"));
    }

    [Fact]
    public async Task ConsumeAsync_Reconnect_RewindsCursor()
    {
        var rewind = TimeSpan.FromSeconds(5);
        var lastTimeUs = 100_000_000_000;
        var source = new ScriptedSource()
            .Connection(Commit(lastTimeUs))
            .Connection(Commit(lastTimeUs + 1));
        var consumer = new JetstreamConsumer(
            Options(maxReconnects: 1, rewind: rewind), source.Connect);

        await DrainAsync(consumer);

        Assert.Equal(2 + 1, source.ObservedCursors.Count); // initial + 1st reconnect + final (empty default)
        Assert.Equal(lastTimeUs - (long)rewind.TotalMicroseconds, source.ObservedCursors[1]);
    }

    [Fact]
    public async Task ConsumeAsync_Reconnect_SkipsReplayedEvents()
    {
        var source = new ScriptedSource()
            .Connection(Commit(1_000), Commit(2_000))
            // Replay from the rewound cursor: 1_000 and 2_000 were already delivered.
            .Connection(Commit(1_000), Commit(2_000), Commit(3_000));
        var consumer = new JetstreamConsumer(
            Options(maxReconnects: 1, rewind: TimeSpan.FromMilliseconds(1)), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Equal([1_000L, 2_000L, 3_000L], events.Select(e => e.TimeUs).ToArray());
    }

    [Fact]
    public async Task ConsumeAsync_MaxReconnectAttempts_StopsConnecting()
    {
        var source = new ScriptedSource(); // Every connection is empty.
        var consumer = new JetstreamConsumer(Options(maxReconnects: 2), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Empty(events);
        Assert.Equal(3, source.ConnectionCount); // initial + 2 reconnect attempts
    }

    [Fact]
    public async Task ConsumeAsync_ReconnectAttemptsExhausted_ThrowsRatherThanEndingLikeTheStream()
    {
        var failure = new InvalidOperationException("connection reset");
        var source = new ScriptedSource()
            .Connection(Commit(100))
            .FailingConnection(failure)
            .FailingConnection(failure);
        var consumer = new JetstreamConsumer(Options(maxReconnects: 2), source.Connect);

        var events = new List<JetstreamEvent>();
        var ex = await Assert.ThrowsAsync<EventStreamException>(async () =>
        {
            await foreach (var evt in consumer.ConsumeAsync(cancellationToken: TestContext.Current.CancellationToken))
                events.Add(evt);
        });

        Assert.Single(events);
        Assert.Same(failure, ex.InnerException);
        Assert.Equal(3, source.ConnectionCount);
    }

    [Fact]
    public async Task ConsumeAsync_ReconnectAttemptsResetOnceAConnectionDelivers()
    {
        // Two drops in a row would exhaust one attempt, but each connection delivers first.
        var source = new ScriptedSource()
            .Connection(Commit(100))
            .Connection(Commit(200))
            .Connection(Commit(300));
        var consumer = new JetstreamConsumer(Options(maxReconnects: 1), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Equal([100L, 200L, 300L], events.Select(e => e.TimeUs).ToArray());
    }

    [Fact]
    public async Task ConsumeAsync_ConnectionException_TriggersReconnect()
    {
        var source = new ScriptedSource()
            .FailingConnection(new InvalidOperationException("connect refused"))
            .Connection(Commit(100));
        var consumer = new JetstreamConsumer(Options(maxReconnects: 1), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Single(events);
        Assert.Equal(100L, events[0].TimeUs);
    }

    [Fact]
    public async Task ConsumeAsync_Cancellation_PersistsFinalCursor()
    {
        var store = new InMemoryStreamCursorStore();
        var source = new ScriptedSource().Connection(
            Commit(100), Commit(200), Commit(300));
        var consumer = new JetstreamConsumer(Options(store, maxReconnects: -1), source.Connect);
        using var cts = new CancellationTokenSource();

        var events = new List<JetstreamEvent>();
        await foreach (var evt in consumer.ConsumeAsync(cancellationToken: cts.Token))
        {
            events.Add(evt);
            if (events.Count == 2)
                cts.Cancel();
        }

        Assert.Equal(2, events.Count);
        Assert.Equal(200L, await store.GetCursorAsync("wss://jetstream.test"));
    }

    [Fact]
    public void JetstreamConsumer_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new JetstreamConsumer(null!));
    }

    [Fact]
    public void JetstreamConsumer_InvalidReconnectPolicy_Throws()
    {
        var options = new JetstreamConsumerOptions
        {
            ServiceUrl = "wss://jetstream.test",
            Reconnect = new StreamReconnectPolicy { MaxAttempts = -1 },
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new JetstreamConsumer(options));
    }
}
