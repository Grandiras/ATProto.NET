using ATProtoNet.Identity;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.TestSupport;

/// <summary>
/// Behaviour every <see cref="ISpaceAuthorityStore"/> implementation must have. A derived class
/// supplies <see cref="CreateStore"/> and the two ways a bespoke space gets declared and deleted
/// (a sync call on <c>InMemorySpaceAuthorityStore</c>, an async one on the EF Core store); keep
/// anything implementation-specific (schema, collation, cross-restart persistence) in that
/// derived class instead.
/// </summary>
public abstract class SpaceAuthorityStoreContractTests
{
    protected static readonly SpaceUri Space = SpaceUri.Parse("at://did:plc:authority/space/com.example.forum/main");

    protected abstract ISpaceAuthorityStore CreateStore();

    /// <summary>Declares a bespoke space directly to the authority store, bypassing simplespace.</summary>
    protected abstract Task DeclareSpaceAsync(ISpaceAuthorityStore store, SpaceUri space);

    /// <summary>Marks a bespoke space deleted directly on the authority store.</summary>
    protected abstract Task MarkDeletedAsync(ISpaceAuthorityStore store, SpaceUri space);

    [Fact]
    public async Task GetSpaceStateAsync_UndeclaredSpace_IsNotFound()
    {
        var store = CreateStore();

        Assert.Equal(SpaceAccessOutcome.SpaceNotFound, await store.GetSpaceStateAsync(Space));
    }

    [Fact]
    public async Task DeclareSpaceAsync_IsIdempotent_AndGrants()
    {
        var store = CreateStore();

        await DeclareSpaceAsync(store, Space);
        await DeclareSpaceAsync(store, Space);

        Assert.Equal(SpaceAccessOutcome.Granted, await store.GetSpaceStateAsync(Space));
    }

    [Fact]
    public async Task MarkDeletedAsync_KeepsTheSpaceAnswering_SpaceDeleted()
    {
        // A deleted space must not read as "never existed": SpaceDeleted is how a syncer that
        // missed the notification learns to drop its copy.
        var store = CreateStore();
        await DeclareSpaceAsync(store, Space);

        await MarkDeletedAsync(store, Space);

        Assert.Equal(SpaceAccessOutcome.SpaceDeleted, await store.GetSpaceStateAsync(Space));
    }

    [Fact]
    public async Task RecordWriteAsync_FirstNotification_AddsTheRepoToTheWriterSet()
    {
        var store = CreateStore();

        await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kaaaaaaaaaaa"), [1, 2, 3]);

        var repos = await store.ListReposAsync(Space, 10, null);
        var alice = Assert.Single(repos.Repos);
        Assert.Equal("did:plc:alice", alice.Did);
        Assert.Equal("3kaaaaaaaaaaa", alice.RepoRev);
        Assert.Equal([1, 2, 3], alice.Hash);
    }

    [Fact]
    public async Task RecordWriteAsync_OlderRevision_DoesNotWalkTheRepoBackwards()
    {
        var store = CreateStore();
        await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kbbbbbbbbbbb"), [2]);

        await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kaaaaaaaaaaa"), [1]);

        var repos = await store.ListReposAsync(Space, 10, null);
        Assert.Equal("3kbbbbbbbbbbb", Assert.Single(repos.Repos).RepoRev);
    }

    [Fact]
    public async Task RecordWriteAsync_NewerRevision_Advances()
    {
        var store = CreateStore();
        await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kaaaaaaaaaaa"), [1]);

        await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kbbbbbbbbbbb"), [2]);

        var repos = await store.ListReposAsync(Space, 10, null);
        var alice = Assert.Single(repos.Repos);
        Assert.Equal("3kbbbbbbbbbbb", alice.RepoRev);
        Assert.Equal([2], alice.Hash);
    }

    [Fact]
    public async Task RecordWriteAsync_TheSameRevisionAgain_IsIgnored()
    {
        var store = CreateStore();
        var first = await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kaaaaaaaaaaa"), [1]);

        var again = await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kaaaaaaaaaaa"), [9]);

        Assert.NotNull(first);
        Assert.Null(again);
        var alice = Assert.Single((await store.ListReposAsync(Space, 10, null)).Repos);
        Assert.Equal([1], alice.Hash);
        Assert.Equal(first.SpaceRev, alice.SpaceRev);
    }

