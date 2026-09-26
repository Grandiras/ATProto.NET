using ATProtoNet.Identity;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Server.Spaces;

public class InMemorySpaceAuthorityStoreTests
{
    private static readonly SpaceUri Space =
        SpaceUri.Parse("at://did:plc:bbbbbbbbbbbbbbbbbbbbbbbb/space/com.example.bespoke/default");

    [Fact]
    public async Task MarkDeleted_ADeclaredSpace_AnswersSpaceDeleted()
    {
        // A bespoke space type has no simplespace store to read its deletion from, so the
        // authority store is where it is recorded.
        var store = new InMemorySpaceAuthorityStore();
        store.DeclareSpace(Space);

        store.MarkDeleted(Space);

        Assert.Equal(SpaceAccessOutcome.SpaceDeleted, await store.GetSpaceStateAsync(Space));
    }

    [Fact]
    public async Task ListReposAsync_PagesByDidWithACursor()
    {
        var store = new InMemorySpaceAuthorityStore();
        foreach (var did in new[] { "did:plc:c", "did:plc:a", "did:plc:b" })
            await store.RecordWriteAsync(Space, Did.Parse(did), Tid.Parse("3kaaaaaaaaaaa"), [1]);

        var first = await store.ListReposAsync(Space, limit: 2, cursor: null);
        var second = await store.ListReposAsync(Space, limit: 2, cursor: first.Cursor);

        Assert.Equal(["did:plc:a", "did:plc:b"], first.Repos.Select(r => r.Did.Value));
        Assert.Equal("did:plc:b", first.Cursor);
        Assert.Equal(["did:plc:c"], second.Repos.Select(r => r.Did.Value));
        Assert.Null(second.Cursor);
    }
}
