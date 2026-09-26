using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

/// <summary>
/// A managed Jetstream consumer that handles reconnection, cursor persistence,
/// and duplicate suppression across reconnects.
/// </summary>
/// <remarks>
/// <para>How it resumes depends on <see cref="JetstreamConsumerOptions.Protocol"/>. On
/// <see cref="JetstreamProtocol.V1"/> the cursor is a timestamp, so the consumer rewinds it by
/// <see cref="JetstreamConsumerOptions.ReconnectRewind"/> to compensate for events lost
/// in flight and filters out replayed events it already delivered
/// (<c>time_us &lt;= <see cref="LastTimeUs"/></c>). On <see cref="JetstreamProtocol.V2"/> the
/// cursor is a sequence number the server replays inclusively, so the consumer reconnects at
/// <see cref="LastCursor"/> exactly and filters out the one replayed event. A v2 start cursor of
/// 10^15 or more is a timestamp seek (<see cref="JetstreamCursor"/>), which is how a stored v1
/// cursor carries over; the consumer resumes by sequence number from the first event.</para>
/// <para>A subscription the server rejects before the WebSocket upgrade — a cursor below the
/// retention floor, a retired zstd dictionary, a malformed filter — is not retried: the
/// <see cref="JetstreamException"/> is rethrown, because reconnecting with the same
/// request would loop forever and silently skip the gap. Neither is an error frame that is not
/// retryable; one that is (<c>ConsumerTooSlow</c>) is reported to
/// <see cref="StreamConsumerOptions.OnStreamError"/> and followed by a reconnect.</para>
/// <para>Delivery is <b>at-least-once</b>: an event's position is recorded when the caller asks
/// for the next one, and the cursor is saved every
/// <see cref="StreamConsumerOptions.CursorPersistInterval"/> events and when the enumeration ends,
/// so events after the last saved cursor may be delivered again when resuming from the store.
/// Processing must be idempotent. Cancelling the token ends the enumeration normally.</para>
/// <para>Jetstream events carry no MST proofs or signatures and cannot be cryptographically
/// verified — see <see cref="JetstreamEvent"/>.</para>
/// </remarks>
/// <example>
/// <code>
/// var consumer = new JetstreamConsumer(new JetstreamConsumerOptions
/// {
///     ServiceUrl = JetstreamEndpoints.UsEast,
///     Protocol = JetstreamProtocol.V2,
///     WantedCollections = ["app.bsky.feed.post", "app.bsky.feed.like"],
///     CursorStore = new InMemoryStreamCursorStore(),
///     Reconnect = new StreamReconnectPolicy { MaxAttempts = null },
/// });
/// await foreach (var evt in consumer.ConsumeAsync())
/// {
///     if (evt is JetstreamCommitEvent commit)
///         Console.WriteLine($"{commit.Operation} {commit.Uri}");
/// }
/// </code>
/// </example>
public sealed class JetstreamConsumer
{
    private readonly JetstreamConsumerOptions _options;
    private readonly ILogger _logger;
    private readonly Func<long?, CancellationToken, IAsyncEnumerable<JetstreamEvent>> _connectionFactory;

    /// <summary>The <c>time_us</c> of the last delivered event. The reconnect cursor base on
    /// <see cref="JetstreamProtocol.V1"/>.</summary>
    public long? LastTimeUs { get; private set; }

    /// <summary>The sequence number (<see cref="JetstreamEvent.Cursor"/>) of the last delivered
    /// event. The reconnect cursor on <see cref="JetstreamProtocol.V2"/>; null until an event
    /// carrying one has been delivered.</summary>
    public long? LastCursor { get; private set; }

    /// <summary>
    /// Create a managed Jetstream consumer.
    /// </summary>
    /// <param name="options">Consumer configuration.</param>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    public JetstreamConsumer(JetstreamConsumerOptions options)
        : this(options, (cursor, ct) => SubscribeOnce(options, cursor, ct))
    {
    }

