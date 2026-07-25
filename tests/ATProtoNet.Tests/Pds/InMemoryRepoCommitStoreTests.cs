using ATProtoNet.Pds;

namespace ATProtoNet.Tests.Pds;

public sealed class InMemoryRepoCommitStoreTests
{
    private readonly InMemoryRepoCommitStore _store = new();

    private static RepoCommitState State(string did, string rev = "3ku2ipumwvw2a") => new()
    {
        Did = did,
        CommitCid = "bafyreib2rxk3rh6kzwq6qsnb7pkkjuc4zqk4jgnvbdbqqmgxpvxvknwpvm",
        Rev = rev,
        DataCid = "bafyreid7pgvhdvvnrn3jdvpqbtxgvbqhdlnvzqqpvmkqxnvpvxvknwpvm",
        CommitBlock = [1, 2, 3],
    };

    [Fact]
    public async Task GetAsync_UnknownDid_ReturnsNull()
    {
        Assert.Null(await _store.GetAsync("did:plc:nobody"));
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTrips()
    {
        await _store.SetAsync(State("did:plc:alice"));
        var head = await _store.GetAsync("did:plc:alice");

        Assert.NotNull(head);
        Assert.Equal("3ku2ipumwvw2a", head!.Rev);
    }

    [Fact]
    public async Task SetAsync_ReplacesThePreviousHead()
    {
        await _store.SetAsync(State("did:plc:alice", "3ku2ipumwvw2a"));
        await _store.SetAsync(State("did:plc:alice", "3ku2ipumwvw2b"));

        Assert.Equal("3ku2ipumwvw2b", (await _store.GetAsync("did:plc:alice"))!.Rev);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheHead()
    {
        await _store.SetAsync(State("did:plc:alice"));
        await _store.DeleteAsync("did:plc:alice");

        Assert.Null(await _store.GetAsync("did:plc:alice"));
    }

    [Fact]
    public async Task DeleteAsync_UnknownDid_IsANoOp()
    {
        await _store.DeleteAsync("did:plc:nobody");
    }

    [Fact]
    public async Task ListAsync_OrdersByDidOrdinally()
    {
        await _store.SetAsync(State("did:plc:carol"));
        await _store.SetAsync(State("did:plc:alice"));
        await _store.SetAsync(State("did:plc:bob"));

        var page = await _store.ListAsync(10, null);

        Assert.Equal(["did:plc:alice", "did:plc:bob", "did:plc:carol"], page.Select(s => s.Did));
    }

    [Fact]
    public async Task ListAsync_HonoursTheLimit()
    {
        for (var i = 0; i < 5; i++)
            await _store.SetAsync(State($"did:plc:user{i}"));

        Assert.Equal(2, (await _store.ListAsync(2, null)).Count);
    }

    [Fact]
    public async Task ListAsync_ResumesStrictlyAfterTheCursor()
    {
        await _store.SetAsync(State("did:plc:alice"));
        await _store.SetAsync(State("did:plc:bob"));
        await _store.SetAsync(State("did:plc:carol"));

        var page = await _store.ListAsync(10, "did:plc:alice");

        Assert.Equal(["did:plc:bob", "did:plc:carol"], page.Select(s => s.Did));
    }

    [Fact]
    public async Task ListAsync_CursorPastTheEnd_ReturnsEmpty()
    {
        await _store.SetAsync(State("did:plc:alice"));
        Assert.Empty(await _store.ListAsync(10, "did:plc:zzzz"));
    }

    [Fact]
    public async Task ListAsync_EmptyStore_ReturnsEmpty()
    {
        Assert.Empty(await _store.ListAsync(10, null));
    }
}
