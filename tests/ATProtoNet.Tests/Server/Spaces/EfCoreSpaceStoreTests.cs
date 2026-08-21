using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Server.EntityFrameworkCore;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// The EF Core space stores, over a real relational provider.
/// </summary>
/// <remarks>
/// SQLite rather than the in-memory provider on purpose: what makes these stores safe across
/// instances is the database enforcing a primary key — a replayed token identifier is detected
/// by its insert failing — and the in-memory provider models none of that.
/// </remarks>
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

    private EfCoreSpaceAuthorityStore<SpaceDbContext> Authority(TimeProvider? clock = null) =>
        new(new Factory(_options), clock ?? TimeProvider.System);

    private EfCoreSimpleSpaceStore<SpaceDbContext> SimpleSpace() => new(new Factory(_options));

    private EfCoreSpaceReplayStore<SpaceDbContext> Replay(TimeProvider? clock = null) =>
        new(new Factory(_options), clock ?? TimeProvider.System);

    // ── the authority store ────────────────────────────────────────────────

    [Fact]
    public async Task GetSpaceStateAsync_UndeclaredSpace_IsNotFound()
    {
        var store = Authority();

        Assert.Equal(SpaceAccessOutcome.SpaceNotFound, await store.GetSpaceStateAsync(Space));
    }

    [Fact]
    public async Task DeclareSpaceAsync_IsIdempotent_AndGrants()
    {
        var store = Authority();

        await store.DeclareSpaceAsync(Space);
        await store.DeclareSpaceAsync(Space);

        Assert.Equal(SpaceAccessOutcome.Granted, await store.GetSpaceStateAsync(Space));
    }

    [Fact]
    public async Task MarkDeletedAsync_KeepsTheSpaceAnswering_SpaceDeleted()
    {
        // A deleted space must not read as "never existed": SpaceDeleted is how a syncer that
        // missed the notification learns to drop its copy.
        var store = Authority();
        await store.DeclareSpaceAsync(Space);

        await store.MarkDeletedAsync(Space);

        Assert.Equal(SpaceAccessOutcome.SpaceDeleted, await store.GetSpaceStateAsync(Space));
    }

    [Fact]
    public async Task RecordWriteAsync_FirstNotification_AddsTheRepoToTheWriterSet()
    {
        var store = Authority();
        await store.DeclareSpaceAsync(Space);

        await store.RecordWriteAsync(Space, "did:plc:alice", "3kaa", [1, 2, 3]);

        var repos = await store.ListReposAsync(Space, 10, null);
        var alice = Assert.Single(repos.Repos);
        Assert.Equal("did:plc:alice", alice.Did);
        Assert.Equal("3kaa", alice.Rev);
        Assert.Equal([1, 2, 3], alice.Hash);
    }

    [Fact]
    public async Task RecordWriteAsync_OlderRevision_DoesNotWalkTheRepoBackwards()
    {
        var store = Authority();
        await store.RecordWriteAsync(Space, "did:plc:alice", "3kbb", [2]);

        await store.RecordWriteAsync(Space, "did:plc:alice", "3kaa", [1]);

        var repos = await store.ListReposAsync(Space, 10, null);
        Assert.Equal("3kbb", Assert.Single(repos.Repos).Rev);
    }

    [Fact]
    public async Task RecordWriteAsync_NewerRevision_Advances()
    {
        var store = Authority();
        await store.RecordWriteAsync(Space, "did:plc:alice", "3kaa", [1]);

        await store.RecordWriteAsync(Space, "did:plc:alice", "3kbb", [2]);

        var repos = await store.ListReposAsync(Space, 10, null);
        var alice = Assert.Single(repos.Repos);
        Assert.Equal("3kbb", alice.Rev);
        Assert.Equal([2], alice.Hash);
    }

    [Fact]
    public async Task ListReposAsync_PagesByDid_AndTheCursorResumesWhereItLeftOff()
    {
        var store = Authority();
        foreach (var did in new[] { "did:plc:c", "did:plc:a", "did:plc:b" })
            await store.RecordWriteAsync(Space, did, "3kaa", [1]);

        var first = await store.ListReposAsync(Space, 2, null);
        Assert.Equal(["did:plc:a", "did:plc:b"], first.Repos.Select(r => r.Did));
        Assert.Equal("did:plc:b", first.Cursor);

        var second = await store.ListReposAsync(Space, 2, first.Cursor);
        Assert.Equal(["did:plc:c"], second.Repos.Select(r => r.Did));
        Assert.Null(second.Cursor);
    }

    [Fact]
    public async Task ListSubscribersAsync_ReturnsRenewedRegistrations_AndDropsLapsedOnes()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-21T12:00:00Z", null));
        var store = Authority(clock);

        await store.RegisterNotifyAsync(Space, "did:web:syncer#s", clock.GetUtcNow().AddDays(7));
        await store.RegisterNotifyAsync(Space, "did:web:lapsing#s", clock.GetUtcNow().AddMinutes(5));

        Assert.Equal(2, (await store.ListSubscribersAsync(Space)).Count);

        clock.Advance(TimeSpan.FromHours(1));
        var live = await store.ListSubscribersAsync(Space);

        Assert.Equal("did:web:syncer#s", Assert.Single(live).Service);
    }

    [Fact]
    public async Task RegisterNotifyAsync_Twice_RenewsRatherThanDuplicates()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-21T12:00:00Z", null));
        var store = Authority(clock);
        var renewed = clock.GetUtcNow().AddDays(7);

        await store.RegisterNotifyAsync(Space, "did:web:syncer#s", clock.GetUtcNow().AddMinutes(1));
        await store.RegisterNotifyAsync(Space, "did:web:syncer#s", renewed);

        var subscriber = Assert.Single(await store.ListSubscribersAsync(Space));
        Assert.Equal(renewed, subscriber.ExpiresAt);
    }

    [Fact]
    public async Task UnregisterNotifyAsync_IsIdempotent()
    {
        var store = Authority();
        await store.RegisterNotifyAsync(Space, "did:web:syncer#s", DateTimeOffset.UtcNow.AddDays(1));

        await store.UnregisterNotifyAsync(Space, "did:web:syncer#s");
        await store.UnregisterNotifyAsync(Space, "did:web:syncer#s");

        Assert.Empty(await store.ListSubscribersAsync(Space));
    }

    [Fact]
    public async Task WriterSet_SurvivesTheStoreItWasWrittenThrough()
    {
        // The point of the whole exercise: state outlives the process that recorded it, and a
        // second instance reading the same database sees it.
        await Authority().RecordWriteAsync(Space, "did:plc:alice", "3kaa", [7]);

        var repos = await Authority().ListReposAsync(Space, 10, null);

        Assert.Equal("did:plc:alice", Assert.Single(repos.Repos).Did);
    }

    // ── the simplespace store ──────────────────────────────────────────────

    [Fact]
    public async Task CreateSpaceAsync_TheSameUriTwice_IsRefused()
    {
        var store = SimpleSpace();
        var record = new SimpleSpaceRecord(Space, "did:plc:authority", new MemberListPolicy(), new OpenAppAccess());

        Assert.True(await store.CreateSpaceAsync(record));
        Assert.False(await store.CreateSpaceAsync(record));
    }

    [Fact]
    public async Task GetSpaceAsync_RoundTripsBothPolicyUnions()
    {
        var store = SimpleSpace();
        await store.CreateSpaceAsync(new SimpleSpaceRecord(
            Space,
            "did:plc:authority",
            new ManagingAppPolicy { ManagingApp = "did:web:forum.example#forum" },
            new AllowListAppAccess { Allowed = ["https://forum.example/client-metadata.json"] }));

        var loaded = await store.GetSpaceAsync(Space);

        Assert.NotNull(loaded);
        Assert.Equal(Space.Value, loaded.Uri.Value);
        Assert.Equal("did:plc:authority", loaded.Owner);
        var policy = Assert.IsType<ManagingAppPolicy>(loaded.Policy);
        Assert.Equal("did:web:forum.example#forum", policy.ManagingApp);
        var access = Assert.IsType<AllowListAppAccess>(loaded.AppAccess);
        Assert.Equal(["https://forum.example/client-metadata.json"], access.Allowed);
        Assert.False(loaded.Deleted);
    }

    [Fact]
    public async Task GetSpaceAsync_UnknownSpace_IsNull()
    {
        Assert.Null(await SimpleSpace().GetSpaceAsync(Space));
    }

    [Fact]
    public async Task UpdateSpaceAsync_ReplacesThePolicy()
    {
        var store = SimpleSpace();
        var record = new SimpleSpaceRecord(Space, "did:plc:authority", new MemberListPolicy(), new OpenAppAccess());
        await store.CreateSpaceAsync(record);

        await store.UpdateSpaceAsync(record with { Policy = new PublicPolicy() });

        var loaded = await store.GetSpaceAsync(Space);
        Assert.IsType<PublicPolicy>(loaded!.Policy);
    }

    [Fact]
    public async Task DeleteSpaceAsync_FlagsRatherThanRemoves()
    {
        var store = SimpleSpace();
        await store.CreateSpaceAsync(
            new SimpleSpaceRecord(Space, "did:plc:authority", new MemberListPolicy(), new OpenAppAccess()));

        await store.DeleteSpaceAsync(Space);
        await store.DeleteSpaceAsync(Space);

        var loaded = await store.GetSpaceAsync(Space);
        Assert.NotNull(loaded);
        Assert.True(loaded.Deleted);
    }

    [Fact]
    public async Task Members_AreAddedRemovedAndQueried_Idempotently()
    {
        var store = SimpleSpace();
        await store.CreateSpaceAsync(
            new SimpleSpaceRecord(Space, "did:plc:authority", new MemberListPolicy(), new OpenAppAccess()));

        await store.AddMemberAsync(Space, "did:plc:alice");
        await store.AddMemberAsync(Space, "did:plc:alice");
        Assert.True(await store.IsMemberAsync(Space, "did:plc:alice"));
        Assert.False(await store.IsMemberAsync(Space, "did:plc:bob"));

        await store.RemoveMemberAsync(Space, "did:plc:alice");
        await store.RemoveMemberAsync(Space, "did:plc:alice");
        Assert.False(await store.IsMemberAsync(Space, "did:plc:alice"));
    }

    [Fact]
    public async Task AddMemberAsync_ForASpaceThatDoesNotExist_IsANoOp()
    {
        var store = SimpleSpace();

        await store.AddMemberAsync(Space, "did:plc:alice");

        Assert.False(await store.IsMemberAsync(Space, "did:plc:alice"));
    }

    [Fact]
    public async Task ListMembersAsync_PagesByDid_AndTheCursorResumesWhereItLeftOff()
    {
        var store = SimpleSpace();
        await store.CreateSpaceAsync(
            new SimpleSpaceRecord(Space, "did:plc:authority", new MemberListPolicy(), new OpenAppAccess()));
        foreach (var did in new[] { "did:plc:c", "did:plc:a", "did:plc:b" })
            await store.AddMemberAsync(Space, did);

        var first = await store.ListMembersAsync(Space, 2, null);
        Assert.Equal(["did:plc:a", "did:plc:b"], first.Members.Select(m => m.Did));
        Assert.Equal("did:plc:b", first.Cursor);

        var second = await store.ListMembersAsync(Space, 2, first.Cursor);
        Assert.Equal(["did:plc:c"], second.Members.Select(m => m.Did));
        Assert.Null(second.Cursor);
    }

    [Fact]
    public async Task MemberList_SurvivesTheStoreItWasWrittenThrough()
    {
        // A member list is never published to the network, so a restart that loses it loses the
        // space's access control with nothing to rebuild it from.
        await SimpleSpace().CreateSpaceAsync(
            new SimpleSpaceRecord(Space, "did:plc:authority", new MemberListPolicy(), new OpenAppAccess()));
        await SimpleSpace().AddMemberAsync(Space, "did:plc:alice");

        Assert.True(await SimpleSpace().IsMemberAsync(Space, "did:plc:alice"));
    }

    // ── the replay store ───────────────────────────────────────────────────

    [Fact]
    public async Task TryConsumeAsync_SpendsAnIdentifierExactlyOnce()
    {
        var store = Replay();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.True(await store.TryConsumeAsync("did:plc:a", "nonce", expiry));
        Assert.False(await store.TryConsumeAsync("did:plc:a", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_AcrossTwoStoreInstances_StillSpendsItOnce()
    {
        // The whole reason this store exists: two instances behind a load balancer share one
        // table, so the second presentation of a captured delegation token is refused by the
        // instance that never saw the first.
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.True(await Replay().TryConsumeAsync("did:plc:a", "nonce", expiry));
        Assert.False(await Replay().TryConsumeAsync("did:plc:a", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_SameNonceFromAnotherIssuer_IsNotACollision()
    {
        var store = Replay();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);
        await store.TryConsumeAsync("did:plc:a", "nonce", expiry);

        Assert.True(await store.TryConsumeAsync("did:plc:b", "nonce", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_ConcurrentPresentations_YieldExactlyOneSuccess()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(1);
        var stores = Enumerable.Range(0, 8).Select(_ => Replay()).ToList();

        var results = await Task.WhenAll(
            stores.Select(store => store.TryConsumeAsync("did:plc:a", "nonce", expiry).AsTask()));

        Assert.Equal(1, results.Count(consumed => consumed));
    }

    [Fact]
    public async Task TryConsumeAsync_ExpiredEntries_AreSweptOut()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-21T12:00:00Z", null));
        var store = Replay(clock);

        await store.TryConsumeAsync("did:plc:a", "short", clock.GetUtcNow().AddSeconds(30));
        Assert.Equal(1, await CountReplayEntriesAsync());

        // Past both the entry's expiry and the sweep interval.
        clock.Advance(TimeSpan.FromMinutes(2));
        await store.TryConsumeAsync("did:plc:a", "fresh", clock.GetUtcNow().AddMinutes(1));

        Assert.Equal(1, await CountReplayEntriesAsync());
    }

    private async Task<int> CountReplayEntriesAsync()
    {
        await using var context = new SpaceDbContext(_options);
        return await context.AtProtoSpaceReplay.CountAsync();
    }

    private sealed class Factory(DbContextOptions<SpaceDbContext> options) : IDbContextFactory<SpaceDbContext>
    {
        public SpaceDbContext CreateDbContext() => new(options);
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
