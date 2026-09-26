using ATProtoNet.Auth;
using ATProtoNet.Identity;
using ATProtoNet.Server;
using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Tests.Auth;
using ATProtoNet.Tests.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Server.EntityFrameworkCore;

public class EfCoreAtProtoSessionStoreTests : SessionStoreContractTests, IAsyncLifetime
{
    private readonly IDataProtectionProvider _dataProtection = DataProtectionProvider.Create("ATProtoNet.Tests");
    private SqliteConnection _connection = null!;
    private IDbContextFactory<AtProtoTokenDbContext> _contextFactory = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source=tokens-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AtProtoTokenDbContext>().UseSqlite(_connection).Options;
        _contextFactory = new TestDbContextFactory(options);

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        await ctx.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    protected override IAtProtoSessionStore CreateStore() => new EfCoreAtProtoSessionStore<AtProtoTokenDbContext>(
        _contextFactory,
        _dataProtection,
        NullLogger<EfCoreAtProtoSessionStore<AtProtoTokenDbContext>>.Instance);

    private static OAuthSession TestSession(string did = "did:plc:alice") =>
        OAuthSession([10, 20, 30, 40, 50], accessToken: "access-token-value", refreshToken: "refresh-token-value")
            with { Did = Did.Parse(did) };

    [Fact]
    public async Task Session_IsEncryptedInTheExistingTable()
    {
        await CreateStore().SetAsync(TestSession());

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

        var session = Assert.IsType<OAuthSession>(await CreateStore().GetAsync(Alice));

        Assert.Equal("legacy-access", session.AccessToken);
        Assert.Equal("legacy-refresh", session.RefreshToken);
    }

    [Fact]
    public async Task WithEfCoreSessionStore_ReplacesTheDefaultStoreEitherWayRound()
    {
        foreach (var efFirst in new[] { true, false })
        {
            var services = new ServiceCollection().AddLogging().AddSingleton(_contextFactory);
            var atproto = services.AddAtProto();
            if (efFirst)
                atproto.WithEfCoreSessionStore<AtProtoTokenDbContext>().WithClientFactory();
            else
                atproto.WithClientFactory().WithEfCoreSessionStore<AtProtoTokenDbContext>();

            await using var provider = services.BuildServiceProvider();

            var store = Assert.IsType<EfCoreAtProtoSessionStore<AtProtoTokenDbContext>>(
                provider.GetRequiredService<IAtProtoSessionStore>());
            Assert.Single(services, d => d.ServiceType == typeof(IAtProtoSessionStore));

            // It works over the registered context: what it writes, it reads back.
            await store.SetAsync(PasswordSession("access", "refresh"));
            Assert.NotNull(await store.GetAsync(Alice));
            await store.RemoveAsync(Alice);
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<AtProtoTokenDbContext> options)
        : IDbContextFactory<AtProtoTokenDbContext>
    {
        public AtProtoTokenDbContext CreateDbContext() => new(options);
    }
}
