using System.Runtime.CompilerServices;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

/// <summary>
/// Configuration options for the typed firehose consumer.
/// </summary>
public sealed class TypedFirehoseConsumerOptions
{
    /// <summary>The relay/PDS WebSocket URL.</summary>
    public required string ServiceUrl { get; init; }

    /// <summary>Optional cursor store for persistent resume across restarts.</summary>
    public IFirehoseCursorStore? CursorStore { get; init; }

    /// <summary>Stream identifier used as the key for cursor storage. Defaults to the service URL.</summary>
    public string? StreamId { get; init; }

    /// <summary>Optional verifier for CID and signature verification of commit events.</summary>
    public FirehoseVerifier? Verifier { get; init; }

    /// <summary>Whether to verify CID integrity on commit events. Default: false.</summary>
    public bool VerifyCids { get; init; }

    /// <summary>Whether to verify commit signatures. Requires <see cref="Verifier"/>. Default: false.</summary>
    public bool VerifySignatures { get; init; }

    /// <summary>
    /// Optional collection filter. Only commit events containing operations in the specified collections 
    /// will be emitted. If null or empty, all commit events are emitted.
    /// </summary>
    public IReadOnlySet<string>? CollectionFilter { get; init; }

    /// <summary>Interval (in number of events) between cursor persistence. Default: 100.</summary>
    public int CursorPersistInterval { get; init; } = 100;

    /// <summary>Optional logger.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>Delay between reconnection attempts. Default: 5 seconds.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Max reconnection attempts. Default: 10. Use -1 for unlimited.</summary>
    public int MaxReconnectAttempts { get; init; } = 10;

    /// <summary>The resolved stream identifier for cursor storage.</summary>
    internal string ResolvedStreamId => StreamId ?? ServiceUrl;
}

/// <summary>
/// A high-level firehose consumer that parses CBOR frames into typed <see cref="FirehoseMessage"/> objects,
/// supports collection filtering, CID/signature verification, and persistent cursor storage.
/// </summary>
/// <remarks>
/// <para>Built on top of <see cref="FirehoseConsumer"/> for reconnection handling.</para>
/// </remarks>
/// <example>
/// <code>
/// var options = new TypedFirehoseConsumerOptions
/// {
///     ServiceUrl = "wss://bsky.network",
///     CollectionFilter = new HashSet&lt;string&gt; { "app.bsky.feed.post" },
///     CursorStore = new InMemoryFirehoseCursorStore(),
///     VerifyCids = true,
/// };
/// var consumer = new TypedFirehoseConsumer(options);
/// await foreach (var msg in consumer.ConsumeAsync())
/// {
///     if (msg is CommitEvent commit)
///         Console.WriteLine($"Post from {commit.Repo}");
/// }
/// </code>
/// </example>
public sealed class TypedFirehoseConsumer : IDisposable
{
    private readonly TypedFirehoseConsumerOptions _options;
    private readonly FirehoseConsumer _consumer;
    private readonly ILogger _logger;
    private bool _disposed;
    private int _eventsSinceLastPersist;

    /// <summary>The last successfully processed sequence number.</summary>
    public long? LastSeq { get; private set; }

    /// <summary>
    /// Create a typed firehose consumer.
    /// </summary>
    /// <param name="options">Consumer configuration.</param>
    public TypedFirehoseConsumer(TypedFirehoseConsumerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = options.Logger ?? NullLogger.Instance;
        _consumer = new FirehoseConsumer(
            options.ServiceUrl,
            options.Logger,
            options.ReconnectDelay,
            options.MaxReconnectAttempts);
    }

