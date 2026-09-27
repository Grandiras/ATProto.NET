using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Identity;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

/// <summary>
/// <see cref="JetstreamConsumer"/> on the v1 wire, where the cursor is a timestamp. The reconnect
/// loop and the cursor store are shared with every consumer: see <see cref="EventStreamLoopTests"/>.
/// </summary>
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
        var source = new ScriptedJetstream().Connection(Commit(100), Commit(200), Commit(300));
        var consumer = new JetstreamConsumer(Options(), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Equal(3, events.Count);
        Assert.Equal(300L, consumer.LastTimeUs);
        Assert.Equal([null], source.ObservedCursors);
    }

    [Theory]
    [InlineData(null, 12345L)]
    [InlineData(99999L, 99999L)]
    public async Task ConsumeAsync_ResumesFromTheStoredCursor_AndAnExplicitCursorWins(long? cursor, long expected)
    {
        var store = new InMemoryStreamCursorStore();
        await store.StoreCursorAsync("wss://jetstream.test", 12345, TestContext.Current.CancellationToken);
        var source = new ScriptedJetstream().Connection(Commit(100_000_000));
        var consumer = new JetstreamConsumer(Options(store), source.Connect);

        await DrainAsync(consumer, cursor);

        Assert.Equal([expected], source.ObservedCursors);
    }

    [Fact]
    public async Task ConsumeAsync_Cancellation_EndsNormallyAndPersistsTheFinalCursor()
    {
        var store = new InMemoryStreamCursorStore();
        var source = new ScriptedJetstream().Connection(Commit(100), Commit(200), Commit(300));
        var consumer = new JetstreamConsumer(Options(store, maxReconnects: -1), source.Connect);
        using var cts = new CancellationTokenSource();

        var events = new List<JetstreamEvent>();
        await foreach (var evt in consumer.ConsumeAsync(cancellationToken: cts.Token))
        {
            events.Add(evt);
            if (events.Count == 2)
                await cts.CancelAsync();
        }

        Assert.Equal(2, events.Count);
        Assert.Equal(200L, await store.GetCursorAsync("wss://jetstream.test", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConsumeAsync_PersistsCursorAtIntervalAndOnShutdown()
    {
        var store = new InMemoryStreamCursorStore();
        var source = new ScriptedJetstream().Connection(
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
        var source = new ScriptedJetstream()
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
        var source = new ScriptedJetstream()
            .Connection(Commit(1_000), Commit(2_000))
            // Replay from the rewound cursor: 1_000 and 2_000 were already delivered.
            .Connection(Commit(1_000), Commit(2_000), Commit(3_000));
        var consumer = new JetstreamConsumer(
            Options(maxReconnects: 1, rewind: TimeSpan.FromMilliseconds(1)), source.Connect);

        var events = await DrainAsync(consumer);

        Assert.Equal([1_000L, 2_000L, 3_000L], events.Select(e => e.TimeUs).ToArray());
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
