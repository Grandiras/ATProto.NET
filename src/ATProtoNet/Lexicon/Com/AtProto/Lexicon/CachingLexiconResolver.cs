using ATProtoNet.Caching;
using ATProtoNet.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Lexicon.Com.AtProto.Lexicon;

/// <summary>Caches the schemas another <see cref="ILexiconResolver"/> resolves.</summary>
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
    private readonly ResolutionCache<Nsid, ResolvedLexicon, LexiconResolutionException> _cache;

    /// <summary>Creates a cache over <paramref name="inner"/>.</summary>
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
        options ??= new LexiconCacheOptions();
        options.Validate();

        // Schemas and failures share one capacity. The inner resolver bounds each resolution.
        var log = logger ?? NullLogger.Instance;
        _cache = new ResolutionCache<Nsid, ResolvedLexicon, LexiconResolutionException>(
            nsid => _inner.ResolveAsync(nsid, CancellationToken.None),
            static cached => new LexiconResolutionException(cached.Message, cached.Nsid, cached.Kind, cached),
            options.Capacity,
            failureCapacity: null,
            options.StaleAfter,
            options.ExpireAfter,
            options.FailureTtl,
            timeProvider ?? TimeProvider.System,
            refreshFailed: (nsid, ex) => log.LogInformation(ex, "Refreshing Lexicon {Nsid} failed; serving the cached schema.", nsid));
    }

    /// <summary>The number of schemas and remembered failures held.</summary>
    internal int Count => _cache.Count;

    /// <summary>Whether a resolution of <paramref name="nsid"/> is under way.</summary>
    internal bool IsResolving(Nsid nsid) => _cache.IsFetching(nsid);

    /// <inheritdoc/>
    public Task<ResolvedLexicon> ResolveAsync(Nsid nsid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nsid);
        return _cache.GetAsync(nsid, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A resolution already under way for the NSID is detached: its callers still get its result,
    /// but it is not stored.
    /// </remarks>
    public async Task InvalidateAsync(Nsid nsid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nsid);
        await _cache.InvalidateAsync(nsid, cancellationToken).ConfigureAwait(false);
        await _inner.InvalidateAsync(nsid, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsInner && _inner is IDisposable disposable)
            disposable.Dispose();
    }
}

/// <summary>Configuration for a <see cref="CachingLexiconResolver"/>.</summary>
public sealed class LexiconCacheOptions
{
    /// <summary>The most schemas and failures held. The least recently used goes first. Defaults to 1,000.</summary>
    public int Capacity { get; set; } = 1_000;

    /// <summary>How long a schema is served without being re-resolved. After that it is still served, and re-resolved in the background. Defaults to five minutes.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a schema may be served at all, including while re-resolution keeps failing. After that it is resolved again before use. Defaults to 24 hours, the permission specification's upper bound.</summary>
    public TimeSpan ExpireAfter { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How long a failed resolution is remembered, and how long a failed background refresh waits before the next. Defaults to one minute.</summary>
    public TimeSpan FailureTtl { get; set; } = TimeSpan.FromMinutes(1);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Capacity, 1, nameof(Capacity));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(StaleAfter, TimeSpan.Zero, nameof(StaleAfter));
        ArgumentOutOfRangeException.ThrowIfLessThan(ExpireAfter, StaleAfter, nameof(ExpireAfter));
        ArgumentOutOfRangeException.ThrowIfLessThan(FailureTtl, TimeSpan.Zero, nameof(FailureTtl));
    }
}
