using ATProtoNet.Auth.OAuth;
using ATProtoNet.Server.TokenStore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Server;

public class FileAtProtoTokenStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileAtProtoTokenStore _store;

    public FileAtProtoTokenStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "atproto-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        var dataProtection = DataProtectionProvider.Create("ATProtoNet.Tests");
        var options = new FileTokenStoreOptions { Directory = _testDir };
        _store = new FileAtProtoTokenStore(dataProtection, NullLogger<FileAtProtoTokenStore>.Instance, options);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private static AtProtoTokenData CreateTestTokenData(string did = "did:plc:abc123", string handle = "alice.bsky.social") => new()
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
    public async Task StoredData_PersistsToFile()
    {
        var data = CreateTestTokenData();
        await _store.StoreAsync("did:plc:abc123", data);

        // Verify a file was created in the directory
        var files = Directory.GetFiles(_testDir, "*.dat");
        Assert.Single(files);
    }

    [Fact]
    public async Task StoredData_IsEncrypted()
    {
        var data = CreateTestTokenData();
        await _store.StoreAsync("did:plc:abc123", data);

        var files = Directory.GetFiles(_testDir, "*.dat");
        var content = await File.ReadAllTextAsync(files[0]);

        // Content should not contain plaintext tokens
        Assert.DoesNotContain("access-token-value", content);
        Assert.DoesNotContain("refresh-token-value", content);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenFileCorrupted()
    {
        var data = CreateTestTokenData();
        await _store.StoreAsync("did:plc:abc123", data);

        // Corrupt the file
        var files = Directory.GetFiles(_testDir, "*.dat");
        await File.WriteAllTextAsync(files[0], "corrupted data");

        var result = await _store.GetAsync("did:plc:abc123");
        Assert.Null(result); // Should handle gracefully, not throw
    }

    [Fact]
    public async Task StoreAsync_ThrowsOnNullDid()
    {
        var data = CreateTestTokenData();
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.StoreAsync(null!, data));
    }

    [Fact]
    public async Task StoreAsync_ThrowsOnNullData()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.StoreAsync("did:plc:abc123", null!));
    }

    [Fact]
    public async Task CreatesDirectoryIfNotExists()
    {
        var newDir = Path.Combine(_testDir, "nested", "subdir");
        var dataProtection = DataProtectionProvider.Create("ATProtoNet.Tests");
        var options = new FileTokenStoreOptions { Directory = newDir };
        var store = new FileAtProtoTokenStore(dataProtection, NullLogger<FileAtProtoTokenStore>.Instance, options);

        var data = CreateTestTokenData();
        await store.StoreAsync("did:plc:abc123", data);

        Assert.True(Directory.Exists(newDir));
        var retrieved = await store.GetAsync("did:plc:abc123");
        Assert.NotNull(retrieved);
    }

    [Fact]
    public async Task SameData_ReadableByNewStoreInstance()
    {
        // Store data with the first instance
        var data = CreateTestTokenData();
        await _store.StoreAsync("did:plc:abc123", data);

        // Create a new store instance with same Data Protection provider and directory
        var dataProtection = DataProtectionProvider.Create("ATProtoNet.Tests");
        var options = new FileTokenStoreOptions { Directory = _testDir };
        var newStore = new FileAtProtoTokenStore(dataProtection, NullLogger<FileAtProtoTokenStore>.Instance, options);

        var retrieved = await newStore.GetAsync("did:plc:abc123");
        Assert.NotNull(retrieved);
        Assert.Equal("did:plc:abc123", retrieved.Did);
        Assert.Equal("access-token-value", retrieved.AccessToken);
    }
}
