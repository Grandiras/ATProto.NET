using System.Runtime.CompilerServices;
using ATProtoNet.Labeling;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

/// <summary>
/// Configuration options for <see cref="LabelStreamConsumer"/>. <see cref="StreamConsumerOptions.ServiceUrl"/>
/// is the labeler's WebSocket URL.
/// </summary>
public sealed class LabelStreamConsumerOptions : StreamConsumerOptions
{
    /// <summary>
    /// Verifies the signature of every label the stream delivers, against its issuer's
    /// <c>#atproto_label</c> key, and reports each outcome in <see cref="LabelsEvent.Verification"/>.
    /// Null, the default, verifies nothing.
    /// </summary>
    /// <remarks>
    /// A label that does not verify is still delivered, with its outcome: filter on
    /// <see cref="LabelVerificationResult.IsValid"/> to act only on verified labels. The spec asks
    /// a service to verify labels it receives from another; an end client usually trusts the
    /// labelers it chose.
    /// </remarks>
    public LabelVerifier? Verifier { get; init; }
}

/// <summary>
/// A reconnecting consumer of a labeler's label stream (<c>com.atproto.label.subscribeLabels</c>),
/// with persistent cursor storage.
/// </summary>
/// <remarks>
/// <para>It delivers <see cref="LabelsEvent"/> messages, whose <see cref="LabelsEvent.Seq"/> is the
/// cursor, and <see cref="LabelInfoEvent"/> notices such as <c>OutdatedCursor</c>. Delivery is
/// <b>at-least-once</b>: an event's position is recorded when the caller asks for the next one, and
/// saved every <see cref="StreamConsumerOptions.CursorPersistInterval"/> events and when the
/// enumeration ends.</para>
/// <para>Cancelling the token ends the enumeration normally. An error frame is reported to
/// <see cref="StreamConsumerOptions.OnStreamError"/>; the consumer reconnects after a retryable one
/// and throws an <see cref="EventStreamException"/> for one that is not (<c>FutureCursor</c>), and
/// when <see cref="StreamConsumerOptions.Reconnect"/> gives up.</para>
/// <para>With a <see cref="LabelStreamConsumerOptions.Verifier"/>, every <see cref="LabelsEvent"/>
/// carries the outcome of verifying each of its labels in <see cref="LabelsEvent.Verification"/>.</para>
/// </remarks>
/// <example>
/// <code>
/// var consumer = new LabelStreamConsumer(new LabelStreamConsumerOptions
/// {
///     ServiceUrl = "wss://mod.bsky.app",
///     CursorStore = myCursorStore,
/// });
/// await foreach (var message in consumer.ConsumeAsync())
/// {
///     if (message is LabelsEvent labels)
///         foreach (var label in labels.Labels)
///             Console.WriteLine($"{label.Src} labelled {label.Uri} {label.Val}");
/// }
/// </code>
/// </example>
public sealed class LabelStreamConsumer
{
    private readonly LabelStreamConsumerOptions _options;
    private readonly StreamConnector _connector;
    private readonly ILogger _logger;
    private CursorTracker? _cursor;

    /// <summary>
    /// Create a label stream consumer.
    /// </summary>
    /// <param name="options">Consumer configuration.</param>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    public LabelStreamConsumer(LabelStreamConsumerOptions options)
        : this(options, StreamSocket.Connector)
    {
    }

    internal LabelStreamConsumer(LabelStreamConsumerOptions options, StreamConnector connector)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _connector = connector;
        _logger = options.Logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// The cursor of the current or last <see cref="ConsumeAsync"/> run: the sequence number it has
    /// passed and would resume after, or null when it started live and has seen no event yet.
    /// </summary>
    public long? LastSeq => _cursor?.Current;

