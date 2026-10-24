using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Identity;

/// <summary>Options of <see cref="CachingDidResolver"/>.</summary>
public sealed record DidCacheOptions
{
    /// <summary>How long a document is served without refreshing it.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromHours(1);

    /// <summary>How long a document is served at all; past <see cref="StaleAfter"/> it is refreshed in the background.</summary>
    public TimeSpan ExpireAfter { get; init; } = TimeSpan.FromHours(24);

    /// <summary>How long a failed resolution is remembered.</summary>
    public TimeSpan FailureTtl { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The shortest interval between two <see cref="IDidResolver.RefreshAsync"/> fetches of one DID, failed ones included.</summary>
    public TimeSpan MinRefreshInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The most documents held in memory.</summary>
    public int Capacity { get; init; } = 10_000;

    /// <summary>The most failures held in memory, apart from the documents.</summary>
    public int FailureCapacity { get; init; } = 1_000;

    /// <summary>The prefix of the distributed cache's keys.</summary>
    public string DistributedCacheKeyPrefix { get; init; } = "atproto:did:";

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(StaleAfter, TimeSpan.Zero, nameof(StaleAfter));
        ArgumentOutOfRangeException.ThrowIfLessThan(ExpireAfter, StaleAfter, nameof(ExpireAfter));
        ArgumentOutOfRangeException.ThrowIfLessThan(FailureTtl, TimeSpan.Zero, nameof(FailureTtl));
        ArgumentOutOfRangeException.ThrowIfLessThan(MinRefreshInterval, TimeSpan.Zero, nameof(MinRefreshInterval));
        ArgumentOutOfRangeException.ThrowIfLessThan(Capacity, 1, nameof(Capacity));
        ArgumentOutOfRangeException.ThrowIfLessThan(FailureCapacity, 1, nameof(FailureCapacity));
        ArgumentNullException.ThrowIfNull(DistributedCacheKeyPrefix);
    }
}

/// <summary>Caches the documents another <see cref="IDidResolver"/> resolves.</summary>
/// <remarks>
/// <para>A document younger than <see cref="DidCacheOptions.StaleAfter"/> is served as is; one younger than
/// <see cref="DidCacheOptions.ExpireAfter"/> is served while a background fetch replaces it (and keeps being served if that fails);
/// an older one is fetched first. Failed resolutions are remembered for <see cref="DidCacheOptions.FailureTtl"/>, apart from the
/// documents, so a stream of bogus DIDs cannot evict them. Concurrent misses share one fetch, which completes even if every
/// caller gives up.</para>
/// <para>An optional <see cref="IDistributedCache"/> shares documents between instances, best effort: its failures count as
/// misses. <see cref="InvalidateAsync"/> removes the shared copy, but not other instances' memory.</para>
/// </remarks>
public sealed partial class CachingDidResolver : IDidResolver
{
    private readonly DidCache _cache;

    /// <summary>Creates a cache.</summary>
    /// <param name="inner">The resolver documents are fetched through, which the caller owns. It bounds each fetch itself.</param>
    /// <param name="options">Options. Defaults apply when omitted.</param>
    /// <param name="shared">An optional cache shared between instances.</param>
    /// <param name="time">The clock. Defaults to the system clock.</param>
    /// <param name="logger">Optional logger.</param>
    public CachingDidResolver(IDidResolver inner, DidCacheOptions? options = null, IDistributedCache? shared = null, TimeProvider? time = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        options ??= new();
        options.Validate();
        var log = logger ?? NullLogger.Instance;
        _cache = new((did, mode) => mode switch
        {
            DidCache.Mode.Refresh => inner.RefreshAsync(did, CancellationToken.None),
            DidCache.Mode.Fresh => inner.FetchFreshAsync(did, CancellationToken.None),
            _ => inner.ResolveAsync(did, CancellationToken.None),
        }, options, shared, time ?? TimeProvider.System, (operation, did, ex) => LogSharedFailure(log, ex, operation, did));
    }

    /// <inheritdoc />
    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default) =>
        _cache.GetAsync(did ?? throw new ArgumentNullException(nameof(did)), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Within <see cref="DidCacheOptions.MinRefreshInterval"/> of the last fetch attempt the cached document, or failure, is returned instead.</remarks>
    public Task<DidDocument> RefreshAsync(Did did, CancellationToken cancellationToken = default) =>
        _cache.RefreshAsync(did ?? throw new ArgumentNullException(nameof(did)), cancellationToken);

    /// <inheritdoc />
    /// <remarks>The result replaces what the cache held.</remarks>
    public Task<DidDocument> FetchFreshAsync(Did did, CancellationToken cancellationToken = default) =>
        _cache.FetchFreshAsync(did ?? throw new ArgumentNullException(nameof(did)), cancellationToken);

    /// <inheritdoc />
    /// <remarks>A fetch under way is detached: its callers still get its result, but it is not stored. A distributed write still in flight is undone once it lands.</remarks>
    public ValueTask InvalidateAsync(Did did, CancellationToken cancellationToken = default) =>
        _cache.InvalidateAsync(did ?? throw new ArgumentNullException(nameof(did)), cancellationToken);

    [LoggerMessage(1030, LogLevel.Warning, "The distributed DID document cache failed to {Operation} {Did}.")]
    private static partial void LogSharedFailure(ILogger logger, Exception exception, string operation, Did did);
}
