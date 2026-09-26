using Microsoft.Extensions.Logging;

namespace ATProtoNet.Streaming;

/// <summary>
/// The settings every reconnecting stream consumer shares: where to connect, how to reconnect,
/// and where to keep the cursor.
/// </summary>
public abstract class StreamConsumerOptions
{
    /// <summary>The service's WebSocket URL, e.g. <c>wss://bsky.network</c>.</summary>
    public required string ServiceUrl { get; init; }

    /// <summary>Where to persist the cursor across restarts. Null keeps it in memory only.</summary>
    public IStreamCursorStore? CursorStore { get; init; }

    /// <summary>The key the cursor is stored under. Defaults to <see cref="ServiceUrl"/>.</summary>
    public string? StreamId { get; init; }

    /// <summary>
    /// How many events pass between two cursor saves. Every event counts, including those a
    /// filter drops. Default: 100.
    /// </summary>
    public int CursorPersistInterval { get; init; } = 100;

    /// <summary>How to reconnect after the connection drops, and when to give up.</summary>
    public StreamReconnectPolicy Reconnect { get; init; } = new();

    /// <summary>Optional logger.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Invoked for every error frame the server sends before closing the stream, such as
    /// <c>ConsumerTooSlow</c>. The consumer reconnects afterwards when the error is retryable and
    /// throws an <see cref="EventStreamException"/> when it is not.
    /// </summary>
    public Action<EventStreamError>? OnStreamError { get; init; }

    /// <summary>
    /// Invoked for every event the consumer skips because it cannot deliver it: a frame that does
    /// not parse (for example an identifier that is not valid), a message type this SDK version
    /// does not model, or a commit that failed verification. The cursor still moves past it.
    /// </summary>
    public Action<DroppedStreamEvent>? OnEventDropped { get; init; }

    /// <summary>The resolved stream identifier for cursor storage.</summary>
    internal string ResolvedStreamId => StreamId ?? ServiceUrl;

    /// <summary>Throws for settings no consumer can run with.</summary>
    internal virtual void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ServiceUrl, nameof(ServiceUrl));
        ArgumentOutOfRangeException.ThrowIfLessThan(CursorPersistInterval, 1, nameof(CursorPersistInterval));
        ArgumentNullException.ThrowIfNull(Reconnect, nameof(Reconnect));
        Reconnect.Validate();
    }
}

/// <summary>Why a stream consumer skipped an event.</summary>
public enum StreamDropReason
{
    /// <summary>
    /// The frame could not be read: malformed data, a missing required field, or an identifier
    /// that does not parse.
    /// </summary>
    Malformed,

    /// <summary>A message type this SDK version does not model.</summary>
    UnknownType,

    /// <summary>A commit whose CIDs or signature did not verify.</summary>
    VerificationFailed,
}

/// <summary>An event a stream consumer skipped, reported to <see cref="StreamConsumerOptions.OnEventDropped"/>.</summary>
/// <param name="Reason">Why it was skipped.</param>
/// <param name="Cursor">The event's position in the stream, when it could be read.</param>
/// <param name="Detail">A description, such as the message type or the verification error.</param>
public sealed record DroppedStreamEvent(StreamDropReason Reason, long? Cursor, string? Detail);