    /// <summary>
    /// Consume the label stream with automatic reconnection and cursor persistence.
    /// </summary>
    /// <param name="cursor">The sequence number to resume after. When null, the stored cursor is used
    /// if there is a <see cref="StreamConsumerOptions.CursorStore"/>, and the live stream otherwise.</param>
    /// <param name="cancellationToken">Cancellation token to stop consuming.</param>
    /// <exception cref="EventStreamException">The labeler sent an error that reconnecting cannot
    /// fix, or every reconnect attempt the policy allows failed.</exception>
    public async IAsyncEnumerable<LabelStreamMessage> ConsumeAsync(
        long? cursor = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tracker = new CursorTracker(
            _options.CursorStore, _options.ResolvedStreamId, _options.CursorPersistInterval, _logger);
        _cursor = tracker;

        var start = cursor;
        if (start is null)
        {
            try
            {
                start = await tracker.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
        }

        tracker.Start(start);
        var serviceUrl = _options.ServiceUrl.TrimEnd('/');

        try
        {
            var handler = new LabelStreamHandler(
                () => new Uri($"{serviceUrl}/xrpc/com.atproto.label.subscribeLabels" +
                    (tracker.Current is { } value ? $"?cursor={value}" : string.Empty)),
                _logger,
                tracker,
                _options.OnEventDropped,
                _options.Verifier);

            await foreach (var message in EventStreamLoop.RunAsync(
                handler, _connector, _options.Reconnect, _logger, _options.OnStreamError, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return message;
            }
        }
        finally
        {
            await tracker.FlushAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Reads label stream frames: <c>#labels</c> and <c>#info</c>. With a cursor tracker it records
/// each event's position; without one it serves a single connection.
/// </summary>
internal sealed class LabelStreamHandler(
    Func<Uri> endpoint,
    ILogger logger,
    CursorTracker? cursor = null,
    Action<DroppedStreamEvent>? onDropped = null,
    LabelVerifier? verifier = null) : EventStreamHandler<LabelStreamMessage>
{
    public LabelStreamHandler(Uri endpoint, ILogger logger)
        : this(() => endpoint, logger)
    {
    }

    public override string Stream => "label stream";

    public override ValueTask<(Uri Endpoint, StreamSocketOptions Options)> ConnectAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult((endpoint(), default(StreamSocketOptions)));

    public override async ValueTask<LabelStreamMessage?> HandleAsync(
        string type, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        LabelStreamMessage? message = type switch
        {
            "#labels" => EventStreamFrame.Deserialize<LabelsEvent>(body),
            "#info" => EventStreamFrame.Deserialize<LabelInfoEvent>(body),
            _ => null,
        };

        if (message is null)
        {
            Dropped(type is "#labels" or "#info" ? StreamDropReason.Malformed : StreamDropReason.UnknownType,
                EventStreamFrame.ReadSeq(body), type);
        }
        else if (message is LabelsEvent labels)
        {
            // Already delivered: a replay from an inclusive cursor.
            if (cursor is not null && labels.Seq <= cursor.Current)
                return null;

            if (verifier is not null)
                return await VerifyAsync(labels, verifier, cancellationToken).ConfigureAwait(false);
        }

        return message;
    }

    private static async Task<LabelsEvent> VerifyAsync(
        LabelsEvent labels, LabelVerifier verifier, CancellationToken cancellationToken) => new()
    {
        Seq = labels.Seq,
        Labels = labels.Labels,
        ExtensionData = labels.ExtensionData,
        Verification = await verifier.VerifyAllAsync(labels.Labels, cancellationToken).ConfigureAwait(false),
    };

    public override void Delivered(LabelStreamMessage message)
    {
        if (message is LabelsEvent labels)
            cursor?.Advance(labels.Seq);
    }

    public override void Dropped(StreamDropReason reason, long? position, string? detail)
    {
        if (position is { } value)
            cursor?.Advance(value);

        logger.LogDebug("Skipped label stream frame {Cursor} ({Reason}): {Detail}", position, reason, detail);
        onDropped?.Invoke(new DroppedStreamEvent(reason, position, detail));
    }
}
