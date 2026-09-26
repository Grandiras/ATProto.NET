using ATProtoNet.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Lexicon.Com.AtProto.Lexicon;

/// <summary>
/// Caches the schemas another <see cref="ILexiconResolver"/> resolves.
/// </summary>
/// <remarks>
/// <para>The same model as <see cref="CachingDidResolver"/>: a schema younger than
/// <see cref="LexiconCacheOptions.StaleAfter"/> is served as is; one younger than
/// <see cref="LexiconCacheOptions.ExpireAfter"/> is served while a background resolution
/// replaces it, and keeps being served if that fails; an older one is resolved again before use.
/// A failed resolution is remembered for <see cref="LexiconCacheOptions.FailureTtl"/>. Concurrent
/// requests for an NSID that is not cached share one resolution, which runs to completion even if
/// every caller that asked for it gives up.</para>
/// <para>The defaults follow the permission specification: an authorization server re-resolves a
/// set every few minutes (the reference refreshes after five), and 24 hours is the firm upper
/// bound on serving a stale one. The Lexicon specification asks the same of DNS answers, which
/// can move a namespace to another repository without any event on the firehose.</para>
/// </remarks>
public sealed class CachingLexiconResolver : ILexiconResolver, IDisposable
{
    private readonly ILexiconResolver _inner;
    private readonly bool _ownsInner;
    private readonly LexiconCacheOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    private readonly Lock _lock = new();
    private readonly Dictionary<Nsid, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recency = new(); // most recently used first
    private readonly Dictionary<Nsid, Fetch> _inflight = new();

