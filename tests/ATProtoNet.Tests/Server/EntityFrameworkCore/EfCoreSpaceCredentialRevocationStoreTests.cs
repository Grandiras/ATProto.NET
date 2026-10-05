using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Tests.Server.Spaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Server.EntityFrameworkCore;

/// <summary>The revocation contract over SQLite, plus what only a shared database can show.</summary>
public sealed class EfCoreSpaceCredentialRevocationStoreTests : SpaceCredentialRevocationStoreContractTests, IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<SpaceDbContext> _options = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source=revocation-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await _connection.OpenAsync();

        _options = new DbContextOptionsBuilder<SpaceDbContext>().UseSqlite(_connection).Options;

        await using var context = new SpaceDbContext(_options);
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    protected override ISpaceCredentialRevocationStore CreateStore() =>
        new EfCoreSpaceCredentialRevocationStore<SpaceDbContext>(new Factory(_options));

    [Fact]
    public async Task IsRevokedAsync_AcrossTwoStoreInstances_SeesTheRevocation()
    {
        await CreateStore().RevokeAsync(Space, ["a"], Now.AddMinutes(60));

        Assert.True(await CreateStore().IsRevokedAsync(Space, "a", Now));
    }

    [Fact]
    public async Task RevokeAsync_SweepsExpiredRows_AndOnlyThose()
    {
        var clock = new FakeTimeProvider(Now);
        var store = new EfCoreSpaceCredentialRevocationStore<SpaceDbContext>(new Factory(_options), clock);
        await store.RevokeAsync(Space, ["expired"], Now.AddMinutes(30));
        await store.RevokeAsync(Space, ["kept"], Now.AddMinutes(90));

        clock.Advance(TimeSpan.FromMinutes(31));
        await store.RevokeAsync(Space, ["new"], clock.GetUtcNow().AddMinutes(60));
        await store.LastSweep;

        await using var context = new SpaceDbContext(_options);
        Assert.Equal(["kept", "new"], context.AtProtoSpaceCredentialRevocations.Select(e => e.CredentialId).OrderBy(id => id).ToList());
    }

    [Fact]
    public void ConfigureSpaceCredentialRevocationModel_KeysRowsOnSpaceAndCredential()
    {
        using var context = new SpaceDbContext(_options);
        var entity = context.Model.FindEntityType(typeof(SpaceCredentialRevocationEntity))!;

        Assert.Equal("AtProtoSpaceCredentialRevocations", entity.GetTableName());
        Assert.Equal(
            [nameof(SpaceCredentialRevocationEntity.Space), nameof(SpaceCredentialRevocationEntity.CredentialId)],
            entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
    }

    private sealed class Factory(DbContextOptions<SpaceDbContext> options) : IDbContextFactory<SpaceDbContext>
    {
        public SpaceDbContext CreateDbContext() => new(options);
    }
}
