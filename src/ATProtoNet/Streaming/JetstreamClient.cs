using System.Runtime.CompilerServices;
using ATProtoNet.Http;
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
/// <para>This client handles one connection with no reconnect logic; each subscription owns its
/// socket, and disposing the client ends them all. For a managed production consumer, use
/// <see cref="JetstreamConsumer"/>. An enumeration ends normally when the server closes the
/// connection or the token is cancelled, and throws a <see cref="JetstreamException"/> when the
/// server refuses the subscription or sends an error frame.</para>
/// </remarks>
/// <example>
/// <code>
/// await using var client = new JetstreamClient(new JetstreamConsumerOptions
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
public sealed class JetstreamClient : IAsyncDisposable
{
    /// <summary>The v2 endpoint path — the subscription Lexicon's canonical XRPC route.</summary>
    private const string V2Path = "/xrpc/network.bsky.jetstream.subscribeEvents";

    /// <summary>The WebSocket subprotocol the v2 wire is framed under (atproto proposal 0015).</summary>
    private const string V2SubProtocol = "xrpc.v1.json";

    private readonly JetstreamConsumerOptions _options;
    private readonly ILogger _logger;
    private readonly StreamConnector _connector;
    private readonly CancellationTokenSource _disposed = new();

    /// <summary>
    /// Create a Jetstream client.
    /// </summary>
    /// <param name="options">Subscription configuration.</param>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    public JetstreamClient(JetstreamConsumerOptions options)
        : this(options, StreamSocket.Connector)
    {
    }

    internal JetstreamClient(JetstreamConsumerOptions options, StreamConnector connector)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _logger = options.Logger ?? NullLogger.Instance;
        _connector = connector;
    }

    /// <summary>
    /// Subscribe to the Jetstream event stream.
    /// </summary>
    /// <param name="cursor">Optional resume position: a sequence number on
    /// <see cref="JetstreamProtocol.V2"/>, where a value of 10^15 or more is read as a
    /// unix-microseconds timestamp instead (see <see cref="JetstreamCursor"/>), or a
    /// unix-microseconds timestamp on <see cref="JetstreamProtocol.V1"/>. If null, starts from the
    /// live stream.</param>
    /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
    /// <exception cref="JetstreamException">The server refused the subscription (see
    /// <see cref="EventStreamException.IsRetryable"/>) or sent an error frame.</exception>
    public async IAsyncEnumerable<JetstreamEvent> SubscribeAsync(
        long? cursor = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposed.Token);

        var endpoint = BuildSubscribeUri(_options, cursor);
        var socketOptions = new StreamSocketOptions(
            SubProtocol: _options.Protocol == JetstreamProtocol.V2 ? V2SubProtocol : null);

        _logger.LogInformation("Connecting to Jetstream at {Endpoint}", endpoint);

        var connection = _connector(endpoint, socketOptions, linked.Token).GetAsyncEnumerator(linked.Token);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                StreamSocketMessage message;
                try
                {
                    if (!await connection.MoveNextAsync().ConfigureAwait(false))
                        break;
                    message = connection.Current;
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    break;
                }
                catch (EventStreamException ex) when (ex is not JetstreamException)
                {
                    // The v2 endpoint validates the subscription before the upgrade and rejects a
                    // stale cursor, a retired dictionary, or a malformed filter with an HTTP status.
                    throw new JetstreamException(
                        $"Jetstream rejected the subscription at {endpoint.GetLeftPart(UriPartial.Path)}" +
                        (ex.StatusCode is { } status ? $" with HTTP {status}." : "."),
                        ex.StatusCode,
                        ex.Error,
                        innerException: ex);
                }

                var frame = Parse(message);

                if (frame.Error is { } error)
                {
                    // An error frame is terminal: the server closes the stream right after it.
                    _logger.LogWarning("Jetstream stream error {Error}: {Message}", error.Error, error.Message);
                    _options.OnStreamError?.Invoke(error);
                    throw new JetstreamException(
                        $"Jetstream sent error {error.Error}" + (error.Message is null ? "." : $": {error.Message}"),
                        error: error.Error);
                }

                if (frame.Info is { } info)
                {
                    _logger.LogInformation("Jetstream info {Name}: {Message}", info.Name, info.Message);
                    _options.OnInfo?.Invoke(info);
                    continue;
                }

                if (frame.Event is { } evt)
                    yield return evt;
            }
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        _logger.LogInformation("Jetstream subscription ended");
    }

    /// <summary>Ends every subscription still running on this client.</summary>
    public ValueTask DisposeAsync()
    {
        if (!_disposed.IsCancellationRequested)
            _disposed.Cancel();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Parses a message straight from the receive buffer. Only a compressed frame is copied, by
    /// the decompressor.
    /// </summary>
    private JetstreamFrame Parse(StreamSocketMessage message)
    {
        var json = message.IsBinary && _options.Decompressor is { } decompressor
            ? decompressor.Decompress(message.Data.Span)
            : message.Data;

        var frame = JetstreamEventParser.Parse(json, _options.Protocol, out var dropped);
        if (dropped is { } reason)
        {
            _logger.LogDebug("Skipped Jetstream frame ({Reason}, {Size} bytes)", reason, json.Length);
            _options.OnEventDropped?.Invoke(new DroppedStreamEvent(reason, null, null));
        }

        return frame;
    }

    internal static Uri BuildSubscribeUri(JetstreamConsumerOptions options, long? cursor)
    {
        options.Validate();
        var v2 = options.Protocol == JetstreamProtocol.V2;

        var baseUrl = options.ServiceUrl.TrimEnd('/');
        if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            baseUrl = "wss://" + baseUrl["https://".Length..];
        else if (baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            baseUrl = "ws://" + baseUrl["http://".Length..];

        var query = new XrpcParams()
            .AddAll(v2 ? "collections" : "wantedCollections", options.WantedCollections)
            .AddAll(v2 ? "dids" : "wantedDids", options.WantedDids?.Select(did => did.Value))
            .AddAll("kinds", v2 ? options.WantedKinds?.Select(JetstreamKinds.Name) : null)
            .Add("cursor", cursor)
            .Add("maxMessageSizeBytes", options.MaxMessageSizeBytes);

        if (options.Decompressor is not null)
        {
            if (v2)
                query.Add("zstdDictionary", options.ZstdDictionaryId);
            else
                query.Add("compress", "true");
        }

        return new Uri($"{baseUrl}{(v2 ? V2Path : "/subscribe")}{query.ToQueryString()}");
    }
}
