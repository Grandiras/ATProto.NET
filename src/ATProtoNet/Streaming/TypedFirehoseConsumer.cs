using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using System.Text;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Streaming;

/// <summary>Configuration options for the typed firehose consumer.</summary>
public sealed class TypedFirehoseConsumerOptions : CursorStreamConsumerOptions
{
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
    /// Whether to check the blocks of commit and sync events against their CIDs: a local check,
    /// with no network access, that a relay did not alter them. A <see cref="SyncVerifier"/> does
    /// this and more. Default: false.
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
/// into typed <see cref="FirehoseMessage"/> objects, with collection filtering, CID or Sync 1.1
/// verification, and persistent cursor storage.
/// </summary>
/// <remarks>
/// <para>Each frame is parsed once. With a <see cref="TypedFirehoseConsumerOptions.CollectionFilter"/>,
/// a commit's operation paths are read straight from the CBOR first, so commits in other collections
/// are never deserialized.</para>
/// <para>With a <see cref="TypedFirehoseConsumerOptions.SyncVerifier"/>, each repository's commits
/// are verified as a chain (Sync 1.1), and with <see cref="TypedFirehoseConsumerOptions.Resync"/> a
/// repository whose chain breaks is fetched again and delivered as a <see cref="RepoResyncEvent"/>.</para>
/// <para>Delivery, cancellation and errors follow <see cref="StreamConsumerOptions"/>. For a single
/// connection, set <see cref="StreamReconnectPolicy.MaxAttempts"/> to 0.</para>
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
    private CursorTracker? _cursor;

