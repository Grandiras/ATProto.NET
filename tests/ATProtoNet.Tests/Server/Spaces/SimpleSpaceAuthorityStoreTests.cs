using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// The bridge between the two stores. Which spaces exist is space-management state that
/// <c>com.atproto.simplespace</c> holds; the writer set and the subscriptions are the authority's
/// own, and nothing copies one into the other.
/// </summary>
public class SimpleSpaceAuthorityStoreTests
{
    private const string Owner = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";

    private static readonly SpaceUri Space =
        SpaceUri.Parse($"at://{Owner}/space/com.atmoboards.forum/default");

    private readonly InMemorySimpleSpaceStore _spaces = new();
    private readonly InMemorySpaceAuthorityStore _inner = new();

    private SimpleSpaceAuthorityStore Store => new(_inner, _spaces);

    [Fact]
    public async Task GetSpaceStateAsync_ForASpaceTheSimpleSpaceStoreHolds_Grants()
    {
        await _spaces.CreateSpaceAsync(
            new SimpleSpaceRecord(Space, Owner, new MemberListPolicy(), new OpenAppAccess()));

        Assert.Equal(
            SpaceAccessOutcome.Granted,
            await Store.GetSpaceStateAsync(Space, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSpaceStateAsync_ForADeletedSpace_AnswersSpaceDeleted()
    {
        await _spaces.CreateSpaceAsync(
            new SimpleSpaceRecord(Space, Owner, new MemberListPolicy(), new OpenAppAccess()));
        await _spaces.DeleteSpaceAsync(Space);

        // Read rather than copied, so deletion needs no second write: a syncer that missed the
        // notification learns from this answer that its copy should go.
        Assert.Equal(
            SpaceAccessOutcome.SpaceDeleted,
            await Store.GetSpaceStateAsync(Space, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSpaceStateAsync_ForASpaceDeclaredToTheInnerStore_FallsThrough()
    {
        // A service may run a bespoke space type alongside the baseline; those spaces are still
        // declared to the authority store directly.
        _inner.DeclareSpace(Space);

        Assert.Equal(
            SpaceAccessOutcome.Granted,
            await Store.GetSpaceStateAsync(Space, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSpaceStateAsync_ForASpaceNeitherStoreKnows_AnswersSpaceNotFound()
    {
        Assert.Equal(
            SpaceAccessOutcome.SpaceNotFound,
            await Store.GetSpaceStateAsync(Space, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecordWriteAsync_WritesThroughToTheInnerStore()
    {
        await _spaces.CreateSpaceAsync(
            new SimpleSpaceRecord(Space, Owner, new MemberListPolicy(), new OpenAppAccess()));

        var store = Store;
        await store.RecordWriteAsync(Space, Owner, "3l6oveex3ii2l", [1, 2, 3]);

        var direct = await _inner.ListReposAsync(Space, 10, null, TestContext.Current.CancellationToken);
        var bridged = await store.ListReposAsync(Space, 10, null, TestContext.Current.CancellationToken);

        Assert.Equal(Owner, Assert.Single(direct.Repos).Did);
        Assert.Equal(Owner, Assert.Single(bridged.Repos).Did);
    }
}
