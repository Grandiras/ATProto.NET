using System.Text.Json;
using ATProtoNet.Pds;

namespace ATProtoNet.Tests.Pds;

public class PdsServiceTests
{
    private readonly PdsOptions _options = new()
    {
        Hostname = "test.local",
        PublicUrl = "https://test.local",
        OpenRegistration = true,
        AvailableUserDomains = ["test.local"],
    };

    private PdsService CreateService(
        IAccountStore? accounts = null,
        IRepoStore? repos = null,
        PdsSessionService? sessions = null)
    {
        accounts ??= new InMemoryAccountStore();
        repos ??= new InMemoryRepoStore();
        sessions ??= new PdsSessionService(_options);
        return new PdsService(accounts, repos, sessions, _options);
    }

    // ── Account tests ──

    [Fact]
    public async Task CreateAccountAsync_CreatesAccountAndReturnsTokens()
    {
        var pds = CreateService();
        var result = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        Assert.NotNull(result);
        Assert.StartsWith("did:plc:", result.Did);
        Assert.Equal("alice.test.local", result.Handle);
        Assert.NotEmpty(result.AccessJwt);
        Assert.NotEmpty(result.RefreshJwt);
    }

    [Fact]
    public async Task CreateAccountAsync_WithExplicitDid_UsesProvidedDid()
    {
        var pds = CreateService();
        var result = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123",
            did: "did:plc:custom123");

