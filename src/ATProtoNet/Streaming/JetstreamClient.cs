using System.Globalization;
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
///     ServiceUrl = JetstreamEndpoints.UsEast,
///     Protocol = JetstreamProtocol.V2,
///     WantedCollections = ["app.bsky.feed.post"],
///     WantedKinds = [JetstreamEventKind.Commit],
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
    private const long MaxMaxMessageSizeBytes = 4_294_967_295;

    /// <summary>The v2 endpoint path — the subscription Lexicon's canonical XRPC route.</summary>
    private const string V2Path = "/xrpc/network.bsky.jetstream.subscribeEvents";

    /// <summary>The WebSocket subprotocol the v2 wire is framed under (atproto proposal 0015).</summary>
    private const string V2SubProtocol = "xrpc.v1.json";

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
    /// <param name="cursor">Optional resume position: a sequence number on
    /// <see cref="JetstreamProtocol.V2"/> (a value of 1e15 or greater is read as a
    /// unix-microseconds timestamp instead), or a unix-microseconds timestamp on
    /// <see cref="JetstreamProtocol.V1"/>. If null, starts from the live stream.</param>
    /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
    public async IAsyncEnumerable<JetstreamEvent> SubscribeAsync(
        long? cursor = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var endpoint = BuildSubscribeUri(_options, cursor);

        _ws = new ClientWebSocket();
        _ws.Options.CollectHttpResponseDetails = true;
        if (_options.Protocol == JetstreamProtocol.V2)
            _ws.Options.AddSubProtocol(V2SubProtocol);

        _logger.LogInformation("Connecting to Jetstream at {Endpoint}", endpoint);

        try
        {
            await _ws.ConnectAsync(endpoint, cancellationToken);
        }
        catch (WebSocketException ex)
        {
            // The v2 endpoint validates the subscription before the upgrade and rejects a
            // stale cursor, a retired dictionary, or a malformed filter with an HTTP status.
            int? status = _ws.HttpStatusCode != 0 ? (int)_ws.HttpStatusCode : null;
            throw new JetstreamConnectException(
                $"Jetstream rejected the subscription at {endpoint}" +
                (status is null ? "." : $" with HTTP {status}."),
                status,
                ex);
        }

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

            var frame = JetstreamEventParser.ParseFrame(payload, _options.Protocol);

            if (frame.Error is { } error)
            {
                // An error frame is terminal: the server closes the stream right after it.
                _logger.LogWarning("Jetstream stream error {Error}: {Message}", error.Error, error.Message);
                _options.OnStreamError?.Invoke(error);
                break;
            }

            if (frame.Info is { } info)
            {
                _logger.LogInformation("Jetstream info {Name}: {Message}", info.Name, info.Message);
                _options.OnInfo?.Invoke(info);
                continue;
            }

            if (frame.Event is { } evt)
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
        if (options.MaxMessageSizeBytes is < 0 or > MaxMaxMessageSizeBytes)
            throw new ArgumentException(
                $"MaxMessageSizeBytes must be between 0 and {MaxMaxMessageSizeBytes} " +
                $"(got {options.MaxMessageSizeBytes}).");

        var v2 = options.Protocol == JetstreamProtocol.V2;

        if (!v2)
        {
            if (options.WantedKinds is { Count: > 0 })
                throw new ArgumentException(
                    "WantedKinds requires JetstreamProtocol.V2; the v1 wire has no kinds filter.");
            if (options.ZstdDictionaryId is not null)
                throw new ArgumentException(
                    "ZstdDictionaryId requires JetstreamProtocol.V2; the v1 wire negotiates " +
                    "compression with compress=true and an unversioned dictionary.");
        }
        else
        {
            // The server rejects this pre-upgrade, since a collection filter that can never
            // match a delivered kind is always a mistake. Fail before opening the socket.
            if (options.WantedCollections is { Count: > 0 }
                && options.WantedKinds is { Count: > 0 } kinds
                && !kinds.Contains(JetstreamEventKind.Commit))
                throw new ArgumentException(
                    "WantedCollections only constrains commit events, so it cannot be combined " +
                    "with a WantedKinds list that excludes JetstreamEventKind.Commit.");
            if (options.Decompressor is not null && options.ZstdDictionaryId is null)
                throw new ArgumentException(
                    "JetstreamProtocol.V2 compression is dictionary-versioned: set ZstdDictionaryId " +
                    "to the id of the dictionary the decompressor was built with " +
                    $"(see {nameof(JetstreamDictionaryClient)}).");
            if (options.ZstdDictionaryId is not null && options.Decompressor is null)
                throw new ArgumentException(
                    "ZstdDictionaryId makes the server send binary zstd frames, so it requires " +
                    "a Decompressor to read them.");
        }

        var baseUrl = options.ServiceUrl.TrimEnd('/');
        if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            baseUrl = "wss://" + baseUrl["https://".Length..];
        else if (baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            baseUrl = "ws://" + baseUrl["http://".Length..];

        var query = new StringBuilder();

        if (options.WantedCollections is not null)
        {
            foreach (var collection in options.WantedCollections)
                Append(query, v2 ? "collections" : "wantedCollections", collection);
        }

        if (options.WantedDids is not null)
        {
            foreach (var did in options.WantedDids)
                Append(query, v2 ? "dids" : "wantedDids", did);
        }

        if (v2 && options.WantedKinds is not null)
        {
            foreach (var kind in options.WantedKinds)
                Append(query, "kinds", KindName(kind));
        }

        if (cursor.HasValue)
            Append(query, "cursor", cursor.Value.ToString(CultureInfo.InvariantCulture));

        if (options.MaxMessageSizeBytes.HasValue)
            Append(query, "maxMessageSizeBytes",
                options.MaxMessageSizeBytes.Value.ToString(CultureInfo.InvariantCulture));

        if (options.Decompressor is not null)
        {
            if (v2)
                Append(query, "zstdDictionary",
                    options.ZstdDictionaryId!.Value.ToString(CultureInfo.InvariantCulture));
            else
                Append(query, "compress", "true");
        }

        return new Uri($"{baseUrl}{(v2 ? V2Path : "/subscribe")}{query}");

        static void Append(StringBuilder query, string name, string value)
        {
            query.Append(query.Length == 0 ? '?' : '&');
            query.Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }
    }

    /// <summary>The wire name of an event kind — the <c>$type</c> fragment the server filters on.</summary>
    private static string KindName(JetstreamEventKind kind) => kind switch
    {
        JetstreamEventKind.Commit => "commit",
        JetstreamEventKind.Identity => "identity",
        JetstreamEventKind.Account => "account",
        JetstreamEventKind.Sync => "sync",
        _ => throw new ArgumentException($"Unknown Jetstream event kind: {kind}", nameof(kind)),
    };

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
