using System.Runtime.CompilerServices;
using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Streaming;

/// <summary>
/// A Jetstream consumer that handles reconnection, cursor persistence, and duplicate suppression
/// across reconnects.
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
/// <para>Delivery, cancellation and errors follow <see cref="StreamConsumerOptions"/>. A
/// subscription the server rejects before the WebSocket upgrade (a cursor below the retention
/// floor, a retired zstd dictionary, a malformed filter) is not retried: reconnecting with the
/// same request would loop forever, and dropping the cursor would silently skip the gap. Failures
/// are thrown as a <see cref="JetstreamException"/>. For a single connection, set
/// <see cref="StreamReconnectPolicy.MaxAttempts"/> to 0.</para>
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
    // The v2 endpoint path — the subscription Lexicon's canonical XRPC route.
    private const string V2Path = "/xrpc/network.bsky.jetstream.subscribeEvents";

    // The WebSocket subprotocol the v2 wire is framed under (atproto proposal 0015).
    private const string V2SubProtocol = "xrpc.v1.json";

    private readonly JetstreamConsumerOptions _options;
    private readonly StreamConnector _connector;

    /// <summary>The <c>time_us</c> of the last delivered event. The reconnect cursor base on
    /// <see cref="JetstreamProtocol.V1"/>.</summary>
    public long? LastTimeUs { get; private set; }

    /// <summary>The sequence number (<see cref="JetstreamEvent.Cursor"/>) of the last delivered
    /// event. The reconnect cursor on <see cref="JetstreamProtocol.V2"/>; null until an event
    /// carrying one has been delivered.</summary>
    public long? LastCursor { get; private set; }

    /// <summary>Create a Jetstream consumer.</summary>
    /// <param name="options">Consumer configuration.</param>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    public JetstreamConsumer(JetstreamConsumerOptions options)
        : this(options, StreamSocket.Connector)
    {
    }

    internal JetstreamConsumer(JetstreamConsumerOptions options, StreamConnector connector)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;

        // Decompressing is part of reading the connection: a frame that does not decompress
        // (corrupt, or made with another dictionary) fails the connection, which reconnects from
        // the last cursor, rather than being skipped as if it were one unreadable event.
        _connector = options.Decompressor is { } decompressor
            ? (endpoint, socketOptions, ct) => Decompress(connector(endpoint, socketOptions, ct), decompressor, ct)
            : connector;
    }

    /// <summary>Consume Jetstream events with automatic reconnection and cursor persistence.</summary>
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
        if (await CursorTracker.StartAsync(_options, cursor, cancellationToken).ConfigureAwait(false) is not { } tracker)
            yield break;

        await using (tracker.ConfigureAwait(false))
        {
            await foreach (var evt in EventStreamLoop.RunAsync(new Handler(this, tracker), _connector, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }

    private static async IAsyncEnumerable<StreamSocketMessage> Decompress(
        IAsyncEnumerable<StreamSocketMessage> messages, IJetstreamDecompressor decompressor, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var message in messages.WithCancellation(ct).ConfigureAwait(false))
            yield return message.IsBinary ? new StreamSocketMessage(decompressor.Decompress(message.Data.Span), IsBinary: false) : message;
    }

    // The subscription endpoint for options, resuming at cursor.
    internal static Uri Endpoint(JetstreamConsumerOptions options, long? cursor)
    {
        options.Validate();
        var v2 = options.Protocol == JetstreamProtocol.V2;
        var baseUrl = AtProtoHttp.WithScheme(new Uri(options.ServiceUrl), webSocket: true).GetLeftPart(UriPartial.Path).TrimEnd('/');

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

    private sealed class Handler : EventStreamHandler<JetstreamEvent>
    {
        private readonly JetstreamConsumer _owner;
        private readonly CursorTracker _cursor;
        private readonly bool _v2;
        private readonly long? _start;
        private long? _seqFloor;
        private bool _connected;

        public Handler(JetstreamConsumer owner, CursorTracker cursor)
            : base(owner._options)
        {
            _owner = owner;
            _cursor = cursor;
            _v2 = owner._options.Protocol == JetstreamProtocol.V2;
            _start = cursor.Current;

            // A v2 sequence cursor is replayed inclusively, so it is also the floor below which
            // events were already delivered. A timestamp seek is only a server-side position, no
            // floor for the sequence numbers that follow: the first event sets that.
            var timestampSeek = _v2 && _start is { } start && JetstreamCursor.IsTimestamp(start);
            if (timestampSeek)
                cursor.Start(null);
            _seqFloor = _v2 && !timestampSeek ? _start : null;
        }

        public override string Stream => "Jetstream";

        public override ValueTask<(Uri Endpoint, StreamSocketOptions Options)> ConnectAsync(CancellationToken cancellationToken)
        {
            // v2 cursors are sequence numbers replayed inclusively, so there is nothing to rewind
            // past; v1 cursors are timestamps, which need the in-flight overlap.
            var resume = _v2
                ? _owner.LastCursor
                : _owner.LastTimeUs - (long)_owner._options.ReconnectRewind.TotalMicroseconds;
            var cursor = _connected ? resume ?? _start : _start;
            _connected = true;

            return ValueTask.FromResult((
                Endpoint(_owner._options, cursor),
                new StreamSocketOptions(SubProtocol: _v2 ? V2SubProtocol : null)));
        }

        public override ValueTask<(JetstreamEvent? Message, EventStreamError? Error)> ReadAsync(
            StreamSocketMessage message, CancellationToken cancellationToken)
        {
            // Parsed straight from the receive buffer: only a compressed frame is copied, by the
            // decompressor.
            var frame = JetstreamEventParser.Parse(message.Data, _owner._options.Protocol, out var dropped);
            if (dropped is { } reason)
                Dropped(reason, null, null);

            if (frame.Error is { } error)
                return ValueTask.FromResult<(JetstreamEvent?, EventStreamError?)>((null, error));

            if (frame.Info is { } info)
            {
                Logger.LogInformation("Jetstream info {Name}: {Message}", info.Name, info.Message);
                _owner._options.OnInfo?.Invoke(info);
            }

            if (frame.Event is not { } evt)
                return default;

            // Skip events the server replayed that were already delivered.
            if (_v2
                ? _seqFloor is { } floor && evt.Cursor is { } seq && seq <= floor
                : _owner.LastTimeUs is { } last && evt.TimeUs <= last)
            {
                return default;
            }

            _owner.LastTimeUs = evt.TimeUs;
            if (evt.Cursor is { } eventCursor)
                _owner.LastCursor = _seqFloor = eventCursor;

            return ValueTask.FromResult<(JetstreamEvent?, EventStreamError?)>((evt, null));
        }

        // A v2 event without a sequence number is not recorded: the server could not resume from it.
        public override void Delivered(JetstreamEvent message)
        {
            if ((_v2 ? message.Cursor : message.TimeUs) is { } position)
                _cursor.Advance(position);
        }

        public override Exception Failed(Exception failure) =>
            failure is EventStreamException { } refused and not JetstreamException
                ? new JetstreamException(refused.Message, refused.StatusCode, refused.Error, innerException: refused)
                : failure;
    }
}