    /// <summary>Create a typed firehose consumer.</summary>
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
    }

    /// <summary>
    /// The cursor of the current or last <see cref="ConsumeAsync"/> run: the sequence number it has
    /// passed and would resume after, or null when it started live and has seen no event yet.
    /// </summary>
    public long? LastSeq => _cursor?.Current;

    /// <summary>Consume typed firehose events with parsing, filtering, and optional verification.</summary>
    /// <param name="cursor">The sequence number to resume after. When null, the stored cursor is used
    /// if there is a <see cref="CursorStreamConsumerOptions.CursorStore"/>, and the live stream otherwise.</param>
    /// <param name="cancellationToken">Cancellation token to stop consuming.</param>
    /// <exception cref="EventStreamException">The relay sent an error that reconnecting cannot fix,
    /// such as <c>FutureCursor</c>, or every reconnect attempt the policy allows failed.</exception>
    public async IAsyncEnumerable<FirehoseMessage> ConsumeAsync(
        long? cursor = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (await CursorTracker.StartAsync(_options, cursor, cancellationToken).ConfigureAwait(false) is not { } tracker)
            yield break;

        _cursor = tracker;
        var handler = new Handler(_options, tracker);
        await using (tracker.ConfigureAwait(false))
        await using (handler.ConfigureAwait(false))
        {
            await foreach (var message in EventStreamLoop.RunAsync(handler, _connector, cancellationToken).ConfigureAwait(false))
                yield return message;
        }
    }

    private sealed class Handler : CborEventStreamHandler<FirehoseMessage>, IAsyncDisposable
    {
        private readonly TypedFirehoseConsumerOptions _options;
        private readonly CursorTracker _cursor;
        private readonly CollectionMatcher? _filter;
        private readonly RepoResyncCoordinator? _resync;

        // The repository state the message being delivered moves to, recorded once the caller
        // asks for the next message: recording it earlier would skip the message after a crash.
        private FirehoseMessage? _pendingMessage;
        private RepoSyncState? _pendingState;

        // A held event being delivered: it keeps the saved cursor below it until the caller moves on.
        private FirehoseEvent? _deliveringHeld;

        public Handler(TypedFirehoseConsumerOptions options, CursorTracker cursor)
            : base(options)
        {
            _options = options;
            _cursor = cursor;
            _filter = options.CollectionFilter is { Count: > 0 } collections ? new CollectionMatcher(collections) : null;
            _resync = options.Resync is { } resync
                ? new RepoResyncCoordinator(options.SyncVerifier!, resync, options.ServiceUrl, cursor.SetPersistLimit, Logger)
                : null;
        }

        public override string Stream => "firehose";

        public override ValueTask<(Uri Endpoint, StreamSocketOptions Options)> ConnectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult((Endpoint(_options.ServiceUrl, "com.atproto.sync.subscribeRepos", _cursor.Current), default(StreamSocketOptions)));

        public override async ValueTask<FirehoseMessage?> NextPendingAsync(CancellationToken cancellationToken)
        {
            if (_resync is null)
                return null;

            while (await _resync.NextAsync(cancellationToken).ConfigureAwait(false) is { } pending)
            {
                switch (pending)
                {
                    case RepoResyncCoordinator.Snapshot snapshot:
                        (_pendingMessage, _pendingState) = (snapshot.Message, snapshot.State);
                        return snapshot.Message;

                    case RepoResyncCoordinator.Held held:
                        // Verified again now that the snapshot is in: it must chain on it.
                        if (await AcceptAsync(held.Event, cancellationToken).ConfigureAwait(false))
                        {
                            _deliveringHeld = held.Event;
                            return held.Event;
                        }

                        _resync.Release(held.Event.Seq);
                        break;

                    case RepoResyncCoordinator.Abandoned abandoned:
                        Report(StreamDropReason.Desynchronized, abandoned.Event.Seq, abandoned.Reason);
                        _resync.Release(abandoned.Event.Seq);
                        break;

                    case RepoResyncCoordinator.Refetch refetch:
                        await _resync.RefetchAsync(refetch, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }

            return null;
        }

        protected override async ValueTask<FirehoseMessage?> HandleAsync(
            string type, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            // With a sync verifier every commit is verified, so none can be skipped unread.
            if (_filter is not null && _options.SyncVerifier is null && type == "#commit")
            {
                // Read the paths before deserializing, so commits nobody asked for cost a scan.
                if (!_filter.TryScan(body, out var seq, out var matches))
                {
                    Dropped(StreamDropReason.Malformed, EventStreamFrame.ReadSeq(body), "#commit");
                    return null;
                }

                if (!matches)
                {
                    _cursor.Advance(seq);
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
            if (message is FirehoseEvent sequenced && sequenced.Seq <= _cursor.Current)
                return null;

            if (await AcceptAsync(message, cancellationToken).ConfigureAwait(false))
                return message;

            if (message is FirehoseEvent dropped)
                _cursor.Advance(dropped.Seq);
            return null;
        }

        public override async ValueTask DeliveredAsync(FirehoseMessage message, CancellationToken cancellationToken)
        {
            if (message is FirehoseEvent sequenced)
                _cursor.Advance(sequenced.Seq);

            if (ReferenceEquals(message, _pendingMessage) && _pendingState is { } state)
            {
                (_pendingMessage, _pendingState) = (null, null);

                // Not cancelled with the enumeration: the caller has already processed the message.
                await _options.SyncVerifier!.StateStore.SetAsync(state, CancellationToken.None).ConfigureAwait(false);
            }

            if (_deliveringHeld is { } held && ReferenceEquals(message, held))
            {
                _deliveringHeld = null;
                _resync?.Release(held.Seq);
            }
        }

        public override void Dropped(StreamDropReason reason, long? cursor, string? detail)
        {
            if (cursor is { } position)
                _cursor.Advance(position);
            Report(reason, cursor, detail);
        }

        /// <summary>Reports a skipped event without moving the cursor, which the caller does or must not do.</summary>
        private void Report(StreamDropReason reason, long? cursor, string? detail) => base.Dropped(reason, cursor, detail);

        /// <summary>
        /// Filters and verifies one parsed message. Returns whether it is delivered; the state of a
        /// sync-verified event it delivers is recorded on delivery.
        /// </summary>
        private async ValueTask<bool> AcceptAsync(FirehoseMessage message, CancellationToken cancellationToken)
        {
            if (_options.SyncVerifier is { } syncVerifier)
            {
                switch (message)
                {
                    case CommitEvent commit:
                        var commitResult = await syncVerifier.VerifyCommitAsync(commit, cancellationToken).ConfigureAwait(false);
                        return await AcceptSyncedAsync(syncVerifier, commit, commitResult, _filter?.Matches(commit) ?? true, cancellationToken)
                            .ConfigureAwait(false);

                    case SyncEvent syncEvent:
                        var syncResult = await syncVerifier.VerifySyncAsync(syncEvent, cancellationToken).ConfigureAwait(false);
                        return await AcceptSyncedAsync(syncVerifier, syncEvent, syncResult, matches: true, cancellationToken)
                            .ConfigureAwait(false);

                    case IdentityEvent identity:
                        // The account's keys may have changed: its next commit must not be checked
                        // against a cached document.
                        await syncVerifier.InvalidateIdentityAsync(identity.Did, cancellationToken).ConfigureAwait(false);
                        return true;
                }
            }

            switch (message)
            {
                case CommitEvent commit:
                    if (_filter is not null && !_filter.Matches(commit))
                        return false;
                    return !_options.VerifyCids || CidsVerify(commit.Seq, commit.Repo, commit.Blocks);

                case SyncEvent sync:
                    return !_options.VerifyCids || CidsVerify(sync.Seq, sync.Did, sync.Blocks);

                default:
                    return true;
            }
        }

        /// <summary>Acts on a <see cref="RepoSyncVerifier"/> outcome. Returns whether the event is delivered.</summary>
        private async ValueTask<bool> AcceptSyncedAsync(
            RepoSyncVerifier sync, FirehoseEvent evt, RepoSyncResult result, bool matches, CancellationToken cancellationToken)
        {
            switch (result.Outcome)
            {
                case RepoSyncOutcome.Valid:
                    // A commit the filter skips still moves its repository's chain on.
                    if (!matches)
                        await sync.ApplyAsync(result, cancellationToken).ConfigureAwait(false);
                    else
                        (_pendingMessage, _pendingState) = (evt, result.State!);
                    return matches;

                case RepoSyncOutcome.Stale:
                    Report(StreamDropReason.Stale, evt.Seq, $"{result.Did}: {result.Reason}");
                    return false;

                case RepoSyncOutcome.Desynchronized:
                    // Held events are delivered after the repository's snapshot, not dropped.
                    if (_resync is not null && await _resync.HoldAsync(evt, result, cancellationToken).ConfigureAwait(false))
                        return false;

                    Report(StreamDropReason.Desynchronized, evt.Seq, $"{result.Did}: {result.Reason}");
                    return false;

                default:
                    Logger.LogWarning("Verification failed for event {Seq} from {Did}: {Error}", evt.Seq, result.Did, result.Reason);
                    Report(StreamDropReason.VerificationFailed, evt.Seq, $"{result.Did}: {result.Reason}");
                    return false;
            }
        }

        /// <summary>
        /// Checks every block of an event's CAR against its CID, failing closed on a codec other
        /// than dag-cbor or raw, which could otherwise carry blocks past the check. Reports the
        /// event when they do not verify.
        /// </summary>
        private bool CidsVerify(long seq, Did did, byte[]? blocks)
        {
            string error;
            try
            {
                if (blocks is { Length: > 0 })
                {
                    CarReader.FromBytes(blocks, verifyBlockCids: true);
                    return true;
                }

                error = "The event carries no blocks.";
            }
            catch (FormatException ex)
            {
                error = $"The event's blocks do not verify: {ex.Message}";
            }

            Logger.LogWarning("CID verification failed for event {Seq} from {Did}: {Error}", seq, did, error);
            Report(StreamDropReason.VerificationFailed, seq, error);
            return false;
        }

        public ValueTask DisposeAsync() => _resync?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    /// <summary>Matches commits against a collection filter, on parsed events or straight from the CBOR body.</summary>
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
            catch (Exception ex) when (DagCborDecoder.IsMalformed(ex))
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
