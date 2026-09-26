using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Identity;

/// <summary>
/// Caches the DID documents another <see cref="IDidResolver"/> resolves.
/// </summary>
/// <remarks>
/// <para>A document younger than <see cref="DidCacheOptions.StaleAfter"/> is served as is; one
/// younger than <see cref="DidCacheOptions.ExpireAfter"/> is served while a background fetch
/// replaces it; an older one is refetched before use. At most
/// <see cref="DidCacheOptions.Capacity"/> documents are held, the least recently used going
/// first.</para>
/// <para>A failed resolution is remembered for <see cref="DidCacheOptions.FailureTtl"/>, so a DID
/// that does not resolve costs one fetch per window rather than one per request. Failures are
/// held apart from documents, at most <see cref="DidCacheOptions.FailureCapacity"/> of them, so a
/// stream of bogus DIDs cannot evict the documents that do resolve. Concurrent requests for a DID
/// that is not cached share one fetch, which runs to completion even if every caller that asked
/// for it gives up.</para>
/// <para>An optional <see cref="IDistributedCache"/> shares documents between instances. It is
/// consulted when memory misses and written after every fetch; failures are remembered in memory
/// only. The distributed cache is best-effort: its errors are logged and treated as misses.
/// <see cref="InvalidateAsync"/> removes the shared copy but not the in-memory copies other
/// instances already hold, which live out their own lifetimes; each instance that follows
/// <c>#identity</c> events invalidates its own cache.</para>
/// </remarks>
public sealed class CachingDidResolver : IDidResolver, IDisposable
{
    private readonly IDidResolver _inner;
    private readonly bool _ownsInner;
    private readonly DidCacheOptions _options;
    private readonly IDistributedCache? _distributed;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    private readonly Lock _lock = new();
    private readonly Lru _documents;
    private readonly Lru _failures;
    private readonly Dictionary<Did, Fetch> _inflight = new();
    private readonly Dictionary<Did, PendingWrite> _pendingWrites = new();

    /// <summary>
    /// Creates a cache over a new <see cref="DidResolver"/>, which it owns.
    /// </summary>
    /// <param name="options">
    /// Resolver options; <see cref="IdentityResolverOptions.Cache"/> configures the cache. Defaults
    /// apply when omitted.
    /// </param>
    public CachingDidResolver(IdentityResolverOptions? options = null)
        : this(new DidResolver(options), (options ?? new IdentityResolverOptions()).Cache, null, null, null, ownsInner: true)
    {
    }