    /// <summary>
    /// Consume typed firehose events with parsing, filtering, and optional verification.
    /// </summary>
    /// <param name="cursor">Initial cursor to resume from. If null and a cursor store is configured, 
    /// the stored cursor will be used.</param>
    /// <param name="cancellationToken">Cancellation token to stop consuming.</param>
    public async IAsyncEnumerable<FirehoseMessage> ConsumeAsync(
        long? cursor = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var startCursor = cursor;

        // Try to load cursor from persistent store if not explicitly provided
        if (startCursor is null && _options.CursorStore is not null)
        {
            startCursor = await _options.CursorStore.GetCursorAsync(
                _options.ResolvedStreamId, cancellationToken);

            if (startCursor.HasValue)
                _logger.LogInformation("Resuming firehose from stored cursor {Cursor}", startCursor.Value);
        }

        await foreach (var frame in _consumer.ConsumeAsync(startCursor, cancellationToken))
        {
            FirehoseMessage? message;
            try
            {
                message = FirehoseEventParser.Parse(frame);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse firehose frame ({Size} bytes)", frame.Size);
                continue;
            }

            if (message is null)
                continue;

            // Apply collection filter for commit events
            if (message is CommitEvent commit)
            {
                if (!PassesCollectionFilter(commit))
                    continue;

                // CID verification
                if (_options.VerifyCids)
                {
                    var cidResult = FirehoseVerifier.VerifyCid(commit);
                    if (!cidResult.IsValid)
                    {
                        _logger.LogWarning("CID verification failed for commit from {Repo}: {Error}",
                            commit.Repo, cidResult.Error);
                        continue;
                    }
                }

                // Signature verification
                if (_options.VerifySignatures && _options.Verifier is not null)
                {
                    var sigResult = await _options.Verifier.VerifySignatureAsync(commit, cancellationToken);
                    if (!sigResult.IsValid)
                    {
                        _logger.LogWarning("Signature verification failed for commit from {Repo}: {Error}",
                            commit.Repo, sigResult.Error);
                        continue;
                    }
                }

                TrackCursor(commit.Seq);
            }
            else if (message is SyncEvent syncEvent)
            {
                if (_options.VerifyCids)
                {
                    var cidResult = FirehoseVerifier.VerifyCid(syncEvent);
                    if (!cidResult.IsValid)
                    {
                        _logger.LogWarning("CID verification failed for sync event from {Did}: {Error}",
                            syncEvent.Did, cidResult.Error);
                        continue;
                    }
                }

                TrackCursor(syncEvent.Seq);
            }
            else if (message is IdentityEvent identity)
            {
                TrackCursor(identity.Seq);
            }
            else if (message is AccountEvent account)
            {
                TrackCursor(account.Seq);
            }

            // Persist cursor periodically
            if (_options.CursorStore is not null && LastSeq.HasValue)
            {
                _eventsSinceLastPersist++;
                if (_eventsSinceLastPersist >= _options.CursorPersistInterval)
                {
                    await _options.CursorStore.StoreCursorAsync(
                        _options.ResolvedStreamId, LastSeq.Value, cancellationToken);
                    _eventsSinceLastPersist = 0;
                }
            }

            yield return message;
        }

        // Final cursor persist on shutdown
        if (_options.CursorStore is not null && LastSeq.HasValue)
        {
            await _options.CursorStore.StoreCursorAsync(
                _options.ResolvedStreamId, LastSeq.Value, CancellationToken.None);
        }
    }

    private bool PassesCollectionFilter(CommitEvent commit)
    {
        if (_options.CollectionFilter is null || _options.CollectionFilter.Count == 0)
            return true;

        if (commit.Ops is null || commit.Ops.Count == 0)
            return true; // Allow commits with no ops (could be structural)

        // Pass if ANY operation's path starts with a matching collection
        foreach (var op in commit.Ops)
        {
            if (op.Path is null)
                continue;

            // The path format is "collection/rkey"
            var slashIndex = op.Path.IndexOf('/');
            var collection = slashIndex >= 0 ? op.Path[..slashIndex] : op.Path;

            if (_options.CollectionFilter.Contains(collection))
                return true;
        }

        return false;
    }

    private void TrackCursor(long seq)
    {
        LastSeq = seq;
        _consumer.Acknowledge(seq);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _consumer.Dispose();
    }
}
