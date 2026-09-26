using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using System.Text;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

/// <summary>
/// Configuration options for the typed firehose consumer.
/// </summary>
public sealed class TypedFirehoseConsumerOptions : StreamConsumerOptions
{
    /// <summary>
    /// Verifies every commit's signature, and its blocks against their CIDs, when set. Its DID
    /// cache is invalidated on each <c>#identity</c> event. A commit that fails is dropped and
    /// reported to <see cref="StreamConsumerOptions.OnEventDropped"/>. Set this or
    /// <see cref="SyncVerifier"/>, not both.
    /// </summary>
    public FirehoseVerifier? Verifier { get; init; }

    /// <summary>
    /// Verifies every <c>#commit</c> and <c>#sync</c> event inductively (Sync 1.1) when set: each
    /// commit's signature, blocks and operations, and that it chains on the repository's previous
    /// state, which the verifier's <see cref="RepoSyncVerifier.StateStore"/> keeps. Only events that
    /// pass and chain are delivered; the rest are reported to
    /// <see cref="StreamConsumerOptions.OnEventDropped"/>. A delivered event's repository state is
    /// recorded once you ask for the next message.
    /// </summary>
    /// <remarks>
    /// Every commit must be verified to keep its repository's chain, so a
    /// <see cref="CollectionFilter"/> no longer skips commits before they are parsed: it only
    /// decides which verified commits are delivered. Set <see cref="Resync"/> to fetch
    /// repositories whose chain breaks; without it they stay desynchronized, and their events are
    /// dropped with <see cref="StreamDropReason.Desynchronized"/>.
    /// </remarks>
    public RepoSyncVerifier? SyncVerifier { get; init; }

    /// <summary>
    /// Fetches repositories again when their chain breaks, and delivers each as a
    /// <see cref="RepoResyncEvent"/> followed by the events that arrived meanwhile. Requires
    /// <see cref="SyncVerifier"/>. Null (the default) fetches nothing.
    /// </summary>
    public RepoResyncOptions? Resync { get; init; }

    /// <summary>
    /// Whether to check the blocks of commit and sync events against their CIDs when no
    /// <see cref="Verifier"/> is set: a local check, with no network access. With a verifier this
    /// is implied. Default: false.
    /// </summary>
    public bool VerifyCids { get; init; }

    /// <summary>
    /// Only commit events with at least one operation in these collections are delivered; others
    /// are skipped before they are parsed (unless a <see cref="SyncVerifier"/> must verify them),
    /// though the cursor still moves past them. Null or empty delivers every commit. Commits
    /// without operations, and non-commit events, always pass.
    /// </summary>
    public IReadOnlySet<Nsid>? CollectionFilter { get; init; }

    /// <inheritdoc/>
    internal override void Validate()
    {
        base.Validate();
        if (Verifier is not null && SyncVerifier is not null)
            throw new ArgumentException("Set either Verifier or SyncVerifier: the sync verifier checks signatures itself.", nameof(SyncVerifier));
        if (Resync is not null)
        {
            if (SyncVerifier is null)
                throw new ArgumentException("Resync needs a SyncVerifier to tell which repositories to fetch.", nameof(Resync));
            Resync.Validate();
        }
    }
}