    /// <summary>
    /// Creates a cache over <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">The resolver documents are fetched through. The caller owns it.</param>
    /// <param name="options">Cache options. Defaults apply when omitted.</param>
    /// <param name="distributedCache">An optional cache shared between instances.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    /// <param name="logger">Optional logger.</param>
    public CachingDidResolver(
        IDidResolver inner,
        DidCacheOptions? options = null,
        IDistributedCache? distributedCache = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
        : this(inner, options, distributedCache, timeProvider, logger, ownsInner: false)
    {
    }

    /// <summary>Creates a cache, owning <paramref name="inner"/> when <paramref name="ownsInner"/> is set.</summary>
    internal CachingDidResolver(
        IDidResolver inner,
        DidCacheOptions? options,
        IDistributedCache? distributedCache,
        TimeProvider? timeProvider,
        ILogger? logger,
        bool ownsInner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _ownsInner = ownsInner;
        _options = options ?? new DidCacheOptions();
        _options.Validate();
        _distributed = distributedCache;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _documents = new Lru(_options.Capacity);
        _failures = new Lru(_options.FailureCapacity);
    }

    /// <summary>The number of documents and remembered failures held in memory.</summary>
    internal int Count
    {
        get
        {
            lock (_lock)
                return _documents.Count + _failures.Count;
        }
    }

    /// <summary>The number of documents held in memory.</summary>
    internal int DocumentCount
    {
        get
        {
            lock (_lock)
                return _documents.Count;
        }
    }

    /// <inheritdoc/>
    public async Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        var now = _time.GetUtcNow();
        if (Touch(did) is { } entry && TryServe(entry, now, out var document))
            return document;

        if (_distributed is not null &&
            await ReadDistributedAsync(did, cancellationToken).ConfigureAwait(false) is { } shared &&
            now - shared.FetchedAt < _options.ExpireAfter)
        {
            lock (_lock)
            {
                _failures.Remove(did);
                _documents.Set(new Entry(did, shared.Document, null, shared.FetchedAt, default, shared.FetchedAt));
            }

            if (now - shared.FetchedAt >= _options.StaleAfter)
                RefreshInBackground(did);
            return shared.Document;
        }

        return await StartFetch(did).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Refreshes of one DID are at least <see cref="DidCacheOptions.MinRefreshInterval"/> apart,
    /// counted from the last fetch attempt, failed ones included: within it the cached document,
    /// or the remembered failure, is returned without fetching again. A refresh already under way
    /// is joined.
    /// </remarks>
    public Task<DidDocument> RefreshAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        if (Touch(did) is { } entry && _time.GetUtcNow() - entry.LastAttemptAt < _options.MinRefreshInterval)
        {
            return entry.Document is { } document
                ? Task.FromResult(document)
                : Task.FromException<DidDocument>(Rethrow(entry.Error!));
        }

        return StartFetch(did).WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A fetch already under way for the DID is detached: its callers still get its result, but it
    /// is not stored, and the next resolution starts a new fetch. A distributed-cache write still
    /// in flight is removed again once it lands. Other instances' in-memory copies are not
    /// reached.
    /// </remarks>
    public async Task InvalidateAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        lock (_lock)
        {
            _documents.Remove(did);
            _failures.Remove(did);

            if (_inflight.Remove(did, out var fetch))
                fetch.Detached = true;

            if (_pendingWrites.TryGetValue(did, out var write))
                write.Invalidated = true;
        }

        if (_distributed is not null)
            await RemoveDistributedAsync(did, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Decides whether a cached entry answers the request: a fresh or stale document does (a stale
    /// one also starts a background refresh), a remembered failure throws, and anything expired
    /// does not.
    /// </summary>
    private bool TryServe(Entry entry, DateTimeOffset now, out DidDocument document)
    {
        document = null!;
        var age = now - entry.FetchedAt;

        if (entry.Error is { } error)
        {
            if (age < _options.FailureTtl)
                throw Rethrow(error);
            return false;
        }

        if (age >= _options.ExpireAfter)
            return false;

        if (age >= _options.StaleAfter && now >= entry.RetryAfter)
            RefreshInBackground(entry.Did);

        document = entry.Document!;
        return true;
    }

    /// <summary>
    /// Returns the fetch under way for a DID, or starts one. Every caller of one fetch observes
    /// the same outcome.
    /// </summary>
    private Task<DidDocument> StartFetch(Did did)
    {
        Fetch fetch;
        lock (_lock)
        {
            if (_inflight.TryGetValue(did, out var existing))
                return existing.Completion.Task;

            fetch = new Fetch();
            _inflight.Add(did, fetch);
        }

        // Every caller may have given up (or it was a background refresh nobody awaits), so the
        // outcome is observed here once, and a failure never surfaces as an unobserved exception.
        _ = fetch.Completion.Task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        _ = RunFetchAsync(did, fetch);
        return fetch.Completion.Task;
    }

    private async Task RunFetchAsync(Did did, Fetch fetch)
    {
        DidDocument document;
        try
        {
            // Not the caller's token: the fetch is shared, and one caller giving up must not fail
            // it for the others. The inner resolver bounds it with its own timeout.
            document = await _inner.ResolveAsync(did, CancellationToken.None).ConfigureAwait(false);
        }
        catch (DidResolutionException ex)
        {
            var now = _time.GetUtcNow();
            lock (_lock)
            {
                if (Complete(did, fetch))
                {
                    // A document still inside its lifetime outlives a failed refresh; the next
                    // background attempt, and the next forced refresh, wait out their windows.
                    if (_documents.Get(did) is { } current && now - current.FetchedAt < _options.ExpireAfter)
                    {
                        _documents.Set(current with { RetryAfter = now + _options.FailureTtl, LastAttemptAt = now });
                    }
                    else
                    {
                        _documents.Remove(did);
                        _failures.Set(new Entry(did, null, ex, now, default, now));
                    }
                }
            }

            fetch.Completion.SetException(ex);
            return;
        }
        catch (Exception ex)
        {
            // Not a resolution outcome (a bug or a cancellation in the inner resolver): nothing
            // is remembered, and the next request tries again.
            lock (_lock)
                Complete(did, fetch);

            fetch.Completion.SetException(ex);
            return;
        }

        var fetchedAt = _time.GetUtcNow();
        PendingWrite? write = null;
        lock (_lock)
        {
            if (Complete(did, fetch))
            {
                _failures.Remove(did);
                _documents.Set(new Entry(did, document, null, fetchedAt, default, fetchedAt));

                // Registered in the same lock as the store, so an invalidation that sees the
                // document gone also sees the write it has to undo.
                if (_distributed is not null)
                {
                    if (!_pendingWrites.TryGetValue(did, out write))
                        _pendingWrites.Add(did, write = new PendingWrite());
                    write.Count++;
                }
            }
        }

        fetch.Completion.SetResult(document);

        if (write is not null)
            await WriteDistributedAsync(did, document, fetchedAt, write).ConfigureAwait(false);
    }

    // The fetch remembers its own outcome; nobody awaits it.
    private void RefreshInBackground(Did did) => _ = StartFetch(did);

    /// <summary>Looks an entry up, document first, and marks it most recently used.</summary>
    private Entry? Touch(Did did)
    {
        lock (_lock)
            return _documents.Touch(did) ?? _failures.Touch(did);
    }

    /// <summary>
    /// Retires a fetch from the in-flight set. Returns whether its outcome may be stored, which it
    /// may not once an invalidation detached it.
    /// </summary>
    private bool Complete(Did did, Fetch fetch)
    {
        if (_inflight.TryGetValue(did, out var current) && ReferenceEquals(current, fetch))
            _inflight.Remove(did);

        return !fetch.Detached;
    }

    /// <summary>
    /// A fresh exception for a remembered failure: one instance thrown on several threads at once
    /// would have its stack trace rewritten under each of them.
    /// </summary>
    private static DidResolutionException Rethrow(DidResolutionException cached) =>
        new(cached.Message, cached.Kind, cached.Did, cached);

    private string DistributedKey(Did did) => _options.DistributedCacheKeyPrefix + did.Value;

    private async Task<SharedEntry?> ReadDistributedAsync(Did did, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _distributed!.GetAsync(DistributedKey(did), cancellationToken).ConfigureAwait(false);
            if (bytes is null)
                return null;

            var shared = JsonSerializer.Deserialize<SharedEntry>(bytes, AtProtoJsonDefaults.Options);

            // A document stored under another DID's key is not this DID's document.
            return shared is not null && IdentityFetch.IdsMatch(shared.Document.Id, did) ? shared : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not read {Did} from the distributed DID document cache.", did);
            return null;
        }
    }

    private async Task WriteDistributedAsync(Did did, DidDocument document, DateTimeOffset fetchedAt, PendingWrite write)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new SharedEntry(fetchedAt, document), AtProtoJsonDefaults.Options);
            await _distributed!.SetAsync(
                DistributedKey(did),
                bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _options.ExpireAfter })
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write {Did} to the distributed DID document cache.", did);
        }

        bool undo;
        lock (_lock)
        {
            undo = write.Invalidated;
            if (--write.Count == 0)
                _pendingWrites.Remove(did);
        }

        // The DID was invalidated while this write was in flight, so it may have landed after the
        // removal and put the old document back.
        if (undo)
            await RemoveDistributedAsync(did, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task RemoveDistributedAsync(Did did, CancellationToken cancellationToken)
    {
        try
        {
            await _distributed!.RemoveAsync(DistributedKey(did), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not remove {Did} from the distributed DID document cache.", did);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsInner && _inner is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>A cached document, or a remembered failure.</summary>
    /// <param name="Did">The DID.</param>
    /// <param name="Document">The document, when resolution succeeded.</param>
    /// <param name="Error">The failure, when it did not.</param>
    /// <param name="FetchedAt">When the document was fetched or the failure happened.</param>
    /// <param name="RetryAfter">The earliest a background refresh of a stale document may start.</param>
    /// <param name="LastAttemptAt">When a fetch for the DID last completed, successful or not.</param>
    private sealed record Entry(
        Did Did,
        DidDocument? Document,
        DidResolutionException? Error,
        DateTimeOffset FetchedAt,
        DateTimeOffset RetryAfter,
        DateTimeOffset LastAttemptAt);

    /// <summary>A bounded map from DID to entry, evicting the least recently used. Not thread-safe.</summary>
    private sealed class Lru(int capacity)
    {
        private readonly Dictionary<Did, LinkedListNode<Entry>> _map = new();
        private readonly LinkedList<Entry> _recency = new(); // most recently used first

        public int Count => _map.Count;

        public Entry? Get(Did did) => _map.TryGetValue(did, out var node) ? node.Value : null;

        public Entry? Touch(Did did)
        {
            if (!_map.TryGetValue(did, out var node))
                return null;

            _recency.Remove(node);
            _recency.AddFirst(node);
            return node.Value;
        }

        public void Set(Entry entry)
        {
            if (_map.TryGetValue(entry.Did, out var node))
            {
                node.Value = entry;
                _recency.Remove(node);
                _recency.AddFirst(node);
                return;
            }

            _map.Add(entry.Did, _recency.AddFirst(entry));
            if (_map.Count > capacity)
            {
                var last = _recency.Last!;
                _recency.RemoveLast();
                _map.Remove(last.Value.Did);
            }
        }

        public void Remove(Did did)
        {
            if (_map.Remove(did, out var node))
                _recency.Remove(node);
        }
    }

    /// <summary>One fetch in flight, and whether an invalidation detached it.</summary>
    private sealed class Fetch
    {
        public TaskCompletionSource<DidDocument> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Detached { get; set; }
    }

    /// <summary>Distributed-cache writes in flight for a DID, and whether it was invalidated meanwhile.</summary>
    private sealed class PendingWrite
    {
        public int Count { get; set; }

        public bool Invalidated { get; set; }
    }

    /// <summary>What the distributed cache holds for a DID.</summary>
    private sealed record SharedEntry(
        [property: JsonPropertyName("fetchedAt")] DateTimeOffset FetchedAt,
        [property: JsonPropertyName("document")] DidDocument Document);
}
