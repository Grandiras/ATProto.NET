using ATProtoNet.Caching;

namespace ATProtoNet.Tests.Caching;

public class LruCacheTests
{
    [Fact]
    public void Set_OverCapacity_EvictsTheLeastRecentlyUsed()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.TryGetValue("a", out _)); // a is now the most recent

        var evicted = cache.Set("c", 3);

        Assert.Equal(new KeyValuePair<string, int>("b", 2), evicted);
        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryPeek("b", out _));
    }

    [Fact]
    public void TryPeek_DoesNotCountAsAUse()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.TryPeek("a", out _));

        Assert.Equal("a", cache.Set("c", 3)?.Key);
    }

    [Fact]
    public void Set_ExistingKey_ReplacesWithoutEvicting()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);

        Assert.Null(cache.Set("a", 10));
        Assert.True(cache.TryPeek("a", out var value));
        Assert.Equal(10, value);
    }

    [Fact]
    public void GetOrAdd_KeepsAValueAlreadyHeld()
    {
        var cache = new LruCache<string, string>(2);
        cache.Set("a", "first");

        Assert.Equal("first", cache.GetOrAdd("a", "second", out var evicted));
        Assert.Null(evicted);
    }

    [Fact]
    public async Task AddOrUpdate_AWriteWhileTheUpdateIsComputed_LandsAfterItRatherThanBeingOverwritten()
    {
        // The client-attestation cache marks a fetch attempt with AddOrUpdate while a fetch that
        // completes on another thread stores its keys with Set. Read-then-write in two steps would
        // let the stale marker overwrite the keys.
        var cache = new LruCache<string, string>(4);
        cache.Set("client", "marker");
        Task? fetchCompletes = null;

        cache.AddOrUpdate("client", "new marker", existing =>
        {
            fetchCompletes = Task.Run(() => cache.Set("client", "keys"));
            Assert.False(fetchCompletes.Wait(TimeSpan.FromMilliseconds(200)), "The write landed inside the update.");
            return existing + " (retried)";
        });
        await fetchCompletes!;

        Assert.True(cache.TryPeek("client", out var value));
        Assert.Equal("keys", value);
    }

    [Fact]
    public async Task TryGetValue_WhileAWriteHoldsTheLock_DoesNotWaitForIt()
    {
        // The nonce and JWK caches are read on every DPoP request: a hit must not queue behind
        // writers, or behind other readers.
        var cache = new LruCache<string, int>(4);
        cache.Set("hot", 1);
        cache.Set("other", 2);
        Task<bool>? lookup = null;

        cache.AddOrUpdate("other", 0, value =>
        {
            lookup = Task.Run(() => cache.TryGetValue("hot", out _));
            Assert.True(lookup.Wait(TimeSpan.FromSeconds(5)), "The lookup waited for the write.");
            return value + 1;
        });

        Assert.True(await lookup!);
    }

    [Fact]
    public void AddOrUpdate_AbsentKey_AddsTheValue()
    {
        var cache = new LruCache<string, int>(2);

        Assert.Equal(1, cache.AddOrUpdate("a", 1, v => v + 1));
        Assert.Equal(2, cache.AddOrUpdate("a", 1, v => v + 1));
    }

    [Fact]
    public void Remove_WithAValue_RemovesOnlyWhileTheKeyStillHoldsIt()
    {
        var cache = new LruCache<string, string>(2);
        cache.Set("a", "old");
        cache.Set("a", "new");

        Assert.False(cache.Remove("a", "old"));
        Assert.True(cache.Remove("a", "new"));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void RemoveLeastRecent_TakesTheOldestMatch()
    {
        var cache = new LruCache<string, int>(4);
        cache.Set("odd1", 1);
        cache.Set("even", 2);
        cache.Set("odd3", 3);

        Assert.Equal("odd1", cache.RemoveLeastRecent(value => value % 2 == 1)?.Key);
        Assert.Null(cache.RemoveLeastRecent(value => value > 10));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public async Task ConcurrentUse_KeepsTheBoundAndLeavesACacheThatStillEvictsInOrder()
    {
        // Lock-free hits race with locked writes; afterwards the map and the recency list must still agree.
        const int capacity = 64;
        var cache = new LruCache<int, int>(capacity);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(worker => Task.Run(() =>
        {
            var random = new Random(worker);
            for (var i = 0; i < 20_000; i++)
            {
                var key = random.Next(256);
                switch (random.Next(4))
                {
                    case 0: cache.Set(key, key); break;
                    case 1: cache.Remove(key); break;
                    case 2: cache.AddOrUpdate(key, key, value => value); break;
                    default:
                        if (cache.TryGetValue(key, out var value))
                            Assert.Equal(key, value);
                        break;
                }

                Assert.True(cache.Count <= capacity);
            }
        }, TestContext.Current.CancellationToken)));

        // Refill from one thread: each new key evicts exactly the oldest, which only holds if every node
        // the storm left in the map is also in the list, and the other way round.
        for (var key = 1_000; key < 1_000 + capacity; key++)
            cache.Set(key, key);
        Assert.Equal(capacity, cache.Count);
        for (var key = 1_000 + capacity; key < 1_000 + 2 * capacity; key++)
            Assert.Equal(key - capacity, cache.Set(key, key)?.Key);
    }
}
