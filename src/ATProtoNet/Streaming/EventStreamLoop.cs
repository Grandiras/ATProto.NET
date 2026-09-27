using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

/// <summary>
/// What one stream consumer does with its messages. The shared <see cref="EventStreamLoop"/>
/// handles connecting, error frames and reconnecting; the handler owns the position and turns
/// messages into what it delivers.
/// </summary>
internal abstract class EventStreamHandler<T>(StreamConsumerOptions options) where T : class
{
    public StreamConsumerOptions Options => options;

    public ILogger Logger { get; } = options.Logger ?? NullLogger.Instance;

    /// <summary>The stream's name in log and exception messages, e.g. <c>firehose</c>.</summary>
    public abstract string Stream { get; }

    /// <summary>The endpoint and upgrade options for the next connection, from the current position.</summary>
    public abstract ValueTask<(Uri Endpoint, StreamSocketOptions Options)> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads one message into what to deliver, or null to skip it, having recorded any position it
    /// carried; or into the error an error frame carries, which ends the connection.
    /// </summary>
    /// <param name="message">The message; its bytes are valid only until this call returns.</param>
    public abstract ValueTask<(T? Message, EventStreamError? Error)> ReadAsync(
        StreamSocketMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// The caller took <paramref name="message"/> and asked for the next one, so its position may
    /// be recorded: at-least-once delivery.
    /// </summary>
    public virtual void Delivered(T message)
    {
    }

    /// <summary><see cref="Delivered"/>, for a handler that has to await what it records.</summary>
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

    /// <summary>A message was skipped because it could not be delivered.</summary>
    public virtual void Dropped(StreamDropReason reason, long? cursor, string? detail)
    {
        Logger.LogDebug("Skipped {Stream} message {Cursor} ({Reason}): {Detail}", Stream, cursor, reason, detail);
        Options.OnEventDropped?.Invoke(new DroppedStreamEvent(reason, cursor, detail));
    }

    /// <summary>
    /// The exception a failure ends the connection with: one the connection threw, or the
    /// <see cref="EventStreamException"/> of an error frame.
    /// </summary>
    public virtual Exception Failed(Exception failure) => failure;
}

/// <summary>
/// The connect, read and reconnect loop every stream consumer runs, with the contract
/// <see cref="StreamConsumerOptions"/> describes.
/// </summary>
internal static class EventStreamLoop
{
    /// <summary>Reads the stream through <paramref name="handler"/>, reconnecting per its options.</summary>
    public static async IAsyncEnumerable<T> RunAsync<T>(
        EventStreamHandler<T> handler,
        StreamConnector connector,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : class
    {
        var logger = handler.Logger;
        var backoff = new ReconnectBackoff(handler.Options.Reconnect, logger, handler.Stream);

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
                failure = handler.Failed(ex);
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
                            failure = handler.Failed(ex);
                            break;
                        }

                        T? message;
                        EventStreamError? error;
                        try
                        {
                            (message, error) = await handler.ReadAsync(frame, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        if (error is not null)
                        {
                            logger.LogWarning("The {Stream} sent error {Error}: {Message}", handler.Stream, error.Error, error.Message);
                            handler.Options.OnStreamError?.Invoke(error);
                            failure = handler.Failed(EventStreamException.FromErrorFrame(handler.Stream, error));
                            break;
                        }

                        // A frame arrived, so the connection works: the next drop starts a fresh count.
                        backoff.Reset();

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

            if (failure is EventStreamException { IsRetryable: false })
                ExceptionDispatchInfo.Throw(failure);

            if (!await backoff.WaitAsync(failure, cancellationToken).ConfigureAwait(false))
                yield break;
        }
    }
}
