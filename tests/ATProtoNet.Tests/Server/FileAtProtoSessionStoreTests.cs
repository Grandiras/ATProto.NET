using ATProtoNet.Auth;
using ATProtoNet.Identity;
using ATProtoNet.Server.TokenStore;
using ATProtoNet.Tests.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Server;

public class FileAtProtoSessionStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly IDataProtectionProvider _dataProtection = DataProtectionProvider.Create("ATProtoNet.Tests");
    private readonly FileAtProtoSessionStore _store;

    public FileAtProtoSessionStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "atproto-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _store = NewStore(_testDir);
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

    private FileAtProtoSessionStore NewStore(string directory) =>
        new(_dataProtection, NullLogger<FileAtProtoSessionStore>.Instance, new FileSessionStoreOptions { Directory = directory });

    private static OAuthSession TestSession(string did = "did:plc:alice") =>
        OAuthSession([10, 20, 30, 40, 50], accessToken: "access-token-value", refreshToken: "refresh-token-value")
            with { Did = Did.Parse(did) };

    [Fact]
    public async Task SetAsync_And_GetAsync_RoundTrips()
    {
        var session = TestSession();
        await _store.SetAsync(session);

        var retrieved = Assert.IsType<OAuthSession>(await _store.GetAsync(Alice));

        Assert.Equal(session with { DPoPKey = retrieved.DPoPKey }, retrieved);
        Assert.Equal(new byte[] { 10, 20, 30, 40, 50 }, retrieved.DPoPKey.ToArray());
    }

    [Fact]
    public async Task APasswordSession_RoundTrips()
    {
        var session = PasswordSession("access", "refresh");
        await _store.SetAsync(session);

        Assert.Equal(session, await _store.GetAsync(Alice));
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotFound()
    {
        Assert.Null(await _store.GetAsync(Did.Parse("did:plc:nonexistent")));
    }

    [Fact]
    public async Task SetAsync_ReplacesTheStoredSession()
    {
        await _store.SetAsync(TestSession());
        await _store.SetAsync(TestSession() with { AccessToken = "new-access-token" });

        var retrieved = Assert.IsType<OAuthSession>(await _store.GetAsync(Alice));
        Assert.Equal("new-access-token", retrieved.AccessToken);
    }

    [Fact]
    public async Task RemoveAsync_RemovesTheSession()
    {
        await _store.SetAsync(TestSession());
        await _store.RemoveAsync(Alice);

        Assert.Null(await _store.GetAsync(Alice));
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow_WhenNotFound()
    {
        await _store.RemoveAsync(Did.Parse("did:plc:nonexistent"));
    }

    [Fact]
    public async Task Accounts_AreIsolated()
    {
        await _store.SetAsync(TestSession("did:plc:alice") with { AccessToken = "alice" });
        await _store.SetAsync(TestSession("did:plc:bob") with { AccessToken = "bob" });

        Assert.Equal("alice", Assert.IsType<OAuthSession>(await _store.GetAsync(Did.Parse("did:plc:alice"))).AccessToken);
        Assert.Equal("bob", Assert.IsType<OAuthSession>(await _store.GetAsync(Did.Parse("did:plc:bob"))).AccessToken);
    }

    [Fact]
    public async Task StoredSession_IsOneEncryptedFile()
    {
        await _store.SetAsync(TestSession());

        var file = Assert.Single(Directory.GetFiles(_testDir, "*.dat"));
        var content = await File.ReadAllTextAsync(file);
        Assert.DoesNotContain("access-token-value", content);
        Assert.DoesNotContain("refresh-token-value", content);
    }

    [Fact]
    public async Task GetAsync_CorruptedFile_ReturnsNullAndRemovesIt()
    {
        await _store.SetAsync(TestSession());
        var file = Assert.Single(Directory.GetFiles(_testDir, "*.dat"));
        await File.WriteAllTextAsync(file, "corrupted data");

        Assert.Null(await _store.GetAsync(Alice));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task GetAsync_AFileWrittenBy06_ReadsAsAnOAuthSession()
    {
        // What FileAtProtoTokenStore 0.6 wrote: the same file name and Data Protection purpose,
        // over camel-cased AtProtoTokenData.
        await _store.SetAsync(TestSession());
        var file = Assert.Single(Directory.GetFiles(_testDir, "*.dat"));
        var legacy = SessionTests.Legacy06TokenData.Serialize(new SessionTests.Legacy06TokenData
        {
            Did = "did:plc:alice",
            Handle = "alice.test",
            IsHandleVerified = true,
            AccessToken = "legacy-access",
            RefreshToken = "legacy-refresh",
            PdsUrl = "https://pds.example.com",
            Issuer = Issuer,
            TokenEndpoint = TokenEndpoint.ToString(),
            DPoPPrivateKey = [1, 2, 3],
        });
        await File.WriteAllTextAsync(file, _dataProtection.CreateProtector("ATProtoNet.TokenStore").Protect(legacy));

        var session = Assert.IsType<OAuthSession>(await _store.GetAsync(Alice));

        Assert.Equal("legacy-access", session.AccessToken);
        Assert.Equal("legacy-refresh", session.RefreshToken);
        Assert.Equal(AliceHandle, session.Handle);
    }

    [Fact]
    public async Task SetAsync_ThrowsOnNullSession()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _store.SetAsync(null!));
    }

    [Fact]
    public async Task CreatesDirectoryIfNotExists()
    {
        var newDir = Path.Combine(_testDir, "nested", "subdir");
        var store = NewStore(newDir);

        await store.SetAsync(TestSession());

        Assert.True(Directory.Exists(newDir));
        Assert.NotNull(await store.GetAsync(Alice));
    }

    [Fact]
    public async Task ReadsDuringRewrites_AlwaysSeeAWholeSession()
    {
        // Reads take no lock: a reader must still never see a missing or half-written file while
        // the same account's session is being replaced.
        await _store.SetAsync(TestSession() with { AccessToken = "v0" });
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var writers = Enumerable.Range(1, 4).Select(w => Task.Run(async () =>
        {
            for (var i = 0; !stop.IsCancellationRequested; i++)
                await _store.SetAsync(TestSession() with { AccessToken = $"w{w}-{i}" });
        }));
        var readers = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            var reads = 0;
            while (!stop.IsCancellationRequested)
            {
                var session = Assert.IsType<OAuthSession>(await _store.GetAsync(Alice));
                Assert.StartsWith(session.AccessToken == "v0" ? "v0" : "w", session.AccessToken);
                reads++;
            }
            return reads;
        })).ToList();

        await Task.WhenAll(writers);
        Assert.All(await Task.WhenAll(readers), reads => Assert.True(reads > 0));
        Assert.Single(Directory.GetFiles(_testDir, "*.dat"));
        Assert.Empty(Directory.GetFiles(_testDir, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentWritesAndRemovals_OfOneAccount_LeaveOneOutcome()
    {
        await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(async () =>
        {
            if (i % 2 == 0)
                await _store.SetAsync(TestSession() with { AccessToken = $"t{i}" });
            else
                await _store.RemoveAsync(Alice);
        })));

        // Whatever the order, the store holds a whole session or none, and nothing half-written.
        var stored = await _store.GetAsync(Alice);
        Assert.True(stored is null || ((OAuthSession)stored).AccessToken.StartsWith('t'));
        Assert.Empty(Directory.GetFiles(_testDir, "*.tmp"));
    }

    [Fact]
    public async Task StoredSession_IsReadableByANewStoreInstance()
    {
        await _store.SetAsync(TestSession());

        var retrieved = Assert.IsType<OAuthSession>(await NewStore(_testDir).GetAsync(Alice));

        Assert.Equal("access-token-value", retrieved.AccessToken);
    }
}
