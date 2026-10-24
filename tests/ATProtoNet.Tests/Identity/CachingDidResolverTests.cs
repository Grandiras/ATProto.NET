using ATProtoNet.Identity;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Identity;

public sealed class CachingDidResolverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Did Alice = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Did Bob = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");

    private readonly FakeTimeProvider _clock = new();
    private readonly StubDidResolver _inner;
    private int _version;
    private IdentityResolutionError? _fail;
    private Exception? _throw;
    private TaskCompletionSource? _gate;

    public CachingDidResolverTests() => _inner = new(async did =>
    {
        if (_gate is { } gate) await gate.Task;
        if (_throw is { } exception) throw exception;
        if (_fail is { } error) throw new IdentityResolutionException(error, "scripted", did);
        return Docs.Parse(did.Value, handle: $"v{_version}.example.com");
    });

    private CachingDidResolver Create(DidCacheOptions? options = null, DictionaryCache? shared = null) => new(_inner, options, shared, _clock);

    // A background fetch stores its result on the thread pool; wait for it without a clock.
    private static async Task Eventually(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100_000; i++)
        {
            if (await condition()) return;
            await Task.Yield();
        }
        Assert.Fail("The condition never held.");
    }

    [Fact]
    [Trait("Spec", "IDN-37")]
    public async Task ResolveAsync_Fresh_IsServedFromMemory()
    {
        var cache = Create();
        var first = await cache.ResolveAsync(Alice, cancellationToken: Ct);
        _clock.Advance(TimeSpan.FromMinutes(59));

        Assert.Same(first, await cache.ResolveAsync(Alice, cancellationToken: Ct));
        Assert.Equal(1, _inner.Calls(Alice));
    }

    [Fact]
    [Trait("Spec", "IDN-37")]
    public async Task ResolveAsync_Stale_ServesTheCachedDocumentAndRefreshesInTheBackground()
    {
        var cache = Create();
        var original = await cache.ResolveAsync(Alice, cancellationToken: Ct);
        _version++;
        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Same(original, await cache.ResolveAsync(Alice, cancellationToken: Ct));
        await _inner.Reached(Alice, 2);
        await Eventually(async () => (await cache.ResolveAsync(Alice, cancellationToken: Ct)).GetHandle()!.Value == "v1.example.com");
        Assert.Equal(2, _inner.Calls(Alice));
    }

    [Fact]
    [Trait("Spec", "IDN-37")]
    public async Task ResolveAsync_HardLifetime_NeverServesADocumentPastIt()
    {
        var lifetime = TimeSpan.FromMinutes(5);
        var cache = Create(new() { StaleAfter = lifetime, ExpireAfter = lifetime });
        var original = await cache.ResolveAsync(Alice, cancellationToken: Ct);
        _clock.Advance(lifetime - TimeSpan.FromSeconds(1));
        Assert.Same(original, await cache.ResolveAsync(Alice, cancellationToken: Ct));

        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.NotSame(original, await cache.ResolveAsync(Alice, cancellationToken: Ct));
        Assert.Equal(2, _inner.Calls(Alice));
    }

    [Fact]
    [Trait("Spec", "IDN-38")]
    public async Task ResolveAsync_StaleRefreshFails_KeepsServingTheDocumentAndWaitsBeforeRetrying()
    {
        var cache = Create();
        var original = await cache.ResolveAsync(Alice, cancellationToken: Ct);
        _fail = IdentityResolutionError.Unavailable;
        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Same(original, await cache.ResolveAsync(Alice, cancellationToken: Ct));
        await _inner.Reached(Alice, 2);
        // The failed refresh pushes the next attempt out by the failure TTL; RefreshAsync's floor shows when it landed.
        await Eventually(async () =>
        {
            try
            {
                return ReferenceEquals(await cache.RefreshAsync(Alice, cancellationToken: Ct), original);
            }
            catch (IdentityResolutionException)
            {
                return false; // joined the failing refresh before it landed
            }
        });
        Assert.Same(original, await cache.ResolveAsync(Alice, cancellationToken: Ct));
        Assert.Equal(2, _inner.Calls(Alice));

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Same(original, await cache.ResolveAsync(Alice, cancellationToken: Ct));
        await _inner.Reached(Alice, 3);
    }

    [Fact]
    [Trait("Spec", "IDN-38")]
    public async Task ResolveAsync_Failure_IsRememberedForTheFailureTtl()
    {
        var cache = Create();
        _fail = IdentityResolutionError.NotFound;

        var first = await Assert.ThrowsAsync<IdentityResolutionException>(() => cache.ResolveAsync(Alice, cancellationToken: Ct));
        var second = await Assert.ThrowsAsync<IdentityResolutionException>(() => cache.ResolveAsync(Alice, cancellationToken: Ct));

        Assert.Equal((IdentityResolutionError.NotFound, IdentityResolutionError.NotFound), (first.Error, second.Error));
        Assert.NotSame(first, second);
        Assert.Equal(1, _inner.Calls(Alice));
        _fail = null;
        _clock.Advance(TimeSpan.FromMinutes(1));
        await cache.ResolveAsync(Alice, cancellationToken: Ct);
        Assert.Equal(2, _inner.Calls(Alice));
    }

    [Fact]
    [Trait("Spec", "IDN-38")]
    public async Task ResolveAsync_UnexpectedException_IsNotRemembered()
    {
        var cache = Create();
        _throw = new InvalidOperationException("bug");
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.ResolveAsync(Alice, cancellationToken: Ct));
        _throw = null;

        await cache.ResolveAsync(Alice, cancellationToken: Ct);

        Assert.Equal(2, _inner.Calls(Alice));
    }

    [Fact]
    [Trait("Spec", "IDN-38")]
    public async Task ResolveAsync_ManyFailures_DoNotEvictDocuments()
    {
        var cache = Create(new() { Capacity = 2, FailureCapacity = 2 });
        await cache.ResolveAsync(Alice, cancellationToken: Ct);
        _fail = IdentityResolutionError.NotFound;

        for (var i = 0; i < 10; i++)
            await Assert.ThrowsAsync<IdentityResolutionException>(() => cache.ResolveAsync(Did.Parse($"did:plc:bogus{i:D19}"), cancellationToken: Ct));

        _fail = null;
        await cache.ResolveAsync(Alice, cancellationToken: Ct);
        Assert.Equal(1, _inner.Calls(Alice));
    }

    [Fact]
    [Trait("Spec", "IDN-39")]
    public async Task ResolveAsync_ConcurrentMisses_ShareOneFetch()
    {
        var cache = Create();
        _gate = new();

        var callers = Enumerable.Range(0, 32).Select(_ => cache.ResolveAsync(Alice, cancellationToken: Ct)).ToArray();
        await _inner.Reached(Alice, 1);
        _gate.SetResult();
        var documents = await Task.WhenAll(callers);

        Assert.Equal(1, _inner.Calls(Alice));
        Assert.All(documents, d => Assert.Same(documents[0], d));
    }

    [Fact]
    [Trait("Spec", "IDN-39")]
    public async Task ResolveAsync_OneCallerCancels_TheSharedFetchCompletesForTheOthers()
    {
        var cache = Create();
        _gate = new();
        using var cts = new CancellationTokenSource();

        var cancelled = cache.ResolveAsync(Alice, cts.Token);
        var patient = cache.ResolveAsync(Alice, cancellationToken: Ct);
        await _inner.Reached(Alice, 1);
        await cts.CancelAsync();
        _gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(Alice, (await patient).Id);
        Assert.Equal(1, _inner.Calls(Alice));
    }

    [Fact]
    [Trait("Spec", "IDN-41")]
    public async Task InvalidateAsync_DuringAFetch_DiscardsItsResult()
    {
        var cache = Create();
        _gate = new();
        var inFlight = cache.ResolveAsync(Alice, cancellationToken: Ct);
        await _inner.Reached(Alice, 1);

        await cache.InvalidateAsync(Alice, cancellationToken: Ct);
        _gate.SetResult();
        await inFlight;
        _gate = null;
        await cache.ResolveAsync(Alice, cancellationToken: Ct);

        Assert.Equal(2, _inner.Calls(Alice));
    }

    [Fact]
    [Trait("Spec", "IDN-40")]
    public async Task RefreshAsync_AfterTheMinimumInterval_FetchesPastTheCache()
    {
        var cache = Create();
        var original = await cache.ResolveAsync(Alice, cancellationToken: Ct);
        _clock.Advance(TimeSpan.FromSeconds(30));

        var refreshed = await cache.RefreshAsync(Alice, cancellationToken: Ct);

        Assert.NotSame(original, refreshed);
        Assert.Same(refreshed, await cache.ResolveAsync(Alice, cancellationToken: Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-40")]
    public async Task RefreshAsync_WithinTheMinimumInterval_DoesNotFetchAgain()
    {
        var cache = Create();
        var original = await cache.ResolveAsync(Alice, cancellationToken: Ct);

        for (var i = 0; i < 10; i++)
            Assert.Same(original, await cache.RefreshAsync(Alice, cancellationToken: Ct));

        Assert.Equal(1, _inner.Calls(Alice));
    }

    [Fact]
    [Trait("Spec", "IDN-40")]
    public async Task RefreshAsync_AfterAFailedRefresh_TheFloorStillHolds()
    {
        var cache = Create();
        await cache.ResolveAsync(Alice, cancellationToken: Ct);
        _clock.Advance(TimeSpan.FromSeconds(30));
        _fail = IdentityResolutionError.Unavailable;
        await Assert.ThrowsAsync<IdentityResolutionException>(() => cache.RefreshAsync(Alice, cancellationToken: Ct));

        for (var i = 0; i < 5; i++)
            await cache.RefreshAsync(Alice, cancellationToken: Ct);

        Assert.Equal(2, _inner.Calls(Alice));
    }

    [Fact]
    [Trait("Spec", "IDN-40")]
    public async Task FetchFreshAsync_IsNotRateLimitedAndReplacesTheCachedDocument()
    {
        var cache = Create();
        await cache.ResolveAsync(Alice, cancellationToken: Ct);
        _version++;

        var fresh = await cache.FetchFreshAsync(Alice, cancellationToken: Ct);
        await cache.FetchFreshAsync(Alice, cancellationToken: Ct);

        Assert.Equal("v1.example.com", fresh.GetHandle()!.Value);
        Assert.Equal(3, _inner.Calls(Alice));
        Assert.Equal("v1.example.com", (await cache.ResolveAsync(Alice, cancellationToken: Ct)).GetHandle()!.Value);
    }

    [Fact]
    [Trait("Spec", "IDN-43")]
    public async Task ResolveAsync_OverCapacity_EvictsTheLeastRecentlyUsed()
    {
        var cache = Create(new() { Capacity = 2 });
        await cache.ResolveAsync(Alice, cancellationToken: Ct);
        await cache.ResolveAsync(Bob, cancellationToken: Ct);
        await cache.ResolveAsync(Alice, cancellationToken: Ct);
        await cache.ResolveAsync(Did.Parse("did:plc:cccccccccccccccccccccccc"), cancellationToken: Ct);

        await cache.ResolveAsync(Alice, cancellationToken: Ct);
        await cache.ResolveAsync(Bob, cancellationToken: Ct);

        Assert.Equal((1, 2), (_inner.Calls(Alice), _inner.Calls(Bob)));
    }

    [Fact]
    [Trait("Spec", "IDN-42")]
    public async Task DistributedCache_ASecondInstanceReadsWhatTheFirstFetched()
    {
        var shared = new DictionaryCache();
        await Create(shared: shared).ResolveAsync(Alice, cancellationToken: Ct);
        await shared.WriteLanded.Task;

        var document = await Create(shared: shared).ResolveAsync(Alice, cancellationToken: Ct);

        Assert.Equal(Alice, document.Id);
        Assert.Equal(1, _inner.Calls(Alice));
    }

    [Fact]
    [Trait("Spec", "IDN-42")]
    public async Task DistributedCache_EntryUnderTheWrongKey_IsIgnored()
    {
        var shared = new DictionaryCache();
        await Create(shared: shared).ResolveAsync(Bob, cancellationToken: Ct);
        await shared.WriteLanded.Task;
        shared.Set("atproto:did:" + Alice.Value, shared.Get("atproto:did:" + Bob.Value)!, new());

        var document = await Create(shared: shared).ResolveAsync(Alice, cancellationToken: Ct);

        Assert.Equal(Alice, document.Id);
        Assert.Equal(1, _inner.Calls(Alice));
    }

    [Fact]
    [Trait("Spec", "IDN-42")]
    public async Task DistributedCache_Failing_FallsBackToFetching()
    {
        var cache = Create(shared: new DictionaryCache { Fail = new IOException("redis down") });

        Assert.Equal(Alice, (await cache.ResolveAsync(Alice, cancellationToken: Ct)).Id);
    }

    [Fact]
    [Trait("Spec", "IDN-41")]
    public async Task InvalidateAsync_WhileTheDistributedWriteIsInFlight_TheOldDocumentDoesNotSurvive()
    {
        var shared = new DictionaryCache { HoldWrites = new() };
        var cache = Create(shared: shared);
        await cache.ResolveAsync(Alice, cancellationToken: Ct);
        await shared.WriteStarted.Task;

        await cache.InvalidateAsync(Alice, cancellationToken: Ct);
        shared.HoldWrites.SetResult();
        await shared.WriteLanded.Task;

        await Eventually(() => Task.FromResult(shared.Get("atproto:did:" + Alice.Value) is null));
    }

    [Fact]
    [Trait("Spec", "IDN-41")]
    public async Task DistributedCache_SlowReadOverlappingAFreshFetch_DoesNotRestoreTheOldDocument()
    {
        var shared = new DictionaryCache();
        await Create(shared: shared).ResolveAsync(Alice, cancellationToken: Ct);
        await shared.WriteLanded.Task;
        shared.HoldReads = new();
        var cache = Create(shared: shared);
        var slow = cache.ResolveAsync(Alice, cancellationToken: Ct);
        await shared.ReadStarted.Task;
        _version++;

        var fresh = await cache.FetchFreshAsync(Alice, cancellationToken: Ct);
        shared.HoldReads.SetResult();
        await slow;

        Assert.Equal("v1.example.com", fresh.GetHandle()!.Value);
        Assert.Equal("v1.example.com", (await cache.ResolveAsync(Alice, cancellationToken: Ct)).GetHandle()!.Value);
    }

    [Fact]
    [Trait("Spec", "IDN-38")]
    public async Task ResolveAsync_RememberedFailure_KeepsTheFetchErrorAsItsInnerException()
    {
        var cache = Create();
        _throw = new IdentityResolutionException(IdentityResolutionError.Unavailable, "down", Alice, new TimeoutException());

        await Assert.ThrowsAsync<IdentityResolutionException>(() => cache.ResolveAsync(Alice, cancellationToken: Ct));
        var remembered = await Assert.ThrowsAsync<IdentityResolutionException>(() => cache.ResolveAsync(Alice, cancellationToken: Ct));

        Assert.IsType<TimeoutException>(remembered.InnerException);
    }

    [Fact]
    [Trait("Spec", "IDN-37")]
    public async Task RefreshAsync_WithinTheFloorPastTheLifetime_FetchesRatherThanServingAnExpiredDocument()
    {
        var cache = Create();
        await cache.ResolveAsync(Alice, cancellationToken: Ct);
        _clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(10));
        _fail = IdentityResolutionError.Unavailable;
        await Assert.ThrowsAsync<IdentityResolutionException>(() => cache.RefreshAsync(Alice, cancellationToken: Ct));
        _fail = null;
        _version++;
        _clock.Advance(TimeSpan.FromSeconds(15));

        Assert.Equal("v1.example.com", (await cache.RefreshAsync(Alice, cancellationToken: Ct)).GetHandle()!.Value);
    }

    [Fact]
    [Trait("Spec", "IDN-37")]
    public void Options_ExpireBeforeStale_AreRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new() { StaleAfter = TimeSpan.FromHours(2), ExpireAfter = TimeSpan.FromHours(1) }));
}
