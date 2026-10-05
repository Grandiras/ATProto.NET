using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Server.EntityFrameworkCore;

/// <summary>The contract every <see cref="ISpaceAuthorityStore"/> must satisfy, over SQLite.</summary>
public sealed class EfCoreSpaceAuthorityStoreContractTests : SpaceAuthorityStoreContractTests, IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<SpaceDbContext> _options = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source=spaceauthority-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<SpaceDbContext>().UseSqlite(_connection).Options;

        await using var context = new SpaceDbContext(_options);
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    protected override ISpaceAuthorityStore CreateStore() => new EfCoreSpaceAuthorityStore<SpaceDbContext>(new Factory(_options));

    protected override Task DeclareSpaceAsync(ISpaceAuthorityStore store, SpaceUri space) =>
        ((EfCoreSpaceAuthorityStore<SpaceDbContext>)store).DeclareSpaceAsync(space);

    protected override Task MarkDeletedAsync(ISpaceAuthorityStore store, SpaceUri space) =>
        ((EfCoreSpaceAuthorityStore<SpaceDbContext>)store).MarkDeletedAsync(space);

    private sealed class Factory(DbContextOptions<SpaceDbContext> options) : IDbContextFactory<SpaceDbContext>
    {
        public SpaceDbContext CreateDbContext() => new(options);
    }
}

/// <summary>The contract every <see cref="ISimpleSpaceStore"/> must satisfy, over SQLite.</summary>
public sealed class EfCoreSimpleSpaceStoreContractTests : SimpleSpaceStoreContractTests, IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<SpaceDbContext> _options = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source=simplespace-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<SpaceDbContext>().UseSqlite(_connection).Options;

        await using var context = new SpaceDbContext(_options);
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    protected override ISimpleSpaceStore CreateStore() => new EfCoreSimpleSpaceStore<SpaceDbContext>(new Factory(_options));

    private sealed class Factory(DbContextOptions<SpaceDbContext> options) : IDbContextFactory<SpaceDbContext>
    {
        public SpaceDbContext CreateDbContext() => new(options);
    }
}

/// <summary>
/// What the contracts above cannot exercise: SQL-collation edge cases, and that state written
/// through one store instance is read back by another over the same database — the whole reason
/// an EF Core store exists. The plain CRUD behaviour of the authority, simplespace and replay
/// stores is covered by <see cref="EfCoreSpaceAuthorityStoreContractTests"/>,
/// <see cref="EfCoreSimpleSpaceStoreContractTests"/> and
/// <see cref="EfCoreJtiReplayStoreTests"/> (parameterized over <see cref="JtiReplayDbContext"/>,
/// the same store class this file's <see cref="SpaceDbContext"/> also carries).
/// </summary>
public sealed class EfCoreSpaceStoreTests : IAsyncLifetime
{
    private static readonly SpaceUri Space =
        SpaceUri.Parse("at://did:plc:authority/space/com.example.forum/main");

    private SqliteConnection _connection = null!;
    private DbContextOptions<SpaceDbContext> _options = null!;

