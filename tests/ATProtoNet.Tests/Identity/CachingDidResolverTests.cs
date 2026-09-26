using ATProtoNet.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// The DID document cache: lifetimes, remembered failures, one fetch per DID at a time,
/// invalidation, refresh rate-limiting, the size bound and the distributed backing.
/// </summary>
public class CachingDidResolverTests
{
    private static readonly Did Alice = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Did Bob = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");

    private readonly ManualClock _clock = new();
    private readonly CountingResolver _inner = new();

    private CachingDidResolver Create(DidCacheOptions? options = null, IDistributedCache? distributed = null) =>
        new(_inner, options ?? new DidCacheOptions(), distributed, _clock);

    // ── Lifetimes ────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_Fresh_IsServedFromMemory()
    {
        using var cache = Create();

        var first = await cache.ResolveAsync(Alice);
        _clock.Advance(TimeSpan.FromMinutes(59));
        var second = await cache.ResolveAsync(Alice);

        Assert.Same(first, second);
        Assert.Equal(1, _inner.Calls(Alice));
    }

    [Fact]
    public async Task ResolveAsync_Stale_ServesTheCachedDocumentAndRefreshesInTheBackground()
    {
        using var cache = Create();
        var original = await cache.ResolveAsync(Alice);
        _inner.Version++;

        _clock.Advance(TimeSpan.FromHours(2));
        var served = await cache.ResolveAsync(Alice);
        await _inner.WaitForCallsAsync(Alice, 2);
        await WaitUntilAsync(async () => !ReferenceEquals(await cache.ResolveAsync(Alice), original));

        Assert.Same(original, served);
        Assert.Equal(2, _inner.Calls(Alice));
    }

