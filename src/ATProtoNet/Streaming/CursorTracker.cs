using Microsoft.Extensions.Logging;

namespace ATProtoNet.Streaming;

/// <summary>
/// The cursor of one consumer run: loads the stored position, moves it forward monotonically, and
/// persists it through an <see cref="IStreamCursorStore"/>.
/// </summary>
/// <remarks>
/// <para>Every event the stream passes advances the cursor, including the ones a filter or a
/// failed verification drops. A filter that rarely matches must not leave the stored position at
/// its last match, or a restart would replay everything since.</para>
/// <para>Saves run in the background, at most one at a time: when one is due while another is
/// still running, the running one saves again with the newest value when it finishes. A slow
/// store therefore never blocks the read loop, which a server would disconnect as too slow.
/// <see cref="FlushAsync"/> waits for that and saves the final position; consumers call it from
/// <c>finally</c>, so a <c>break</c>, a cancellation and an exception all keep it.</para>
/// </remarks>
internal sealed class CursorTracker
{
    private readonly IStreamCursorStore? _store;
    private readonly string _streamId;
    private readonly int _persistInterval;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    private long? _current;
    private long? _saved;
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

    /// <summary>The last position the stream passed, or null before the first one.</summary>
    public long? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    /// <summary>Loads the stored cursor, or null when there is no store or nothing stored.</summary>
    public async ValueTask<long?> LoadAsync(CancellationToken cancellationToken)
    {
        if (_store is null)
            return null;

        var stored = await _store.GetCursorAsync(_streamId, cancellationToken).ConfigureAwait(false);
        _saved = stored;
        return stored;
    }

    /// <summary>
    /// Sets the floor the cursor only moves up from, without saving it: the position the run
    /// resumes after.
    /// </summary>
    public void Start(long? position)
    {
        lock (_gate)
            _current = position;
    }

    /// <summary>
    /// Moves the cursor to <paramref name="position"/> when that is past the current one, and
    /// starts a background save every <c>persistInterval</c> moves.
    /// </summary>
    /// <returns>Whether the position was new; false for one at or below the current cursor.</returns>
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

    /// <summary>
    /// Waits for a background save, then saves the final position if it has not been saved.
    /// Never throws: a failed save is logged, and the next run replays from the last saved one.
    /// </summary>
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
            position = _current == _saved ? null : _current;

        if (position is { } final)
            await SaveAsync(final).ConfigureAwait(false);
    }

    private async Task SaveLoopAsync()
    {
        while (true)
        {
            long position;
            lock (_gate)
                position = _current!.Value;

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
