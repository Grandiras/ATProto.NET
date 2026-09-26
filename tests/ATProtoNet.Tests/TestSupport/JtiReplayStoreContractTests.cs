using ATProtoNet.Server.Authentication;

namespace ATProtoNet.Tests.TestSupport;

/// <summary>
/// Behaviour every <see cref="IJtiReplayStore"/> implementation must have. A derived class
/// supplies <see cref="CreateStore"/>; keep anything implementation-specific (the sweep's
/// internal timing hooks, schema, cross-instance sharing) in that derived class instead.
/// </summary>
public abstract class JtiReplayStoreContractTests
{
    protected abstract IJtiReplayStore CreateStore();

    [Fact]
    public async Task TryConsumeAsync_FirstUse_Succeeds()
    {
        var store = CreateStore();

        Assert.True(await store.TryConsumeAsync("did:plc:a", "nonce", DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public async Task TryConsumeAsync_SecondUse_Fails()
    {
        var store = CreateStore();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);
        await store.TryConsumeAsync("did:plc:a", "nonce", expiry);

        Assert.False(await store.TryConsumeAsync("did:plc:a", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_SameNonceFromAnotherIssuer_IsNotACollision()
    {
        // Entries are keyed on (issuer, jti, expiry): two issuers picking the same nonce are not
        // the same token, and one must not be able to burn the other's.
        var store = CreateStore();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);
        await store.TryConsumeAsync("did:plc:a", "nonce", expiry);

        Assert.True(await store.TryConsumeAsync("did:plc:b", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_ConcurrentPresentations_YieldExactlyOneSuccess()
    {
        var store = CreateStore();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => store.TryConsumeAsync("did:plc:a", "nonce", expiry).AsTask()));

        Assert.Equal(1, results.Count(consumed => consumed));
    }
}
