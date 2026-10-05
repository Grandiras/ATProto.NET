using ATProtoNet.Identity;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>The contract every <see cref="ISpaceAuthorityStore"/> must satisfy, held in memory.</summary>
public class InMemorySpaceAuthorityStoreTests : SpaceAuthorityStoreContractTests
{
    protected override ISpaceAuthorityStore CreateStore() => new InMemorySpaceAuthorityStore();

    protected override Task DeclareSpaceAsync(ISpaceAuthorityStore store, SpaceUri space)
    {
        ((InMemorySpaceAuthorityStore)store).DeclareSpace(space);
        return Task.CompletedTask;
    }

    protected override Task MarkDeletedAsync(ISpaceAuthorityStore store, SpaceUri space)
    {
        ((InMemorySpaceAuthorityStore)store).MarkDeleted(space);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RecordWriteAsync_FromManyWritersAtOnce_ChainsEveryRevisionExactlyOnce()
    {
        var store = CreateStore();

        var sequences = await Task.WhenAll(Enumerable.Range(0, 200).Select(i =>
            Task.Run(async () => (await store.RecordWriteAsync(Space, Did.Parse($"did:plc:w{i}"), Tid.Parse("3kaaaaaaaaaaa"), [1]))!)));

        // Every write took a distinct revision, and each one's predecessor is another write's revision
        // (or none, once): the revisions form a single chain, so no checkpoint can skip a write.
        var byRev = sequences.ToDictionary(s => s.SpaceRev);
        Assert.Equal(200, byRev.Count);
        Assert.Single(sequences, s => s.PrevSpaceRev is null);
        Assert.All(sequences.Where(s => s.PrevSpaceRev is not null), s => Assert.Contains(s.PrevSpaceRev!, byRev.Keys));
        Assert.Equal(200, sequences.Select(s => s.PrevSpaceRev).Where(p => p is not null).Distinct().Count() + 1);
    }
}
