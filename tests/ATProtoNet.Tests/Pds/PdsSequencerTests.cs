using ATProtoNet.Pds;

namespace ATProtoNet.Tests.Pds;

public sealed class PdsSequencerTests
{
    private static byte[] Frame(long seq) => [(byte)(seq & 0xFF)];

    private static PdsFirehoseEvent Publish(PdsSequencer sequencer) =>
        sequencer.Publish("#commit", DateTimeOffset.UtcNow, Frame);

    // ── Sequence numbering ───────────────────────────────────

    [Fact]
    public void Publish_AssignsMonotonicallyIncreasingSequenceNumbers()
    {
        var sequencer = new PdsSequencer();

        Assert.Equal(1, Publish(sequencer).Seq);
        Assert.Equal(2, Publish(sequencer).Seq);
        Assert.Equal(3, Publish(sequencer).Seq);
        Assert.Equal(3, sequencer.CurrentSeq);
    }

    [Fact]
    public void Publish_PassesTheAssignedSequenceToTheFrameFactory()
    {
        var sequencer = new PdsSequencer();
        long seen = 0;

        sequencer.Publish("#commit", DateTimeOffset.UtcNow, seq => { seen = seq; return []; });

        Assert.Equal(1, seen);
    }

    [Fact]
    public void Constructor_StartSeq_ResumesAfterTheGivenSequence()
    {
        // A restarted PDS must not reissue sequence numbers a relay already consumed.
        var sequencer = new PdsSequencer(startSeq: 500);
        Assert.Equal(501, Publish(sequencer).Seq);
    }

