using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Streaming;

/// <summary>
/// How a <see cref="TypedFirehoseConsumer"/> fetches desynchronized repositories again. Set it
/// on <see cref="TypedFirehoseConsumerOptions.Resync"/>, together with a
/// <see cref="TypedFirehoseConsumerOptions.SyncVerifier"/>.
/// </summary>
/// <remarks>
/// <para>When a repository's chain breaks, the consumer fetches its full export in the
/// background, from <see cref="Fetchers"/> in turn, verifies it, and delivers it as a
/// <see cref="RepoResyncEvent"/>. The repository's events that arrive meanwhile are verified and
/// held; once the snapshot is delivered they follow it in order, so nothing between the snapshot
/// and the live stream is lost. The cursor is not saved past a held event until it is delivered,
/// so a process that stops in between replays it. Repositories the state store lists as not
/// synchronized, left over from an earlier run or marked for fetching, are picked up every
/// minute.</para>
/// <para>Everything is bounded. Four repositories are fetched or held fetched at once, each export
/// at most 256 MiB, so fetched exports take at most about 1.25 GiB of memory, the one being
/// delivered included; 1,000 more wait for a turn, and the events held meanwhile are bounded at
/// 1,000 per repository and 64 MiB in all. A repository over a limit is fetched again later rather
/// than growing memory, one whose fetch failed waits a minute before the next attempt, and each
/// download may take five minutes.</para>
/// </remarks>
public sealed class RepoResyncOptions
{
    /// <summary>
    /// Where to fetch repositories from, in order: the first that returns a verified export at
    /// least as new as the event that broke the chain wins. Default: the relay (the consumer's
    /// <see cref="StreamConsumerOptions.ServiceUrl"/> over <c>https</c>), then the account's PDS, as
    /// the sync spec recommends, both over public addresses only.
    /// </summary>
    public IReadOnlyList<IRepoFetcher>? Fetchers { get; init; }

    // The limits the remarks describe; tests shorten them.
    internal int MaxConcurrency { get; init; } = 4;

    internal int MaxPendingRepos { get; init; } = 1_000;

    internal long MaxRepoBytes { get; init; } = 256L * 1024 * 1024;

    internal TimeSpan FetchTimeout { get; init; } = RepoDownload.DefaultTimeout;

    internal int MaxHeldEventsPerRepo { get; init; } = 1_000;

    internal long MaxHeldBytes { get; init; } = 64L * 1024 * 1024;

    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(1);

    internal TimeSpan ScanInterval { get; init; } = TimeSpan.FromMinutes(1);

    internal void Validate()
    {
        if (Fetchers is { Count: 0 })
            throw new ArgumentException("At least one fetcher is needed.", nameof(Fetchers));
    }
}

/// <summary>
/// A repository fetched again because its chain of commits broke: its full, verified contents at
/// one revision. <see cref="TypedFirehoseConsumer"/> delivers it when
/// <see cref="TypedFirehoseConsumerOptions.Resync"/> is set.
/// </summary>
/// <remarks>
/// <para>Reconcile what you hold for <see cref="Did"/> with <see cref="Snapshot"/>: records you lack
/// were created, records whose CID differs were updated, and records you hold that the snapshot
/// lacks were deleted. The repository's commits after the snapshot follow it in the stream.</para>
/// <para>The repository counts as synchronized once you ask for the next message, as with every
/// other message; a process that stops before then fetches it again.</para>
/// <para>This is not a message of the <c>subscribeRepos</c> wire protocol, and it is not sequenced:
/// it does not move the cursor.</para>
/// </remarks>
public sealed class RepoResyncEvent : FirehoseMessage
{
    /// <summary>The repository.</summary>
    public required Did Did { get; init; }

    /// <summary>The repository's verified contents.</summary>
    public required RepoSnapshot Snapshot { get; init; }

    /// <summary>Why the repository needed fetching, when it is known.</summary>
    public string? Reason { get; init; }
}

// Runs the resynchronizations of one TypedFirehoseConsumer.ConsumeAsync run.
//
// Every state change happens on the consumer's own loop, one event at a time: the background work only
// downloads and verifies, and posts what it found. That keeps a repository's events, its snapshot and
// the events held during the fetch in one order, with no locking around the state store.
internal sealed class RepoResyncCoordinator : IAsyncDisposable
{
    private readonly RepoSyncVerifier _verifier;
    private readonly RepoResyncOptions _options;
    private readonly IReadOnlyList<IRepoFetcher> _fetchers;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _concurrency;
    private readonly CancellationTokenSource _stopping = new();

