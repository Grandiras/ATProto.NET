using Microsoft.Extensions.Caching.Distributed;

namespace ATProtoNet.Caching;

// The cache behind Identity.CachingDidResolver and Lexicon.CachingLexiconResolver: what another resolver
// resolves, served stale while it is refreshed, with failures remembered and one fetch per key.
//
// A value younger than staleAfter is served as is; one younger than expireAfter is served while a
// background fetch replaces it, and keeps being served if that fetch fails, the next attempt waiting out
// failureTtl; an older one is fetched before use. A failure (TError) is remembered for failureTtl; any
// other exception is not remembered. Concurrent requests for a key that is not cached share one fetch,
// which runs to completion even if every caller that asked for it gives up.
//
// Failures are held apart from values when given their own capacity, so a stream of keys that do not
// resolve evicts other failures, never the values of keys that do.
//
// An optional SharedTier shares values between instances: it is consulted when memory misses and written
// after every fetch, failures are remembered in memory only, and its errors are reported and treated as
// misses.
//
// What is logged, and under which message and property names, is each resolver's: dashboards and alerts
// key on them.
internal sealed class ResolutionCache<TKey, TValue, TError>
    where TKey : notnull
    where TValue : class
    where TError : Exception
{
    private readonly Func<TKey, Task<TValue>> _fetch;
    private readonly Func<TError, TError> _rethrow;
    private readonly TimeSpan _staleAfter;
    private readonly TimeSpan _expireAfter;
    private readonly TimeSpan _failureTtl;
    private readonly TimeProvider _time;
    private readonly Action<TKey, TError>? _refreshFailed;
    private readonly SharedTier? _shared;

    private readonly Lock _lock = new();
    private readonly LruCache<TKey, Entry> _values;
    private readonly LruCache<TKey, Entry> _failures;
    private readonly Dictionary<TKey, Fetch> _inflight = new();
    private readonly Dictionary<TKey, PendingWrite> _pendingWrites = new();

    // fetch: Resolves a key. It is not given a caller's token: a fetch is shared, and one caller giving
    // up must not fail it for the others, so it must bound itself.
    //
    // rethrow: A fresh exception for a remembered failure: one instance thrown on several threads at
    // once would have its stack trace rewritten under each of them.
    //
    // failureCapacity: The most failures held apart from values, or null to hold them among the values.
    //
    // refreshFailed: Told when a refresh fails and the cached value keeps being served. Called under the
    // cache's lock.
    public ResolutionCache(
        Func<TKey, Task<TValue>> fetch,
        Func<TError, TError> rethrow,
        int capacity,
        int? failureCapacity,
        TimeSpan staleAfter,
        TimeSpan expireAfter,
        TimeSpan failureTtl,
        TimeProvider time,
        Action<TKey, TError>? refreshFailed = null,
        SharedTier? shared = null)
    {
        _fetch = fetch;
        _rethrow = rethrow;
        _staleAfter = staleAfter;
        _expireAfter = expireAfter;
        _failureTtl = failureTtl;
        _time = time;
        _refreshFailed = refreshFailed;
        _shared = shared;
        _values = new LruCache<TKey, Entry>(capacity);
        _failures = failureCapacity is { } separate ? new LruCache<TKey, Entry>(separate) : _values;
    }

    // The values and remembered failures held in memory.
    public int Count
    {
        get
        {
            lock (_lock)
                return ReferenceEquals(_values, _failures) ? _values.Count : _values.Count + _failures.Count;
        }
    }

    // The values held in memory, when failures are held apart.
    public int ValueCount => _values.Count;

    public bool IsFetching(TKey key)
    {
        lock (_lock)
            return _inflight.ContainsKey(key);
    }

    // A value from memory, the shared tier or a fetch, or a remembered failure thrown.
    public async Task<TValue> GetAsync(TKey key, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        if (Touch(key) is { } entry && TryServe(key, entry, now, out var value))
            return value;

        if (_shared is not null &&
            await ReadSharedAsync(key, cancellationToken).ConfigureAwait(false) is { } shared &&
            now - shared.FetchedAt < _expireAfter)
        {
            lock (_lock)
            {
                _failures.Remove(key);
                _values.Set(key, new Entry(shared.Value, null, shared.FetchedAt, default, shared.FetchedAt));
            }

            if (now - shared.FetchedAt >= _staleAfter)
                RefreshInBackground(key);
            return shared.Value;
        }

        return await StartFetch(key).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Fetches a key afresh, unless a fetch for it completed less than minInterval ago: the value, or
    // failure, it left is returned instead. A fetch under way is joined.
    public Task<TValue> RefreshAsync(TKey key, TimeSpan minInterval, CancellationToken cancellationToken)
    {
        if (Touch(key) is { } entry && _time.GetUtcNow() - entry.LastAttemptAt < minInterval)
        {
            return entry.Value is { } value
                ? Task.FromResult(value)
                : Task.FromException<TValue>(_rethrow(entry.Error!));
        }

        return StartFetch(key).WaitAsync(cancellationToken);
    }

    // Drops what is held for a key. A fetch under way is detached: its callers still get its result, but
    // it is not stored. A shared-tier write in flight is removed again once it lands.
    public async Task InvalidateAsync(TKey key, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _values.Remove(key);
            _failures.Remove(key);

            if (_inflight.Remove(key, out var fetch))
                fetch.Detached = true;

            if (_pendingWrites.TryGetValue(key, out var write))
                write.Invalidated = true;
        }

        if (_shared is not null)
            await RemoveSharedAsync(key, cancellationToken).ConfigureAwait(false);
    }

    // Decides whether a cached entry answers the request: a fresh or stale value does (a stale one also
    // starts a background refresh), a remembered failure throws, and anything expired does not.
    private bool TryServe(TKey key, Entry entry, DateTimeOffset now, out TValue value)
    {
        value = null!;
        var age = now - entry.FetchedAt;

        if (entry.Error is { } error)
        {
            if (age < _failureTtl)
                throw _rethrow(error);
            return false;
        }

        if (age >= _expireAfter)
            return false;

        if (age >= _staleAfter && now >= entry.RetryAfter)
            RefreshInBackground(key);

        value = entry.Value!;
        return true;
    }

    // Returns the fetch under way for a key, or starts one. Every caller of one fetch observes the same
    // outcome.
    private Task<TValue> StartFetch(TKey key)
    {
        Fetch fetch;
        lock (_lock)
        {
            if (_inflight.TryGetValue(key, out var existing))
                return existing.Completion.Task;

            fetch = new Fetch();
            _inflight.Add(key, fetch);
        }

        // Every caller may have given up (or it is a background refresh nobody awaits), so the
        // outcome is observed here once, and a failure never surfaces as an unobserved exception.
        _ = fetch.Completion.Task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // On the thread pool, not the first caller's context: a fetch that awaits without
        // ConfigureAwait(false) would otherwise resume there, and stall for every caller that joins
        // it if that context stops running.
        _ = Task.Run(() => RunFetchAsync(key, fetch));
        return fetch.Completion.Task;
    }

    private async Task RunFetchAsync(TKey key, Fetch fetch)
    {
        TValue value;
        try
        {
            value = await _fetch(key).ConfigureAwait(false);
        }
        catch (TError ex)
        {
            var now = _time.GetUtcNow();
            lock (_lock)
            {
                if (Complete(key, fetch))
                {
                    // A value still inside its lifetime outlives a failed refresh; the next
                    // background attempt, and the next forced refresh, wait out their windows.
                    if (_values.TryPeek(key, out var current) && current.Error is null && now - current.FetchedAt < _expireAfter)
                    {
                        _refreshFailed?.Invoke(key, ex);
                        _values.Set(key, current with { RetryAfter = now + _failureTtl, LastAttemptAt = now });
                    }
                    else
                    {
                        _values.Remove(key);
                        _failures.Set(key, new Entry(null, ex, now, default, now));
                    }
                }
            }

            fetch.Completion.SetException(ex);
            return;
        }
        catch (Exception ex)
        {
            // Not a resolution outcome (a bug, or a cancellation in the fetch): nothing is
            // remembered, and the next request tries again.
            lock (_lock)
                Complete(key, fetch);

            fetch.Completion.SetException(ex);
            return;
        }

        var fetchedAt = _time.GetUtcNow();
        PendingWrite? write = null;
        lock (_lock)
        {
            if (Complete(key, fetch))
            {
                _failures.Remove(key);
                _values.Set(key, new Entry(value, null, fetchedAt, default, fetchedAt));

                // Registered in the same lock as the store, so an invalidation that sees the
                // value gone also sees the write it has to undo.
                if (_shared is not null)
                {
                    if (!_pendingWrites.TryGetValue(key, out write))
                        _pendingWrites.Add(key, write = new PendingWrite());
                    write.Count++;
                }
            }
        }

        fetch.Completion.SetResult(value);

        if (write is not null)
            await WriteSharedAsync(key, value, fetchedAt, write).ConfigureAwait(false);
    }

    // The fetch remembers its own outcome; nobody awaits it.
    private void RefreshInBackground(TKey key) => _ = StartFetch(key);

    // Looks an entry up, value first, and marks it most recently used.
    private Entry? Touch(TKey key)
    {
        lock (_lock)
        {
            return _values.TryGetValue(key, out var value) ? value
                : _failures.TryGetValue(key, out var failure) ? failure
                : null;
        }
    }

    // Retires a fetch. Returns whether its outcome may be stored, which it may not once an invalidation
    // detached it.
    private bool Complete(TKey key, Fetch fetch)
    {
        if (_inflight.TryGetValue(key, out var current) && ReferenceEquals(current, fetch))
            _inflight.Remove(key);

        return !fetch.Detached;
    }

    private async Task<(TValue Value, DateTimeOffset FetchedAt)?> ReadSharedAsync(TKey key, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _shared!.Cache.GetAsync(_shared.Key(key), cancellationToken).ConfigureAwait(false);
            return bytes is null ? null : _shared.Read(key, bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _shared!.Failed(SharedOperation.Read, key, ex);
            return null;
        }
    }

    private async Task WriteSharedAsync(TKey key, TValue value, DateTimeOffset fetchedAt, PendingWrite write)
    {
        try
        {
            await _shared!.Cache.SetAsync(
                _shared.Key(key),
                _shared.Write(value, fetchedAt),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _expireAfter })
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _shared!.Failed(SharedOperation.Write, key, ex);
        }

        bool undo;
        lock (_lock)
        {
            undo = write.Invalidated;
            if (--write.Count == 0)
                _pendingWrites.Remove(key);
        }

        // The key was invalidated while this write was in flight, so it may have landed after the
        // removal and put the old value back.
        if (undo)
            await RemoveSharedAsync(key, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task RemoveSharedAsync(TKey key, CancellationToken cancellationToken)
    {
        try
        {
            await _shared!.Cache.RemoveAsync(_shared.Key(key), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _shared!.Failed(SharedOperation.Remove, key, ex);
        }
    }

    // A cache shared between instances, and how values are stored in it.
    //
    // Read: A stored value and when it was fetched, or null when the bytes are not this key's value.
    //
    // Failed: Told when the shared cache fails an operation, which then counts as a miss.
    public sealed record SharedTier(
        IDistributedCache Cache,
        Func<TKey, string> Key,
        Func<TKey, byte[], (TValue Value, DateTimeOffset FetchedAt)?> Read,
        Func<TValue, DateTimeOffset, byte[]> Write,
        Action<SharedOperation, TKey, Exception> Failed);

    // A cached value, or a remembered failure.
    //
    // FetchedAt: When the value was fetched or the failure happened.
    //
    // RetryAfter: The earliest a background refresh of a stale value may start.
    //
    // LastAttemptAt: When a fetch for the key last completed, successful or not.
    private sealed record Entry(
        TValue? Value, TError? Error, DateTimeOffset FetchedAt, DateTimeOffset RetryAfter, DateTimeOffset LastAttemptAt);

    // One fetch in flight, and whether an invalidation detached it.
    private sealed class Fetch
    {
        public TaskCompletionSource<TValue> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Detached { get; set; }
    }

    // Shared-tier writes in flight for a key, and whether it was invalidated meanwhile.
    private sealed class PendingWrite
    {
        public int Count { get; set; }

        public bool Invalidated { get; set; }
    }
}

internal enum SharedOperation
{
    Read,
    Write,
    Remove,
}
