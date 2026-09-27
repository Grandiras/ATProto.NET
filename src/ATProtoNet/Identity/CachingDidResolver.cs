using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Caching;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Identity;

/// <summary>Caches the DID documents another <see cref="IDidResolver"/> resolves.</summary>
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
    private readonly ResolutionCache<Did, DidDocument, DidResolutionException> _cache;

    /// <summary>Creates a cache over a new <see cref="DidResolver"/>, which it owns.</summary>
    /// <param name="options">
    /// Resolver options; <see cref="IdentityResolverOptions.Cache"/> configures the cache. Defaults
    /// apply when omitted.
    /// </param>
    public CachingDidResolver(IdentityResolverOptions? options = null)
        : this(new DidResolver(options), (options ?? new IdentityResolverOptions()).Cache, null, null, null, ownsInner: true)
    {
    }

    /// <summary>Creates a cache over <paramref name="inner"/>.</summary>
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

    // Creates a cache, owning inner when ownsInner is set.
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

        // The inner resolver bounds each fetch with its own timeout.
        var log = logger ?? NullLogger.Instance;
        _cache = new ResolutionCache<Did, DidDocument, DidResolutionException>(
            did => _inner.ResolveAsync(did, CancellationToken.None),
            static cached => new DidResolutionException(cached.Message, cached.Kind, cached.Did, cached),
            _options.Capacity,
            _options.FailureCapacity,
            _options.StaleAfter,
            _options.ExpireAfter,
            _options.FailureTtl,
            timeProvider ?? TimeProvider.System,
            refreshFailed: null,
            distributedCache is null ? null : new(
                distributedCache,
                did => _options.DistributedCacheKeyPrefix + did.Value,
                static (did, bytes) =>
                    JsonSerializer.Deserialize<SharedEntry>(bytes, AtProtoJsonDefaults.Options) is { } shared &&
                    // A document stored under another DID's key is not this DID's document.
                    IdentityFetch.IdsMatch(shared.Document.Id, did)
                        ? (shared.Document, shared.FetchedAt)
                        : null,
                static (document, fetchedAt) =>
                    JsonSerializer.SerializeToUtf8Bytes(new SharedEntry(fetchedAt, document), AtProtoJsonDefaults.Options),
                (operation, did, ex) => LogSharedFailure(log, operation, did, ex)));
    }

    private static void LogSharedFailure(ILogger logger, SharedOperation operation, Did did, Exception ex)
    {
        switch (operation)
        {
            case SharedOperation.Read:
                logger.LogWarning(ex, "Could not read {Did} from the distributed DID document cache.", did);
                break;
            case SharedOperation.Write:
                logger.LogWarning(ex, "Could not write {Did} to the distributed DID document cache.", did);
                break;
            default:
                logger.LogWarning(ex, "Could not remove {Did} from the distributed DID document cache.", did);
                break;
        }
    }

    // The number of documents and remembered failures held in memory.
    internal int Count => _cache.Count;

    // The number of documents held in memory.
    internal int DocumentCount => _cache.ValueCount;

    /// <inheritdoc/>
    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);
        return _cache.GetAsync(did, cancellationToken);
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
        return _cache.RefreshAsync(did, _options.MinRefreshInterval, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A fetch already under way for the DID is detached: its callers still get its result, but it
    /// is not stored, and the next resolution starts a new fetch. A distributed-cache write still
    /// in flight is removed again once it lands. Other instances' in-memory copies are not
    /// reached.
    /// </remarks>
    public Task InvalidateAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);
        return _cache.InvalidateAsync(did, cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsInner && _inner is IDisposable disposable)
            disposable.Dispose();
    }

    // What the distributed cache holds for a DID.
    private sealed record SharedEntry(
        [property: JsonPropertyName("fetchedAt")] DateTimeOffset FetchedAt,
        [property: JsonPropertyName("document")] DidDocument Document);
}
