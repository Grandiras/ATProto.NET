using ATProtoNet.Identity;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.TestSupport;

/// <summary>
/// Behaviour every <see cref="IRepoSyncStateStore"/> implementation must have. A derived class
/// supplies <see cref="CreateStore"/>; keep anything implementation-specific (ordering
/// guarantees on <see cref="IRepoSyncStateStore.ListUnsynchronizedAsync"/>, schema) in that
/// derived class instead.
/// </summary>
public abstract class RepoSyncStateStoreContractTests
{
    protected static readonly Did Alice = Did.Parse("did:plc:aliceaaaaaaaaaaaaaaaaaaa");
    protected static readonly Did Bob = Did.Parse("did:plc:bobbbbbbbbbbbbbbbbbbbbbb");
    protected static readonly Tid Rev = Tid.Parse("3mwgncrvwtj2t");
    protected static readonly Cid Data = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm");

    protected abstract IRepoSyncStateStore CreateStore();

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTrips()
    {
        var store = CreateStore();
        var state = new RepoSyncState(Alice, Rev, Data, RepoSyncStatus.Synchronized);

        await store.SetAsync(state);

        Assert.Equal(state, await store.GetAsync(Alice));
        Assert.Null(await store.GetAsync(Bob));
    }

    [Fact]
    public async Task SetAsync_ExistingRow_IsReplaced()
    {
        var store = CreateStore();
        await store.SetAsync(new RepoSyncState(Alice, Rev, Data, RepoSyncStatus.Synchronized));
        var desynchronized = new RepoSyncState(Alice, Tid.Parse("3mwgncs2p2324"), Data, RepoSyncStatus.Desynchronized);

        await store.SetAsync(desynchronized);

        Assert.Equal(desynchronized, await store.GetAsync(Alice));
    }

    [Fact]
    public async Task SetAsync_UnknownRevisionAndTree_RoundTrip()
    {
        var store = CreateStore();
        var marked = new RepoSyncState(Alice, null, null, RepoSyncStatus.Desynchronized);

        await store.SetAsync(marked);

        Assert.Equal(marked, await store.GetAsync(Alice));
    }

    [Fact]
    public async Task RemoveAsync_ForgetsTheRepository()
    {
        var store = CreateStore();
        await store.SetAsync(new RepoSyncState(Alice, Rev, Data, RepoSyncStatus.Synchronized));

        await store.RemoveAsync(Alice);
        await store.RemoveAsync(Bob);

        Assert.Null(await store.GetAsync(Alice));
    }

    [Fact]
    public async Task ListUnsynchronizedAsync_ExcludesSynchronized_AndRespectsTheLimit()
    {
        var store = CreateStore();
        await store.SetAsync(new RepoSyncState(Bob, null, null, RepoSyncStatus.Desynchronized));
        await store.SetAsync(new RepoSyncState(Alice, Rev, Data, RepoSyncStatus.Resynchronizing));
        await store.SetAsync(new RepoSyncState(Did.Parse("did:plc:carolcccccccccccccccccc"), Rev, Data, RepoSyncStatus.Synchronized));

        var all = await store.ListUnsynchronizedAsync(10);
        var limited = await store.ListUnsynchronizedAsync(1);

        Assert.Equal(2, all.Count);
        Assert.All(all, s => Assert.NotEqual(RepoSyncStatus.Synchronized, s.Status));
        Assert.Single(limited);
    }
}
