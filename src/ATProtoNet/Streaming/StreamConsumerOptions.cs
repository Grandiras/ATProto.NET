using Microsoft.Extensions.Logging;

namespace ATProtoNet.Streaming;

/// <summary>
/// The settings every stream consumer shares: where to connect, how to reconnect, and what to
/// report. How every consumer delivers is described here once.
/// </summary>
/// <remarks>
/// <para><b>Delivery</b> is at-least-once. A message's position is recorded when the caller asks
/// for the next one, having handled it, so the message being handled when the process stops is
/// delivered again. A consumer with a <see cref="CursorStreamConsumerOptions.CursorStore"/> saves
/// the position in the background every
/// <see cref="CursorStreamConsumerOptions.CursorPersistInterval"/> events and once more when the
/// enumeration ends, however it ends; the events after the last saved position are delivered
/// again after a restart. Events a filter or a failed verification skips still move the
/// position, so a filter that rarely matches does not replay everything since its last match.</para>
/// <para><b>Cancelling</b> the token ends the enumeration normally: no
/// <see cref="OperationCanceledException"/> is thrown, and the position is saved.</para>
/// <para><b>Errors.</b> An error frame the server sends before closing the stream is reported to
/// <see cref="OnStreamError"/>. The consumer reconnects after one that is retryable, such as
/// <c>ConsumerTooSlow</c>, per <see cref="Reconnect"/>, and throws an
/// <see cref="EventStreamException"/> for one that is not, such as <c>FutureCursor</c>, and for a
/// subscription the server refused (see <see cref="EventStreamException.IsRetryable"/>). When the
/// policy gives up it throws an <see cref="EventStreamException"/> whose
/// <see cref="Exception.InnerException"/> is the last failure.</para>
/// </remarks>
public abstract class StreamConsumerOptions
{
    /// <summary>The service's URL, e.g. <c>wss://bsky.network</c>.</summary>
    public required string ServiceUrl { get; init; }

    /// <summary>How to reconnect after the connection drops, and when to give up.</summary>
    public StreamReconnectPolicy Reconnect { get; init; } = new();

    /// <summary>Optional logger.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>Invoked for every error frame the server sends before closing the stream.</summary>
    public Action<EventStreamError>? OnStreamError { get; init; }

    /// <summary>
    /// Invoked for every message the consumer skips because it cannot deliver it: one that does
    /// not parse (for example an identifier that is not valid), of a type this SDK version does
    /// not model, or that failed verification.
    /// </summary>
    public Action<DroppedStreamEvent>? OnEventDropped { get; init; }

    /// <summary>Throws for settings no consumer can run with.</summary>
    internal virtual void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ServiceUrl, nameof(ServiceUrl));
        ArgumentNullException.ThrowIfNull(Reconnect, nameof(Reconnect));
        Reconnect.Validate();
    }
}

/// <summary>
/// The settings of a stream consumer that keeps its position in an
/// <see cref="IStreamCursorStore"/>, and resumes from it.
/// </summary>
public abstract class CursorStreamConsumerOptions : StreamConsumerOptions
{
    /// <summary>Where to persist the cursor across restarts. Null keeps it in memory only.</summary>
    public IStreamCursorStore? CursorStore { get; init; }

    /// <summary>The key the cursor is stored under. Defaults to <see cref="StreamConsumerOptions.ServiceUrl"/>.</summary>
    public string? StreamId { get; init; }

    /// <summary>How many events pass between two cursor saves, filtered ones included. Default: 100.</summary>
    public int CursorPersistInterval { get; init; } = 100;

    /// <summary>The resolved stream identifier for cursor storage.</summary>
    internal string ResolvedStreamId => StreamId ?? ServiceUrl;

    /// <inheritdoc/>
    internal override void Validate()
    {
        base.Validate();
        ArgumentOutOfRangeException.ThrowIfLessThan(CursorPersistInterval, 1, nameof(CursorPersistInterval));
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

    /// <summary>
    /// An event no newer than the last one verified for its repository, such as a replay after a
    /// reconnect (with <see cref="TypedFirehoseConsumerOptions.SyncVerifier"/>).
    /// </summary>
    Stale,

    /// <summary>
    /// An authentic event for a repository whose chain of commits is broken, which must be fetched
    /// again before its events are delivered (with <see cref="TypedFirehoseConsumerOptions.SyncVerifier"/>).
    /// </summary>
    Desynchronized,
}

/// <summary>An event a stream consumer skipped, reported to <see cref="StreamConsumerOptions.OnEventDropped"/>.</summary>
/// <param name="Reason">Why it was skipped.</param>
/// <param name="Cursor">The event's position in the stream, when it could be read.</param>
/// <param name="Detail">A description, such as the message type or the verification error.</param>
public sealed record DroppedStreamEvent(StreamDropReason Reason, long? Cursor, string? Detail);
