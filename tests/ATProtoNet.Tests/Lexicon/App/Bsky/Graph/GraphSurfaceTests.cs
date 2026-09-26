using ATProtoNet.Identity;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.Graph;

/// <summary>
/// getListsWithMembership, getStarterPacksWithMembership and searchStarterPacksV2.
/// </summary>
public sealed class GraphSurfaceTests : IDisposable
{
    private readonly ScriptedXrpcHandler _handler = new();
    private readonly AtProtoClient _client;

    public GraphSurfaceTests() => _client = _handler.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task GetListsWithMembershipAsync_SendsActorAndPurposes_BindsMembership()
    {
        _handler.On("app.bsky.graph.getListsWithMembership", $$"""
            {"cursor":"n","listsWithMembership":[
              {"list":{{ListViewJson}},"listItem":{{ListItemViewJson}}},
              {"list":{{ListViewJson}}}
            ]}
            """);

        var page = await _client.Bsky.Graph.GetListsWithMembershipAsync(
            Handle.Parse("bob.test"), ["curatelist", "modlist"], limit: 20, cursor: "c");

        Assert.Equal(
            "actor=bob.test&purposes=curatelist&purposes=modlist&limit=20&cursor=c",
            Assert.Single(_handler.Requests).Query);
        Assert.Equal("n", page.Cursor);
        Assert.Equal(2, page.ListsWithMembership.Count);
        Assert.Equal(ListUri, page.ListsWithMembership[0].List.Uri.Value);
        Assert.Equal(ListItemUri, page.ListsWithMembership[0].ListItem!.Uri.Value);
        Assert.Equal(BobDid, page.ListsWithMembership[0].ListItem!.Subject.Did.Value);
        Assert.Null(page.ListsWithMembership[1].ListItem);
    }

    [Fact]
    public async Task EnumerateListsWithMembershipAsync_FollowsTheCursor()
    {
        _handler
            .On("app.bsky.graph.getListsWithMembership", $$"""{"cursor":"n","listsWithMembership":[{"list":{{ListViewJson}}}]}""")
            .On("app.bsky.graph.getListsWithMembership", $$"""{"listsWithMembership":[{"list":{{ListViewJson}}}]}""");

        var lists = await _client.Bsky.Graph.EnumerateListsWithMembershipAsync(Did.Parse(BobDid)).ToListAsync();

        Assert.Equal(2, lists.Count);
        Assert.Equal([$"actor={BobDid}", $"actor={BobDid}&cursor=n"], _handler.Requests.Select(r => Uri.UnescapeDataString(r.Query)));
    }

    [Fact]
    public async Task GetStarterPacksWithMembershipAsync_BindsMembership()
    {
        _handler.On("app.bsky.graph.getStarterPacksWithMembership", $$"""
            {"starterPacksWithMembership":[{"starterPack":{{StarterPackViewJson}},"listItem":{{ListItemViewJson}}}]}
            """);

        var page = await _client.Bsky.Graph.GetStarterPacksWithMembershipAsync(Did.Parse(BobDid), limit: 10);

        Assert.Equal($"actor={BobDid}&limit=10", Uri.UnescapeDataString(Assert.Single(_handler.Requests).Query));
        var entry = Assert.Single(page.StarterPacksWithMembership);
        Assert.Equal(StarterPackUri, entry.StarterPack.Uri.Value);
        Assert.Equal(4, entry.StarterPack.JoinedAllTimeCount);
        Assert.Equal(ListItemUri, entry.ListItem!.Uri.Value);
    }

    [Fact]
    public async Task EnumerateStarterPacksWithMembershipAsync_FollowsTheCursor()
    {
        _handler
            .On("app.bsky.graph.getStarterPacksWithMembership", $$"""{"cursor":"n","starterPacksWithMembership":[{"starterPack":{{StarterPackViewJson}}}]}""")
            .On("app.bsky.graph.getStarterPacksWithMembership", $$"""{"starterPacksWithMembership":[{"starterPack":{{StarterPackViewJson}}}]}""");

        var packs = await _client.Bsky.Graph.EnumerateStarterPacksWithMembershipAsync(Did.Parse(BobDid), pageSize: 1).ToListAsync();

        Assert.Equal(2, packs.Count);
    }

    [Fact]
    public async Task SearchStarterPacksV2Async_ReturnsFullViewsAndHits()
    {
        _handler.On("app.bsky.graph.searchStarterPacksV2", $$"""{"cursor":"25","hitsTotal":40,"starterPacks":[{{StarterPackViewJson}}]}""");

        var page = await _client.Bsky.Graph.SearchStarterPacksV2Async("science", limit: 25);

        Assert.Equal("q=science&limit=25", Assert.Single(_handler.Requests).Query);
        Assert.Equal(40, page.HitsTotal);
        var pack = Assert.Single(page.StarterPacks);
        Assert.Equal("alice.test", pack.Creator.Handle.Value);
        Assert.Equal("app.bsky.graph.defs#referencelist", pack.List!.Purpose);
    }

    [Fact]
    public async Task EnumerateSearchStarterPacksV2Async_FollowsTheCursor()
    {
        _handler
            .On("app.bsky.graph.searchStarterPacksV2", $$"""{"cursor":"25","starterPacks":[{{StarterPackViewJson}}]}""")
            .On("app.bsky.graph.searchStarterPacksV2", $$"""{"starterPacks":[{{StarterPackViewJson}}]}""");

        var packs = await _client.Bsky.Graph.EnumerateSearchStarterPacksV2Async("science").ToListAsync();

        Assert.Equal(2, packs.Count);
        Assert.Equal(["q=science", "q=science&cursor=25"], _handler.Requests.Select(r => r.Query));
    }
}
