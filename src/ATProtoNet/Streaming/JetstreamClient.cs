using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

/// <summary>
/// Client for consuming a Jetstream event stream over a single WebSocket connection.
/// </summary>
/// <remarks>
/// <para>Jetstream is a JSON alternative to the binary firehose with <b>server-side</b>
/// collection and DID filtering — ideal for indexing a small set of collections without
/// downloading the whole network's commit stream.</para>
/// <para>Jetstream events carry no MST proofs or signatures and cannot be cryptographically
/// verified; use the binary firehose (<see cref="TypedFirehoseConsumer"/>) when verification
/// matters.</para>
/// <para>This client handles one connection with no reconnect logic.
/// For a managed production consumer, use <see cref="JetstreamConsumer"/>.</para>
/// </remarks>
/// <example>
/// <code>
/// var client = new JetstreamClient(new JetstreamConsumerOptions
/// {
///     ServiceUrl = "wss://jetstream2.us-east.bsky.network",
///     WantedCollections = ["app.bsky.feed.post"],
/// });
/// await foreach (var evt in client.SubscribeAsync())
/// {
///     if (evt is JetstreamCommitEvent commit)
///         Console.WriteLine($"{commit.Operation} {commit.Uri}");
/// }
/// </code>
/// </example>
public sealed class JetstreamClient : IDisposable
{
    private const int MaxWantedCollections = 100;
    private const int MaxWantedDids = 10_000;

    private readonly JetstreamConsumerOptions _options;
    private readonly ILogger _logger;
    private ClientWebSocket? _ws;
    private bool _disposed;

    /// <summary>
    /// Create a Jetstream client.
    /// </summary>
    /// <param name="options">Subscription configuration.</param>
    public JetstreamClient(JetstreamConsumerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ServiceUrl);
        _options = options;
        _logger = options.Logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Subscribe to the Jetstream event stream.
    /// </summary>
    /// <param name="cursor">Optional unix-microseconds timestamp to resume from.
    /// If null, starts from the live stream.</param>
    /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
    public async IAsyncEnumerable<JetstreamEvent> SubscribeAsync(
        long? cursor = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var endpoint = BuildSubscribeUri(_options, cursor);

        _ws = new ClientWebSocket();
        _logger.LogInformation("Connecting to Jetstream at {Endpoint}", endpoint);

        await _ws.ConnectAsync(endpoint, cancellationToken);
        _logger.LogInformation("Connected to Jetstream");

        var buffer = new byte[1024 * 64];

        while (_ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            byte[]? payload;
            try
            {
                payload = await ReadMessageAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "Jetstream WebSocket error");
                break;
            }

            if (payload is null)
                break;

            var evt = JetstreamEventParser.Parse(payload);
            if (evt is not null)
                yield return evt;
            else
                _logger.LogDebug("Skipped unparseable Jetstream frame ({Size} bytes)", payload.Length);
        }

        _logger.LogInformation("Jetstream subscription ended");
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
                _logger.LogWarning(ex, "Error closing Jetstream WebSocket");
            }
        }
    }

    internal static Uri BuildSubscribeUri(JetstreamConsumerOptions options, long? cursor)
    {
        if (options.WantedCollections is { Count: > MaxWantedCollections })
            throw new ArgumentException(
                $"Jetstream accepts at most {MaxWantedCollections} wantedCollections entries " +
                $"(got {options.WantedCollections.Count}).");
        if (options.WantedDids is { Count: > MaxWantedDids })
            throw new ArgumentException(
                $"Jetstream accepts at most {MaxWantedDids} wantedDids entries " +
                $"(got {options.WantedDids.Count}).");

        var baseUrl = options.ServiceUrl.TrimEnd('/');
        if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            baseUrl = "wss://" + baseUrl["https://".Length..];
        else if (baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            baseUrl = "ws://" + baseUrl["http://".Length..];

        var query = new StringBuilder();

        if (options.WantedCollections is not null)
        {
            foreach (var collection in options.WantedCollections)
                Append(query, "wantedCollections", collection);
        }

        if (options.WantedDids is not null)
        {
            foreach (var did in options.WantedDids)
                Append(query, "wantedDids", did);
        }

        if (cursor.HasValue)
            Append(query, "cursor", cursor.Value.ToString());

        if (options.MaxMessageSizeBytes.HasValue)
            Append(query, "maxMessageSizeBytes", options.MaxMessageSizeBytes.Value.ToString());

        if (options.Decompressor is not null)
            Append(query, "compress", "true");

        return new Uri($"{baseUrl}/subscribe{query}");

        static void Append(StringBuilder query, string name, string value)
        {
            query.Append(query.Length == 0 ? '?' : '&');
            query.Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }
    }

    private async Task<byte[]?> ReadMessageAsync(byte[] buffer, CancellationToken cancellationToken)
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

        var payload = ms.ToArray();

        if (result.MessageType == WebSocketMessageType.Binary && _options.Decompressor is not null)
            payload = _options.Decompressor.Decompress(payload);

        return payload;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ws?.Dispose();
    }
}