/// <summary>
/// A reconnecting firehose consumer (<c>com.atproto.sync.subscribeRepos</c>) that parses frames
/// into typed <see cref="FirehoseMessage"/> objects, with collection filtering, CID and signature
/// verification, and persistent cursor storage.
/// </summary>
/// <remarks>
/// <para>Each frame is parsed once. With a <see cref="TypedFirehoseConsumerOptions.CollectionFilter"/>,
/// a commit's operation paths are read straight from the CBOR first, so commits in other collections
/// are never deserialized.</para>
/// <para>With a <see cref="TypedFirehoseConsumerOptions.SyncVerifier"/>, each repository's commits
/// are verified as a chain (Sync 1.1), and with <see cref="TypedFirehoseConsumerOptions.Resync"/> a
/// repository whose chain breaks is fetched again and delivered as a <see cref="RepoResyncEvent"/>.</para>
/// <para>Delivery is <b>at-least-once</b>: an event's position is recorded when the caller asks for
/// the next one, and the cursor is saved every
/// <see cref="StreamConsumerOptions.CursorPersistInterval"/> events and when the enumeration ends,
/// so events after the last saved cursor may be delivered again after a restart. Events a filter
/// or a failed verification skips still move the cursor.</para>
/// <para>Cancelling the token ends the enumeration normally. An error frame is reported to
/// <see cref="StreamConsumerOptions.OnStreamError"/>; the consumer reconnects after a retryable
/// one and throws an <see cref="EventStreamException"/> for one that is not (<c>FutureCursor</c>),
/// and when <see cref="StreamConsumerOptions.Reconnect"/> gives up.</para>
/// </remarks>
/// <example>
/// <code>
/// var consumer = new TypedFirehoseConsumer(new TypedFirehoseConsumerOptions
/// {
///     ServiceUrl = "wss://bsky.network",
///     CollectionFilter = new HashSet&lt;Nsid&gt; { Nsid.Parse("app.bsky.feed.post") },
///     CursorStore = new InMemoryStreamCursorStore(),
///     VerifyCids = true,
/// });
/// await foreach (var msg in consumer.ConsumeAsync())
/// {
///     if (msg is CommitEvent commit)
///         Console.WriteLine($"Post from {commit.Repo}");
/// }
/// </code>
/// </example>
public sealed class TypedFirehoseConsumer
{
    private readonly TypedFirehoseConsumerOptions _options;
    private readonly StreamConnector _connector;
    private readonly ILogger _logger;
    private readonly CollectionMatcher? _filter;
    private CursorTracker? _cursor;

    /// <summary>
    /// Create a typed firehose consumer.
    /// </summary>
    /// <param name="options">Consumer configuration.</param>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    public TypedFirehoseConsumer(TypedFirehoseConsumerOptions options)
        : this(options, StreamSocket.Connector)
    {
    }