    // Touched only on the consumer's loop.
    private readonly Dictionary<Did, Job> _jobs = [];
    private readonly Dictionary<Did, DateTimeOffset> _backoff = [];
    private readonly Queue<Pending> _ready = new();
    private readonly List<Task> _workers = [];

    // The sequence numbers of events held and not yet delivered or dropped, counted, so the cursor is
    // never saved past the oldest: a process that stops before delivering them must see them again.
    private readonly SortedDictionary<long, int> _undelivered = [];
    private readonly Action<long?> _persistLimit;
    private long _heldBytes;
    private DateTimeOffset _nextScan;

    // Posted by the background fetches.
    private readonly System.Collections.Concurrent.ConcurrentQueue<Completion> _completions = new();

    // verifier: The consumer's sync verifier.
    //
    // options: The resync options.
    //
    // serviceUrl: The firehose's URL, which the default upstream is derived from.
    //
    // persistLimit: Told the highest cursor position that may be saved while events are held, or null when
    // none are.
    //
    // logger: The consumer's logger.
    //
    // timeProvider: The clock retries and scans are timed by.
    public RepoResyncCoordinator(
        RepoSyncVerifier verifier, RepoResyncOptions options, string serviceUrl, Action<long?> persistLimit,
        ILogger logger, TimeProvider? timeProvider = null)
    {
        _verifier = verifier;
        _persistLimit = persistLimit;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _concurrency = new SemaphoreSlim(options.MaxConcurrency);
        _nextScan = DateTimeOffset.MinValue;

        if (options.Fetchers is { } fetchers)
        {
            _fetchers = fetchers;
        }
        else
        {
            var defaults = new List<IRepoFetcher>(2);
            if (UpstreamOf(serviceUrl) is { } upstream)
                defaults.Add(new HostRepoFetcher(upstream));
            defaults.Add(new PdsRepoFetcher(verifier.DidResolver));
            _fetchers = defaults;
        }
    }

    // What the consumer's loop has to deliver or process next.
    internal abstract record Pending;

    // A finished resync, to deliver; State is recorded once it is delivered.
    internal sealed record Snapshot(RepoResyncEvent Message, RepoSyncState State) : Pending;

    // An event held during a resync, to verify again now that the snapshot is in.
    internal sealed record Held(FirehoseEvent Event) : Pending;

    // An event held for a fetch that failed, to report as dropped.
    internal sealed record Abandoned(FirehoseEvent Event, string Reason) : Pending;

    // A repository whose held events overflowed: fetch it again, at least at MinRev.
    internal sealed record Refetch(Did Did, Tid? MinRev, string Reason) : Pending;

