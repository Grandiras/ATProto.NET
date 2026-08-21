using ATProtoNet.Server.Redis;
using StackExchange.Redis;

namespace ATProtoNet.IntegrationTests;

/// <summary>
/// <see cref="RedisSpaceReplayStore"/> against a real server.
/// </summary>
/// <remarks>
/// What makes this store worth having is a property of Redis rather than of the C# around it:
/// <c>SET … NX</c> is atomic, so two service instances presented the same single-use token see
/// exactly one success between them. A substitute can only show which command was issued — this
/// shows what the command does.
/// </remarks>
public class RedisReplayStoreTests : IAsyncLifetime
{
    private readonly string _prefix = $"atproto-test:{Guid.NewGuid():N}:";
    private ConnectionMultiplexer? _connection;

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(TestConfig.RedisUrl))
            return;

        _connection = await ConnectionMultiplexer.ConnectAsync(TestConfig.RedisUrl);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    [RequiresRedisFact]
    public async Task TryConsumeAsync_AcrossTwoInstances_SpendsTheTokenOnce()
    {
        // Two stores over one Redis, standing in for two replicas behind a load balancer: the
        // second presentation of a captured delegation token is refused by an instance that never
        // saw the first.
        var first = Store();
        var second = Store();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.True(await first.TryConsumeAsync("did:plc:a", "nonce", expiry));
        Assert.False(await second.TryConsumeAsync("did:plc:a", "nonce", expiry));
    }

    [RequiresRedisFact]
    public async Task TryConsumeAsync_ConcurrentPresentations_YieldExactlyOneSuccess()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);
        var stores = Enumerable.Range(0, 16).Select(_ => Store()).ToList();

        var results = await Task.WhenAll(
            stores.Select(store => store.TryConsumeAsync("did:plc:a", "race", expiry).AsTask()));

        Assert.Equal(1, results.Count(consumed => consumed));
    }

    [RequiresRedisFact]
    public async Task TryConsumeAsync_SameNonceFromAnotherIssuer_IsNotACollision()
    {
        var store = Store();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);
        await store.TryConsumeAsync("did:plc:a", "shared-nonce", expiry);

        Assert.True(await store.TryConsumeAsync("did:plc:b", "shared-nonce", expiry));
    }

    [RequiresRedisFact]
    public async Task TryConsumeAsync_EntryExpiresWithTheToken()
    {
        // The TTL is what keeps the key space bounded without a sweep. Once the entry is gone the
        // identifier is free again — by which point the token itself is rejected on its expiry.
        var store = Store();
        var expiry = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.True(await store.TryConsumeAsync("did:plc:a", "short", expiry));
        Assert.False(await store.TryConsumeAsync("did:plc:a", "short", expiry));

        await Task.Delay(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken);

        Assert.True(await store.TryConsumeAsync("did:plc:a", "short", expiry));
    }

    [RequiresRedisFact]
    public async Task TryConsumeAsync_SetsTheKeysTimeToLive()
    {
        var store = Store();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(2);

        await store.TryConsumeAsync("did:plc:a", "ttl", expiry);

        // The key layout is part of what the store documents: (iss, jti, exp) under the prefix.
        var key = $"{_prefix}did:plc:a|ttl|{expiry.ToUnixTimeSeconds()}";
        var ttl = await Database().KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(2));
    }

    private RedisSpaceReplayStore Store() => new(Database(), _prefix);

    private IDatabase Database() =>
        (_connection ?? throw new InvalidOperationException("No Redis connection.")).GetDatabase();
}
