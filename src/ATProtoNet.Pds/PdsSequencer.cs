using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ATProtoNet.Pds;

/// <summary>
/// Assigns sequence numbers to firehose events, keeps a bounded backlog for cursor-based
/// replay, and fans events out to live <c>com.atproto.sync.subscribeRepos</c> subscribers.
/// <para>
/// The backlog is in memory and bounded, so a relay that reconnects with a cursor older than
/// the retained window is told so with an <c>#info</c> frame naming <c>OutdatedCursor</c> and
/// then resumes from the oldest event still held — the same contract the reference PDS offers,
/// just with a shorter window.
/// </para>
/// </summary>
public sealed class PdsSequencer
{
    /// <summary>Default number of events retained for cursor replay.</summary>
    public const int DefaultBacklogCapacity = 1024;

    private readonly object _gate = new();
    private readonly Queue<PdsFirehoseEvent> _backlog = new();
    private readonly List<Subscriber> _subscribers = [];
    private readonly int _capacity;
    private long _seq;

    /// <summary>Creates a sequencer.</summary>
    /// <param name="backlogCapacity">How many past events to retain for cursor replay.</param>
    /// <param name="startSeq">
    /// The sequence number to resume from. Pass the highest sequence previously emitted so a
    /// restarted process does not reuse numbers a relay has already seen.
    /// </param>
    public PdsSequencer(int backlogCapacity = DefaultBacklogCapacity, long startSeq = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(backlogCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(startSeq);

        _capacity = backlogCapacity;
        _seq = startSeq;
    }

    /// <summary>The most recently assigned sequence number.</summary>
    public long CurrentSeq
    {
        get { lock (_gate) return _seq; }
    }

    /// <summary>The number of live subscribers.</summary>
    public int SubscriberCount
    {
        get { lock (_gate) return _subscribers.Count; }
    }

    /// <summary>
    /// Assigns the next sequence number, builds the frame with it, and publishes the result.
    /// <para>
    /// The frame factory runs under the sequencer lock so that the order events are appended
    /// to the backlog always matches the order of their sequence numbers — building the frame
    /// outside would let two concurrent commits interleave and hand a relay a backlog whose
    /// sequence numbers go backwards.
    /// </para>
    /// </summary>
    /// <param name="type">The event type discriminator, e.g. <c>#commit</c>.</param>
    /// <param name="time">The event timestamp.</param>
    /// <param name="buildFrame">Builds the encoded frame for the assigned sequence number.</param>
    /// <returns>The published event.</returns>
    public PdsFirehoseEvent Publish(string type, DateTimeOffset time, Func<long, byte[]> buildFrame)
    {
        ArgumentNullException.ThrowIfNull(buildFrame);

        PdsFirehoseEvent evt;
        Subscriber[] targets;

        lock (_gate)
        {
            var seq = ++_seq;
            evt = new PdsFirehoseEvent(seq, type, time, buildFrame(seq));

            _backlog.Enqueue(evt);
            while (_backlog.Count > _capacity)
                _backlog.Dequeue();

            targets = [.. _subscribers];
        }

        foreach (var subscriber in targets)
            subscriber.Offer(evt);

        return evt;
    }

    /// <summary>
    /// Returns the retained events with a sequence number greater than <paramref name="cursor"/>.
    /// </summary>
    public IReadOnlyList<PdsFirehoseEvent> Backfill(long cursor)
    {
        lock (_gate)
            return [.. _backlog.Where(e => e.Seq > cursor)];
    }

    /// <summary>The oldest sequence number still available for replay, or 0 when the backlog is empty.</summary>
    public long OldestAvailableSeq
    {
        get
        {
            lock (_gate)
                return _backlog.Count == 0 ? 0 : _backlog.Peek().Seq;
        }
    }

    /// <summary>
    /// Streams events to a subscriber, starting with any retained events after
    /// <paramref name="cursor"/> and then following live.
    /// </summary>
    /// <param name="cursor">
    /// Resume after this sequence number. <c>null</c> streams live events only, which is what
    /// a relay connecting for the first time wants.
    /// </param>
    /// <param name="cancellationToken">Stops the subscription.</param>
    /// <remarks>
    /// A subscriber that cannot keep up with <see cref="DefaultBufferCapacity"/> queued events
    /// is dropped rather than allowed to grow the buffer without bound; the stream simply ends,
    /// and the consumer is expected to reconnect with its last cursor.
    /// </remarks>
    public async IAsyncEnumerable<PdsFirehoseEvent> SubscribeAsync(
        long? cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subscriber = new Subscriber();
        IReadOnlyList<PdsFirehoseEvent> replay;

        // Register and snapshot the backlog atomically: anything published from here on lands
        // in the subscriber's channel, so there is no window in which an event is neither in
        // the replay list nor in the live stream.
        lock (_gate)
        {
            replay = cursor is { } c ? [.. _backlog.Where(e => e.Seq > c)] : [];
            _subscribers.Add(subscriber);
        }

        try
        {
            var lastReplayed = 0L;
            foreach (var evt in replay)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lastReplayed = evt.Seq;
                yield return evt;
            }

            await foreach (var evt in subscriber.Channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // Skip anything the replay pass already delivered.
                if (evt.Seq <= lastReplayed) continue;
                yield return evt;
            }
        }
        finally
        {
            lock (_gate)
                _subscribers.Remove(subscriber);
        }
    }

    /// <summary>How many events a single subscriber may fall behind before being dropped.</summary>
    public const int DefaultBufferCapacity = 512;

    private sealed class Subscriber
    {
        public Channel<PdsFirehoseEvent> Channel { get; } =
            System.Threading.Channels.Channel.CreateBounded<PdsFirehoseEvent>(
                new BoundedChannelOptions(DefaultBufferCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropWrite,
                    SingleReader = true,
                });

        public void Offer(PdsFirehoseEvent evt)
        {
            // A full buffer means this consumer is too slow to keep the stream coherent;
            // dropping an event silently would hand it a repo diff with a hole in it, so end
            // the subscription instead and let it reconnect with a cursor.
            if (!Channel.Writer.TryWrite(evt))
                Channel.Writer.TryComplete();
        }
    }
}
