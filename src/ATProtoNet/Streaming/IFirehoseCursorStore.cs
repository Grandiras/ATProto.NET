namespace ATProtoNet.Streaming;

/// <summary>
/// Provides persistent storage for firehose cursor positions, enabling resumable consumption
/// across process restarts.
/// </summary>
public interface IFirehoseCursorStore
{
    /// <summary>
    /// Gets the last stored cursor position for the given stream.
    /// </summary>
    /// <param name="streamId">Identifier for the stream (e.g., the relay URL).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The last stored cursor, or <c>null</c> if no cursor has been stored.</returns>
    Task<long?> GetCursorAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the cursor position for the given stream.
    /// </summary>
    /// <param name="streamId">Identifier for the stream.</param>
    /// <param name="cursor">The cursor (sequence number) to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreCursorAsync(string streamId, long cursor, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory cursor store suitable for development and testing.
/// Cursor positions are lost when the process exits.
/// </summary>
public sealed class InMemoryFirehoseCursorStore : IFirehoseCursorStore
{
    private readonly Dictionary<string, long> _cursors = new();
    private readonly object _lock = new();

    /// <inheritdoc/>
    public Task<long?> GetCursorAsync(string streamId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_cursors.TryGetValue(streamId, out var cursor) ? (long?)cursor : null);
        }
    }

    /// <inheritdoc/>
    public Task StoreCursorAsync(string streamId, long cursor, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _cursors[streamId] = cursor;
        }

        return Task.CompletedTask;
    }
}