    internal JetstreamConsumer(
        JetstreamConsumerOptions options,
        Func<long?, CancellationToken, IAsyncEnumerable<JetstreamEvent>> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _logger = options.Logger ?? NullLogger.Instance;
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Consume Jetstream events with automatic reconnection and cursor persistence.
    /// </summary>
    /// <param name="cursor">Initial cursor to resume from — a sequence number on
    /// <see cref="JetstreamProtocol.V2"/> (or a timestamp from <see cref="JetstreamCursor.FromTimestamp"/>),
    /// a unix-microseconds timestamp on <see cref="JetstreamProtocol.V1"/>. If null and a cursor
    /// store is configured, the stored cursor is used; otherwise consumption starts live.</param>
    /// <param name="cancellationToken">Cancellation token to stop consuming.</param>
    /// <exception cref="JetstreamException">The server rejected the subscription before the
    /// WebSocket upgrade, or sent an error frame, and retrying it unchanged cannot succeed.</exception>
    /// <exception cref="EventStreamException">Every reconnect attempt
    /// <see cref="StreamConsumerOptions.Reconnect"/> allows failed.</exception>
    public async IAsyncEnumerable<JetstreamEvent> ConsumeAsync(
        long? cursor = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var v2 = _options.Protocol == JetstreamProtocol.V2;
        var tracker = new CursorTracker(
            _options.CursorStore, _options.ResolvedStreamId, _options.CursorPersistInterval, _logger);

        var startCursor = cursor;
        if (startCursor is null)
        {
            try
            {
                startCursor = await tracker.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            if (startCursor.HasValue)
                _logger.LogInformation("Resuming Jetstream from stored cursor {Cursor}", startCursor.Value);
        }

        // A v2 sequence cursor is replayed inclusively, so it is also the floor below which events
        // were already delivered. A timestamp seek is only a server-side position, no floor for
        // the sequence numbers that follow: the first event sets that.
        var timestampSeek = v2 && startCursor is { } start && JetstreamCursor.IsTimestamp(start);
        tracker.Start(timestampSeek ? null : startCursor);
        var seqFloor = v2 && !timestampSeek ? startCursor : null;

        var rewindMicros = (long)_options.ReconnectRewind.TotalMicroseconds;
        var backoff = new ReconnectBackoff(_options.Reconnect, _logger, "Jetstream");
        var firstConnection = true;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // v2 cursors are sequence numbers replayed inclusively, so there is nothing to
                // rewind past; v1 cursors are timestamps, which need the in-flight overlap.
                var resumeCursor = v2 ? LastCursor : LastTimeUs is { } last ? last - rewindMicros : null;
                var connectCursor = firstConnection ? startCursor : resumeCursor ?? startCursor;
                firstConnection = false;

                Exception? failure = null;
                var enumerator = _connectionFactory(connectCursor, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        JetstreamEvent evt;
                        try
                        {
                            if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                                break;
                            evt = enumerator.Current;
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

                        backoff.Reset();

                        // Skip events the server replayed that were already delivered.
                        if (v2)
                        {
                            if (seqFloor.HasValue && evt.Cursor is { } seq && seq <= seqFloor.Value)
                                continue;
                        }
                        else if (LastTimeUs.HasValue && evt.TimeUs <= LastTimeUs.Value)
                        {
                            continue;
                        }

                        LastTimeUs = evt.TimeUs;
                        if (evt.Cursor is { } eventCursor)
                            LastCursor = seqFloor = eventCursor;

                        yield return evt;

                        // Recorded once the caller asks for the next event, having handled this one.
                        if (StoredCursor(evt) is { } stored)
                            tracker.Advance(stored);
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                if (failure is EventStreamException { IsRetryable: false })
                    ExceptionDispatchInfo.Throw(failure);

                if (!await backoff.WaitAsync(failure, cancellationToken).ConfigureAwait(false))
                    break;
            }
        }
        finally
        {
            await tracker.FlushAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The value to persist for an event: its sequence number on
    /// <see cref="JetstreamProtocol.V2"/>, its <c>time_us</c> on
    /// <see cref="JetstreamProtocol.V1"/>. Null when a v2 event carried no sequence number,
    /// which would otherwise store a resume position the server cannot honour.
    /// </summary>
    private long? StoredCursor(JetstreamEvent evt)
        => _options.Protocol == JetstreamProtocol.V2 ? evt.Cursor : evt.TimeUs;

    private static async IAsyncEnumerable<JetstreamEvent> SubscribeOnce(
        JetstreamConsumerOptions options,
        long? cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = new JetstreamClient(options);
        await using (client.ConfigureAwait(false))
        {
            await foreach (var evt in client.SubscribeAsync(cursor, cancellationToken).ConfigureAwait(false))
                yield return evt;
        }
    }
}
