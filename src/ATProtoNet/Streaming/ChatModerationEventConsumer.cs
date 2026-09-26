using System.Runtime.CompilerServices;
using ATProtoNet.Lexicon.Chat.Bsky.Moderation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

/// <summary>
/// Configuration options for <see cref="ChatModerationEventConsumer"/>.
/// </summary>
public sealed class ChatModerationEventConsumerOptions
{
    /// <summary>The chat service's WebSocket URL, e.g. <c>wss://api.bsky.chat</c>.</summary>
    public required string ServiceUrl { get; init; }

    /// <summary>
    /// Returns the bearer token for the next connection. It is called before every connection,
    /// reconnects included, so it can hand out a fresh short-lived service-auth token: one whose
    /// audience is the chat service and whose <c>lxm</c> is
    /// <c>chat.bsky.moderation.subscribeModEvents</c>.
    /// </summary>
    public required Func<CancellationToken, ValueTask<string>> GetAccessTokenAsync { get; init; }

    /// <summary>How to reconnect after the connection drops, and when to give up.</summary>
    public StreamReconnectPolicy Reconnect { get; init; } = new();

    /// <summary>Optional logger.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Invoked for every error frame the service sends before closing the stream, such as
    /// <c>ConsumerTooSlow</c>.
    /// </summary>
    public Action<EventStreamError>? OnStreamError { get; init; }

    /// <summary>Invoked for every frame the consumer skips because it cannot be read.</summary>
    public Action<DroppedStreamEvent>? OnEventDropped { get; init; }
}

/// <summary>
/// A reconnecting consumer of the chat service's moderation event stream
/// (<c>chat.bsky.moderation.subscribeModEvents</c>): first messages, group chat membership and
/// settings changes, and rate limits, for moderation services such as Ozone.
/// </summary>
/// <remarks>
/// <para>The endpoint is private: every connection is authenticated with the bearer token
/// <see cref="ChatModerationEventConsumerOptions.GetAccessTokenAsync"/> returns. An event this SDK
/// version does not model is delivered as <see cref="UnknownChatModerationEvent"/>.</para>
/// <para>The cursor is the <see cref="ChatModerationEvent.Rev"/> of the last event delivered, which
/// the consumer resumes after when it reconnects. It is not persisted: store
/// <see cref="LastRev"/> (or each event's <c>Rev</c>) yourself and pass it back to
/// <see cref="ConsumeAsync"/> after a restart. Delivery is at-least-once.</para>
/// <para>Cancelling the token ends the enumeration normally. The consumer throws an
/// <see cref="EventStreamException"/> for an error reconnecting cannot fix, and when
/// <see cref="ChatModerationEventConsumerOptions.Reconnect"/> gives up.</para>
/// </remarks>
/// <example>
/// <code>
/// var consumer = new ChatModerationEventConsumer(new ChatModerationEventConsumerOptions
/// {
///     ServiceUrl = "wss://api.bsky.chat",
///     GetAccessTokenAsync = async ct => await MintServiceAuthAsync(
///         audience: "did:web:api.bsky.chat", lxm: "chat.bsky.moderation.subscribeModEvents", ct),
/// });
/// await foreach (var evt in consumer.ConsumeAsync(cursor: savedRev))
/// {
///     if (evt is GroupChatCreatedEvent created)
///         Console.WriteLine($"{created.OwnerDid} created {created.GroupName}");
///     savedRev = evt.Rev;
/// }
/// </code>
/// </example>
public sealed class ChatModerationEventConsumer
{
    /// <summary>The cursor that replays the stream from its beginning.</summary>
    public const string BeginningCursor = "2222222222222";

    private const string Nsid = "chat.bsky.moderation.subscribeModEvents";

    private readonly ChatModerationEventConsumerOptions _options;
    private readonly StreamConnector _connector;
    private readonly ILogger _logger;
    private string? _lastRev;

    /// <summary>
    /// Create a chat moderation event consumer.
    /// </summary>
    /// <param name="options">Consumer configuration.</param>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    public ChatModerationEventConsumer(ChatModerationEventConsumerOptions options)
        : this(options, StreamSocket.Connector)
    {
    }

    internal ChatModerationEventConsumer(ChatModerationEventConsumerOptions options, StreamConnector connector)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ServiceUrl, nameof(options.ServiceUrl));
        ArgumentNullException.ThrowIfNull(options.GetAccessTokenAsync, nameof(options.GetAccessTokenAsync));
        ArgumentNullException.ThrowIfNull(options.Reconnect, nameof(options.Reconnect));
        options.Reconnect.Validate();
        _options = options;
        _connector = connector;
        _logger = options.Logger ?? NullLogger.Instance;
    }

    /// <summary>The revision of the last event delivered, or null before the first.</summary>
    public string? LastRev => Volatile.Read(ref _lastRev);

    /// <summary>
    /// Consume the moderation event stream with automatic reconnection.
    /// </summary>
    /// <param name="cursor">The revision to resume after: an event's <see cref="ChatModerationEvent.Rev"/>,
    /// or <see cref="BeginningCursor"/> to replay from the beginning. Null starts from the live
    /// stream.</param>
    /// <param name="cancellationToken">Cancellation token to stop consuming.</param>
    /// <exception cref="EventStreamException">The service sent an error that reconnecting cannot
    /// fix, or every reconnect attempt the policy allows failed.</exception>
    public async IAsyncEnumerable<ChatModerationEvent> ConsumeAsync(
        string? cursor = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _lastRev, null);
        var handler = new Handler(this, cursor);

        await foreach (var evt in EventStreamLoop.RunAsync(
            handler, _connector, _options.Reconnect, _logger, _options.OnStreamError, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return evt;
        }
    }

    private sealed class Handler(ChatModerationEventConsumer owner, string? start) : EventStreamHandler<ChatModerationEvent>
    {
        public override string Stream => "chat moderation stream";

        public override async ValueTask<(Uri Endpoint, StreamSocketOptions Options)> ConnectAsync(CancellationToken cancellationToken)
        {
            var token = await owner._options.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var cursor = owner.LastRev ?? start;
            var endpoint = new Uri($"{owner._options.ServiceUrl.TrimEnd('/')}/xrpc/{Nsid}" +
                (cursor is null ? string.Empty : $"?cursor={Uri.EscapeDataString(cursor)}"));
            return (endpoint, new StreamSocketOptions(Authorization: $"Bearer {token}"));
        }

        public override ValueTask<ChatModerationEvent?> HandleAsync(
            string type, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            // Frame headers name the variant relative to the subscription; the union is keyed by
            // the full reference.
            var discriminator = type.StartsWith('#') ? Nsid + type : type;
            var evt = EventStreamFrame.Deserialize<ChatModerationEvent>(body, discriminator);
            if (evt is null)
                Dropped(StreamDropReason.Malformed, null, type);

            return ValueTask.FromResult(evt);
        }

        public override void Delivered(ChatModerationEvent message)
        {
            if (!string.IsNullOrEmpty(message.Rev))
                Volatile.Write(ref owner._lastRev, message.Rev);
        }

        public override void Dropped(StreamDropReason reason, long? cursor, string? detail)
        {
            owner._logger.LogDebug("Skipped chat moderation frame ({Reason}): {Detail}", reason, detail);
            owner._options.OnEventDropped?.Invoke(new DroppedStreamEvent(reason, cursor, detail));
        }
    }
}
