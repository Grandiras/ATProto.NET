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
/// <see cref="LastCursor"/> exactly and filters out the one replayed event.</para>
/// <para>A subscription the server rejects before the WebSocket upgrade — a cursor below the
/// retention floor, a retired zstd dictionary, a malformed filter — is not retried: the
/// <see cref="JetstreamConnectException"/> is rethrown, because reconnecting with the same
/// request would loop forever and silently skip the gap.</para>
/// <para>Delivery is <b>at-least-once across process restarts</b>: the cursor is persisted every
/// <see cref="JetstreamConsumerOptions.CursorPersistInterval"/> events, so events after the last
/// persisted cursor may be redelivered when resuming from the store. Processing must be idempotent.</para>
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
///     CursorStore = new InMemoryFirehoseCursorStore(),
///     MaxReconnectAttempts = -1,
/// });
/// await foreach (var evt in consumer.ConsumeAsync())
/// {
///     if (evt is JetstreamCommitEvent commit)
///         Console.WriteLine($"{commit.Operation} {commit.Uri}");
/// }
/// </code>
/// </example>
public sealed class JetstreamConsumer : IDisposable
{
    private readonly JetstreamConsumerOptions _options;
    private readonly ILogger _logger;
    private readonly Func<long?, CancellationToken, IAsyncEnumerable<JetstreamEvent>> _connectionFactory;
    private bool _disposed;
    private int _eventsSinceLastPersist;

    /// <summary>The <c>time_us</c> of the last delivered event. The reconnect cursor base on
    /// <see cref="JetstreamProtocol.V1"/>.</summary>
    public long? LastTimeUs { get; private set; }

    /// <summary>The sequence number (<see cref="JetstreamEvent.Cursor"/>) of the last delivered
    /// event. The reconnect cursor on <see cref="JetstreamProtocol.V2"/>; null until an event
    /// carrying one has been delivered.</summary>
    public long? LastCursor { get; private set; }

    /// <summary>Whether the consumer is currently receiving events.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Create a managed Jetstream consumer.
    /// </summary>
    /// <param name="options">Consumer configuration.</param>
    public JetstreamConsumer(JetstreamConsumerOptions options)
        : this(options, (cursor, ct) => SubscribeOnce(options, cursor, ct))
    {
    }

    internal JetstreamConsumer(
        JetstreamConsumerOptions options,
        Func<long?, CancellationToken, IAsyncEnumerable<JetstreamEvent>> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = options.Logger ?? NullLogger.Instance;
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Consume Jetstream events with automatic reconnection and cursor persistence.
    /// </summary>
    /// <param name="cursor">Initial cursor to resume from — a sequence number on
    /// <see cref="JetstreamProtocol.V2"/>, a unix-microseconds timestamp on
    /// <see cref="JetstreamProtocol.V1"/>. If null and a cursor store is configured, the stored
    /// cursor is used; otherwise consumption starts live.</param>
    /// <param name="cancellationToken">Cancellation token to stop consuming.</param>
    /// <exception cref="JetstreamConnectException">The server rejected the subscription before
    /// the WebSocket upgrade and retrying it unchanged cannot succeed.</exception>
    public async IAsyncEnumerable<JetstreamEvent> ConsumeAsync(
        long? cursor = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var startCursor = cursor;

        if (startCursor is null && _options.CursorStore is not null)
        {
            startCursor = await _options.CursorStore.GetCursorAsync(
                _options.ResolvedStreamId, cancellationToken);

            if (startCursor.HasValue)
                _logger.LogInformation("Resuming Jetstream from stored cursor {Cursor}", startCursor.Value);
        }

        var v2 = _options.Protocol == JetstreamProtocol.V2;
        var rewindMicros = (long)_options.ReconnectRewind.TotalMicroseconds;
        var reconnectAttempts = 0;
        var firstConnection = true;
        JetstreamConnectException? fatal = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            // v2 cursors are sequence numbers replayed inclusively, so there is nothing to
            // rewind past; v1 cursors are timestamps, which need the in-flight overlap.
            var resumeCursor = v2 ? LastCursor : LastTimeUs is { } last ? last - rewindMicros : null;
            var connectCursor = firstConnection ? startCursor : resumeCursor ?? startCursor;
            firstConnection = false;

            var enumerator = _connectionFactory(connectCursor, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    JetstreamEvent? evt = null;
                    try
                    {
                        if (await enumerator.MoveNextAsync())
                            evt = enumerator.Current;
                    }
                    catch (OperationCanceledException)
                    {
                        // Treated as a normal end of stream; the outer loop observes the token.
                    }
                    catch (JetstreamConnectException ex) when (!ex.IsRetryable)
                    {
                        // The subscription itself was refused; reconnecting would loop.
                        fatal = ex;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Jetstream connection failed");
                    }

                    if (evt is null)
                        break;

                    IsConnected = true;
                    reconnectAttempts = 0;

                    // Skip events the server replayed that were already delivered.
                    if (v2)
                    {
                        if (LastCursor.HasValue && evt.Cursor is { } cursorValue && cursorValue <= LastCursor.Value)
                            continue;
                    }
                    else if (LastTimeUs.HasValue && evt.TimeUs <= LastTimeUs.Value)
                    {
                        continue;
                    }

                    LastTimeUs = evt.TimeUs;
                    if (evt.Cursor is { } seq)
                        LastCursor = seq;

                    if (_options.CursorStore is not null && StoredCursor(evt) is { } storedCursor)
                    {
                        _eventsSinceLastPersist++;
                        if (_eventsSinceLastPersist >= _options.CursorPersistInterval)
                        {
                            await _options.CursorStore.StoreCursorAsync(
                                _options.ResolvedStreamId, storedCursor, cancellationToken);
                            _eventsSinceLastPersist = 0;
                        }
                    }

                    yield return evt;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            IsConnected = false;

            if (fatal is not null || cancellationToken.IsCancellationRequested)
                break;

            reconnectAttempts++;
            if (_options.MaxReconnectAttempts >= 0 && reconnectAttempts > _options.MaxReconnectAttempts)
            {
                _logger.LogError("Max reconnection attempts ({Max}) exceeded", _options.MaxReconnectAttempts);
                break;
            }

            var delay = TimeSpan.FromTicks(_options.ReconnectDelay.Ticks * Math.Min(reconnectAttempts, 6));
            _logger.LogWarning("Jetstream disconnected. Reconnecting in {Delay}s (attempt {Attempt})",
                delay.TotalSeconds, reconnectAttempts);

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Final cursor persist on shutdown
        if (_options.CursorStore is not null && (v2 ? LastCursor : LastTimeUs) is { } finalCursor)
        {
            await _options.CursorStore.StoreCursorAsync(
                _options.ResolvedStreamId, finalCursor, CancellationToken.None);
        }

        if (fatal is not null)
            throw fatal;
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
        using var client = new JetstreamClient(options);
        await foreach (var evt in client.SubscribeAsync(cursor, cancellationToken))
            yield return evt;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
