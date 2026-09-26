using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Unspecced;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.Unspecced;

public sealed class UnspeccedClientTests : IDisposable
{
    private readonly ScriptedXrpcHandler _handler = new();
    private readonly AtProtoClient _client;

    public UnspeccedClientTests() => _client = _handler.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task GetPostThreadV2Async_SendsTheShape_BindsAFlatThread()
    {
        _handler.On("app.bsky.unspecced.getPostThreadV2", $$$"""
            {"hasOtherReplies":true,
             "threadgate":{"uri":"at://{{{AliceDid}}}/app.bsky.feed.threadgate/3lwinfmsd2k2a","cid":"{{{OtherCid}}}","record":{"$type":"app.bsky.feed.threadgate","post":"{{{PostUri}}}","allow":[],"createdAt":"2026-09-20T12:00:00.000Z"},"lists":[]},
             "thread":[
              {"uri":"{{{OtherPostUri}}}","depth":-1,"value":{"$type":"app.bsky.unspecced.defs#threadItemBlocked","author":{"did":"{{{BobDid}}}","viewer":{"blocking":"at://{{{AliceDid}}}/app.bsky.graph.block/3lwinfmsd2k2g"} } } },
              {"uri":"{{{PostUri}}}","depth":0,"value":{"post":{{{PostViewJson}}},"moreParents":false,"moreReplies":0,"opThread":true,"opThreadPostIndex":1,"opThreadPostCount":3,"hiddenByThreadgate":false,"mutedByViewer":false,"$type":"app.bsky.unspecced.defs#threadItemPost"}},
              {"uri":"{{{OtherPostUri}}}","depth":1,"value":{"$type":"app.bsky.unspecced.defs#threadItemNoUnauthenticated"}},
              {"uri":"{{{OtherPostUri}}}","depth":1,"value":{"$type":"app.bsky.unspecced.defs#threadItemNotFound"}},
              {"uri":"{{{OtherPostUri}}}","depth":2,"value":{"$type":"app.bsky.unspecced.defs#threadItemFuture"}}
             ]}
            """);

        var response = await _client.Bsky.Unspecced.GetPostThreadV2Async(
            AtUri.Parse(PostUri), above: false, below: 3, branchingFactor: 5, sort: PostThreadSort.Top);

        Assert.Equal(
            $"anchor={PostUri}&above=false&below=3&branchingFactor=5&sort=top",
            Uri.UnescapeDataString(Assert.Single(_handler.Requests).Query));
        Assert.True(response.HasOtherReplies);
        Assert.Equal(PostUri, response.Threadgate!.Record!.Value.GetProperty("post").GetString());
        Assert.Equal([-1, 0, 1, 1, 2], response.Thread.Select(i => i.Depth));

        Assert.Equal(BobDid, Assert.IsType<ThreadItemBlocked>(response.Thread[0].Value).Author.Did.Value);
        var anchor = Assert.IsType<ThreadItemPost>(response.Thread[1].Value);
        Assert.Equal(PostUri, anchor.Post.Uri.Value);
        Assert.True(anchor.OpThread);
        Assert.Equal(1, anchor.OpThreadPostIndex);
        Assert.Equal(3, anchor.OpThreadPostCount);
        Assert.IsType<ThreadItemNoUnauthenticated>(response.Thread[2].Value);
        Assert.IsType<ThreadItemNotFound>(response.Thread[3].Value);
        Assert.Equal(
            "app.bsky.unspecced.defs#threadItemFuture",
            Assert.IsType<UnknownThreadItemValue>(response.Thread[4].Value).Type);
    }

    [Fact]
    public async Task GetPostThreadV2Async_Defaults_SendOnlyTheAnchor()
    {
        _handler.On("app.bsky.unspecced.getPostThreadV2", """{"thread":[],"hasOtherReplies":false}""");

        await _client.Bsky.Unspecced.GetPostThreadV2Async(AtUri.Parse(PostUri));

        Assert.Equal($"anchor={PostUri}", Uri.UnescapeDataString(Assert.Single(_handler.Requests).Query));
    }

    [Fact]
    public async Task GetPostThreadOtherV2Async_BindsTheHiddenReplies()
    {
        _handler.On("app.bsky.unspecced.getPostThreadOtherV2", $$$"""
            {"thread":[{"uri":"{{{PostUri}}}","depth":1,"value":{"$type":"app.bsky.unspecced.defs#threadItemPost","post":{{{PostViewJson}}},"moreParents":false,"moreReplies":2,"opThread":false,"hiddenByThreadgate":true,"mutedByViewer":false}}]}
            """);

        var response = await _client.Bsky.Unspecced.GetPostThreadOtherV2Async(AtUri.Parse(OtherPostUri));

        Assert.Equal($"anchor={OtherPostUri}", Uri.UnescapeDataString(Assert.Single(_handler.Requests).Query));
        var reply = Assert.IsType<ThreadItemPost>(Assert.Single(response.Thread).Value);
        Assert.True(reply.HiddenByThreadgate);
        Assert.Equal(2, reply.MoreReplies);
        Assert.Null(reply.OpThreadPostIndex);
    }
}
