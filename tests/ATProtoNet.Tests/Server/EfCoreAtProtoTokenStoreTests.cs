using ATProtoNet.Auth.OAuth;
using ATProtoNet.Server.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Server;

public class EfCoreAtProtoTokenStoreTests : IAsyncLifetime
{
    private IDbContextFactory<AtProtoTokenDbContext> _contextFactory = null!;
    private EfCoreAtProtoTokenStore<AtProtoTokenDbContext> _store = null!;
    private readonly string _dbName = $"AtProtoTokens_{Guid.NewGuid():N}";

    public async ValueTask InitializeAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<AtProtoTokenDbContext>()
            .UseInMemoryDatabase(_dbName);

        _contextFactory = new TestDbContextFactory(optionsBuilder.Options);

        // Ensure the schema is created
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        await ctx.Database.EnsureCreatedAsync();

        var dataProtection = DataProtectionProvider.Create("ATProtoNet.Tests");
        _store = new EfCoreAtProtoTokenStore<AtProtoTokenDbContext>(
            _contextFactory,
            dataProtection,
            NullLogger<EfCoreAtProtoTokenStore<AtProtoTokenDbContext>>.Instance);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static AtProtoTokenData CreateTestTokenData(
        string did = "did:plc:abc123",
        string handle = "alice.bsky.social") => new()
    {
        Did = did,
        Handle = handle,
        AccessToken = "access-token-value",
        RefreshToken = "refresh-token-value",
        PdsUrl = "https://bsky.social",
        Issuer = "https://bsky.social",
        TokenEndpoint = "https://bsky.social/oauth/token",
        DPoPPrivateKey = [10, 20, 30, 40, 50],
        ExpiresIn = 3600,
        Scope = "atproto transition:generic",
    };

    [Fact]
    public async Task StoreAsync_And_GetAsync_RoundTrips()
    {
        var data = CreateTestTokenData();
        await _store.StoreAsync("did:plc:abc123", data);

        var retrieved = await _store.GetAsync("did:plc:abc123");

        Assert.NotNull(retrieved);
        Assert.Equal("did:plc:abc123", retrieved.Did);
        Assert.Equal("alice.bsky.social", retrieved.Handle);
        Assert.Equal("access-token-value", retrieved.AccessToken);
        Assert.Equal("refresh-token-value", retrieved.RefreshToken);
        Assert.Equal("https://bsky.social", retrieved.PdsUrl);
        Assert.Equal("https://bsky.social", retrieved.Issuer);
        Assert.Equal("https://bsky.social/oauth/token", retrieved.TokenEndpoint);
        Assert.Equal(new byte[] { 10, 20, 30, 40, 50 }, retrieved.DPoPPrivateKey);
        Assert.Equal(3600, retrieved.ExpiresIn);
        Assert.Equal("atproto transition:generic", retrieved.Scope);
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
        var data = CreateTestTokenData();
        await _store.StoreAsync("did:plc:abc123", data);

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
    public async Task RemoveAsync_NoOp_WhenNotFound()
    {
        // Should not throw
        await _store.RemoveAsync("did:plc:nonexistent");
    }

    [Fact]
    public async Task StoreAsync_MultipleUsers_Independent()
    {
        var alice = CreateTestTokenData("did:plc:alice", "alice.bsky.social");
        var bob = CreateTestTokenData("did:plc:bob", "bob.bsky.social");

        await _store.StoreAsync("did:plc:alice", alice);
        await _store.StoreAsync("did:plc:bob", bob);

        var retrievedAlice = await _store.GetAsync("did:plc:alice");
        var retrievedBob = await _store.GetAsync("did:plc:bob");

        Assert.NotNull(retrievedAlice);
        Assert.NotNull(retrievedBob);
        Assert.Equal("alice.bsky.social", retrievedAlice.Handle);
        Assert.Equal("bob.bsky.social", retrievedBob.Handle);
    }

    [Fact]
    public async Task StoreAsync_NullDid_Throws()
    {
        var data = CreateTestTokenData();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _store.StoreAsync(null!, data));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _store.StoreAsync("", data));
    }

    [Fact]
    public async Task StoreAsync_NullData_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.StoreAsync("did:plc:abc123", null!));
    }

    [Fact]
    public async Task GetAsync_NullDid_Throws()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _store.GetAsync(null!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _store.GetAsync(""));
    }

    [Fact]
    public async Task Data_IsEncryptedInDatabase()
    {
        var data = CreateTestTokenData();
        await _store.StoreAsync("did:plc:abc123", data);

        // Read directly from the database to verify the data is encrypted
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entity = await ctx.Set<AtProtoTokenEntity>().FindAsync("did:plc:abc123");

        Assert.NotNull(entity);
        // The encrypted data should NOT contain the raw access token
        Assert.DoesNotContain("access-token-value", entity.EncryptedTokenData);
    }

    /// <summary>
    /// Simple IDbContextFactory implementation for testing.
    /// </summary>
    private sealed class TestDbContextFactory(DbContextOptions<AtProtoTokenDbContext> options)
        : IDbContextFactory<AtProtoTokenDbContext>
    {
        public AtProtoTokenDbContext CreateDbContext() => new(options);
    }
}
