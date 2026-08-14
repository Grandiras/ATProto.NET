using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Streaming;

/// <summary>
/// Consumes the Jetstream v2 archive — a full-network backfill over the HTTP replay endpoints —
/// and, unless snapshot mode is selected, cuts over into the live tail with no gap at the seam.
/// </summary>
/// <remarks>
/// <para>The backfill is <b>stateless on the server</b>: there is no registered subscription and no
/// per-consumer cursor. Each round trip is
/// <c>planSnapshot</c> → download (<c>getSegment</c> / <c>getBlock</c>) → decode → filter, and the
/// consumer pins the first response's <c>sealedTipSeq</c> as the ceiling for the whole backfill so
/// the window cannot float underneath it. The planner works from bloom filters and per-block
/// summaries, so it never misses matching data but can hand back blocks with none — the exact
/// <see cref="JetstreamConsumerOptions.WantedDids"/> /
/// <see cref="JetstreamConsumerOptions.WantedCollections"/> /
/// <see cref="JetstreamConsumerOptions.WantedKinds"/> filter is applied again here, to what was
/// decoded.</para>
/// <para>The cutover connects the live socket once at the pinned tip. That cursor is inclusive, so
/// events at or below the last sequence number already delivered are dropped and one
/// <c>await foreach</c> spans history and live. If the backfill runs long enough that the pinned
/// tip ages out of the socket's lookback window (36 hours on the Bluesky-hosted instances), the
/// connect is refused with a <see cref="JetstreamConnectException"/> — the consumer re-enters the
/// plan loop from the last sequence number it delivered rather than skipping the gap, up to
/// <see cref="JetstreamArchiveOptions.MaxCutoverAttempts"/> times. A backfill that stops advancing
/// below the pinned tip is retried
/// (<see cref="JetstreamArchiveOptions.MaxStalledPlanAttempts"/>) and then fails with a
/// <see cref="JetstreamArchiveException"/> rather than cutting over across the hole.</para>
/// <para>Delivery is <b>at-least-once and folded, not filtered</b>: every matching event arrives in
/// sequence order, including creates a later delete supersedes, and events after the last persisted
/// cursor are redelivered when a new process resumes. Fold into idempotent writes keyed on the
/// record's <c>at://</c> URI; an account event with <c>Active = false</c>, or a
/// <see cref="JetstreamSyncEvent"/>, removes all of that account's records. Account-level events
/// carry no collection and are delivered even to a collection-filtered consumer, exactly as on the
/// live tail.</para>
/// <para>Requires <see cref="JetstreamProtocol.V2"/> — v1 has no archive.</para>
/// </remarks>
/// <example>
/// <code>
/// var consumer = new JetstreamReplayConsumer(new JetstreamConsumerOptions
/// {
///     ServiceUrl = JetstreamEndpoints.UsEast,
///     Protocol = JetstreamProtocol.V2,
///     WantedCollections = ["app.bsky.feed.post"],
///     WantedKinds = [JetstreamEventKind.Commit],
///     CursorStore = myCursorStore,
///     Archive = new JetstreamArchiveOptions
///     {
///         ApiKey = apiKey,
///         BlockDecompressor = new ZstdBlockDecompressor(),
///     },
/// });
///
/// await foreach (var evt in consumer.ReplayAsync())
/// {
///     // History first, then the live tail, in one sequence-ordered stream.
///     await IndexAsync(evt);
/// }
/// </code>
/// </example>
public sealed class JetstreamReplayConsumer : IDisposable
{
    private readonly JetstreamConsumerOptions _options;
    private readonly JetstreamArchiveOptions _archive;
    private readonly JetstreamArchiveClient _client;
    private readonly bool _ownsClient;
    private readonly ILogger _logger;
    private int _eventsSinceLastPersist;
    private bool _disposed;

    /// <summary>The sequence number of the last event delivered, or null before the first one.</summary>
    public long? LastCursor { get; private set; }

    /// <summary>Whether the consumer is still reading the archive rather than the live tail.</summary>
    public bool IsBackfilling { get; private set; }

    /// <summary>The sealed tip pinned for the current backfill, or null before the first plan.</summary>
    public long? PinnedTipSeq { get; private set; }

    /// <summary>
    /// Create a replay consumer.
    /// </summary>
    /// <param name="options">Consumer configuration.
    /// <see cref="JetstreamConsumerOptions.Protocol"/> must be <see cref="JetstreamProtocol.V2"/>
    /// and <see cref="JetstreamConsumerOptions.Archive"/> must be set.</param>
    /// <exception cref="ArgumentException">The options cannot describe a replay.</exception>
    public JetstreamReplayConsumer(JetstreamConsumerOptions options)
        : this(options, archiveClient: null)
    {
    }

