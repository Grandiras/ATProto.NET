using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Streaming;

/// <summary>
/// What one AT Protocol event stream (firehose, labels, chat moderation) does with its frames. The
/// shared <see cref="EventStreamLoop"/> handles the connection, the header, error frames and
/// reconnecting; the handler owns the position and turns message bodies into messages.
/// </summary>
internal abstract class EventStreamHandler<T> where T : class
{
    /// <summary>The stream's name in log and exception messages, e.g. <c>firehose</c>.</summary>
    public abstract string Stream { get; }

    /// <summary>
    /// The endpoint and upgrade options for the next connection, from the current position.
    /// </summary>
    public abstract ValueTask<(Uri Endpoint, StreamSocketOptions Options)> ConnectAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Turns an <c>op = 1</c> frame into a message to deliver, or returns null to skip it, having
    /// recorded any position the frame carried.
    /// </summary>
    /// <param name="type">The header's <c>t</c>.</param>
    /// <param name="body">The frame body; valid only until this call returns.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public abstract ValueTask<T?> HandleAsync(string type, ReadOnlyMemory<byte> body, CancellationToken cancellationToken);

    /// <summary>
    /// The caller took <paramref name="message"/> and asked for the next one, so its position may
    /// be recorded: at-least-once delivery.
    /// </summary>
    public virtual void Delivered(T message)
    {
    }

    /// <summary>
    /// <see cref="Delivered"/>, for a handler that has to await what it records.
    /// </summary>
    public virtual ValueTask DeliveredAsync(T message, CancellationToken cancellationToken)
    {
        Delivered(message);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// A message the handler has ready that did not come from the frame just read, such as work it
    /// finished in the background; delivered before the next frame is read. Null when there is none.
    /// </summary>
    public virtual ValueTask<T?> NextPendingAsync(CancellationToken cancellationToken) => default;

    /// <summary>A frame was skipped because it could not be read.</summary>
    public abstract void Dropped(StreamDropReason reason, long? cursor, string? detail);
}

/// <summary>
/// The connect, read and reconnect loop of the AT Protocol event streams.
/// </summary>
internal static class EventStreamLoop
{
    /// <summary>
    /// Reads the stream through <paramref name="handler"/>, reconnecting per
    /// <paramref name="reconnect"/>, or over a single connection when it is null.
    /// </summary>
    /// <remarks>
    /// Cancelling ends the enumeration normally. An error frame is reported to
    /// <paramref name="onStreamError"/>; it ends a single connection with an
    /// <see cref="EventStreamException"/>, and a reconnecting run reconnects unless the error is
    /// not retryable.
    /// </remarks>
    public static async IAsyncEnumerable<T> RunAsync<T>(
        EventStreamHandler<T> handler,
        StreamConnector connector,
        StreamReconnectPolicy? reconnect,
        ILogger logger,
        Action<EventStreamError>? onStreamError,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : class
    {
        var backoff = reconnect is null ? null : new ReconnectBackoff(reconnect, logger, handler.Stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            Exception? failure = null;
            IAsyncEnumerator<StreamSocketMessage>? connection = null;

            try
            {
                var (endpoint, options) = await handler.ConnectAsync(cancellationToken).ConfigureAwait(false);
                logger.LogDebug("Connecting to the {Stream} at {Endpoint}", handler.Stream, endpoint);
                connection = connector(endpoint, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            if (connection is not null)
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        T? pending;
                        try
                        {
                            pending = await handler.NextPendingAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        if (pending is not null)
                        {
                            yield return pending;
                            await handler.DeliveredAsync(pending, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        StreamSocketMessage frame;
                        try
                        {
                            if (!await connection.MoveNextAsync().ConfigureAwait(false))
                                break;
                            frame = connection.Current;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            failure = ex;
                            break;
                        }

                        T? message;
                        EventStreamError? error;
                        try
                        {
                            (message, error) = await ReadFrameAsync(handler, frame.Data, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        if (error is not null)
                        {
                            logger.LogWarning("The {Stream} sent error {Error}: {Message}", handler.Stream, error.Error, error.Message);
                            onStreamError?.Invoke(error);
                            failure = EventStreamException.FromErrorFrame(handler.Stream, error);
                            break;
                        }

                        // A frame arrived, so the connection works: the next drop starts a fresh count.
                        backoff?.Reset();

                        if (message is null)
                            continue;

                        yield return message;
                        await handler.DeliveredAsync(message, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }

            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (backoff is null || failure is EventStreamException { IsRetryable: false })
            {
                if (failure is not null)
                    ExceptionDispatchInfo.Throw(failure);
                yield break;
            }

            if (!await backoff.WaitAsync(failure, cancellationToken).ConfigureAwait(false))
                yield break;
        }
    }

    /// <summary>
    /// Reads a frame's header, and hands a message frame to the handler or returns an error
    /// frame's error.
    /// </summary>
    private static async ValueTask<(T? Message, EventStreamError? Error)> ReadFrameAsync<T>(
        EventStreamHandler<T> handler, ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
        where T : class
    {
        if (!EventStreamFrame.TryReadHeader(frame, out var op, out var type, out var bodyOffset))
        {
            handler.Dropped(StreamDropReason.Malformed, null, "The frame header could not be read.");
            return default;
        }

        var body = frame[bodyOffset..];

        if (op == EventStreamFrame.ErrorOp)
            return (null, EventStreamFrame.ReadError(body) ?? new EventStreamError("Unknown", null));

        if (op != EventStreamFrame.MessageOp || string.IsNullOrEmpty(type))
        {
            handler.Dropped(StreamDropReason.Malformed, EventStreamFrame.ReadSeq(body), $"Unexpected frame op {op}.");
            return default;
        }

        return (await handler.HandleAsync(type, body, cancellationToken).ConfigureAwait(false), null);
    }
}