    [Fact]
    public async Task RecordWriteAsync_StaleRevision_IsNotSequenced()
    {
        var store = CreateStore();
        await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kbbbbbbbbbbb"), [2]);

        Assert.Null(await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kaaaaaaaaaaa"), [1]));

        // The stale notification consumed no space revision: the next one chains onto the first.
        var next = await store.RecordWriteAsync(Space, Did.Parse("did:plc:bob"), Tid.Parse("3kaaaaaaaaaaa"), [3]);
        Assert.Equal(Assert.Single((await store.ListReposAsync(Space, 1, null)).Repos).SpaceRev, next!.PrevSpaceRev);
    }

    [Fact]
    public async Task RecordWriteAsync_AcrossWriters_AssignsStrictlyIncreasingChainedSpaceRevisions()
    {
        var store = CreateStore();

        var first = await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kaaaaaaaaaaa"), [1]);
        var second = await store.RecordWriteAsync(Space, Did.Parse("did:plc:bob"), Tid.Parse("3kaaaaaaaaaaa"), [1]);
        var third = await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kbbbbbbbbbbb"), [2]);

        Assert.Null(first!.PrevSpaceRev);
        Assert.Equal(first.SpaceRev, second!.PrevSpaceRev);
        Assert.Equal(second.SpaceRev, third!.PrevSpaceRev);
        Assert.True(first.SpaceRev.CompareTo(second.SpaceRev) < 0);
        Assert.True(second.SpaceRev.CompareTo(third.SpaceRev) < 0);
    }

    [Fact]
    public async Task RecordWriteAsync_InDifferentSpaces_SequencesEachOnItsOwn()
    {
        var store = CreateStore();
        var other = SpaceUri.Parse("at://did:plc:authority/space/com.example.forum/other");
        await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kaaaaaaaaaaa"), [1]);

        var first = await store.RecordWriteAsync(other, Did.Parse("did:plc:alice"), Tid.Parse("3kaaaaaaaaaaa"), [1]);

        Assert.Null(first!.PrevSpaceRev);
    }

    [Fact]
    public async Task ListReposAsync_OrdersBySpaceRev_AndAnUpdatedRepoMovesToTheEnd()
    {
        var store = CreateStore();
        await store.RecordWriteAsync(Space, Did.Parse("did:plc:c"), Tid.Parse("3kaaaaaaaaaaa"), [1]);
        await store.RecordWriteAsync(Space, Did.Parse("did:plc:a"), Tid.Parse("3kaaaaaaaaaaa"), [1]);
        await store.RecordWriteAsync(Space, Did.Parse("did:plc:c"), Tid.Parse("3kbbbbbbbbbbb"), [2]);

        var repos = (await store.ListReposAsync(Space, 10, null)).Repos;

        Assert.Equal(["did:plc:a", "did:plc:c"], repos.Select(r => r.Did.Value));
        Assert.True(repos[0].SpaceRev.CompareTo(repos[1].SpaceRev) < 0);
    }

    [Fact]
    public async Task ListReposAsync_CursorIsAnExclusiveSpaceRevCheckpoint_ReturnedEvenOnAShortPage()
    {
        var store = CreateStore();
        foreach (var did in new[] { "did:plc:c", "did:plc:a", "did:plc:b" })
            await store.RecordWriteAsync(Space, Did.Parse(did), Tid.Parse("3kaaaaaaaaaaa"), [1]);

        var first = await store.ListReposAsync(Space, 2, null);
        Assert.Equal(["did:plc:c", "did:plc:a"], first.Repos.Select(r => r.Did.Value));
        Assert.Equal(first.Repos[^1].SpaceRev.Value, first.Cursor);

        // A short page still carries its checkpoint; only an empty one has none.
        var second = await store.ListReposAsync(Space, 2, first.Cursor);
        Assert.Equal(["did:plc:b"], second.Repos.Select(r => r.Did.Value));
        Assert.Equal(second.Repos[^1].SpaceRev.Value, second.Cursor);

        var empty = await store.ListReposAsync(Space, 2, second.Cursor);
        Assert.Empty(empty.Repos);
        Assert.Null(empty.Cursor);
    }

    [Fact]
    public async Task ListReposAsync_FromACheckpoint_ShowsOnlyWhatAdvancedSince()
    {
        var store = CreateStore();
        await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kaaaaaaaaaaa"), [1]);
        var checkpoint = (await store.ListReposAsync(Space, 10, null)).Cursor;

        await store.RecordWriteAsync(Space, Did.Parse("did:plc:bob"), Tid.Parse("3kaaaaaaaaaaa"), [1]);
        await store.RecordWriteAsync(Space, Did.Parse("did:plc:alice"), Tid.Parse("3kbbbbbbbbbbb"), [2]);

        var since = await store.ListReposAsync(Space, 10, checkpoint);

        Assert.Equal(["did:plc:bob", "did:plc:alice"], since.Repos.Select(r => r.Did.Value));
    }

    [Fact]
    public async Task ListReposAsync_UnknownSpace_IsEmptyWithNoCursor()
    {
        var store = CreateStore();

        var page = await store.ListReposAsync(Space, 10, null);

        Assert.Empty(page.Repos);
        Assert.Null(page.Cursor);
    }

    [Fact]
    public async Task ListSubscribersAsync_ExcludesAlreadyLapsedRegistrations()
    {
        var store = CreateStore();

        await store.RegisterNotifyAsync(Space, "did:web:syncer#s", DateTimeOffset.UtcNow.AddDays(7));
        await store.RegisterNotifyAsync(Space, "did:web:lapsed#s", DateTimeOffset.UtcNow.AddSeconds(-1));

        var live = await store.ListSubscribersAsync(Space);

        Assert.Equal("did:web:syncer#s", Assert.Single(live).Service);
    }

    [Fact]
    public async Task RegisterNotifyAsync_Twice_RenewsRatherThanDuplicates()
    {
        var store = CreateStore();
        var renewed = DateTimeOffset.UtcNow.AddDays(7);

        await store.RegisterNotifyAsync(Space, "did:web:syncer#s", DateTimeOffset.UtcNow.AddMinutes(1));
        await store.RegisterNotifyAsync(Space, "did:web:syncer#s", renewed);

        var subscriber = Assert.Single(await store.ListSubscribersAsync(Space));

        // A tolerance rather than exact equality: a durable store may round-trip an expiry through
        // storage with less than full tick precision.
        Assert.Equal(renewed, subscriber.ExpiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UnregisterNotifyAsync_IsIdempotent()
    {
        var store = CreateStore();
        await store.RegisterNotifyAsync(Space, "did:web:syncer#s", DateTimeOffset.UtcNow.AddDays(1));

        await store.UnregisterNotifyAsync(Space, "did:web:syncer#s");
        await store.UnregisterNotifyAsync(Space, "did:web:syncer#s");

        Assert.Empty(await store.ListSubscribersAsync(Space));
    }
}