        Assert.Equal("did:plc:custom123", result.Did);
    }

    [Fact]
    public async Task CreateAccountAsync_DuplicateHandle_Throws()
    {
        var pds = CreateService();
        await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateAccountAsync("alice.test.local", "bob@test.local", "password456"));
        Assert.Equal("HandleNotAvailable", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateAccountAsync_ClosedRegistration_RequiresInviteCode()
    {
        var opts = new PdsOptions { Hostname = "test.local", OpenRegistration = false };
        var pds = new PdsService(new InMemoryAccountStore(), new InMemoryRepoStore(),
            new PdsSessionService(opts), opts);

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123"));
        Assert.Equal("InvalidInviteCode", ex.ErrorCode);
    }

    // ── Session tests ──

    [Fact]
    public async Task CreateSessionAsync_ValidCredentials_ReturnsSession()
    {
        var pds = CreateService();
        await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var session = await pds.CreateSessionAsync("alice.test.local", "password123");

        Assert.Equal("alice.test.local", session.Handle);
        Assert.NotEmpty(session.AccessJwt);
        Assert.NotEmpty(session.RefreshJwt);
    }

    [Fact]
    public async Task CreateSessionAsync_InvalidPassword_Throws()
    {
        var pds = CreateService();
        await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateSessionAsync("alice.test.local", "wrongpassword"));
        Assert.Equal("AuthenticationRequired", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateSessionAsync_UnknownIdentifier_Throws()
    {
        var pds = CreateService();

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.CreateSessionAsync("nobody.test.local", "password123"));
        Assert.Equal("AuthenticationRequired", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateSessionAsync_ByEmail_Works()
    {
        var pds = CreateService();
        await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var session = await pds.CreateSessionAsync("alice@test.local", "password123");
        Assert.Equal("alice.test.local", session.Handle);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsSessionInfo()
    {
        var pds = CreateService();
        var created = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var info = await pds.GetSessionAsync(created.Did);

        Assert.Equal(created.Did, info.Did);
        Assert.Equal("alice.test.local", info.Handle);
        Assert.True(info.Active);
    }

    [Fact]
    public async Task GetSessionAsync_UnknownDid_Throws()
    {
        var pds = CreateService();

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.GetSessionAsync("did:plc:unknown"));
        Assert.Equal("InvalidToken", ex.ErrorCode);
    }

    [Fact]
    public async Task RefreshSessionAsync_ReturnsNewTokens()
    {
        var pds = CreateService();
        var created = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var refreshed = await pds.RefreshSessionAsync(created.Did);

        Assert.Equal(created.Did, refreshed.Did);
        Assert.Equal("alice.test.local", refreshed.Handle);
        Assert.NotEmpty(refreshed.AccessJwt);
        Assert.NotEmpty(refreshed.RefreshJwt);
    }

    [Fact]
    public async Task DeleteAccountAsync_RemovesAccount()
    {
        var pds = CreateService();
        var created = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        await pds.DeleteAccountAsync(created.Did, "password123");

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.GetSessionAsync(created.Did));
        Assert.Equal("InvalidToken", ex.ErrorCode);
    }

    [Fact]
    public async Task DeleteAccountAsync_WrongPassword_Throws()
    {
        var pds = CreateService();
        var created = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.DeleteAccountAsync(created.Did, "wrongpassword"));
        Assert.Equal("AuthenticationRequired", ex.ErrorCode);
    }

    // ── DescribeServer ──

    [Fact]
    public void DescribeServer_ReturnsOptions()
    {
        var pds = CreateService();

        var description = pds.DescribeServer();

        Assert.False(description.InviteCodeRequired);
        Assert.Contains("test.local", description.AvailableUserDomains);
    }

    // ── Record tests ──

    [Fact]
    public async Task CreateRecordAsync_CreatesRecord()
    {
        var pds = CreateService();
        var account = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var record = JsonSerializer.SerializeToElement(new { text = "Hello, world!", createdAt = "2024-01-01T00:00:00Z" });
        var result = await pds.CreateRecordAsync(account.Did, "app.bsky.feed.post", record);

        Assert.StartsWith($"at://{account.Did}/app.bsky.feed.post/", result.Uri);
        Assert.NotEmpty(result.Cid);
    }

    [Fact]
    public async Task GetRecordAsync_ReturnsRecord()
    {
        var pds = CreateService();
        var account = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var record = JsonSerializer.SerializeToElement(new { text = "Hello!" });
        var created = await pds.CreateRecordAsync(account.Did, "app.bsky.feed.post", record, rkey: "test123");

        var result = await pds.GetRecordAsync(account.Did, "app.bsky.feed.post", "test123");

        Assert.NotNull(result);
        Assert.Equal(created.Uri, result!.Uri);
        Assert.Equal(created.Cid, result.Cid);
    }

    [Fact]
    public async Task GetRecordAsync_NotFound_ReturnsNull()
    {
        var pds = CreateService();
        var result = await pds.GetRecordAsync("did:plc:test", "app.bsky.feed.post", "nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task PutRecordAsync_UpsertsRecord()
    {
        var pds = CreateService();
        var account = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var record1 = JsonSerializer.SerializeToElement(new { text = "First" });
        await pds.PutRecordAsync(account.Did, "app.bsky.actor.profile", "self", record1);

        var record2 = JsonSerializer.SerializeToElement(new { text = "Updated" });
        var result = await pds.PutRecordAsync(account.Did, "app.bsky.actor.profile", "self", record2);

        var fetched = await pds.GetRecordAsync(account.Did, "app.bsky.actor.profile", "self");
        Assert.NotNull(fetched);
        Assert.Equal(result.Cid, fetched!.Cid);
    }

    [Fact]
    public async Task DeleteRecordAsync_DeletesRecord()
    {
        var pds = CreateService();
        var account = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var record = JsonSerializer.SerializeToElement(new { text = "Hello!" });
        await pds.CreateRecordAsync(account.Did, "app.bsky.feed.post", record, rkey: "todelete");

        await pds.DeleteRecordAsync(account.Did, "app.bsky.feed.post", "todelete");

        var result = await pds.GetRecordAsync(account.Did, "app.bsky.feed.post", "todelete");
        Assert.Null(result);
    }

    [Fact]
    public async Task ListRecordsAsync_ReturnsPage()
    {
        var pds = CreateService();
        var account = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        for (int i = 0; i < 5; i++)
        {
            var record = JsonSerializer.SerializeToElement(new { text = $"Post {i}" });
            await pds.CreateRecordAsync(account.Did, "app.bsky.feed.post", record, rkey: $"post{i}");
        }

        var page = await pds.ListRecordsAsync(account.Did, "app.bsky.feed.post", limit: 3);

        Assert.Equal(3, page.Records.Count);
        Assert.NotNull(page.Cursor);
    }

    [Fact]
    public async Task ListRecordsAsync_WithCursor_Paginates()
    {
        var pds = CreateService();
        var account = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        for (int i = 0; i < 5; i++)
        {
            var record = JsonSerializer.SerializeToElement(new { text = $"Post {i}" });
            await pds.CreateRecordAsync(account.Did, "app.bsky.feed.post", record, rkey: $"post{i}");
        }

        var page1 = await pds.ListRecordsAsync(account.Did, "app.bsky.feed.post", limit: 3);
        var page2 = await pds.ListRecordsAsync(account.Did, "app.bsky.feed.post", limit: 3, cursor: page1.Cursor);

        Assert.Equal(3, page1.Records.Count);
        Assert.Equal(2, page2.Records.Count);
        Assert.Null(page2.Cursor);
    }

    // ── Blob tests ──

    [Fact]
    public async Task UploadBlobAsync_StoresBlob()
    {
        var pds = CreateService();
        var account = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var result = await pds.UploadBlobAsync(account.Did, data, "image/png");

        Assert.NotEmpty(result.Cid);
        Assert.Equal("image/png", result.MimeType);
        Assert.Equal(4, result.Size);
    }

    [Fact]
    public async Task GetBlobAsync_ReturnsBlob()
    {
        var pds = CreateService();
        var account = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var uploaded = await pds.UploadBlobAsync(account.Did, data, "image/png");

        var blob = await pds.GetBlobAsync(account.Did, uploaded.Cid);

        Assert.NotNull(blob);
        Assert.Equal(data, blob!.Data);
        Assert.Equal("image/png", blob.MimeType);
    }

    [Fact]
    public async Task GetBlobAsync_NotFound_ReturnsNull()
    {
        var pds = CreateService();
        var blob = await pds.GetBlobAsync("did:plc:test", "bafynotfound");
        Assert.Null(blob);
    }

    [Fact]
    public async Task UploadBlobAsync_TooLarge_Throws()
    {
        var opts = new PdsOptions { Hostname = "test.local", MaxBlobSize = 10, OpenRegistration = true };
        var pds = new PdsService(new InMemoryAccountStore(), new InMemoryRepoStore(),
            new PdsSessionService(opts), opts);
        var account = await pds.CreateAccountAsync("alice.test.local", "alice@test.local", "password123");

        var data = new byte[20];
        var ex = await Assert.ThrowsAsync<PdsException>(
            () => pds.UploadBlobAsync(account.Did, data, "application/octet-stream"));
        Assert.Equal("BlobTooLarge", ex.ErrorCode);
    }
}
