using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

// The cursor of one consumer run: loads the stored position, moves it forward monotonically, and
// persists it through an IStreamCursorStore.
//
// Every event the stream passes advances the cursor, including the ones a filter or a failed
// verification drops. A filter that rarely matches must not leave the stored position at its last
// match, or a restart would replay everything since.
//
// Saves run in the background, at most one at a time: when one is due while another is still running,
// the running one saves again with the newest value when it finishes. A slow store therefore never
// blocks the read loop, which a server would disconnect as too slow. FlushAsync waits for that and
// saves the final position; consumers dispose the tracker with their enumeration, so a break, a
// cancellation and an exception all keep it.
internal sealed class CursorTracker : IAsyncDisposable
{
    private readonly IStreamCursorStore? _store;
    private readonly string _streamId;
    private readonly int _persistInterval;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    private long? _current;
    private long? _saved;
    private long _persistLimit = long.MaxValue;
    private int _sinceSave;
    private Task? _saving;
    private bool _saveAgain;

    public CursorTracker(IStreamCursorStore? store, string streamId, int persistInterval, ILogger logger)
    {
        _store = store;
        _streamId = streamId;
        _persistInterval = Math.Max(1, persistInterval);
        _logger = logger;
    }

    // Starts the tracker of one consumer run: resuming after cursor, or after the stored cursor when it is
    // null. Returns null when cancellationToken is cancelled while the stored cursor is loaded.
    public static async ValueTask<CursorTracker?> StartAsync(
        CursorStreamConsumerOptions options, long? cursor, CancellationToken cancellationToken)
    {
        var logger = options.Logger ?? NullLogger.Instance;
        var tracker = new CursorTracker(options.CursorStore, options.ResolvedStreamId, options.CursorPersistInterval, logger);
        if (cursor is null)
        {
            try
            {
                cursor = await tracker.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (cursor is { } stored)
                logger.LogInformation("Resuming {StreamId} from stored cursor {Cursor}", options.ResolvedStreamId, stored);
        }

        tracker.Start(cursor);
        return tracker;
    }

    // The last position the stream passed, or null before the first one.
    public long? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    // Loads the stored cursor, or null when there is no store or nothing stored.
    public async ValueTask<long?> LoadAsync(CancellationToken cancellationToken)
    {
        if (_store is null)
            return null;

        var stored = await _store.GetCursorAsync(_streamId, cancellationToken).ConfigureAwait(false);
        _saved = stored;
        return stored;
    }

    // Sets the floor the cursor only moves up from, without saving it: the position the run resumes after.
    public void Start(long? position)
    {
        lock (_gate)
            _current = position;
    }

    // Caps the position saved at limit, or lifts the cap when null. A consumer sets it below events it has
    // taken off the stream but not yet delivered, so a restart replays them rather than resuming past
    // them. Current is not capped.
    public void SetPersistLimit(long? limit)
    {
        lock (_gate)
            _persistLimit = limit ?? long.MaxValue;
    }

    // Moves the cursor to position when that is past the current one, and starts a background save every
    // persistInterval moves.
    //
    // Returns: Whether the position was new; false for one at or below the current cursor.
    public bool Advance(long position)
    {
        lock (_gate)
        {
            if (_current is { } current && position <= current)
                return false;

            _current = position;

            if (_store is null || ++_sinceSave < _persistInterval)
                return true;

            _sinceSave = 0;
            if (_saving is not null)
            {
                _saveAgain = true;
                return true;
            }

            _saving = Task.Run(SaveLoopAsync);
            return true;
        }
    }

    // Waits for a background save, then saves the final position if it has not been saved. Never throws: a
    // failed save is logged, and the next run replays from the last saved one.
    public async ValueTask FlushAsync()
    {
        if (_store is null)
            return;

        Task? saving;
        lock (_gate)
            saving = _saving;

        if (saving is not null)
            await saving.ConfigureAwait(false);

        long? position;
        lock (_gate)
        {
            position = Persistable();
            if (position == _saved)
                position = null;
        }

        if (position is { } final)
            await SaveAsync(final).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => FlushAsync();

    // The position a save may record: Current, capped by the persist limit. Call under the lock.
    private long? Persistable() => _current is { } current ? Math.Min(current, _persistLimit) : null;

    private async Task SaveLoopAsync()
    {
        while (true)
        {
            long position;
            lock (_gate)
                position = Persistable()!.Value;

            await SaveAsync(position).ConfigureAwait(false);

            lock (_gate)
            {
                if (!_saveAgain)
                {
                    _saving = null;
                    return;
                }

                _saveAgain = false;
            }
        }
    }

    private async Task SaveAsync(long position)
    {
        try
        {
            await _store!.StoreCursorAsync(_streamId, position, CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                if (_saved is not { } saved || position > saved)
                    _saved = position;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not store cursor {Cursor} for stream {StreamId}", position, _streamId);
        }
    }
}
