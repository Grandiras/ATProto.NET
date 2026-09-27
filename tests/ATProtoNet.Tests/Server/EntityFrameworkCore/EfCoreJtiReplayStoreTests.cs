using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Tests.Identity;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Server.EntityFrameworkCore;

/// <summary>
/// The EF Core store over its own context, for a service with service auth and no space server.
/// </summary>
public sealed class EfCoreJtiReplayStoreTests : JtiReplayStoreContractTests, IAsyncLifetime
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

    protected override IJtiReplayStore CreateStore() => new EfCoreJtiReplayStore<JtiReplayDbContext>(new Factory(_options));

    [Fact]
    public async Task TryConsumeAsync_AcrossTwoStoreInstances_SpendsAnIdentifierOnce()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.True(await CreateStore().TryConsumeAsync("did:plc:a", "nonce", expiry));
        Assert.False(await CreateStore().TryConsumeAsync("did:plc:a", "nonce", expiry));
        Assert.True(await CreateStore().TryConsumeAsync("did:plc:b", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_EntryDueLaterWithinTheSecond_SurvivesTheSweep()
    {
        // Rows hold whole seconds. An entry that must outlive 60.5 s is stored as 60; a sweep at
        // 60.2 s that deleted "60 or earlier" dropped it while its token was still accepted.
        // The sweep that consumption starts must have finished before the replay is checked, and
        // must have run: it drops the entry that expired at 10 s.
        var clock = new FakeTimeProvider();
        var start = clock.GetUtcNow();
        var store = new EfCoreJtiReplayStore<JtiReplayDbContext>(new Factory(_options), clock);
        await store.TryConsumeAsync("did:plc:a", "old", start.AddSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(60.2));
        var retainUntil = start.AddSeconds(60.5);

        Assert.True(await store.TryConsumeAsync("did:plc:a", "nonce", retainUntil));
        await store.LastSweep;

        await using (var context = new JtiReplayDbContext(_options))
            Assert.Equal(1, await context.AtProtoJtiReplay.CountAsync());
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
