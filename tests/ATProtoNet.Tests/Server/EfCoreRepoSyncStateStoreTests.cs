using ATProtoNet.Identity;
using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Streaming;
using ATProtoNet.Tests.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Server;

/// <summary>The EF Core repo sync state store, over SQLite.</summary>
public sealed class EfCoreRepoSyncStateStoreTests : IAsyncLifetime
{
    private static readonly Did Alice = Did.Parse("did:plc:aliceaaaaaaaaaaaaaaaaaaa");
    private static readonly Did Bob = Did.Parse("did:plc:bobbbbbbbbbbbbbbbbbbbbbb");
    private static readonly Tid Rev = Tid.Parse("3mwgncrvwtj2t");
    private static readonly Cid Data = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm");

    private readonly ManualClock _clock = new();
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

    private EfCoreRepoSyncStateStore<RepoSyncStateDbContext> Store() => new(new Factory(_options), _clock);

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTrips()
    {
        var state = new RepoSyncState(Alice, Rev, Data, RepoSyncStatus.Synchronized);

        await Store().SetAsync(state);

        Assert.Equal(state, await Store().GetAsync(Alice));
        Assert.Null(await Store().GetAsync(Bob));
    }

    [Fact]
    public async Task SetAsync_ExistingRow_IsReplaced()
    {
        await Store().SetAsync(new RepoSyncState(Alice, Rev, Data, RepoSyncStatus.Synchronized));
        var desynchronized = new RepoSyncState(Alice, Tid.Parse("3mwgncs2p2324"), Data, RepoSyncStatus.Desynchronized);

        await Store().SetAsync(desynchronized);

        Assert.Equal(desynchronized, await Store().GetAsync(Alice));
    }

    [Fact]
    public async Task SetAsync_UnknownRevisionAndTree_RoundTrip()
    {
        var marked = new RepoSyncState(Alice, null, null, RepoSyncStatus.Desynchronized);

        await Store().SetAsync(marked);

        Assert.Equal(marked, await Store().GetAsync(Alice));
    }

    [Fact]
    public async Task RemoveAsync_ForgetsTheRepository()
    {
        await Store().SetAsync(new RepoSyncState(Alice, Rev, Data, RepoSyncStatus.Synchronized));

        await Store().RemoveAsync(Alice);
        await Store().RemoveAsync(Bob);

        Assert.Null(await Store().GetAsync(Alice));
    }

    [Fact]
    public async Task ListUnsynchronizedAsync_ReturnsTheOldestFirst_UpToTheLimit()
    {
        var store = Store();
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
