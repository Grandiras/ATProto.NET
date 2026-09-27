namespace ATProtoNet.Streaming;

/// <summary>
/// An error frame a server sent on an event stream before closing it: <c>op = -1</c> on the
/// firehose, label and chat moderation streams, or Jetstream v2's <c>error</c> envelope.
/// </summary>
/// <param name="Error">The error name, such as <c>FutureCursor</c> or <c>ConsumerTooSlow</c>.</param>
/// <param name="Message">A human-readable description, if the server sent one.</param>
public sealed record EventStreamError(string Error, string? Message);

/// <summary>
/// Thrown when an event stream fails in a way the consumer cannot recover from by itself: the
/// server sent an error frame, refused the subscription, or kept dropping the connection until the
/// <see cref="StreamReconnectPolicy"/> gave up.
/// </summary>
/// <remarks>
/// When a consumer throws it, and when it handles a failure itself, is described on
/// <see cref="StreamConsumerOptions"/>.
/// </remarks>
public class EventStreamException : AtProtoException
{
    /// <summary>Creates an event stream exception.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="error">The protocol error name, if the server sent one.</param>
    /// <param name="statusCode">The HTTP status the server answered with, if any.</param>
    /// <param name="innerException">The underlying failure, if any.</param>
    public EventStreamException(
        string message,
        string? error = null,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        StatusCode = statusCode;
    }

    /// <summary>
    /// The protocol error name: an error frame's <c>error</c> (<c>FutureCursor</c>,
    /// <c>ConsumerTooSlow</c>, …), or the XRPC error a refused request answered with. Null when
    /// the failure carried none.
    /// </summary>
    public string? Error { get; }

    /// <summary>The HTTP status code the server answered the subscription or request with, if any.</summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Whether retrying the same subscription could succeed. False for <c>FutureCursor</c> (the
    /// cursor is ahead of the server, so every retry fails the same way) and for a 4xx status
    /// other than 408 and 429, which reject the request itself.
    /// </summary>
    public bool IsRetryable =>
        Error != EventStreamErrors.FutureCursor
        && StatusCode is not (>= 400 and < 500 and not 408 and not 429);

    internal static EventStreamException FromErrorFrame(string stream, EventStreamError error) =>
        new($"The {stream} sent error {error.Error}" + (error.Message is null ? "." : $": {error.Message}"),
            error.Error);
}

/// <summary>The error names the AT Protocol event streams declare.</summary>
public static class EventStreamErrors
{
    /// <summary>The requested cursor is ahead of the server's latest sequence number.</summary>
    public const string FutureCursor = "FutureCursor";

    /// <summary>The consumer fell too far behind and the server dropped it; reconnect from the last cursor.</summary>
    public const string ConsumerTooSlow = "ConsumerTooSlow";
}