    /// <summary>
    /// Creates a cache over <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">The resolver schemas are resolved through. The caller owns it.</param>
    /// <param name="options">Cache options. Defaults apply when omitted.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    /// <param name="logger">Optional logger.</param>
    public CachingLexiconResolver(
        ILexiconResolver inner,
        LexiconCacheOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
        : this(inner, options, timeProvider, logger, ownsInner: false)
    {
    }

    /// <summary>Creates a cache, owning <paramref name="inner"/> when <paramref name="ownsInner"/> is set.</summary>
    internal CachingLexiconResolver(
        ILexiconResolver inner,
        LexiconCacheOptions? options,
        TimeProvider? timeProvider,
        ILogger? logger,
        bool ownsInner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _ownsInner = ownsInner;
        _options = options ?? new LexiconCacheOptions();
        _options.Validate();
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The number of schemas and remembered failures held.</summary>
    internal int Count
    {
        get
        {
            lock (_lock)
                return _entries.Count;
        }
    }

    /// <summary>Whether a resolution of <paramref name="nsid"/> is under way.</summary>
    internal bool IsResolving(Nsid nsid)
    {
        lock (_lock)
            return _inflight.ContainsKey(nsid);
    }

    /// <inheritdoc/>
    public Task<ResolvedLexicon> ResolveAsync(Nsid nsid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nsid);

        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (_entries.TryGetValue(nsid, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);

                var entry = node.Value;
                var age = now - entry.FetchedAt;
                if (entry.Error is { } error)
                {
                    if (age < _options.FailureTtl)
                        return Task.FromException<ResolvedLexicon>(Rethrow(error));
                }
                else if (age < _options.ExpireAfter)
                {
                    if (age >= _options.StaleAfter && now >= entry.RetryAfter)
                        _ = StartFetchLocked(nsid);
                    return Task.FromResult(entry.Value!);
                }
            }

            return StartFetchLocked(nsid).WaitAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A resolution already under way for the NSID is detached: its callers still get its result,
    /// but it is not stored.
    /// </remarks>
    public Task InvalidateAsync(Nsid nsid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nsid);

        lock (_lock)
        {
            if (_entries.Remove(nsid, out var node))
                _recency.Remove(node);

            if (_inflight.Remove(nsid, out var fetch))
                fetch.Detached = true;
        }

        return _inner.InvalidateAsync(nsid, cancellationToken);
    }

    /// <summary>Returns the resolution under way for an NSID, or starts one. Call under the lock.</summary>
    private Task<ResolvedLexicon> StartFetchLocked(Nsid nsid)
    {
        if (_inflight.TryGetValue(nsid, out var existing))
            return existing.Completion.Task;

        var fetch = new Fetch();
        _inflight.Add(nsid, fetch);

        // Every caller may have given up (or it is a background refresh nobody awaits), so the
        // outcome is observed here once, and a failure never surfaces as an unobserved exception.
        _ = fetch.Completion.Task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        _ = Task.Run(() => RunFetchAsync(nsid, fetch));
        return fetch.Completion.Task;
    }

    private async Task RunFetchAsync(Nsid nsid, Fetch fetch)
    {
        ResolvedLexicon resolved;
        try
        {
            // Not a caller's token: the resolution is shared. The inner resolver bounds it.
            resolved = await _inner.ResolveAsync(nsid, CancellationToken.None).ConfigureAwait(false);
        }
        catch (LexiconResolutionException ex)
        {
            var now = _time.GetUtcNow();
            lock (_lock)
            {
                if (Complete(nsid, fetch))
                {
                    // A schema still inside its lifetime outlives a failed refresh, as the
                    // permission specification asks; the next attempt waits out FailureTtl.
                    if (_entries.TryGetValue(nsid, out var node) && node.Value.Error is null &&
                        now - node.Value.FetchedAt < _options.ExpireAfter)
                    {
                        _logger.LogInformation(ex, "Refreshing Lexicon {Nsid} failed; serving the cached schema.", nsid);
                        node.Value = node.Value with { RetryAfter = now + _options.FailureTtl };
                    }
                    else
                    {
                        Store(new Entry(nsid, null, ex, now, default));
                    }
                }
            }

            fetch.Completion.SetException(ex);
            return;
        }
        catch (Exception ex)
        {
            // Not a resolution outcome (a bug, or a cancellation in the inner resolver): nothing
            // is remembered, and the next request tries again.
            lock (_lock)
                Complete(nsid, fetch);

            fetch.Completion.SetException(ex);
            return;
        }

        lock (_lock)
        {
            if (Complete(nsid, fetch))
                Store(new Entry(nsid, resolved, null, _time.GetUtcNow(), default));
        }

        fetch.Completion.SetResult(resolved);
    }

    /// <summary>Retires a fetch. Returns whether its outcome may be stored. Call under the lock.</summary>
    private bool Complete(Nsid nsid, Fetch fetch)
    {
        if (_inflight.TryGetValue(nsid, out var current) && ReferenceEquals(current, fetch))
            _inflight.Remove(nsid);

        return !fetch.Detached;
    }

    /// <summary>Stores an entry as the most recently used, evicting the least. Call under the lock.</summary>
    private void Store(Entry entry)
    {
        if (_entries.Remove(entry.Nsid, out var old))
            _recency.Remove(old);

        _entries.Add(entry.Nsid, _recency.AddFirst(entry));
        if (_entries.Count > _options.Capacity)
        {
            var last = _recency.Last!;
            _recency.RemoveLast();
            _entries.Remove(last.Value.Nsid);
        }
    }

    /// <summary>
    /// A fresh exception for a remembered failure: one instance thrown on several threads at once
    /// would have its stack trace rewritten under each of them.
    /// </summary>
    private static LexiconResolutionException Rethrow(LexiconResolutionException cached) =>
        new(cached.Message, cached.Nsid, cached.Kind, cached);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsInner && _inner is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>A cached schema, or a remembered failure.</summary>
    private sealed record Entry(
        Nsid Nsid,
        ResolvedLexicon? Value,
        LexiconResolutionException? Error,
        DateTimeOffset FetchedAt,
        DateTimeOffset RetryAfter);

    /// <summary>One resolution in flight, and whether an invalidation detached it.</summary>
    private sealed class Fetch
    {
        public TaskCompletionSource<ResolvedLexicon> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Detached { get; set; }
    }
}

/// <summary>
/// Configuration for a <see cref="CachingLexiconResolver"/>.
/// </summary>
public sealed class LexiconCacheOptions
{
    /// <summary>The most schemas and failures held. The least recently used goes first. Defaults to 1,000.</summary>
    public int Capacity { get; set; } = 1_000;

    /// <summary>
    /// How long a schema is served without being re-resolved. After that it is still served, and
    /// re-resolved in the background. Defaults to five minutes.
    /// </summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a schema may be served at all, including while re-resolution keeps failing. After
    /// that it is resolved again before use. Defaults to 24 hours, the permission specification's
    /// upper bound.
    /// </summary>
    public TimeSpan ExpireAfter { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a failed resolution is remembered, and how long a failed background refresh waits
    /// before the next. Defaults to one minute.
    /// </summary>
    public TimeSpan FailureTtl { get; set; } = TimeSpan.FromMinutes(1);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Capacity, 1, nameof(Capacity));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(StaleAfter, TimeSpan.Zero, nameof(StaleAfter));
        ArgumentOutOfRangeException.ThrowIfLessThan(ExpireAfter, StaleAfter, nameof(ExpireAfter));
        ArgumentOutOfRangeException.ThrowIfLessThan(FailureTtl, TimeSpan.Zero, nameof(FailureTtl));
    }
}
