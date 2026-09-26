using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Lexicon;
using ATProtoNet.Tests.Identity;

namespace ATProtoNet.Tests.Lexicon.Com.AtProto;

/// <summary>
/// <see cref="CachingLexiconResolver"/>: fresh, stale and expired schemas, remembered failures,
/// shared resolutions and invalidation.
/// </summary>
public sealed class CachingLexiconResolverTests
{
    private static readonly Nsid Post = Nsid.Parse("com.example.lexicon.post");

    private readonly ManualClock _clock = new();
    private readonly CountingResolver _inner = new();

    [Fact]
    public void Defaults_FollowThePermissionSpecification()
    {
        var options = new LexiconCacheOptions();

        Assert.Equal(TimeSpan.FromMinutes(5), options.StaleAfter);
        Assert.Equal(TimeSpan.FromHours(24), options.ExpireAfter);
        Assert.Equal(TimeSpan.FromMinutes(1), options.FailureTtl);
    }

    [Fact]
    public async Task ResolveAsync_Fresh_IsServedFromTheCache()
    {
        var cache = Create();

        var first = await cache.ResolveAsync(Post);
        _clock.Advance(TimeSpan.FromMinutes(4));
        var second = await cache.ResolveAsync(Post);

        Assert.Same(first, second);
        Assert.Equal(1, _inner.Calls);
    }

    [Fact]
    public async Task ResolveAsync_Stale_IsServedWhileRefreshedInTheBackground()
    {
        var cache = Create();
        var first = await cache.ResolveAsync(Post);
        _clock.Advance(TimeSpan.FromMinutes(6));

        var stale = await cache.ResolveAsync(Post);
        await _inner.WaitForCallsAsync(2);
        await WaitUntilAsync(async () => !ReferenceEquals(await cache.ResolveAsync(Post), first));

        Assert.Same(first, stale);
        Assert.Equal(2, _inner.Calls);
    }

    [Fact]
    public async Task ResolveAsync_Expired_IsResolvedBeforeUse()
    {
        var cache = Create();
        var first = await cache.ResolveAsync(Post);
        _clock.Advance(TimeSpan.FromHours(25));

        var second = await cache.ResolveAsync(Post);

        Assert.NotSame(first, second);
        Assert.Equal(2, _inner.Calls);
    }

    [Fact]
    public async Task ResolveAsync_Failure_IsRememberedForTheFailureTtl()
    {
        var cache = Create();
        _inner.Fail = true;

        await Assert.ThrowsAsync<LexiconResolutionException>(() => cache.ResolveAsync(Post));
        var cached = await Assert.ThrowsAsync<LexiconResolutionException>(() => cache.ResolveAsync(Post));
        Assert.Equal(1, _inner.Calls);
        Assert.Equal(LexiconResolutionErrorKind.NotFound, cached.Kind);

        _inner.Fail = false;
        _clock.Advance(TimeSpan.FromMinutes(2));
        await cache.ResolveAsync(Post);
        Assert.Equal(2, _inner.Calls);
    }

    [Fact]
    public async Task ResolveAsync_FailedRefresh_KeepsServingTheStaleSchema()
    {
        var cache = Create();
        var first = await cache.ResolveAsync(Post);
        _inner.Fail = true;
        _clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Same(first, await cache.ResolveAsync(Post));
        await _inner.WaitForCallsAsync(2);
        await WaitUntilAsync(() => Task.FromResult(!cache.IsResolving(Post)));

        // Still served; the next background attempt waits out the failure TTL.
        Assert.Same(first, await cache.ResolveAsync(Post));
        Assert.Equal(2, _inner.Calls);
    }

    [Fact]
    public async Task ResolveAsync_ConcurrentMisses_ShareOneResolution()
    {
        _inner.Gate = new TaskCompletionSource();
        var cache = Create();

        var requests = Enumerable.Range(0, 8).Select(_ => cache.ResolveAsync(Post)).ToList();
        _inner.Gate.SetResult();
        var results = await Task.WhenAll(requests);

        Assert.Equal(1, _inner.Calls);
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public async Task ResolveAsync_CallerGivesUp_ResolutionStillCompletesForTheCache()
    {
        _inner.Gate = new TaskCompletionSource();
        var cache = Create();
        using var cts = new CancellationTokenSource();

        var abandoned = cache.ResolveAsync(Post, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        _inner.Gate.SetResult();
        await WaitUntilAsync(() => Task.FromResult(!cache.IsResolving(Post)));

        await cache.ResolveAsync(Post);
        Assert.Equal(1, _inner.Calls);
    }

    [Fact]
    public async Task InvalidateAsync_DropsTheSchema()
    {
        var cache = Create();
        await cache.ResolveAsync(Post);

        await cache.InvalidateAsync(Post);
        await cache.ResolveAsync(Post);

        Assert.Equal(2, _inner.Calls);
    }

    [Fact]
    public async Task ResolveAsync_OverCapacity_EvictsTheLeastRecentlyUsed()
    {
        var cache = Create(new LexiconCacheOptions { Capacity = 2 });
        var a = Nsid.Parse("com.example.a.one");
        var b = Nsid.Parse("com.example.b.two");
        var c = Nsid.Parse("com.example.c.three");

        await cache.ResolveAsync(a);
        await cache.ResolveAsync(b);
        await cache.ResolveAsync(a);
        await cache.ResolveAsync(c);
        Assert.Equal(2, cache.Count);

        await cache.ResolveAsync(a);
        Assert.Equal(3, _inner.Calls);
        await cache.ResolveAsync(b);
        Assert.Equal(4, _inner.Calls);
    }

    [Fact]
    public void Constructor_ExpireBeforeStale_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new LexiconCacheOptions
        {
            StaleAfter = TimeSpan.FromHours(2),
            ExpireAfter = TimeSpan.FromHours(1),
        }));
    }

    private CachingLexiconResolver Create(LexiconCacheOptions? options = null) =>
        new(_inner, options, _clock);

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

    /// <summary>Resolves every NSID to a new schema object, and counts the calls.</summary>
    private sealed class CountingResolver : ILexiconResolver
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public bool Fail { get; set; }

        public TaskCompletionSource? Gate { get; set; }

        public async Task<ResolvedLexicon> ResolveAsync(Nsid nsid, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            if (Gate is { } gate)
                await gate.Task;

            if (Fail)
                throw new LexiconResolutionException("not published", nsid, LexiconResolutionErrorKind.NotFound);

            return new ResolvedLexicon
            {
                Uri = AtUri.Parse($"at://did:plc:aaaaaaaaaaaaaaaaaaaaaaaa/com.atproto.lexicon.schema/{nsid}"),
                Cid = Cid.Parse("bafyreibleucvt34j2gzwyzpamdn4vcqvwoheqhqdhn57qqivqkgfjwvfdy"),
                Schema = new LexiconSchemaRecord { Lexicon = 1, Id = nsid },
            };
        }

        public async Task WaitForCallsAsync(int count)
        {
            for (var i = 0; i < 200 && Calls < count; i++)
                await Task.Delay(10);
        }
    }
}