    internal TypedFirehoseConsumer(TypedFirehoseConsumerOptions options, StreamConnector connector)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _connector = connector;
        _logger = options.Logger ?? NullLogger.Instance;
        _filter = options.CollectionFilter is { Count: > 0 } collections ? new CollectionMatcher(collections) : null;
    }

    /// <summary>
    /// The cursor of the current or last <see cref="ConsumeAsync"/> run: the sequence number it has
    /// passed and would resume after, or null when it started live and has seen no event yet.
    /// </summary>
    public long? LastSeq => _cursor?.Current;

    /// <summary>
    /// Consume typed firehose events with parsing, filtering, and optional verification.
    /// </summary>
    /// <param name="cursor">The sequence number to resume after. When null, the stored cursor is used
    /// if there is a <see cref="StreamConsumerOptions.CursorStore"/>, and the live stream otherwise.</param>
    /// <param name="cancellationToken">Cancellation token to stop consuming.</param>
    /// <exception cref="EventStreamException">The relay sent an error that reconnecting cannot fix,
    /// such as <c>FutureCursor</c>, or every reconnect attempt the policy allows failed.</exception>
    public async IAsyncEnumerable<FirehoseMessage> ConsumeAsync(
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

            if (start.HasValue)
                _logger.LogInformation("Resuming the firehose from stored cursor {Cursor}", start.Value);
        }

        tracker.Start(start);

        var resync = _options.Resync is { } resyncOptions
            ? new RepoResyncCoordinator(_options.SyncVerifier!, resyncOptions, _options.ServiceUrl, tracker.SetPersistLimit, _logger)
            : null;

        try
        {
            var handler = new Handler(this, tracker, resync);
            await foreach (var message in EventStreamLoop.RunAsync(
                handler, _connector, _options.Reconnect, _logger, _options.OnStreamError, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return message;
            }
        }
        finally
        {
            if (resync is not null)
                await resync.DisposeAsync().ConfigureAwait(false);
            await tracker.FlushAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Filters and verifies one parsed message. Returns whether it is delivered. A sync-verified
    /// event's state is recorded at once.
    /// </summary>
    internal ValueTask<bool> AcceptAsync(FirehoseMessage message, CancellationToken cancellationToken)
        => AcceptAsync(message, handler: null, cancellationToken);

    /// <summary>
    /// Filters and verifies one parsed message. Returns whether it is delivered; the state of a
    /// sync-verified event it delivers is recorded by <paramref name="handler"/> on delivery.
    /// </summary>
    private async ValueTask<bool> AcceptAsync(FirehoseMessage message, Handler? handler, CancellationToken cancellationToken)
    {
        if (_options.SyncVerifier is { } syncVerifier)
        {
            switch (message)
            {
                case CommitEvent commit:
                    var commitResult = await syncVerifier.VerifyCommitAsync(commit, cancellationToken).ConfigureAwait(false);
                    return await AcceptSyncedAsync(syncVerifier, commit, commitResult, _filter?.Matches(commit) ?? true, handler, cancellationToken)
                        .ConfigureAwait(false);

                case SyncEvent syncEvent:
                    var syncResult = await syncVerifier.VerifySyncAsync(syncEvent, cancellationToken).ConfigureAwait(false);
                    return await AcceptSyncedAsync(syncVerifier, syncEvent, syncResult, matches: true, handler, cancellationToken)
                        .ConfigureAwait(false);

                case IdentityEvent identity:
                    await syncVerifier.InvalidateIdentityAsync(identity.Did, cancellationToken).ConfigureAwait(false);
                    return true;
            }
        }

        switch (message)
        {
            case CommitEvent commit:
                if (_filter is not null && !_filter.Matches(commit))
                    return false;

                // The signature check verifies every block against its CID as well, so the CAR is
                // parsed and hashed once however many checks are on.
                var result = _options.Verifier is { } verifier
                    ? await verifier.VerifySignatureAsync(commit, cancellationToken).ConfigureAwait(false)
                    : _options.VerifyCids ? FirehoseVerifier.VerifyCid(commit) : null;
                if (result is { IsValid: false })
                {
                    _logger.LogWarning("Verification failed for commit {Seq} from {Repo}: {Error}",
                        commit.Seq, commit.Repo, result.Error);
                    Drop(StreamDropReason.VerificationFailed, commit.Seq, result.Error);
                    return false;
                }

                return true;

            case SyncEvent sync:
                if (_options.VerifyCids || _options.Verifier is not null)
                {
                    var cids = FirehoseVerifier.VerifyCid(sync);
                    if (!cids.IsValid)
                    {
                        _logger.LogWarning("CID verification failed for sync event {Seq} from {Did}: {Error}",
                            sync.Seq, sync.Did, cids.Error);
                        Drop(StreamDropReason.VerificationFailed, sync.Seq, cids.Error);
                        return false;
                    }
                }

                return true;

            case IdentityEvent identity:
                // The account's handle or keys changed: the verifier must not go on checking its
                // commits against a cached document.
                if (_options.Verifier is not null)
                    await _options.Verifier.InvalidateIdentityAsync(identity.Did, cancellationToken).ConfigureAwait(false);
                return true;

            default:
                return true;
        }
    }

    /// <summary>Acts on a <see cref="RepoSyncVerifier"/> outcome. Returns whether the event is delivered.</summary>
    private async ValueTask<bool> AcceptSyncedAsync(
        RepoSyncVerifier sync, FirehoseEvent evt, RepoSyncResult result, bool matches, Handler? handler,
        CancellationToken cancellationToken)
    {
        switch (result.Outcome)
        {
            case RepoSyncOutcome.Valid:
                // A commit the filter skips still moves its repository's chain on.
                if (!matches || handler is null)
                    await sync.ApplyAsync(result, cancellationToken).ConfigureAwait(false);
                else
                    handler.ApplyOnDelivery(evt, result.State!);
                return matches;

            case RepoSyncOutcome.Stale:
                Drop(StreamDropReason.Stale, evt.Seq, $"{result.Did}: {result.Reason}");
                return false;

            case RepoSyncOutcome.Desynchronized:
                // Held events are delivered after the repository's snapshot, not dropped.
                if (handler?.Resync is { } resync && await resync.HoldAsync(evt, result, cancellationToken).ConfigureAwait(false))
                    return false;

                Drop(StreamDropReason.Desynchronized, evt.Seq, $"{result.Did}: {result.Reason}");
                return false;

            default:
                _logger.LogWarning("Verification failed for event {Seq} from {Did}: {Error}", evt.Seq, result.Did, result.Reason);
                Drop(StreamDropReason.VerificationFailed, evt.Seq, $"{result.Did}: {result.Reason}");
                return false;
        }
    }

    private void Drop(StreamDropReason reason, long? cursor, string? detail)
    {
        _logger.LogDebug("Dropped firehose event {Cursor} ({Reason}): {Detail}", cursor, reason, detail);
        _options.OnEventDropped?.Invoke(new DroppedStreamEvent(reason, cursor, detail));
    }

    private static Uri Endpoint(string serviceUrl, long? cursor) =>
        new($"{serviceUrl.TrimEnd('/')}/xrpc/com.atproto.sync.subscribeRepos" +
            (cursor is { } value ? $"?cursor={value}" : string.Empty));

    private sealed class Handler(TypedFirehoseConsumer owner, CursorTracker cursor, RepoResyncCoordinator? resync)
        : EventStreamHandler<FirehoseMessage>
    {
        // The repository state the message being delivered moves to, recorded once the caller
        // asks for the next message: recording it earlier would skip the message after a crash.
        private FirehoseMessage? _pendingMessage;
        private RepoSyncState? _pendingState;

        // A held event being delivered: it keeps the saved cursor below it until the caller moves on.
        private FirehoseEvent? _deliveringHeld;

        public RepoResyncCoordinator? Resync => resync;

        public override string Stream => "firehose";

        public void ApplyOnDelivery(FirehoseMessage message, RepoSyncState state)
        {
            _pendingMessage = message;
            _pendingState = state;
        }

        public override ValueTask<(Uri Endpoint, StreamSocketOptions Options)> ConnectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult((Endpoint(owner._options.ServiceUrl, cursor.Current), default(StreamSocketOptions)));

        public override async ValueTask<FirehoseMessage?> NextPendingAsync(CancellationToken cancellationToken)
        {
            if (resync is null)
                return null;

            while (await resync.NextAsync(cancellationToken).ConfigureAwait(false) is { } pending)
            {
                switch (pending)
                {
                    case RepoResyncCoordinator.Snapshot snapshot:
                        ApplyOnDelivery(snapshot.Message, snapshot.State);
                        return snapshot.Message;

                    case RepoResyncCoordinator.Held held:
                        // Verified again now that the snapshot is in: it must chain on it.
                        if (await owner.AcceptAsync(held.Event, this, cancellationToken).ConfigureAwait(false))
                        {
                            _deliveringHeld = held.Event;
                            return held.Event;
                        }

                        resync.Release(held.Event.Seq);
                        break;

                    case RepoResyncCoordinator.Abandoned abandoned:
                        owner.Drop(StreamDropReason.Desynchronized, abandoned.Event.Seq, abandoned.Reason);
                        resync.Release(abandoned.Event.Seq);
                        break;

                    case RepoResyncCoordinator.Refetch refetch:
                        await resync.RefetchAsync(refetch, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }

            return null;
        }

        public override async ValueTask DeliveredAsync(FirehoseMessage message, CancellationToken cancellationToken)
        {
            Delivered(message);
            if (ReferenceEquals(message, _pendingMessage) && _pendingState is { } state)
            {
                _pendingMessage = null;
                _pendingState = null;

                // Not cancelled with the enumeration: the caller has already processed the message.
                await owner._options.SyncVerifier!.StateStore.SetAsync(state, CancellationToken.None).ConfigureAwait(false);
            }

            if (_deliveringHeld is { } held && ReferenceEquals(message, held))
            {
                _deliveringHeld = null;
                resync?.Release(held.Seq);
            }
        }

        public override async ValueTask<FirehoseMessage?> HandleAsync(
            string type, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            // With a sync verifier every commit is verified, so none can be skipped unread.
            if (owner._filter is not null && owner._options.SyncVerifier is null && type == "#commit")
            {
                // Read the paths before deserializing, so commits nobody asked for cost a scan.
                if (!owner._filter.TryScan(body, out var seq, out var matches))
                {
                    Dropped(StreamDropReason.Malformed, EventStreamFrame.ReadSeq(body), "#commit");
                    return null;
                }

                if (!matches)
                {
                    cursor.Advance(seq);
                    return null;
                }
            }

            var message = FirehoseEventParser.ParseBody(type, body);
            if (message is null)
            {
                // Still move the cursor past it, with a scan that reads nothing but the seq.
                Dropped(FirehoseEventParser.IsKnownType(type) ? StreamDropReason.Malformed : StreamDropReason.UnknownType,
                    EventStreamFrame.ReadSeq(body), type);
                return null;
            }

            // A relay replaying from an inclusive cursor, or rewinding, must not redeliver.
            if (message is FirehoseEvent sequenced && sequenced.Seq <= cursor.Current)
                return null;

            if (await owner.AcceptAsync(message, this, cancellationToken).ConfigureAwait(false))
                return message;

            if (message is FirehoseEvent dropped)
                cursor.Advance(dropped.Seq);
            return null;
        }

        public override void Delivered(FirehoseMessage message)
        {
            if (message is FirehoseEvent sequenced)
                cursor.Advance(sequenced.Seq);
        }

        public override void Dropped(StreamDropReason reason, long? position, string? detail)
        {
            if (position is { } value)
                cursor.Advance(value);
            owner.Drop(reason, position, detail);
        }
    }

    /// <summary>
    /// Matches commits against a collection filter, on parsed events or straight from the CBOR body.
    /// </summary>
    private sealed class CollectionMatcher
    {
        private readonly HashSet<string> _collections;
        private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;

        public CollectionMatcher(IEnumerable<Nsid> collections)
        {
            _collections = new HashSet<string>(collections.Select(c => c.Value), StringComparer.Ordinal);
            _lookup = _collections.GetAlternateLookup<ReadOnlySpan<char>>();
        }

        public bool Matches(CommitEvent commit)
        {
            if (commit.Ops is not { Count: > 0 } ops)
                return true;

            foreach (var op in ops)
            {
                var slash = op.Path.IndexOf('/');
                if (_lookup.Contains(slash >= 0 ? op.Path.AsSpan(0, slash) : op.Path.AsSpan()))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Reads a <c>#commit</c> body's <c>seq</c> and whether any <c>ops[].path</c> is in a
        /// filtered collection, without deserializing it.
        /// </summary>
        public bool TryScan(ReadOnlyMemory<byte> body, out long seq, out bool matches)
        {
            seq = 0;
            matches = false;
            var hasSeq = false;
            var hasOps = false;

            try
            {
                var reader = new CborReader(body, CborConformanceMode.Lax);
                reader.ReadStartMap();
                while (reader.PeekState() != CborReaderState.EndMap)
                {
                    var key = reader.ReadDefiniteLengthTextStringBytes().Span;
                    if (key.SequenceEqual("seq"u8))
                    {
                        seq = reader.ReadInt64();
                        hasSeq = true;
                    }
                    else if (key.SequenceEqual("ops"u8) && reader.PeekState() == CborReaderState.StartArray)
                    {
                        reader.ReadStartArray();
                        while (reader.PeekState() != CborReaderState.EndArray)
                        {
                            hasOps = true;
                            matches |= ScanOp(reader);
                        }

                        reader.ReadEndArray();
                    }
                    else
                    {
                        reader.SkipValue();
                    }
                }
            }
            catch (Exception ex) when (EventStreamFrame.IsMalformed(ex))
            {
                return false;
            }

            // A commit without operations passes the filter, as it does once parsed.
            matches |= !hasOps;
            return hasSeq;
        }

        private bool ScanOp(CborReader reader)
        {
            var matches = false;
            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                if (reader.ReadDefiniteLengthTextStringBytes().Span.SequenceEqual("path"u8)
                    && reader.PeekState() == CborReaderState.TextString)
                {
                    matches = MatchesPath(reader.ReadDefiniteLengthTextStringBytes().Span);
                }
                else
                {
                    reader.SkipValue();
                }
            }

            reader.ReadEndMap();
            return matches;
        }

        private bool MatchesPath(ReadOnlySpan<byte> path)
        {
            var slash = path.IndexOf((byte)'/');
            var collection = slash >= 0 ? path[..slash] : path;

            // An NSID is at most 317 characters; anything longer cannot be in the filter.
            if (collection.Length > 317)
                return false;

            Span<char> chars = stackalloc char[collection.Length];
            return Encoding.UTF8.TryGetChars(collection, chars, out var written)
                && _lookup.Contains(chars[..written]);
        }
    }
}
