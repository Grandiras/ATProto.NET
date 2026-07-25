using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ATProtoNet.Pds;

/// <summary>
/// A registered <c>com.atproto.sync.subscribeRepos</c> subscription: the replay snapshot taken
/// when it registered, plus the live channel every subsequent event is offered to.
/// <para>
/// Registration happens in <see cref="PdsSequencer.Subscribe"/>, not on first enumeration, so a
/// caller can register before it tells the peer the stream is open. Anything published from that
/// moment on is buffered until <see cref="ReadAllAsync"/> gets around to reading it, which is
/// what makes "connected" and "receiving" the same instant from the peer's point of view.
/// </para>
/// </summary>
public sealed class PdsFirehoseSubscription : IDisposable
{
    private readonly PdsSequencer _sequencer;
    private readonly IReadOnlyList<PdsFirehoseEvent> _replay;
    private readonly Channel<PdsFirehoseEvent> _channel =
        Channel.CreateBounded<PdsFirehoseEvent>(
            new BoundedChannelOptions(PdsSequencer.DefaultBufferCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            });

    private int _enumerated;
    private bool _disposed;

    internal PdsFirehoseSubscription(
        PdsSequencer sequencer,
        long? cursor,
        IReadOnlyList<PdsFirehoseEvent> replay,
        long currentSeq,
        long oldestAvailableSeq)
    {
        _sequencer = sequencer;
        _replay = replay;
        Cursor = cursor;
        CurrentSeq = currentSeq;
        OldestAvailableSeq = oldestAvailableSeq;
    }

    /// <summary>The cursor this subscription resumes after, or <c>null</c> for live events only.</summary>
    public long? Cursor { get; }

    /// <summary>
    /// The most recently assigned sequence number at the moment this subscription registered.
    /// Compare a requested cursor against this rather than re-reading
    /// <see cref="PdsSequencer.CurrentSeq"/>, so a concurrent publish cannot make a valid cursor
    /// look like a future one.
    /// </summary>
    public long CurrentSeq { get; }

    /// <summary>
    /// The oldest sequence number still replayable at the moment this subscription registered,
    /// or 0 when the backlog was empty.
    /// </summary>
    public long OldestAvailableSeq { get; }

    /// <summary>The events retained for replay when this subscription registered.</summary>
    public IReadOnlyList<PdsFirehoseEvent> Replay => _replay;

    /// <summary>
    /// Streams the replay snapshot and then follows live until the subscription is disposed,
    /// dropped for falling too far behind, or <paramref name="cancellationToken"/> fires.
    /// </summary>
    /// <exception cref="InvalidOperationException">The subscription was already enumerated.</exception>
    public async IAsyncEnumerable<PdsFirehoseEvent> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _enumerated, 1) != 0)
            throw new InvalidOperationException("A firehose subscription can only be enumerated once.");

        var lastReplayed = 0L;
        foreach (var evt in _replay)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastReplayed = evt.Seq;
            yield return evt;
        }

        await foreach (var evt in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // Skip anything the replay pass already delivered.
            if (evt.Seq <= lastReplayed) continue;
            yield return evt;
        }
    }

    /// <summary>Unregisters the subscription and ends any in-flight enumeration.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sequencer.Unsubscribe(this);
        _channel.Writer.TryComplete();
    }

    internal void Offer(PdsFirehoseEvent evt)
    {
        // A full buffer means this consumer is too slow to keep the stream coherent;
        // dropping an event silently would hand it a repo diff with a hole in it, so end
        // the subscription instead and let it reconnect with a cursor.
        if (!_channel.Writer.TryWrite(evt))
            _channel.Writer.TryComplete();
    }
}
