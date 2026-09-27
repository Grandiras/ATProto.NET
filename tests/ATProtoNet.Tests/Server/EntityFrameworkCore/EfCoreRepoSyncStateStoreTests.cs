using ATProtoNet.Identity;
using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Streaming;
using ATProtoNet.Tests.Identity;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Server.EntityFrameworkCore;

/// <summary>
/// The contract every <see cref="IRepoSyncStateStore"/> must satisfy, plus the EF store's own
/// ordering and schema guarantees, over SQLite.
/// </summary>
public sealed class EfCoreRepoSyncStateStoreTests : RepoSyncStateStoreContractTests, IAsyncLifetime
{
    private readonly FakeTimeProvider _clock = new();
    private SqliteConnection _connection = null!;
    private DbContextOptions<RepoSyncStateDbContext> _options = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source=reposync-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<RepoSyncStateDbContext>().UseSqlite(_connection).Options;

        await using var context = new RepoSyncStateDbContext(_options);
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    protected override IRepoSyncStateStore CreateStore() => new EfCoreRepoSyncStateStore<RepoSyncStateDbContext>(new Factory(_options), _clock);

    [Fact]
    public async Task ListUnsynchronizedAsync_ReturnsTheOldestFirst_UpToTheLimit()
    {
        var store = CreateStore();
        await store.SetAsync(new RepoSyncState(Bob, null, null, RepoSyncStatus.Desynchronized));
        _clock.Advance(TimeSpan.FromSeconds(1));
        await store.SetAsync(new RepoSyncState(Alice, Rev, Data, RepoSyncStatus.Resynchronizing));
        _clock.Advance(TimeSpan.FromSeconds(1));
        await store.SetAsync(new RepoSyncState(Did.Parse("did:plc:carolcccccccccccccccccc"), Rev, Data, RepoSyncStatus.Synchronized));

        var all = await store.ListUnsynchronizedAsync(10);
        var first = await store.ListUnsynchronizedAsync(1);

        Assert.Equal([Bob, Alice], all.Select(s => s.Did));
        Assert.Equal(Bob, Assert.Single(first).Did);
    }

    [Fact]
    public void ConfigureRepoSyncStateModel_MapsTheTableAndStoresStatusByName()
    {
        using var context = new RepoSyncStateDbContext(_options);
        var entity = context.Model.FindEntityType(typeof(RepoSyncStateEntity))!;

        Assert.Equal("AtProtoRepoSyncStates", entity.GetTableName());
        Assert.Equal([nameof(RepoSyncStateEntity.Did)], entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(typeof(string), entity.FindProperty(nameof(RepoSyncStateEntity.Status))!.GetProviderClrType());
    }

    [Fact]
    public void AddAtProtoEfCoreRepoSyncStateStore_RegistersTheStore()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<RepoSyncStateDbContext>(options => options.UseSqlite(_connection));
        services.AddAtProtoEfCoreRepoSyncStateStore<RepoSyncStateDbContext>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfCoreRepoSyncStateStore<RepoSyncStateDbContext>>(provider.GetRequiredService<IRepoSyncStateStore>());
    }

    private sealed class Factory(DbContextOptions<RepoSyncStateDbContext> options) : IDbContextFactory<RepoSyncStateDbContext>
    {
        public RepoSyncStateDbContext CreateDbContext() => new(options);
    }
}
