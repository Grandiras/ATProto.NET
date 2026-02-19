using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

/// <summary>
/// Client for consuming the AT Protocol event stream (firehose) over WebSocket.
/// Connects to <c>com.atproto.sync.subscribeRepos</c> or similar event stream endpoints.
/// </summary>
/// <remarks>
/// <para>The firehose delivers events as CBOR-encoded frames (header + body).
/// This implementation reads frames and exposes them as an <see cref="IAsyncEnumerable{T}"/>.</para>
/// <para>For a managed production consumer, consider using <see cref="FirehoseConsumer"/>.</para>
/// </remarks>
/// <example>
/// <code>
/// var firehose = new FirehoseClient("wss://bsky.network");
/// await foreach (var msg in firehose.SubscribeAsync())
/// {
///     if (msg is CommitEvent commit)
///         Console.WriteLine($"Commit from {commit.Repo}: {commit.Ops?.Count} ops");
/// }
/// </code>
/// </example>
public sealed class FirehoseClient : IDisposable
{
    private readonly Uri _serviceUri;
    private readonly ILogger _logger;
    private ClientWebSocket? _ws;
    private bool _disposed;

    /// <summary>
    /// Create a firehose client connected to the given relay or PDS.
    /// </summary>
    /// <param name="serviceUrl">The WebSocket URL of the relay (e.g., "wss://bsky.network").</param>
    /// <param name="logger">Optional logger.</param>
    public FirehoseClient(string serviceUrl, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUrl);
        _serviceUri = new Uri(serviceUrl.TrimEnd('/'));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Subscribe to the repository event stream.
    /// Returns an async enumerable of firehose messages.
    /// </summary>
    /// <param name="cursor">Optional sequence number to resume from.
    /// If null, starts from the live stream (no backfill).</param>
    /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
    public async IAsyncEnumerable<FirehoseFrame> SubscribeAsync(
        long? cursor = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var endpoint = $"{_serviceUri}/xrpc/com.atproto.sync.subscribeRepos";
        if (cursor.HasValue)
            endpoint += $"?cursor={cursor.Value}";

        _ws = new ClientWebSocket();
        _logger.LogInformation("Connecting to firehose at {Endpoint}", endpoint);

        await _ws.ConnectAsync(new Uri(endpoint), cancellationToken);
        _logger.LogInformation("Connected to firehose");

        var buffer = new byte[1024 * 64]; // 64KB buffer

        while (_ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            FirehoseFrame? frame;
            try
            {
                frame = await ReadFrameAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                _logger.LogWarning("WebSocket connection closed prematurely");
                break;
            }

            if (frame is not null)
                yield return frame;
        }

        _logger.LogInformation("Firehose subscription ended");
    }

    /// <summary>
    /// Subscribe to label events.
    /// </summary>
    public async IAsyncEnumerable<FirehoseFrame> SubscribeLabelsAsync(
        long? cursor = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var endpoint = $"{_serviceUri}/xrpc/com.atproto.label.subscribeLabels";
        if (cursor.HasValue)
            endpoint += $"?cursor={cursor.Value}";

        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(endpoint), cancellationToken);

        var buffer = new byte[1024 * 64];

        while (_ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            FirehoseFrame? frame;
            try
            {
                frame = await ReadFrameAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException)
            {
                break;
            }

            if (frame is not null)
                yield return frame;
        }
    }

    /// <summary>
    /// Disconnect from the event stream.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_ws is { State: WebSocketState.Open })
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing WebSocket");
            }
        }
    }

    private async Task<FirehoseFrame?> ReadFrameAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await _ws!.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        if (ms.Length == 0)
            return null;

        return new FirehoseFrame
        {
            RawData = ms.ToArray(),
            MessageType = result.MessageType,
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ws?.Dispose();
    }
}

/// <summary>
/// A raw frame received from the firehose WebSocket.
/// AT Protocol uses DAG-CBOR encoding for firehose frames.
/// </summary>
public sealed class FirehoseFrame
{
    /// <summary>The raw binary data of the frame.</summary>
    public required byte[] RawData { get; init; }

    /// <summary>The WebSocket message type.</summary>
    public WebSocketMessageType MessageType { get; init; }

    /// <summary>The size of the frame in bytes.</summary>
    public int Size => RawData.Length;
}

/// <summary>
/// A managed firehose consumer that handles reconnection and cursor management.
/// </summary>
public sealed class FirehoseConsumer : IDisposable
{
    private readonly string _serviceUrl;
    private readonly ILogger _logger;
    private readonly TimeSpan _reconnectDelay;
    private readonly int _maxReconnectAttempts;
    private FirehoseClient? _client;
    private bool _disposed;

    /// <summary>The last successfully processed sequence number.</summary>
    public long? LastSeq { get; private set; }

    /// <summary>Whether the consumer is currently connected.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Create a managed firehose consumer.
    /// </summary>
    /// <param name="serviceUrl">The relay/PDS WebSocket URL.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="reconnectDelay">Delay between reconnection attempts. Default: 5 seconds.</param>
    /// <param name="maxReconnectAttempts">Max reconnection attempts. Default: 10. Use -1 for unlimited.</param>
    public FirehoseConsumer(
        string serviceUrl,
        ILogger? logger = null,
        TimeSpan? reconnectDelay = null,
        int maxReconnectAttempts = 10)
    {
        _serviceUrl = serviceUrl;
        _logger = logger ?? NullLogger.Instance;
        _reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(5);
        _maxReconnectAttempts = maxReconnectAttempts;
    }

    /// <summary>
    /// Start consuming the firehose with automatic reconnection.
    /// </summary>
    /// <param name="cursor">Initial cursor to resume from.</param>
    /// <param name="cancellationToken">Cancellation token to stop consuming.</param>
    public async IAsyncEnumerable<FirehoseFrame> ConsumeAsync(
        long? cursor = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var currentCursor = cursor;
        var reconnectAttempts = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            _client?.Dispose();
            _client = new FirehoseClient(_serviceUrl, _logger);

            await foreach (var frame in _client.SubscribeAsync(currentCursor, cancellationToken))
            {
                IsConnected = true;
                reconnectAttempts = 0;
                yield return frame;
            }

            IsConnected = false;

            if (cancellationToken.IsCancellationRequested)
                break;

            reconnectAttempts++;
            if (_maxReconnectAttempts >= 0 && reconnectAttempts > _maxReconnectAttempts)
            {
                _logger.LogError("Max reconnection attempts ({Max}) exceeded", _maxReconnectAttempts);
                break;
            }

            var delay = TimeSpan.FromTicks(_reconnectDelay.Ticks * Math.Min(reconnectAttempts, 6));
            _logger.LogWarning("Firehose disconnected. Reconnecting in {Delay}s (attempt {Attempt})",
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
    }

    /// <summary>
    /// Update the last processed sequence number (for cursor management).
    /// </summary>
    public void Acknowledge(long seq)
    {
        LastSeq = seq;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client?.Dispose();
    }
}