    public async ValueTask InitializeAsync()
    {
        // A named shared-cache database rather than ":memory:", so every context the factory
        // opens sees the same one for as long as this connection is held open.
        _connection = new SqliteConnection($"Data Source=spaces-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await _connection.OpenAsync();

        _options = new DbContextOptionsBuilder<SpaceDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using var context = new SpaceDbContext(_options);
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private EfCoreSpaceAuthorityStore<SpaceDbContext> Authority() => new(new Factory(_options), TimeProvider.System);

    private EfCoreSimpleSpaceStore<SpaceDbContext> SimpleSpace() => new(new Factory(_options));

    private EfCoreJtiReplayStore<SpaceDbContext> Replay() => new(new Factory(_options), TimeProvider.System);

    [Fact]
    public async Task RecordWriteAsync_UnderACollationThatIsNotOrdinal_StillOrdersRevisionsByTheirBytes()
    {
        // A SQL comparison follows the column's collation, and a TID's order is its bytes' only
        // under some (Danish sorts "aa" after "z"). A reversed collation is the extreme case.
        await using var connection = new SqliteConnection($"Data Source=collated-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        connection.CreateCollation("REVERSED", (x, y) => -string.CompareOrdinal(x, y));
        var options = new DbContextOptionsBuilder<ReversedRevisionContext>().UseSqlite(connection).Options;
        await using (var context = new ReversedRevisionContext(options))
            await context.Database.EnsureCreatedAsync();

        var store = new EfCoreSpaceAuthorityStore<ReversedRevisionContext>(
            new ReversedRevisionFactory(options), TimeProvider.System);
        var alice = Did.Parse("did:plc:alice");

        await store.RecordWriteAsync(Space, alice, Tid.Parse("3kbbbbbbbbbbb"), [2]);
        await store.RecordWriteAsync(Space, alice, Tid.Parse("3kaaaaaaaaaaa"), [1]);
        Assert.Equal("3kbbbbbbbbbbb", Assert.Single((await store.ListReposAsync(Space, 10, null)).Repos).RepoRev);

        await store.RecordWriteAsync(Space, alice, Tid.Parse("3kccccccccccc"), [3]);
        var repo = Assert.Single((await store.ListReposAsync(Space, 10, null)).Repos);
        Assert.Equal("3kccccccccccc", repo.RepoRev);
        Assert.Equal([3], repo.Hash);
    }

    [Fact]
    public async Task ListSubscribersAsync_ReturnsRenewedRegistrations_AndDropsLapsedOnes()
    {
        // The contract only checks that an already-lapsed registration is excluded; this exercises
        // the EF store's own clock injection to show one that lapses while it is being watched.
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-21T12:00:00Z", null));
        var store = new EfCoreSpaceAuthorityStore<SpaceDbContext>(new Factory(_options), clock);

        await store.RegisterNotifyAsync(Space, "did:web:syncer#s", clock.GetUtcNow().AddDays(7));
        await store.RegisterNotifyAsync(Space, "did:web:lapsing#s", clock.GetUtcNow().AddMinutes(5));

        Assert.Equal(2, (await store.ListSubscribersAsync(Space)).Count);

        clock.Advance(TimeSpan.FromHours(1));
        var live = await store.ListSubscribersAsync(Space);

        Assert.Equal("did:web:syncer#s", Assert.Single(live).Service);
    }

    [Fact]
    public async Task WriterSet_SurvivesTheStoreItWasWrittenThrough()
    {
        // The point of the whole exercise: state outlives the process that recorded it, and a
        // second instance reading the same database sees it.
        await Authority().RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kaaaaaaaaaaa"), [7]);

        var repos = await Authority().ListReposAsync(Space, 10, null);

        Assert.Equal("did:plc:alice", Assert.Single(repos.Repos).Did);
    }

    [Fact]
    public async Task MemberList_SurvivesTheStoreItWasWrittenThrough()
    {
        // A member list is never published to the network, so a restart that loses it loses the
        // space's access control with nothing to rebuild it from.
        await SimpleSpace().CreateSpaceAsync(
            new SimpleSpaceRecord(Space, Did.Parse("did:plc:authority"), new MemberListPolicy(), new MemberListPolicy(), new OpenAppAccess()));
        await SimpleSpace().PutMemberAsync(Space, Did.Parse("did:plc:alice"), read: true, write: false);

        var member = await SimpleSpace().GetMemberAsync(Space, Did.Parse("did:plc:alice"));
        Assert.NotNull(member);
        Assert.True(member.Read);
        Assert.False(member.Write);
    }

    [Fact]
    public async Task ReplayStore_CarriedBySpaceDbContext_SpendsAnIdentifierExactlyOnce()
    {
        // The replay store's own behaviour (collision handling, concurrency, sweeping) is proven
        // once by EfCoreJtiReplayStoreTests over JtiReplayDbContext; this only shows the same store
        // class works correctly when SpaceDbContext carries the table instead.
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.True(await Replay().TryConsumeAsync("did:plc:a", "nonce", expiry));
        Assert.False(await Replay().TryConsumeAsync("did:plc:a", "nonce", expiry));
    }

    private sealed class Factory(DbContextOptions<SpaceDbContext> options) : IDbContextFactory<SpaceDbContext>
    {
        public SpaceDbContext CreateDbContext() => new(options);
    }

    /// <summary>The authority model with its revision column under a collation that reverses the byte order.</summary>
    private sealed class ReversedRevisionContext(DbContextOptions<ReversedRevisionContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            SpaceDbContext.ConfigureSpaceAuthorityModel(modelBuilder);
            modelBuilder.Entity<SpaceWriterEntity>().Property(e => e.RepoRev).UseCollation("REVERSED");
        }
    }

    private sealed class ReversedRevisionFactory(DbContextOptions<ReversedRevisionContext> options)
        : IDbContextFactory<ReversedRevisionContext>
    {
        public ReversedRevisionContext CreateDbContext() => new(options);
    }
}