    [Fact]
    public void Constructor_NegativeStartSeq_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdsSequencer(startSeq: -1));
    }

    [Fact]
    public void Constructor_ZeroCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdsSequencer(backlogCapacity: 0));
    }

    // ── Backlog ──────────────────────────────────────────────

    [Fact]
    public void Backfill_ReturnsOnlyEventsAfterTheCursor()
    {
        var sequencer = new PdsSequencer();
        for (var i = 0; i < 5; i++) Publish(sequencer);

        var replay = sequencer.Backfill(3);

        Assert.Equal([4L, 5L], replay.Select(e => e.Seq));
    }

    [Fact]
    public void Backfill_CursorZero_ReturnsEverythingRetained()
    {
        var sequencer = new PdsSequencer();
        for (var i = 0; i < 3; i++) Publish(sequencer);

        Assert.Equal(3, sequencer.Backfill(0).Count);
    }

    [Fact]
    public void Publish_BacklogIsBoundedByCapacity()
    {
        var sequencer = new PdsSequencer(backlogCapacity: 3);
        for (var i = 0; i < 10; i++) Publish(sequencer);

        var replay = sequencer.Backfill(0);

        Assert.Equal(3, replay.Count);
        Assert.Equal([8L, 9L, 10L], replay.Select(e => e.Seq));
        Assert.Equal(8, sequencer.OldestAvailableSeq);
    }

    [Fact]
    public void OldestAvailableSeq_EmptyBacklog_IsZero()
    {
        Assert.Equal(0, new PdsSequencer().OldestAvailableSeq);
    }

    // ── Subscriptions ────────────────────────────────────────

    [Fact]
    public async Task SubscribeAsync_ReceivesLiveEvents()
    {
        var sequencer = new PdsSequencer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var received = new List<long>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var evt in sequencer.SubscribeAsync(null, cts.Token))
            {
                received.Add(evt.Seq);
                if (received.Count == 3) break;
            }
        }, cts.Token);

        await WaitForSubscriberAsync(sequencer, cts.Token);
        for (var i = 0; i < 3; i++) Publish(sequencer);

        await consumer;
        Assert.Equal([1L, 2L, 3L], received);
    }

    [Fact]
    public async Task SubscribeAsync_WithCursor_ReplaysBacklogBeforeLiveEvents()
    {
        var sequencer = new PdsSequencer();
        for (var i = 0; i < 3; i++) Publish(sequencer);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<long>();

        var consumer = Task.Run(async () =>
        {
            await foreach (var evt in sequencer.SubscribeAsync(1, cts.Token))
            {
                received.Add(evt.Seq);
                if (received.Count == 3) break;
            }
        }, cts.Token);

        await WaitForSubscriberAsync(sequencer, cts.Token);
        Publish(sequencer);

        await consumer;

        // 2 and 3 from the backlog, then 4 live — no duplicates, no gaps.
        Assert.Equal([2L, 3L, 4L], received);
    }

    [Fact]
    public async Task SubscribeAsync_WithoutCursor_SkipsTheBacklog()
    {
        var sequencer = new PdsSequencer();
        for (var i = 0; i < 3; i++) Publish(sequencer);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<long>();

        var consumer = Task.Run(async () =>
        {
            await foreach (var evt in sequencer.SubscribeAsync(null, cts.Token))
            {
                received.Add(evt.Seq);
                break;
            }
        }, cts.Token);

        await WaitForSubscriberAsync(sequencer, cts.Token);
        Publish(sequencer);

        await consumer;
        Assert.Equal([4L], received);
    }

    [Fact]
    public async Task SubscribeAsync_Cancelled_RemovesTheSubscriber()
    {
        var sequencer = new PdsSequencer();
        using var cts = new CancellationTokenSource();

        var consumer = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in sequencer.SubscribeAsync(null, cts.Token)) { }
            }
            catch (OperationCanceledException) { }
        });

        await WaitForSubscriberAsync(sequencer, CancellationToken.None);
        await cts.CancelAsync();
        await consumer;

        Assert.Equal(0, sequencer.SubscriberCount);
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSubscribers_AllReceiveTheSameEvent()
    {
        var sequencer = new PdsSequencer();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var firstSeqs = new long[2];
        var consumers = Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
        {
            await foreach (var evt in sequencer.SubscribeAsync(null, cts.Token))
            {
                firstSeqs[index] = evt.Seq;
                break;
            }
        }, cts.Token)).ToArray();

        await WaitForSubscriberAsync(sequencer, cts.Token, expected: 2);
        Publish(sequencer);

        await Task.WhenAll(consumers);
        Assert.Equal([1L, 1L], firstSeqs);
    }

    [Fact]
    public async Task Subscribe_BuffersEventsPublishedBeforeReadingStarts()
    {
        var sequencer = new PdsSequencer();
        using var subscription = sequencer.Subscribe(cursor: null);

        // Registration is what Subscribe does — no enumeration yet, and no polling for it.
        Assert.Equal(1, sequencer.SubscriberCount);

        // This is the gap a WebSocket handler spends accepting the handshake. Anything
        // published in it must be waiting when the handler finally reads.
        for (var i = 0; i < 3; i++) Publish(sequencer);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<long>();
        await foreach (var evt in subscription.ReadAllAsync(cts.Token))
        {
            received.Add(evt.Seq);
            if (received.Count == 3) break;
        }

        Assert.Equal([1L, 2L, 3L], received);
    }

    [Fact]
    public void Subscribe_SnapshotsTheSequenceStateAtRegistration()
    {
        var sequencer = new PdsSequencer(backlogCapacity: 2);
        for (var i = 0; i < 3; i++) Publish(sequencer);

        using var subscription = sequencer.Subscribe(cursor: 1);

        // A later publish must not move the snapshot the cursor checks are made against,
        // or a valid cursor could be reported as a FutureCursor.
        Publish(sequencer);

        Assert.Equal(1, subscription.Cursor);
        Assert.Equal(3, subscription.CurrentSeq);
        Assert.Equal(2, subscription.OldestAvailableSeq);
        Assert.Equal([2L, 3L], subscription.Replay.Select(e => e.Seq));
    }

    [Fact]
    public void Subscribe_Dispose_RemovesTheSubscriber()
    {
        var sequencer = new PdsSequencer();

        var subscription = sequencer.Subscribe(cursor: null);
        Assert.Equal(1, sequencer.SubscriberCount);

        subscription.Dispose();
        Assert.Equal(0, sequencer.SubscriberCount);

        // Disposal is idempotent — the endpoint's `using` may run after an early return.
        subscription.Dispose();
        Assert.Equal(0, sequencer.SubscriberCount);
    }

    [Fact]
    public async Task Subscribe_EnumeratedTwice_Throws()
    {
        var sequencer = new PdsSequencer();
        using var subscription = sequencer.Subscribe(cursor: null);

        Publish(sequencer);
        await foreach (var _ in subscription.ReadAllAsync(CancellationToken.None)) break;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in subscription.ReadAllAsync(CancellationToken.None)) break;
        });
    }

    private static async Task WaitForSubscriberAsync(
        PdsSequencer sequencer, CancellationToken cancellationToken, int expected = 1)
    {
        while (sequencer.SubscriberCount < expected)
            await Task.Delay(5, cancellationToken);
    }
}