    // The http(s) form of the firehose's WebSocket URL: the relay to ask first.
    internal static Uri? UpstreamOf(string serviceUrl) =>
        Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri) && AtProtoHttp.WithScheme(uri, webSocket: false) is { Scheme: "https" or "http" } http
            ? new Uri(http.GetLeftPart(UriPartial.Authority) + "/")
            : null;

    // Takes an event whose repository is desynchronized: starts fetching the repository if nothing is, and
    // holds the event to replay after the snapshot. Returns whether the event was held.
    public async ValueTask<bool> HoldAsync(FirehoseEvent evt, RepoSyncResult result, CancellationToken cancellationToken)
    {
        var job = await EnsureJobAsync(result.Did, result.Rev ?? result.State?.Rev, result.Reason, cancellationToken).ConfigureAwait(false);
        if (job is null)
            return false;

        if (result.Rev is { } rev && (job.LatestRev is null || rev.CompareTo(job.LatestRev) > 0))
            job.LatestRev = rev;
        if (job.Overflowed)
            return false;

        var size = evt switch
        {
            CommitEvent commit => commit.Blocks?.LongLength ?? 0,
            SyncEvent sync => sync.Blocks?.LongLength ?? 0,
            _ => 0,
        };

        if (job.Events.Count >= _options.MaxHeldEventsPerRepo || _heldBytes + size > _options.MaxHeldBytes)
        {
            // Rather than grow without bound, drop what is held; the repository is fetched again
            // once this snapshot is in.
            _logger.LogWarning("Too many events for {Did} arrived while it was being fetched; it will be fetched again", job.Did);
            // The snapshot is then recorded as desynchronized, so a process that stops before the
            // refetch still fetches the repository again.
            job.Overflowed = true;
            _heldBytes -= job.HeldBytes;
            job.HeldBytes = 0;
            foreach (var dropped in job.Events)
                Release(dropped.Seq);
            job.Events.Clear();
            return false;
        }

        job.Events.Add(evt);
        job.HeldBytes += size;
        _heldBytes += size;
        _undelivered[evt.Seq] = _undelivered.GetValueOrDefault(evt.Seq) + 1;
        PublishPersistLimit();
        return true;
    }

    // A held event was delivered, dropped, or held again: it no longer stops the cursor from being saved
    // past it (unless held again, which counts it anew).
    public void Release(long seq)
    {
        if (!_undelivered.TryGetValue(seq, out var count))
            return;

        if (count > 1)
            _undelivered[seq] = count - 1;
        else
            _undelivered.Remove(seq);
        PublishPersistLimit();
    }

    private void PublishPersistLimit()
    {
        long? limit = null;
        foreach (var (seq, _) in _undelivered)
        {
            limit = seq - 1;
            break;
        }

        _persistLimit(limit);
    }

    // The next thing the consumer's loop has to deliver or process: a finished resync, an event held
    // during one, or null when there is nothing. Scans the state store when a scan is due.
    public async ValueTask<Pending?> NextAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_ready.TryDequeue(out var ready))
                return ready;

            if (!_completions.TryDequeue(out var completion))
                break;

            await CompleteAsync(completion, cancellationToken).ConfigureAwait(false);
        }

        var now = _timeProvider.GetUtcNow();
        if (now >= _nextScan)
        {
            _nextScan = now + _options.ScanInterval;
            await ScanAsync(cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    // Starts fetching a repository again after its held events overflowed.
    public async ValueTask RefetchAsync(Refetch refetch, CancellationToken cancellationToken)
    {
        await _verifier.SetStatusAsync(refetch.Did, RepoSyncStatus.Desynchronized, cancellationToken).ConfigureAwait(false);
        await EnsureJobAsync(refetch.Did, refetch.MinRev, refetch.Reason, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Job?> EnsureJobAsync(Did did, Tid? minRev, string? reason, CancellationToken cancellationToken)
    {
        if (_jobs.TryGetValue(did, out var existing))
            return existing;

        var now = _timeProvider.GetUtcNow();
        if (_backoff.TryGetValue(did, out var until))
        {
            if (now < until)
                return null;
            _backoff.Remove(did);
        }

        if (_jobs.Count >= _options.MaxPendingRepos)
            return null;

        var job = new Job(did, minRev, reason);
        _jobs[did] = job;
        await _verifier.SetStatusAsync(did, RepoSyncStatus.Resynchronizing, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Resynchronizing {Did}: {Reason}", did, reason ?? "marked for fetching");
        _workers.RemoveAll(static task => task.IsCompleted);
        _workers.Add(Task.Run(() => FetchAsync(job)));
        return job;
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        var room = _options.MaxPendingRepos - _jobs.Count;
        if (room <= 0)
            return;

        // Ask for more than there is room for: repositories already being fetched or waiting out
        // a failure are listed too.
        var states = await _verifier.StateStore
            .ListUnsynchronizedAsync(Math.Min(room + _jobs.Count + _backoff.Count, int.MaxValue / 2), cancellationToken)
            .ConfigureAwait(false);

        foreach (var state in states)
        {
            if (_jobs.Count >= _options.MaxPendingRepos)
                break;
            if (!_jobs.ContainsKey(state.Did))
                await EnsureJobAsync(state.Did, state.Rev, reason: null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask CompleteAsync(Completion completion, CancellationToken cancellationToken)
    {
        // Taken off the queue: the next fetch may start.
        _concurrency.Release();

        var job = completion.Job;
        _jobs.Remove(job.Did);
        _heldBytes -= job.HeldBytes;

        if (completion.Snapshot is not { } snapshot)
        {
            _logger.LogWarning(completion.Error, "Could not resynchronize {Did}; retrying in {Delay}", job.Did, _options.RetryDelay);

            // Bounded: a map of failures as large as the queue is plenty to space out retries.
            if (_backoff.Count < _options.MaxPendingRepos * 4)
                _backoff[job.Did] = _timeProvider.GetUtcNow() + _options.RetryDelay;

            await _verifier.SetStatusAsync(job.Did, RepoSyncStatus.Desynchronized, cancellationToken).ConfigureAwait(false);

            // The held events are dropped with the failed fetch; the next one covers them.
            foreach (var held in job.Events)
                _ready.Enqueue(new Abandoned(held, $"Fetching the repository failed: {completion.Error?.Message}"));
            return;
        }

        _logger.LogInformation("Resynchronized {Did} at revision {Rev} ({Count} records)", job.Did, snapshot.Rev, snapshot.Count);
        _ready.Enqueue(new Snapshot(
            new RepoResyncEvent { Did = job.Did, Snapshot = snapshot, Reason = job.Reason },
            new RepoSyncState(job.Did, snapshot.Rev, snapshot.Data,
                job.Overflowed ? RepoSyncStatus.Desynchronized : RepoSyncStatus.Synchronized)));

        foreach (var held in job.Events)
            _ready.Enqueue(new Held(held));

        if (job.Overflowed)
            _ready.Enqueue(new Refetch(job.Did, job.LatestRev, "Events overflowed while the repository was being fetched."));
    }

    // Downloads and verifies one repository, off the consumer's loop, and posts the outcome.
    private async Task FetchAsync(Job job)
    {
        var token = _stopping.Token;
        try
        {
            await _concurrency.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Completion completion;
        try
        {
            completion = new Completion(job, await FetchSnapshotAsync(job, token).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The run is ending; the repository stays marked and is fetched on a later run.
            _concurrency.Release();
            return;
        }
        catch (Exception ex)
        {
            completion = new Completion(job, null, ex);
        }

        // The slot stays taken until the consumer's loop takes the completion: a fetched snapshot
        // waiting there holds a whole repository in memory, as much as a download in flight.
        _completions.Enqueue(completion);
    }

    private async Task<RepoSnapshot> FetchSnapshotAsync(Job job, CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var fetcher in _fetchers)
        {
            byte[]? car;
            try
            {
                // Each attempt gets FetchTimeout, whichever fetcher makes it.
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(_options.FetchTimeout);
                car = await fetcher.FetchAsync(job.Did, _options.MaxRepoBytes, attempt.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add($"{fetcher}: took longer than {_options.FetchTimeout}");
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"{fetcher}: {ex.Message}");
                continue;
            }

            if (car is null)
            {
                failures.Add($"{fetcher}: does not hold the repository");
                continue;
            }

            RepoSnapshot snapshot;
            try
            {
                snapshot = RepoSnapshot.Read(car, job.Did);
            }
            catch (RepoVerificationException ex)
            {
                failures.Add($"{fetcher}: {ex.Message}");
                continue;
            }

            if (await _verifier.VerifySignatureAsync(job.Did, snapshot.CommitBlock, cancellationToken).ConfigureAwait(false) is { } signatureError)
            {
                failures.Add($"{fetcher}: {signatureError}");
                continue;
            }

            // A cached export older than the event that broke the chain would only break it again.
            if (job.MinRev is { } minRev && snapshot.Rev.CompareTo(minRev) < 0)
            {
                failures.Add($"{fetcher}: served revision {snapshot.Rev}, older than {minRev}");
                continue;
            }

            return snapshot;
        }

        throw new RepoFetchException($"No source served a verified copy of {job.Did}: {string.Join("; ", failures)}");
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Each fetch reports its own failure; nothing is left to report on the way out.
        }

        _stopping.Dispose();
        _concurrency.Dispose();
    }

    private sealed class Job(Did did, Tid? minRev, string? reason)
    {
        public Did Did { get; } = did;

        // The revision the snapshot must reach: that of the event that broke the chain.
        public Tid? MinRev { get; } = minRev;

        public string? Reason { get; } = reason;

        public List<FirehoseEvent> Events { get; } = [];

        public long HeldBytes { get; set; }

        public bool Overflowed { get; set; }

        // The newest revision of the events that arrived during the fetch, held or not.
        public Tid? LatestRev { get; set; }
    }

    private sealed record Completion(Job Job, RepoSnapshot? Snapshot, Exception? Error);
}
