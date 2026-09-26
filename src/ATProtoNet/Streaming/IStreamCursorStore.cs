using System.Collections.Concurrent;

namespace ATProtoNet.Streaming;

/// <summary>
/// Persists stream cursor positions, so a consumer resumes where it left off after a restart.
/// Shared by the firehose, label and Jetstream consumers.
/// </summary>
/// <remarks>
/// <para>A consumer saves its cursor in the background every <c>CursorPersistInterval</c> events,
/// one save at a time and never on the read loop, so a slow store does not stall the socket; it
/// saves once more when the enumeration ends, however it ends. Implementations must still be safe
/// to call from any thread.</para>
/// <para>Each stream has its own kind of cursor: a sequence number on the firehose, label stream
/// and Jetstream v2, and a unix-microseconds timestamp on Jetstream v1. Give each stream its own
/// <c>StreamId</c> when they share a store.</para>
/// </remarks>
public interface IStreamCursorStore
{
    /// <summary>
    /// Gets the last stored cursor for a stream.
    /// </summary>
    /// <param name="streamId">The stream's identifier, such as the service URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored cursor, or <see langword="null"/> when none has been stored.</returns>
    ValueTask<long?> GetCursorAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the cursor for a stream, replacing the previous one.
    /// </summary>
    /// <param name="streamId">The stream's identifier.</param>
    /// <param name="cursor">The cursor to resume after.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask StoreCursorAsync(string streamId, long cursor, CancellationToken cancellationToken = default);
}

/// <summary>
/// An in-memory cursor store for development and tests. Cursors are lost when the process exits.
/// </summary>
public sealed class InMemoryStreamCursorStore : IStreamCursorStore
{
    private readonly ConcurrentDictionary<string, long> _cursors = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public ValueTask<long?> GetCursorAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        return ValueTask.FromResult(_cursors.TryGetValue(streamId, out var cursor) ? cursor : (long?)null);
    }

    /// <inheritdoc/>
    public ValueTask StoreCursorAsync(string streamId, long cursor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        _cursors[streamId] = cursor;
        return ValueTask.CompletedTask;
    }
}
