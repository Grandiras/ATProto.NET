using ATProtoNet.Auth;
using ATProtoNet.Identity;
using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Tests.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Server;

public class EfCoreAtProtoSessionStoreTests : IAsyncLifetime
{
    private readonly IDataProtectionProvider _dataProtection = DataProtectionProvider.Create("ATProtoNet.Tests");
    private IDbContextFactory<AtProtoTokenDbContext> _contextFactory = null!;
    private EfCoreAtProtoSessionStore<AtProtoTokenDbContext> _store = null!;
    private readonly string _dbName = $"AtProtoTokens_{Guid.NewGuid():N}";

    public async ValueTask InitializeAsync()
    {
        var optionsBuilder = new DbContextOptionsBuilder<AtProtoTokenDbContext>()
            .UseInMemoryDatabase(_dbName);

        _contextFactory = new TestDbContextFactory(optionsBuilder.Options);

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        await ctx.Database.EnsureCreatedAsync();

        _store = new EfCoreAtProtoSessionStore<AtProtoTokenDbContext>(
            _contextFactory,
            _dataProtection,
            NullLogger<EfCoreAtProtoSessionStore<AtProtoTokenDbContext>>.Instance);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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
    public async Task RemoveAsync_NoOp_WhenNotFound()
    {
        await _store.RemoveAsync(Did.Parse("did:plc:nonexistent"));
    }

    [Fact]
    public async Task Accounts_AreIndependent()
    {
        await _store.SetAsync(TestSession("did:plc:alice") with { AccessToken = "alice" });
        await _store.SetAsync(TestSession("did:plc:bob") with { AccessToken = "bob" });

        Assert.Equal("alice", Assert.IsType<OAuthSession>(await _store.GetAsync(Did.Parse("did:plc:alice"))).AccessToken);
        Assert.Equal("bob", Assert.IsType<OAuthSession>(await _store.GetAsync(Did.Parse("did:plc:bob"))).AccessToken);
    }

    [Fact]
    public async Task SetAsync_NullSession_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _store.SetAsync(null!));
    }

    [Fact]
    public async Task GetAsync_NullDid_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _store.GetAsync(null!));
    }

    [Fact]
    public async Task Session_IsEncryptedInTheExistingTable()
    {
        await _store.SetAsync(TestSession());

        // The same entity and table as 0.6: one row per DID, the session in EncryptedTokenData.
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entity = await ctx.Set<AtProtoTokenEntity>().FindAsync("did:plc:alice");

        Assert.NotNull(entity);
        Assert.Equal("AtProtoTokens", ctx.Model.FindEntityType(typeof(AtProtoTokenEntity))!.GetTableName());
        Assert.DoesNotContain("access-token-value", entity.EncryptedTokenData);
    }

    [Fact]
    public async Task GetAsync_ARowWrittenBy06_ReadsAsAnOAuthSession()
    {
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

        await using (var ctx = await _contextFactory.CreateDbContextAsync())
        {
            ctx.Set<AtProtoTokenEntity>().Add(new AtProtoTokenEntity
            {
                Did = "did:plc:alice",
                EncryptedTokenData = _dataProtection.CreateProtector("ATProtoNet.TokenStore").Protect(legacy),
            });
            await ctx.SaveChangesAsync();
        }

        var session = Assert.IsType<OAuthSession>(await _store.GetAsync(Alice));

        Assert.Equal("legacy-access", session.AccessToken);
        Assert.Equal("legacy-refresh", session.RefreshToken);
    }

    [Fact]
    public void AddAtProtoEfCoreSessionStore_RegistersTheStore()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_contextFactory)
            .AddAtProtoEfCoreSessionStore<AtProtoTokenDbContext>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreAtProtoSessionStore<AtProtoTokenDbContext>>(provider.GetRequiredService<IAtProtoSessionStore>());
    }

    private sealed class TestDbContextFactory(DbContextOptions<AtProtoTokenDbContext> options)
        : IDbContextFactory<AtProtoTokenDbContext>
    {
        public AtProtoTokenDbContext CreateDbContext() => new(options);
    }
}
