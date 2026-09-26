using System.Runtime.CompilerServices;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

/// <summary>
/// Reads an AT Protocol event stream over a single WebSocket connection, with no reconnecting and
/// no cursor persistence: the repository stream (<c>com.atproto.sync.subscribeRepos</c>, the
/// firehose) of a relay or PDS, or a labeler's label stream (<c>com.atproto.label.subscribeLabels</c>).
/// </summary>
/// <remarks>
/// <para>For a long-running consumer, use <see cref="TypedFirehoseConsumer"/> or
/// <see cref="LabelStreamConsumer"/>, which reconnect and persist the cursor.</para>
/// <para>Each subscription owns its connection, so several can run at once; disposing the client
/// ends them all. An enumeration ends normally when the server closes the connection or the token
/// is cancelled, and throws an <see cref="EventStreamException"/> when the server sends an error
/// frame. Frames that cannot be parsed, or are of a type this SDK version does not model, are
/// skipped.</para>
/// </remarks>
/// <example>
/// <code>
/// await using var firehose = new FirehoseClient("wss://bsky.network");
/// await foreach (var message in firehose.SubscribeAsync())
/// {
///     if (message is CommitEvent commit)
///         Console.WriteLine($"Commit from {commit.Repo}: {commit.Ops?.Count} ops");
/// }
/// </code>
/// </example>
public sealed class FirehoseClient : IAsyncDisposable
{
    private readonly string _serviceUrl;
    private readonly ILogger _logger;
    private readonly StreamConnector _connector;
    private readonly CancellationTokenSource _disposed = new();

    /// <summary>
    /// Create a client for the given relay, PDS or labeler.
    /// </summary>
    /// <param name="serviceUrl">The service's WebSocket URL (e.g., "wss://bsky.network").</param>
    /// <param name="logger">Optional logger.</param>
    public FirehoseClient(string serviceUrl, ILogger? logger = null)
        : this(serviceUrl, logger, StreamSocket.Connector)
    {
    }

    internal FirehoseClient(string serviceUrl, ILogger? logger, StreamConnector connector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUrl);
        _serviceUrl = serviceUrl.TrimEnd('/');
        _logger = logger ?? NullLogger.Instance;
        _connector = connector;
    }

    /// <summary>
    /// Subscribe to the repository event stream (<c>com.atproto.sync.subscribeRepos</c>).
    /// </summary>
    /// <param name="cursor">The sequence number to resume after. If null, starts from the live
    /// stream (no backfill).</param>
    /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
    /// <exception cref="EventStreamException">The server sent an error frame, such as
    /// <c>FutureCursor</c>, or refused the connection.</exception>
    public IAsyncEnumerable<FirehoseMessage> SubscribeAsync(
        long? cursor = null,
        CancellationToken cancellationToken = default)
        => SubscribeAsync(new RepoStreamHandler(Endpoint("com.atproto.sync.subscribeRepos", cursor), _logger), cancellationToken);

    /// <summary>
    /// Subscribe to a labeler's label stream (<c>com.atproto.label.subscribeLabels</c>).
    /// </summary>
    /// <param name="cursor">The sequence number to resume after. If null, starts from the live
    /// stream.</param>
    /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
    /// <exception cref="EventStreamException">The server sent an error frame, such as
    /// <c>FutureCursor</c>, or refused the connection.</exception>
    public IAsyncEnumerable<LabelStreamMessage> SubscribeLabelsAsync(
        long? cursor = null,
        CancellationToken cancellationToken = default)
        => SubscribeAsync(new LabelStreamHandler(Endpoint("com.atproto.label.subscribeLabels", cursor), _logger), cancellationToken);

    /// <summary>Ends every subscription still running on this client.</summary>
    public ValueTask DisposeAsync()
    {
        if (!_disposed.IsCancellationRequested)
            _disposed.Cancel();
        return ValueTask.CompletedTask;
    }

    private async IAsyncEnumerable<T> SubscribeAsync<T>(
        EventStreamHandler<T> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposed.Token);

        await foreach (var message in EventStreamLoop.RunAsync(
            handler, _connector, reconnect: null, _logger, onStreamError: null, linked.Token).ConfigureAwait(false))
        {
            yield return message;
        }
    }

    private Uri Endpoint(string nsid, long? cursor) =>
        new($"{_serviceUrl}/xrpc/{nsid}" + (cursor is { } value ? $"?cursor={value}" : string.Empty));

    /// <summary>One connection to the repository stream, parsing each frame into a message.</summary>
    private sealed class RepoStreamHandler(Uri endpoint, ILogger logger) : EventStreamHandler<FirehoseMessage>
    {
        public override string Stream => "firehose";

        public override ValueTask<(Uri Endpoint, StreamSocketOptions Options)> ConnectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult((endpoint, default(StreamSocketOptions)));

        public override ValueTask<FirehoseMessage?> HandleAsync(string type, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            var message = FirehoseEventParser.ParseBody(type, body);
            if (message is null)
                Dropped(StreamDropReason.Malformed, EventStreamFrame.ReadSeq(body), type);
            return ValueTask.FromResult(message);
        }

        public override void Dropped(StreamDropReason reason, long? cursor, string? detail) =>
            logger.LogDebug("Skipped firehose frame {Cursor} ({Reason}): {Detail}", cursor, reason, detail);
    }
}