    [Fact]
    public async Task ResolveAsync_Expired_RefetchesBeforeUse()
    {
        using var cache = Create();
        var original = await cache.ResolveAsync(Alice);
        _inner.Version++;

        _clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1));
        var refetched = await cache.ResolveAsync(Alice);

        Assert.NotSame(original, refetched);
        Assert.Equal(2, _inner.Calls(Alice));
    }

    [Fact]
    public async Task ResolveAsync_HardLifetime_NeverServesADocumentPastIt()
    {
        // StaleAfter == ExpireAfter: no stale-while-revalidate, as the space server's default.
        var lifetime = TimeSpan.FromMinutes(5);
        using var cache = Create(new DidCacheOptions { StaleAfter = lifetime, ExpireAfter = lifetime });
        var original = await cache.ResolveAsync(Alice);
        _inner.Version++;

        _clock.Advance(lifetime - TimeSpan.FromSeconds(1));
        Assert.Same(original, await cache.ResolveAsync(Alice));
        Assert.Equal(1, _inner.Calls(Alice));

        _clock.Advance(TimeSpan.FromSeconds(1));
        var refetched = await cache.ResolveAsync(Alice);

        Assert.NotSame(original, refetched);
        Assert.Equal(2, _inner.Calls(Alice));
    }

    [Fact]
    public async Task ResolveAsync_StaleRefreshFails_KeepsServingTheDocumentAndWaitsBeforeRetrying()
    {
        using var cache = Create();
        var original = await cache.ResolveAsync(Alice);

        _inner.Fail = DidResolutionErrorKind.NetworkError;
        _clock.Advance(TimeSpan.FromHours(2));
        Assert.Same(original, await cache.ResolveAsync(Alice));
        await _inner.WaitForCallsAsync(Alice, 2);
        await Task.Delay(50);

        // Within the failure window a stale read starts no new fetch.
        Assert.Same(original, await cache.ResolveAsync(Alice));
        await Task.Delay(50);
        Assert.Equal(2, _inner.Calls(Alice));

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Same(original, await cache.ResolveAsync(Alice));
        await _inner.WaitForCallsAsync(Alice, 3);
    }

    // ── Failures ─────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_Failure_IsRememberedForTheFailureTtl()
    {
        using var cache = Create();
        _inner.Fail = DidResolutionErrorKind.NotFound;

        var first = await Assert.ThrowsAsync<DidResolutionException>(() => cache.ResolveAsync(Alice));
        var second = await Assert.ThrowsAsync<DidResolutionException>(() => cache.ResolveAsync(Alice));

        Assert.Equal(DidResolutionErrorKind.NotFound, first.Kind);
        Assert.Equal(DidResolutionErrorKind.NotFound, second.Kind);
        Assert.NotSame(first, second);
        Assert.Equal(1, _inner.Calls(Alice));

        _inner.Fail = null;
        _clock.Advance(TimeSpan.FromMinutes(1));
        await cache.ResolveAsync(Alice);
        Assert.Equal(2, _inner.Calls(Alice));
    }

    [Fact]
    public async Task ResolveAsync_UnexpectedException_IsNotRemembered()
    {
        using var cache = Create();
        _inner.Throw = new InvalidOperationException("bug");

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.ResolveAsync(Alice));
        _inner.Throw = null;
        await cache.ResolveAsync(Alice);

        Assert.Equal(2, _inner.Calls(Alice));
    }

    // ── Single flight ────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ConcurrentMisses_ShareOneFetch()
    {
        using var cache = Create();
        _inner.Gate = new TaskCompletionSource();

        var callers = Enumerable.Range(0, 32).Select(_ => cache.ResolveAsync(Alice)).ToArray();
        await _inner.WaitForCallsAsync(Alice, 1);
        _inner.Gate.SetResult();
        var documents = await Task.WhenAll(callers);

        Assert.Equal(1, _inner.Calls(Alice));
        Assert.All(documents, d => Assert.Same(documents[0], d));
    }

    [Fact]
    public async Task ResolveAsync_OneCallerCancels_TheSharedFetchCompletesForTheOthers()
    {
        using var cache = Create();
        _inner.Gate = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        var cancelled = cache.ResolveAsync(Alice, cts.Token);
        var patient = cache.ResolveAsync(Alice);
        await _inner.WaitForCallsAsync(Alice, 1);
        await cts.CancelAsync();
        _inner.Gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(Alice, (await patient).Id);
        Assert.Equal(1, _inner.Calls(Alice));
    }

    // ── Invalidation and refresh ─────────────────────────────

    [Fact]
    public async Task InvalidateAsync_NextResolutionRefetches()
    {
        using var cache = Create();
        await cache.ResolveAsync(Alice);
        await cache.ResolveAsync(Bob);

        await cache.InvalidateAsync(Alice);
        await cache.ResolveAsync(Alice);
        await cache.ResolveAsync(Bob);

        Assert.Equal(2, _inner.Calls(Alice));
        Assert.Equal(1, _inner.Calls(Bob));
    }

    [Fact]
    public async Task InvalidateAsync_DuringAFetch_DiscardsItsResult()
    {
        using var cache = Create();
        _inner.Gate = new TaskCompletionSource();
        var inFlight = cache.ResolveAsync(Alice);
        await _inner.WaitForCallsAsync(Alice, 1);

        // The fetch began before the identity changed, so what it returns may be the old document.
        await cache.InvalidateAsync(Alice);
        _inner.Gate.SetResult();
        await inFlight;
        _inner.Gate = null;
        await cache.ResolveAsync(Alice);

        Assert.Equal(2, _inner.Calls(Alice));
    }

    [Fact]
    public async Task RefreshAsync_FetchesPastTheCache()
    {
        using var cache = Create();
        var original = await cache.ResolveAsync(Alice);
        _inner.Version++;
        _clock.Advance(TimeSpan.FromMinutes(1));

        var refreshed = await cache.RefreshAsync(Alice);

        Assert.NotSame(original, refreshed);
        Assert.Same(refreshed, await cache.ResolveAsync(Alice));
        Assert.Equal(2, _inner.Calls(Alice));
    }

    [Fact]
    public async Task RefreshAsync_WithinTheMinimumInterval_DoesNotFetchAgain()
    {
        // Anyone can send a bad signature naming any DID; each one must not become a fetch.
        using var cache = Create();
        var original = await cache.ResolveAsync(Alice);

        for (var i = 0; i < 10; i++)
            Assert.Same(original, await cache.RefreshAsync(Alice));

        Assert.Equal(1, _inner.Calls(Alice));
    }

    [Fact]
    public async Task RefreshAsync_Uncached_Fetches()
    {
        using var cache = Create();

        await cache.RefreshAsync(Alice);

        Assert.Equal(1, _inner.Calls(Alice));
    }

    // ── Bound ────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_OverCapacity_EvictsTheLeastRecentlyUsed()
    {
        using var cache = Create(new DidCacheOptions { Capacity = 2 });
        var carol = Did.Parse("did:plc:cccccccccccccccccccccccc");

        await cache.ResolveAsync(Alice);
        await cache.ResolveAsync(Bob);
        await cache.ResolveAsync(Alice); // Bob is now the least recently used
        await cache.ResolveAsync(carol);

        Assert.Equal(2, cache.Count);
        await cache.ResolveAsync(Alice);
        await cache.ResolveAsync(Bob);
        Assert.Equal(1, _inner.Calls(Alice));
        Assert.Equal(2, _inner.Calls(Bob));
    }

    // ── Distributed backing ──────────────────────────────────

    [Fact]
    public async Task DistributedCache_ASecondInstanceReadsWhatTheFirstFetched()
    {
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using var first = Create(distributed: distributed);
        using var second = Create(distributed: distributed);

        await first.ResolveAsync(Alice);
        await WaitUntilAsync(async () => await distributed.GetAsync("atproto:did:" + Alice.Value) is not null);
        var shared = await second.ResolveAsync(Alice);

        Assert.Equal(Alice, shared.Id);
        Assert.Equal(1, _inner.Calls(Alice));
    }

    [Fact]
    public async Task DistributedCache_InvalidateRemovesTheSharedCopy()
    {
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using var cache = Create(distributed: distributed);
        await cache.ResolveAsync(Alice);
        await WaitUntilAsync(async () => await distributed.GetAsync("atproto:did:" + Alice.Value) is not null);

        await cache.InvalidateAsync(Alice);

        Assert.Null(await distributed.GetAsync("atproto:did:" + Alice.Value));
    }

    [Fact]
    public async Task DistributedCache_EntryUnderTheWrongKey_IsIgnored()
    {
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using var writer = Create(distributed: distributed);
        await writer.ResolveAsync(Bob);
        await WaitUntilAsync(async () => await distributed.GetAsync("atproto:did:" + Bob.Value) is not null);
        await distributed.SetAsync("atproto:did:" + Alice.Value, (await distributed.GetAsync("atproto:did:" + Bob.Value))!);

        using var reader = Create(distributed: distributed);
        var document = await reader.ResolveAsync(Alice);

        Assert.Equal(Alice, document.Id);
        Assert.Equal(1, _inner.Calls(Alice));
    }

    [Fact]
    public async Task DistributedCache_Failing_FallsBackToFetching()
    {
        var distributed = Substitute.For<IDistributedCache>();
        distributed.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<byte[]?>(new IOException("redis down")));
        using var cache = Create(distributed: distributed);

        var document = await cache.ResolveAsync(Alice);

        Assert.Equal(Alice, document.Id);
    }

    [Fact]
    public void Options_ExpireBeforeStale_AreRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new DidCacheOptions
        {
            StaleAfter = TimeSpan.FromHours(2),
            ExpireAfter = TimeSpan.FromHours(1),
        }));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            if (await condition())
                return;
            await Task.Delay(10);
        }

        Assert.Fail("The condition never held.");
    }

    /// <summary>Resolves any DID to a fresh document instance, counting and optionally gating calls.</summary>
    private sealed class CountingResolver : IDidResolver
    {
        private readonly Dictionary<Did, int> _calls = new();

        public int Version { get; set; }

        public DidResolutionErrorKind? Fail { get; set; }

        public Exception? Throw { get; set; }

        public TaskCompletionSource? Gate { get; set; }

        public int Calls(Did did)
        {
            lock (_calls)
                return _calls.GetValueOrDefault(did);
        }

        public async Task WaitForCallsAsync(Did did, int count)
        {
            for (var i = 0; i < 500 && Calls(did) < count; i++)
                await Task.Delay(10);

            Assert.True(Calls(did) >= count, $"Expected {count} calls for {did}, saw {Calls(did)}.");
        }

        public async Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default)
        {
            lock (_calls)
                _calls[did] = _calls.GetValueOrDefault(did) + 1;

            if (Gate is { } gate)
                await gate.Task;

            if (Throw is { } exception)
                throw exception;

            if (Fail is { } kind)
                throw new DidResolutionException("scripted failure", kind, did);

            return DidDocs.Parse(did.Value, handle: $"v{Version}.example.com");
        }
    }
}
