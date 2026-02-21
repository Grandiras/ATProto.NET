using ATProtoNet.Auth.OAuth;
using ATProtoNet.Server.TokenStore;

namespace ATProtoNet.Tests.Auth.OAuth;

public class InMemoryAtProtoTokenStoreTests
{
    private readonly InMemoryAtProtoTokenStore _store = new();

    private static AtProtoTokenData CreateTestTokenData(string did = "did:plc:abc123", string handle = "alice.bsky.social") => new()
    {
        Did = did,
        Handle = handle,
        AccessToken = "access-token",
        PdsUrl = "https://bsky.social",
        Issuer = "https://bsky.social",
        TokenEndpoint = "https://bsky.social/oauth/token",
        DPoPPrivateKey = [1, 2, 3, 4, 5],
    };

    [Fact]
    public async Task StoreAsync_And_GetAsync_RoundTrips()
    {
        var data = CreateTestTokenData();
        await _store.StoreAsync("did:plc:abc123", data);

        var retrieved = await _store.GetAsync("did:plc:abc123");

        Assert.NotNull(retrieved);
        Assert.Equal("did:plc:abc123", retrieved.Did);
        Assert.Equal("access-token", retrieved.AccessToken);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _store.GetAsync("did:plc:nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task StoreAsync_OverwritesExistingData()
    {
        var original = CreateTestTokenData();
        await _store.StoreAsync("did:plc:abc123", original);

        var updated = CreateTestTokenData();
        updated.AccessToken = "new-access-token";
        await _store.StoreAsync("did:plc:abc123", updated);

        var retrieved = await _store.GetAsync("did:plc:abc123");
        Assert.Equal("new-access-token", retrieved!.AccessToken);
    }

    [Fact]
    public async Task RemoveAsync_RemovesStoredData()
    {
        var data = CreateTestTokenData();
        await _store.StoreAsync("did:plc:abc123", data);
        await _store.RemoveAsync("did:plc:abc123");

        var result = await _store.GetAsync("did:plc:abc123");
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow_WhenDidNotFound()
    {
        await _store.RemoveAsync("did:plc:nonexistent"); // Should not throw
    }

    [Fact]
    public async Task MultipleUsers_AreIsolated()
    {
        var alice = CreateTestTokenData("did:plc:alice", "alice.bsky.social");
        var bob = CreateTestTokenData("did:plc:bob", "bob.bsky.social");

        await _store.StoreAsync("did:plc:alice", alice);
        await _store.StoreAsync("did:plc:bob", bob);

        var retrievedAlice = await _store.GetAsync("did:plc:alice");
        var retrievedBob = await _store.GetAsync("did:plc:bob");

        Assert.Equal("alice.bsky.social", retrievedAlice!.Handle);
        Assert.Equal("bob.bsky.social", retrievedBob!.Handle);
    }

    [Fact]
    public async Task StoreAsync_ThrowsOnNullDid()
    {
        var data = CreateTestTokenData();
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.StoreAsync(null!, data));
    }

    [Fact]
    public async Task StoreAsync_ThrowsOnEmptyDid()
    {
        var data = CreateTestTokenData();
        await Assert.ThrowsAsync<ArgumentException>(() => _store.StoreAsync("", data));
    }

    [Fact]
    public async Task StoreAsync_ThrowsOnWhitespaceDid()
    {
        var data = CreateTestTokenData();
        await Assert.ThrowsAsync<ArgumentException>(() => _store.StoreAsync("   ", data));
    }

    [Fact]
    public async Task StoreAsync_ThrowsOnNullData()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.StoreAsync("did:plc:abc123", null!));
    }

    [Fact]
    public async Task GetAsync_ThrowsOnNullDid()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.GetAsync(null!));
    }

    [Fact]
    public async Task RemoveAsync_ThrowsOnNullDid()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.RemoveAsync(null!));
    }
}
