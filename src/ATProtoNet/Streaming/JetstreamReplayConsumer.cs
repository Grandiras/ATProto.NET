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
/// <see cref="JetstreamConsumerOptions.WantedKinds"/> filter is applied again here, to each row's
/// columns before the row is decoded. Downloads and decoding run
/// <see cref="JetstreamArchiveOptions.DownloadParallelism"/> wide, and events are still delivered
/// strictly in sequence order.</para>
/// <para>The cutover connects the live socket once at the pinned tip. That cursor is inclusive, so
/// events at or below the last sequence number already delivered are dropped and one
/// <c>await foreach</c> spans history and live. If the backfill runs long enough that the pinned
/// tip ages out of the socket's lookback window (36 hours on the Bluesky-hosted instances), the
/// connect is refused with a <see cref="JetstreamException"/> — the consumer re-enters the
/// plan loop from the last sequence number it delivered rather than skipping the gap, up to
/// <see cref="JetstreamArchiveOptions.MaxCutoverAttempts"/> times. A backfill that stops advancing
/// below the pinned tip is retried
/// (<see cref="JetstreamArchiveOptions.MaxStalledPlanAttempts"/>) and then fails with a
/// <see cref="JetstreamException"/> rather than cutting over across the hole.</para>
/// <para>Delivery is <b>at-least-once and folded, not filtered</b>: every matching event arrives in
/// sequence order, including creates a later delete supersedes, and events after the last persisted
/// cursor are redelivered when a new process resumes. Fold into idempotent writes keyed on the
/// record's <c>at://</c> URI; an account event with <c>Active = false</c>, or a
/// <see cref="JetstreamSyncEvent"/>, removes all of that account's records. Account-level events
/// carry no collection and are delivered even to a collection-filtered consumer, exactly as on the
/// live tail. Cancelling the token ends the enumeration normally.</para>
/// <para>Requires <see cref="JetstreamProtocol.V2"/> — v1 has no archive.</para>
/// </remarks>
/// <example>
/// <code>
/// using var consumer = new JetstreamReplayConsumer(new JetstreamConsumerOptions
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
    private readonly Func<JetstreamConsumerOptions, JetstreamConsumer> _liveFactory;
    private bool _disposed;

    /// <summary>The sequence number of the last event delivered, or null before the first one.</summary>
    public long? LastCursor { get; private set; }

    /// <summary>Whether the consumer is still reading the archive rather than the live tail.</summary>
    public bool IsBackfilling { get; private set; }

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
        : this(options, archiveClient, live => new JetstreamConsumer(live))
    {
    }

    internal JetstreamReplayConsumer(
        JetstreamConsumerOptions options,
        JetstreamArchiveClient? archiveClient,
        Func<JetstreamConsumerOptions, JetstreamConsumer> liveFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _archive = Validate(options);
        _logger = options.Logger ?? NullLogger.Instance;
        _liveFactory = liveFactory;

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
    /// <see cref="StreamConsumerOptions.CursorStore"/>, and failing both the replay starts at
    /// the beginning of the archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">The resume position is a timestamp cursor
    /// (<see cref="JetstreamCursor"/>): the archive is addressed by sequence number only.</exception>
    /// <exception cref="JetstreamException">An archive request failed unrecoverably, the plan
    /// stopped advancing below the pinned tip for
    /// <see cref="JetstreamArchiveOptions.MaxStalledPlanAttempts"/> consecutive pages, or the live
    /// cutover was refused and could not be recovered by re-planning within
    /// <see cref="JetstreamArchiveOptions.MaxCutoverAttempts"/> attempts.</exception>
    /// <exception cref="EventStreamException">The live tail kept disconnecting until
    /// <see cref="StreamConsumerOptions.Reconnect"/> gave up.</exception>
    public async IAsyncEnumerable<JetstreamEvent> ReplayAsync(
        long? afterSeq = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tracker = new CursorTracker(
            _options.CursorStore, _options.ResolvedStreamId, _options.CursorPersistInterval, _logger);

        var start = afterSeq ?? _archive.AfterSeq;
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
                _logger.LogInformation("Resuming Jetstream replay from stored cursor {Cursor}", start.Value);
        }

        if (start is { } resume && JetstreamCursor.IsTimestamp(resume))
            throw new ArgumentException(
                $"The Jetstream archive is addressed by sequence number, and {resume} is a timestamp cursor. " +
                "Replay from a sequence number (or from the start), or tail the live stream from the timestamp " +
                "with JetstreamConsumer.",
                nameof(afterSeq));

        tracker.Start(start);

        // Persist whatever was delivered on every exit path — a cancelled backfill included, so a
        // restart resumes near where it stopped rather than at the last interval boundary.
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                // Backfill: plan, download, decode, filter — everything sealed up to the pinned tip.
                IsBackfilling = true;
                long? tip = null;
                var backfill = BackfillAsync(start ?? 0, pinned => tip = pinned, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            if (!await backfill.MoveNextAsync().ConfigureAwait(false))
                                break;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            yield break;
                        }

                        var evt = backfill.Current;
                        if (!IsNew(evt))
                            continue;

                        yield return evt;
                        tracker.Advance(evt.Cursor!.Value);
                    }
                }
                finally
                {
                    await backfill.DisposeAsync().ConfigureAwait(false);
                }

                IsBackfilling = false;

                if (_archive.SnapshotOnly || cancellationToken.IsCancellationRequested)
                    yield break;

                // Cutover: one live connection at the pinned tip, which the server replays inclusively.
                var cutover = tip ?? LastCursor ?? start ?? 0;
                JetstreamException? refused = null;

                var live = _liveFactory(_options).ConsumeAsync(cutover, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            if (!await live.MoveNextAsync().ConfigureAwait(false))
                                break;
                        }
                        catch (JetstreamException ex) when (!ex.IsRetryable && ex.StatusCode is not null)
                        {
                            // The pinned tip aged out of the lookback window while we backfilled.
                            refused = ex;
                            break;
                        }

                        var evt = live.Current;
                        if (!IsNew(evt))
                            continue;

                        yield return evt;
                        if (evt.Cursor is { } seq)
                            tracker.Advance(seq);
                    }
                }
                finally
                {
                    await live.DisposeAsync().ConfigureAwait(false);
                }

                if (refused is null)
                    yield break;

                if (attempt >= _archive.MaxCutoverAttempts)
                {
                    throw new JetstreamException(
                        $"The Jetstream live tail refused the cutover at sequence {cutover} after " +
                        $"{attempt + 1} backfill attempts; the archive is not catching up to the " +
                        "socket's lookback window.",
                        refused.StatusCode,
                        refused.Error,
                        innerException: refused);
                }

                // Re-enter the plan loop from what we durably delivered rather than skip the gap.
                start = LastCursor ?? start;
                _logger.LogWarning(refused,
                    "Jetstream refused the cutover at {Tip}; re-planning from {Cursor}", cutover, start);
            }
        }
        finally
        {
            await tracker.FlushAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Records an event as the last delivered. Returns false for one delivered already, which the
    /// inclusive cutover cursor and an overlapping re-plan can both produce.
    /// </summary>
    private bool IsNew(JetstreamEvent evt)
    {
        if (evt.Cursor is not { } seq)
            return true;

        if (LastCursor is { } last && seq <= last)
            return false;

        LastCursor = seq;
        return true;
    }

    /// <summary>
    /// Read everything sealed above <paramref name="afterSeq"/>, paging the plan until it reaches
    /// the tip pinned by the first page.
    /// </summary>
    private async IAsyncEnumerable<JetstreamEvent> BackfillAsync(
        long afterSeq,
        Action<long> pinTip,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long? tip = null;
        var planned = afterSeq;
        var stalls = 0;
        var planKinds = _options.WantedKinds is { Count: > 0 } wanted
            ? wanted.Select(JetstreamKinds.Name).ToList()
            : null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var plan = await _client.PlanSnapshotAsync(
                new JetstreamSnapshotRequest
                {
                    Kinds = planKinds,
                    Dids = _options.WantedDids,
                    Collections = _options.WantedCollections,
                    AfterSeq = planned,
                    // Pin the first page's tip as the ceiling for every later page, so the range
                    // cannot float upward while the backfill is downloading it.
                    BeforeSeq = tip ?? _archive.BeforeSeq,
                },
                cancellationToken).ConfigureAwait(false);

            // A snapshot's BeforeSeq caps the ceiling: without it the pinned tip would replace the
            // caller's bound on the second page and the snapshot would run to the sealed tip.
            tip ??= _archive.BeforeSeq is { } before
                ? Math.Min(before, plan.SealedTipSeq)
                : plan.SealedTipSeq;
            pinTip(tip.Value);
            var ceiling = tip.Value;

            // The server truncates a page at a whole work-unit boundary and always admits at least
            // one, so plannedThroughSeq advances every page. A page that does not advance while the
            // ceiling is still ahead means the sealed tip was reported before its segments became
            // servable — wait it out. Truncating here would be a silent, permanent gap: the cutover
            // reconnects at the pinned ceiling and never redelivers the range that was skipped.
            if (plan.PlannedThroughSeq <= planned && planned < ceiling)
            {
                if (++stalls > _archive.MaxStalledPlanAttempts)
                    throw new JetstreamException(
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

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            stalls = 0;

            _logger.LogInformation(
                "Planned {Segments} segment(s) through sequence {Through} of {Tip}",
                plan.Segments.Count, plan.PlannedThroughSeq, ceiling);

            // The planner has no false negatives but does return blocks with no matching rows, so
            // the exact filter is applied to each row, before it is decoded.
            var filter = new JetstreamArchiveRowFilter(_options, LastCursor ?? afterSeq, ceiling);
            await foreach (var evt in DownloadAsync(plan.Segments, filter, cancellationToken).ConfigureAwait(false))
                yield return evt;

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
    /// Download and decode the planned work units — whole segments or single blocks — with
    /// <see cref="JetstreamArchiveOptions.DownloadParallelism"/> in flight, and deliver their
    /// events strictly in plan order so they stay sequence-ordered.
    /// </summary>
    private async IAsyncEnumerable<JetstreamEvent> DownloadAsync(
        IReadOnlyList<JetstreamPlannedSegment> segments,
        JetstreamArchiveRowFilter filter,
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
                    pending = DownloadUnitAsync(unit, filter, abort.Token);
                    await channel.Writer.WriteAsync(pending, abort.Token).ConfigureAwait(false);
                    pending = null;
                }

                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
                if (pending is not null)
                    await Observe(pending).ConfigureAwait(false);
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var download in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var unit = await download.ConfigureAwait(false);
                try
                {
                    await foreach (var evt in unit.ReadAsync(_archive.BlockDecompressor, filter, parallelism, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        yield return evt;
                    }
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
            await abort.CancelAsync().ConfigureAwait(false);
            channel.Writer.TryComplete();
            await Drain(channel.Reader).ConfigureAwait(false);
            await producer.ConfigureAwait(false);
        }

        static async Task Drain(ChannelReader<Task<DownloadedUnit>> reader)
        {
            while (reader.TryRead(out var pending))
                await Observe(pending).ConfigureAwait(false);
        }

        static async Task Observe(Task<DownloadedUnit> pending)
        {
            try
            {
                (await pending.ConfigureAwait(false)).Dispose();
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

    private async Task<DownloadedUnit> DownloadUnitAsync(
        WorkUnit unit, JetstreamArchiveRowFilter filter, CancellationToken cancellationToken)
    {
        if (unit.BlockIndex is { } blockIndex)
        {
            // A block is small, so it is decoded here, in parallel with the other downloads.
            var frame = await _client.GetBlockAsync(unit.Segment.Name, blockIndex, cancellationToken).ConfigureAwait(false);
            return new DownloadedUnit(JetstreamSegmentReader.DecodeEvents(frame, _archive.BlockDecompressor, filter), null);
        }

        // Segments run to hundreds of megabytes, so they are spooled to disk rather than buffered,
        // and decoded block by block as they are read back.
        var path = Path.Combine(
            _archive.SpoolDirectory ?? Path.GetTempPath(),
            $"jetstream-{Guid.NewGuid():n}.jss");

        var file = new FileStream(
            path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        try
        {
            await _client.DownloadSegmentAsync(unit.Segment.Name, file, cancellationToken).ConfigureAwait(false);
            file.Position = 0;
            return new DownloadedUnit(null, file);
        }
        catch
        {
            await file.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static JetstreamArchiveOptions Validate(JetstreamConsumerOptions options)
    {
        options.Validate();

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

    /// <summary>A downloaded work unit: a decoded block's events, or a spooled segment file.</summary>
    private sealed class DownloadedUnit(List<JetstreamEvent>? events, FileStream? file) : IDisposable
    {
        public async IAsyncEnumerable<JetstreamEvent> ReadAsync(
            IJetstreamBlockDecompressor decompressor,
            JetstreamArchiveRowFilter filter,
            int parallelism,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (events is not null)
            {
                foreach (var evt in events)
                    yield return evt;
                yield break;
            }

            // Decode up to `parallelism` blocks ahead of the one being delivered, in order.
            var pending = new Queue<Task<List<JetstreamEvent>>>();
            try
            {
                await foreach (var frame in JetstreamSegmentReader.ReadBlockFramesAsync(file!, cancellationToken)
                    .ConfigureAwait(false))
                {
                    pending.Enqueue(Task.Run(() => JetstreamSegmentReader.DecodeEvents(frame, decompressor, filter), cancellationToken));
                    if (pending.Count < parallelism)
                        continue;

                    foreach (var evt in await pending.Dequeue().ConfigureAwait(false))
                        yield return evt;
                }

                while (pending.Count > 0)
                {
                    foreach (var evt in await pending.Dequeue().ConfigureAwait(false))
                        yield return evt;
                }
            }
            finally
            {
                // Observe decodes an early exit abandoned; their results are not needed.
                while (pending.TryDequeue(out var abandoned))
                {
                    try
                    {
                        await abandoned.ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Already exiting: this failure is not the one being reported.
                    }
                }
            }
        }

        public void Dispose() => file?.Dispose();
    }
}
