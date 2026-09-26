using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Tests.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Server.Authentication;

public class InMemoryJtiReplayStoreTests
{
    [Fact]
    public async Task TryConsumeAsync_FirstUse_Succeeds()
    {
        var store = new InMemoryJtiReplayStore();

        Assert.True(await store.TryConsumeAsync("did:plc:a", "nonce", DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public async Task TryConsumeAsync_SecondUse_Fails()
    {
        var store = new InMemoryJtiReplayStore();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        await store.TryConsumeAsync("did:plc:a", "nonce", expiry);

        Assert.False(await store.TryConsumeAsync("did:plc:a", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_SameNonceFromAnotherIssuer_IsNotACollision()
    {
        // Entries are keyed on (issuer, jti, expiry): two issuers picking the same nonce are not
        // the same token, and one must not be able to burn the other's.
        var store = new InMemoryJtiReplayStore();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        await store.TryConsumeAsync("did:plc:a", "nonce", expiry);

        Assert.True(await store.TryConsumeAsync("did:plc:b", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_ExpiredEntries_AreSweptOut()
    {
        var clock = new ManualClock();
        var store = new InMemoryJtiReplayStore(clock);

        await store.TryConsumeAsync("did:plc:a", "short", clock.GetUtcNow().AddSeconds(30));
        Assert.Equal(1, store.Count);

        // Past both the entry's expiry and the sweep interval.
        clock.Advance(TimeSpan.FromMinutes(2));
        await store.TryConsumeAsync("did:plc:a", "fresh", clock.GetUtcNow().AddMinutes(1));

        Assert.Equal(1, store.Count);
    }
}

/// <summary>
/// The EF Core store over its own context, for a service with service auth and no space server.
/// </summary>
public sealed class EfCoreJtiReplayStoreTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<JtiReplayDbContext> _options = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source=jti-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await _connection.OpenAsync();

        _options = new DbContextOptionsBuilder<JtiReplayDbContext>().UseSqlite(_connection).Options;

        await using var context = new JtiReplayDbContext(_options);
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private EfCoreJtiReplayStore<JtiReplayDbContext> Store() => new(new Factory(_options));

    [Fact]
    public async Task TryConsumeAsync_AcrossTwoStoreInstances_SpendsAnIdentifierOnce()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.True(await Store().TryConsumeAsync("did:plc:a", "nonce", expiry));
        Assert.False(await Store().TryConsumeAsync("did:plc:a", "nonce", expiry));
        Assert.True(await Store().TryConsumeAsync("did:plc:b", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_EntryDueLaterWithinTheSecond_SurvivesTheSweep()
    {
        // Rows hold whole seconds. An entry that must outlive 60.5 s is stored as 60; a sweep at
        // 60.2 s that deleted "60 or earlier" dropped it while its token was still accepted.
        var clock = new ManualClock();
        var start = clock.GetUtcNow();
        var store = new EfCoreJtiReplayStore<JtiReplayDbContext>(new Factory(_options), clock);
        clock.Advance(TimeSpan.FromSeconds(60.2));
        var retainUntil = start.AddSeconds(60.5);

        Assert.True(await store.TryConsumeAsync("did:plc:a", "nonce", retainUntil));
        Assert.False(await store.TryConsumeAsync("did:plc:a", "nonce", retainUntil));
    }

    [Fact]
    public void ConfigureJtiReplayModel_MapsTheAtProtoJtiReplayTable()
    {
        using var context = new JtiReplayDbContext(_options);
        var entity = context.Model.FindEntityType(typeof(JtiReplayEntity))!;

        Assert.Equal("AtProtoJtiReplay", entity.GetTableName());
        Assert.Equal(
            [nameof(JtiReplayEntity.Issuer), nameof(JtiReplayEntity.TokenId), nameof(JtiReplayEntity.ExpiresAt)],
            entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
    }

    [Fact]
    public void SpaceDbContext_CarriesTheSameTable()
    {
        using var context = new SpaceDbContext(new DbContextOptionsBuilder<SpaceDbContext>().UseSqlite(_connection).Options);

        Assert.Equal("AtProtoJtiReplay", context.Model.FindEntityType(typeof(JtiReplayEntity))!.GetTableName());
    }

    private sealed class Factory(DbContextOptions<JtiReplayDbContext> options) : IDbContextFactory<JtiReplayDbContext>
    {
        public JtiReplayDbContext CreateDbContext() => new(options);
    }
}

/// <summary>
/// The shared store wins over the in-process default whichever way round the registrations go,
/// and one store serves service auth and the space server alike.
/// </summary>
public class JtiReplayStoreRegistrationTests
{
    [Fact]
    public void AddAtProtoEfCoreJtiReplayStore_AfterAddAtProtoServiceAuth_Wins()
    {
        var services = Services();
        services.AddAuthentication().AddAtProtoServiceAuth(o => o.Audiences.Add("did:web:example.com#svc"));
        services.AddAtProtoEfCoreJtiReplayStore<JtiReplayDbContext>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreJtiReplayStore<JtiReplayDbContext>>(provider.GetRequiredService<IJtiReplayStore>());
    }

    [Fact]
    public void AddAtProtoEfCoreJtiReplayStore_BeforeAddAtProtoServiceAuth_Wins()
    {
        var services = Services();
        services.AddAtProtoEfCoreJtiReplayStore<JtiReplayDbContext>();
        services.AddAuthentication().AddAtProtoServiceAuth(o => o.Audiences.Add("did:web:example.com#svc"));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreJtiReplayStore<JtiReplayDbContext>>(provider.GetRequiredService<IJtiReplayStore>());
    }

    [Fact]
    public void ServiceAuthAndSpaces_ShareOneStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtProtoSpaces();
        services.AddAuthentication().AddAtProtoServiceAuth(o => o.Audiences.Add("did:web:example.com#svc"));

        using var provider = services.BuildServiceProvider();

        Assert.Single(services, d => d.ServiceType == typeof(IJtiReplayStore));
        Assert.IsType<InMemoryJtiReplayStore>(provider.GetRequiredService<IJtiReplayStore>());
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<JtiReplayDbContext>(
            options => options.UseInMemoryDatabase($"registration-{Guid.NewGuid():N}"));

        return services;
    }
}
