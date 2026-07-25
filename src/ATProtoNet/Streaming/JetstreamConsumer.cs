using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

/// <summary>
/// A managed Jetstream consumer that handles reconnection, cursor persistence,
/// and duplicate suppression across reconnects.
/// </summary>
/// <remarks>
/// <para>On reconnect, the consumer rewinds the cursor by
/// <see cref="JetstreamConsumerOptions.ReconnectRewind"/> to compensate for events lost
/// in flight, and filters out replayed events it already delivered
/// (<c>time_us &lt;= <see cref="LastTimeUs"/></c>).</para>
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
///     ServiceUrl = "wss://jetstream2.us-east.bsky.network",
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

    /// <summary>The <c>time_us</c> of the last delivered event. Used as the reconnect cursor base.</summary>
    public long? LastTimeUs { get; private set; }

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
    /// <param name="cursor">Initial unix-microseconds cursor to resume from. If null and a cursor
    /// store is configured, the stored cursor is used; otherwise consumption starts live.</param>
    /// <param name="cancellationToken">Cancellation token to stop consuming.</param>
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

        var rewindMicros = (long)_options.ReconnectRewind.TotalMicroseconds;
        var reconnectAttempts = 0;
        var firstConnection = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            var connectCursor = firstConnection || LastTimeUs is null
                ? startCursor
                : LastTimeUs.Value - rewindMicros;
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
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Jetstream connection failed");
                    }

                    if (evt is null)
                        break;

                    IsConnected = true;
                    reconnectAttempts = 0;

                    // Skip events replayed by the reconnect rewind (already delivered).
                    if (LastTimeUs.HasValue && evt.TimeUs <= LastTimeUs.Value)
                        continue;

                    LastTimeUs = evt.TimeUs;

                    if (_options.CursorStore is not null)
                    {
                        _eventsSinceLastPersist++;
                        if (_eventsSinceLastPersist >= _options.CursorPersistInterval)
                        {
                            await _options.CursorStore.StoreCursorAsync(
                                _options.ResolvedStreamId, evt.TimeUs, cancellationToken);
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

            if (cancellationToken.IsCancellationRequested)
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
        if (_options.CursorStore is not null && LastTimeUs.HasValue)
        {
            await _options.CursorStore.StoreCursorAsync(
                _options.ResolvedStreamId, LastTimeUs.Value, CancellationToken.None);
        }
    }

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