    /// <summary>
    /// Create a replay consumer over an existing archive client.
    /// </summary>
    /// <param name="options">Consumer configuration.</param>
    /// <param name="archiveClient">The archive client to plan and download with. When null, one is
    /// built from <see cref="JetstreamConsumerOptions.Archive"/> and disposed with this
    /// instance.</param>
    /// <exception cref="ArgumentException">The options cannot describe a replay.</exception>
    public JetstreamReplayConsumer(JetstreamConsumerOptions options, JetstreamArchiveClient? archiveClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _archive = Validate(options);
        _logger = options.Logger ?? NullLogger.Instance;

        _client = archiveClient ?? new JetstreamArchiveClient(
            _archive.ServiceUrl ?? options.ServiceUrl,
            _archive.ApiKey,
            _archive.HttpClient,
            options.Logger)
        {
            MaxRetryAttempts = _archive.MaxRetryAttempts,
            MaxRetryDelay = _archive.MaxRetryDelay,
        };
        _ownsClient = archiveClient is null;
    }

    /// <summary>
    /// Replay the archive from <paramref name="afterSeq"/>, then continue with the live tail unless
    /// <see cref="JetstreamArchiveOptions.SnapshotOnly"/> is set.
    /// </summary>
    /// <param name="afterSeq">Resume position: events at or below this sequence number are not
    /// delivered. When null, <see cref="JetstreamArchiveOptions.AfterSeq"/> is used, then the
    /// <see cref="JetstreamConsumerOptions.CursorStore"/>, and failing both the replay starts at
    /// the beginning of the archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="JetstreamArchiveException">An archive request failed unrecoverably, or the
    /// plan stopped advancing below the pinned tip for
    /// <see cref="JetstreamArchiveOptions.MaxStalledPlanAttempts"/> consecutive pages.</exception>
    /// <exception cref="JetstreamConnectException">The live cutover was refused and could not be
    /// recovered by re-planning within
    /// <see cref="JetstreamArchiveOptions.MaxCutoverAttempts"/> attempts.</exception>
    public async IAsyncEnumerable<JetstreamEvent> ReplayAsync(
        long? afterSeq = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var start = afterSeq ?? _archive.AfterSeq;

        if (start is null && _options.CursorStore is not null)
        {
            start = await _options.CursorStore.GetCursorAsync(_options.ResolvedStreamId, cancellationToken);
            if (start.HasValue)
                _logger.LogInformation("Resuming Jetstream replay from stored cursor {Cursor}", start.Value);
        }

        var filter = new JetstreamArchiveFilter(_options);

        // Persist whatever was delivered on every exit path — a cancelled backfill included, so a
        // restart resumes near where it stopped rather than at the last interval boundary.
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                // Backfill: plan, download, decode, filter — everything sealed up to the pinned tip.
                IsBackfilling = true;
                await foreach (var evt in BackfillAsync(start ?? 0, filter, cancellationToken))
                {
                    if (await TrackAsync(evt, cancellationToken))
                        yield return evt;
                }

                IsBackfilling = false;

                if (_archive.SnapshotOnly)
                    yield break;

                // Cutover: one live connection at the pinned tip, which the server replays inclusively.
                var tip = PinnedTipSeq ?? LastCursor ?? start ?? 0;
                var live = new JetstreamConsumer(_options);
                JetstreamConnectException? refused = null;

                try
                {
                    var enumerator = live.ConsumeAsync(tip, cancellationToken).GetAsyncEnumerator(cancellationToken);
                    try
                    {
                        while (true)
                        {
                            JetstreamEvent? evt = null;
                            try
                            {
                                if (await enumerator.MoveNextAsync())
                                    evt = enumerator.Current;
                            }
                            catch (JetstreamConnectException ex) when (!ex.IsRetryable)
                            {
                                // The pinned tip aged out of the lookback window while we backfilled.
                                refused = ex;
                            }

                            if (evt is null)
                                break;

                            if (await TrackAsync(evt, cancellationToken))
                                yield return evt;
                        }
                    }
                    finally
                    {
                        await enumerator.DisposeAsync();
                    }
                }
                finally
                {
                    live.Dispose();
                }

                if (refused is null)
                    yield break;

                if (attempt >= _archive.MaxCutoverAttempts)
                {
                    throw new JetstreamConnectException(
                        $"The Jetstream live tail refused the cutover at sequence {tip} after " +
                        $"{attempt + 1} backfill attempts; the archive is not catching up to the " +
                        "socket's lookback window.",
                        refused.StatusCode,
                        refused);
                }

                // Re-enter the plan loop from what we durably delivered rather than skip the gap.
                start = LastCursor ?? start;
                PinnedTipSeq = null;
                _logger.LogWarning(refused,
                    "Jetstream refused the cutover at {Tip}; re-planning from {Cursor}", tip, start);
            }
        }
        finally
        {
            await PersistAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Read everything sealed above <paramref name="afterSeq"/>, paging the plan until it reaches
    /// the tip pinned by the first page.
    /// </summary>
    private async IAsyncEnumerable<JetstreamEvent> BackfillAsync(
        long afterSeq,
        JetstreamArchiveFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long? tip = null;
        var planned = afterSeq;
        var stalls = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var plan = await _client.PlanSnapshotAsync(
                new JetstreamSnapshotRequest
                {
                    Kinds = filter.PlanKinds,
                    Dids = _options.WantedDids,
                    Collections = _options.WantedCollections,
                    AfterSeq = planned,
                    // Pin the first page's tip as the ceiling for every later page, so the range
                    // cannot float upward while the backfill is downloading it.
                    BeforeSeq = tip ?? _archive.BeforeSeq,
                },
                cancellationToken);

            // A snapshot's BeforeSeq caps the ceiling: without it the pinned tip would replace the
            // caller's bound on the second page and the snapshot would run to the sealed tip.
            tip ??= _archive.BeforeSeq is { } before
                ? Math.Min(before, plan.SealedTipSeq)
                : plan.SealedTipSeq;
            PinnedTipSeq = tip;
            var ceiling = tip.Value;

            // The server truncates a page at a whole work-unit boundary and always admits at least
            // one, so plannedThroughSeq advances every page. A page that does not advance while the
            // ceiling is still ahead means the sealed tip was reported before its segments became
            // servable — wait it out. Truncating here would be a silent, permanent gap: the cutover
            // reconnects at the pinned ceiling and never redelivers the range that was skipped.
            if (plan.PlannedThroughSeq <= planned && planned < ceiling)
            {
                if (++stalls > _archive.MaxStalledPlanAttempts)
                    throw new JetstreamArchiveException(
                        $"Jetstream planned through {plan.PlannedThroughSeq}, which does not advance " +
                        $"past {planned}, on {stalls} consecutive attempts; the backfill cannot reach " +
                        $"the pinned tip {ceiling}. Delivered through " +
                        $"{LastCursor?.ToString() ?? "nothing"}. Resume from that sequence rather " +
                        "than cutting over, or the range in between is lost.");

                var delay = StallDelay(stalls);
                _logger.LogWarning(
                    "Jetstream planned through {Through}, which does not advance past {Planned} " +
                    "below the tip {Tip}; re-planning in {Delay} (attempt {Attempt} of {Max})",
                    plan.PlannedThroughSeq, planned, ceiling, delay, stalls,
                    _archive.MaxStalledPlanAttempts);

                await Task.Delay(delay, cancellationToken);
                continue;
            }

            stalls = 0;

            _logger.LogInformation(
                "Planned {Segments} segment(s) through sequence {Through} of {Tip}",
                plan.Segments.Count, plan.PlannedThroughSeq, ceiling);

            await foreach (var row in DownloadAsync(plan.Segments, cancellationToken))
            {
                // The planner has no false negatives but does return blocks with no matching rows,
                // so the exact filter is applied to what was decoded.
                if (row.Seq <= (LastCursor ?? afterSeq) || row.Seq > ceiling)
                    continue;
                if (row.ToEvent() is not { } evt || !filter.Matches(evt))
                    continue;

                yield return evt;
            }

            // Reached only at or above the ceiling, since a non-advancing page below it re-plans.
            if (plan.PlannedThroughSeq <= planned)
                break;

            planned = plan.PlannedThroughSeq;
            if (planned >= ceiling)
                break;
        }
    }

    /// <summary>
    /// How long to wait before re-planning a page that did not advance: exponential from a second,
    /// capped by <see cref="JetstreamArchiveOptions.MaxRetryDelay"/>.
    /// </summary>
    private TimeSpan StallDelay(int attempt)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(attempt - 1, 6)));
        return delay < _archive.MaxRetryDelay ? delay : _archive.MaxRetryDelay;
    }

    /// <summary>
    /// Download the planned work units — whole segments or single blocks — with
    /// <see cref="JetstreamArchiveOptions.DownloadParallelism"/> in flight, and decode them
    /// strictly in plan order so events stay sequence-ordered.
    /// </summary>
    private async IAsyncEnumerable<JetstreamArchiveRow> DownloadAsync(
        IReadOnlyList<JetstreamPlannedSegment> segments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var units = WorkUnits(segments).ToList();
        if (units.Count == 0)
            yield break;

        var parallelism = Math.Max(1, _archive.DownloadParallelism);
        var channel = Channel.CreateBounded<Task<DownloadedUnit>>(
            new BoundedChannelOptions(parallelism) { SingleReader = true, SingleWriter = true });

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var producer = Task.Run(async () =>
        {
            Task<DownloadedUnit>? pending = null;
            try
            {
                foreach (var unit in units)
                {
                    // Started here, awaited by the consumer: the bounded channel is what caps how
                    // many downloads run at once.
                    pending = DownloadUnitAsync(unit, abort.Token);
                    await channel.Writer.WriteAsync(pending, abort.Token);
                    pending = null;
                }

                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
                if (pending is not null)
                    await Observe(pending);
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var download in channel.Reader.ReadAllAsync(cancellationToken))
            {
                var unit = await download;
                try
                {
                    await foreach (var row in unit.ReadAsync(_archive.BlockDecompressor, cancellationToken))
                        yield return row;
                }
                finally
                {
                    unit.Dispose();
                }
            }
        }
        finally
        {
            // Stop the prefetch and observe every download still in flight, so an abandoned
            // failure does not surface later as an unobserved task exception.
            await abort.CancelAsync();
            channel.Writer.TryComplete();
            await Drain(channel.Reader);
            await producer;
        }

        static async Task Drain(ChannelReader<Task<DownloadedUnit>> reader)
        {
            while (reader.TryRead(out var pending))
                await Observe(pending);
        }

        static async Task Observe(Task<DownloadedUnit> pending)
        {
            try
            {
                (await pending).Dispose();
            }
            catch (Exception)
            {
                // Already aborting: this download's failure is not the one being reported.
            }
        }
    }

    /// <summary>Expand a plan page into download units, in sequence order.</summary>
    private static IEnumerable<WorkUnit> WorkUnits(IReadOnlyList<JetstreamPlannedSegment> segments)
    {
        foreach (var segment in segments)
        {
            if (segment.DownloadMode == JetstreamSegmentDownloadMode.Segment)
            {
                yield return new WorkUnit(segment, null);
                continue;
            }

            foreach (var range in segment.Blocks!)
            {
                for (var block = range.First; block <= range.Last; block++)
                    yield return new WorkUnit(segment, block);
            }
        }
    }

    private async Task<DownloadedUnit> DownloadUnitAsync(WorkUnit unit, CancellationToken cancellationToken)
    {
        if (unit.BlockIndex is { } blockIndex)
        {
            var frame = await _client.GetBlockAsync(unit.Segment.Name, blockIndex, cancellationToken);
            return new DownloadedUnit(frame, null);
        }

        // Segments run to hundreds of megabytes, so they are spooled to disk rather than buffered.
        var path = Path.Combine(
            _archive.SpoolDirectory ?? Path.GetTempPath(),
            $"jetstream-{Guid.NewGuid():n}.jss");

        var file = new FileStream(
            path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        try
        {
            await _client.DownloadSegmentAsync(unit.Segment.Name, file, cancellationToken);
            file.Position = 0;
            return new DownloadedUnit(null, file);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Record an event as delivered, persisting the cursor on the configured interval.
    /// </summary>
    /// <returns>Whether the event should be delivered: false for one already delivered, which the
    /// inclusive cutover cursor and an overlapping re-plan can both produce.</returns>
    private async Task<bool> TrackAsync(JetstreamEvent evt, CancellationToken cancellationToken)
    {
        if (evt.Cursor is not { } seq)
            return true;

        if (LastCursor.HasValue && seq <= LastCursor.Value)
            return false;

        LastCursor = seq;

        if (_options.CursorStore is not null && ++_eventsSinceLastPersist >= _options.CursorPersistInterval)
            await PersistAsync(cancellationToken);

        return true;
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        if (_options.CursorStore is null || LastCursor is not { } cursor)
            return;

        await _options.CursorStore.StoreCursorAsync(_options.ResolvedStreamId, cursor, cancellationToken);
        _eventsSinceLastPersist = 0;
    }

    private static JetstreamArchiveOptions Validate(JetstreamConsumerOptions options)
    {
        if (options.Protocol != JetstreamProtocol.V2)
            throw new ArgumentException(
                "The Jetstream archive is v2-only: set Protocol = JetstreamProtocol.V2.",
                nameof(options));

        if (options.Archive is not { } archive)
            throw new ArgumentException(
                "Replaying the archive needs JetstreamConsumerOptions.Archive to be set.",
                nameof(options));

        if (archive.BeforeSeq is not null && !archive.SnapshotOnly)
            throw new ArgumentException(
                "BeforeSeq bounds the replay above, so there is nothing to cut over into: " +
                "set SnapshotOnly = true alongside it.",
                nameof(options));

        if (archive.AfterSeq is not null && archive.BeforeSeq is not null
            && archive.AfterSeq >= archive.BeforeSeq)
            throw new ArgumentException(
                $"AfterSeq ({archive.AfterSeq}) must be below BeforeSeq ({archive.BeforeSeq}).",
                nameof(options));

        if (options.WantedCollections is { Count: > 0 }
            && options.WantedKinds is { Count: > 0 } kinds
            && !kinds.Contains(JetstreamEventKind.Commit))
            throw new ArgumentException(
                "WantedCollections only constrains commit events, so it cannot be combined with a " +
                "WantedKinds list that excludes JetstreamEventKind.Commit.",
                nameof(options));

        return archive;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsClient)
            _client.Dispose();
    }

    /// <summary>One planned download: a whole segment, or a single block within one.</summary>
    private sealed record WorkUnit(JetstreamPlannedSegment Segment, int? BlockIndex);

    /// <summary>A downloaded work unit: an in-memory block frame, or a spooled segment file.</summary>
    private sealed class DownloadedUnit(byte[]? frame, FileStream? file) : IDisposable
    {
        public async IAsyncEnumerable<JetstreamArchiveRow> ReadAsync(
            IJetstreamBlockDecompressor decompressor,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (file is not null)
            {
                await foreach (var row in JetstreamSegmentReader.ReadRowsAsync(file, decompressor, cancellationToken))
                    yield return row;

                yield break;
            }

            foreach (var row in JetstreamSegmentReader.DecodeBlockFrame(frame!, decompressor))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
            }
        }

        public void Dispose() => file?.Dispose();
    }
}

/// <summary>
/// The exact client-side filter the replay applies to decoded rows, mirroring the live tail's
/// server-side semantics: DIDs and kinds constrain everything, collections constrain commit events
/// only.
/// </summary>
internal sealed class JetstreamArchiveFilter
{
    private readonly HashSet<string>? _dids;
    private readonly HashSet<string>? _collections;
    private readonly string[] _collectionPrefixes;
    private readonly HashSet<JetstreamEventKind>? _kinds;

    public JetstreamArchiveFilter(JetstreamConsumerOptions options)
    {
        if (options.WantedDids is { Count: > 0 } dids)
            _dids = [.. dids];

        if (options.WantedKinds is { Count: > 0 } kinds)
            _kinds = [.. kinds];

        if (options.WantedCollections is { Count: > 0 } collections)
        {
            _collections = [.. collections.Where(c => !c.EndsWith('*'))];
            _collectionPrefixes = [.. collections.Where(c => c.EndsWith('*')).Select(c => c[..^1])];
        }
        else
        {
            _collectionPrefixes = [];
        }

        PlanKinds = options.WantedKinds is { Count: > 0 } wanted
            ? [.. wanted.Select(KindName)]
            : null;
    }

    /// <summary>The <c>kinds</c> filter as the wire names <c>planSnapshot</c> takes.</summary>
    public IReadOnlyList<string>? PlanKinds { get; }

    public bool Matches(JetstreamEvent evt)
    {
        if (_dids is not null && !_dids.Contains(evt.Did.ToString()))
            return false;

        if (_kinds is not null && !_kinds.Contains(KindOf(evt)))
            return false;

        // A collection filter constrains commit events only: identity, account, and sync events
        // carry no collection and flow regardless, exactly as on the live tail.
        if (_collections is null || evt is not JetstreamCommitEvent commit)
            return true;

        if (_collections.Contains(commit.Collection))
            return true;

        foreach (var prefix in _collectionPrefixes)
        {
            if (commit.Collection.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static JetstreamEventKind KindOf(JetstreamEvent evt) => evt switch
    {
        JetstreamCommitEvent => JetstreamEventKind.Commit,
        JetstreamIdentityEvent => JetstreamEventKind.Identity,
        JetstreamAccountEvent => JetstreamEventKind.Account,
        _ => JetstreamEventKind.Sync,
    };

    private static string KindName(JetstreamEventKind kind) => kind switch
    {
        JetstreamEventKind.Commit => "commit",
        JetstreamEventKind.Identity => "identity",
        JetstreamEventKind.Account => "account",
        JetstreamEventKind.Sync => "sync",
        _ => throw new ArgumentException($"Unknown Jetstream event kind: {kind}", nameof(kind)),
    };
}
