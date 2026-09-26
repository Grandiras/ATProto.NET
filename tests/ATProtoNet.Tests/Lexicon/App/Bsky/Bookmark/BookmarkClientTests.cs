using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Serialization;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.Bookmark;

public sealed class BookmarkClientTests : IDisposable
{
    private readonly ScriptedXrpcHandler _handler = new();
    private readonly AtProtoClient _client;

    public BookmarkClientTests() => _client = _handler.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task CreateBookmarkAsync_PostsUriAndCid()
    {
        _handler.On("app.bsky.bookmark.createBookmark", "{}");

        await _client.Bsky.Bookmark.CreateBookmarkAsync(AtUri.Parse(PostUri), Cid.Parse(PostCid));

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($$"""{"uri":"{{PostUri}}","cid":"{{PostCid}}"}""", request.BodyText);
    }

    [Fact]
    public async Task DeleteBookmarkAsync_PostsUri()
    {
        _handler.On("app.bsky.bookmark.deleteBookmark", "{}");

        await _client.Bsky.Bookmark.DeleteBookmarkAsync(AtUri.Parse(PostUri));

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($$"""{"uri":"{{PostUri}}"}""", request.BodyText);
    }

    [Fact]
    public async Task GetBookmarksAsync_BindsEveryItemVariant()
    {
        _handler.On("app.bsky.bookmark.getBookmarks", $$$"""
            {"cursor":"c2","bookmarks":[
              {"subject":{"uri":"{{{PostUri}}}","cid":"{{{PostCid}}}"},"createdAt":"2026-09-21T08:00:00.000Z","item":{{{PostViewJson}}}},
              {"subject":{"uri":"{{{OtherPostUri}}}","cid":"{{{OtherCid}}}"},"item":{"$type":"app.bsky.feed.defs#notFoundPost","uri":"{{{OtherPostUri}}}","notFound":true}},
              {"subject":{"uri":"{{{OtherPostUri}}}","cid":"{{{OtherCid}}}"},"item":{"$type":"app.bsky.feed.defs#blockedPost","uri":"{{{OtherPostUri}}}","blocked":true,"author":{"did":"{{{BobDid}}}","viewer":{"blocking":"at://{{{AliceDid}}}/app.bsky.graph.block/3lwinfmsd2k2g"} } } },
              {"subject":{"uri":"{{{OtherPostUri}}}","cid":"{{{OtherCid}}}"},"item":{"$type":"app.bsky.feed.defs#futurePost","uri":"{{{OtherPostUri}}}"}}
            ]}
            """);

        var page = await _client.Bsky.Bookmark.GetBookmarksAsync(limit: 10, cursor: "c1");

        Assert.Equal("limit=10&cursor=c1", Assert.Single(_handler.Requests).Query);
        Assert.Equal("c2", page.Cursor);
        Assert.Equal(4, page.Bookmarks.Count);

        var first = page.Bookmarks[0];
        Assert.Equal(PostUri, first.Subject.Uri.Value);
        Assert.Equal("2026-09-21T08:00:00.000Z", first.CreatedAt!.ToString());
        var post = Assert.IsType<PostView>(first.Item);
        Assert.Equal(3, post.BookmarkCount);
        Assert.True(post.Viewer!.Bookmarked);
        Assert.Equal("alice.test", post.Author.Handle.Value);

        Assert.Equal(OtherPostUri, Assert.IsType<NotFoundPost>(page.Bookmarks[1].Item).Uri.Value);
        Assert.Equal(BobDid, Assert.IsType<BlockedPost>(page.Bookmarks[2].Item).Author.Did.Value);
        Assert.Null(page.Bookmarks[1].CreatedAt);

        var unknown = Assert.IsType<UnknownPostEntry>(page.Bookmarks[3].Item);
        Assert.Equal("app.bsky.feed.defs#futurePost", unknown.Type);
    }

    [Fact]
    public async Task EnumerateBookmarksAsync_FollowsTheCursor()
    {
        _handler
            .On("app.bsky.bookmark.getBookmarks", $$"""{"cursor":"next","bookmarks":[{"subject":{"uri":"{{PostUri}}","cid":"{{PostCid}}"},"item":{{PostViewJson}}}]}""")
            .On("app.bsky.bookmark.getBookmarks", $$"""{"bookmarks":[{"subject":{"uri":"{{PostUri}}","cid":"{{PostCid}}"},"item":{{PostViewJson}}}]}""");

        var bookmarks = await _client.Bsky.Bookmark.EnumerateBookmarksAsync(pageSize: 1).ToListAsync();

        Assert.Equal(2, bookmarks.Count);
        Assert.Equal(["limit=1", "limit=1&cursor=next"], _handler.Requests.Select(r => r.Query));
    }

    [Fact]
    public void PostEntry_WritesEachVariantWithItsType()
    {
        var entry = JsonSerializer.Deserialize<PostEntry>(PostViewJson, AtProtoJsonDefaults.Options)!;

        var json = JsonSerializer.Serialize(entry, AtProtoJsonDefaults.Options);

        Assert.StartsWith("""{"$type":"app.bsky.feed.defs#postView","uri":""", json);
    }

    [Fact]
    public void ThreadNode_StillReadsItsPlaceholders()
    {
        // NotFoundPost and BlockedPost are variants of both ThreadNode and PostEntry.
        var node = JsonSerializer.Deserialize<ThreadNode>(
            $$"""{"$type":"app.bsky.feed.defs#notFoundPost","uri":"{{PostUri}}","notFound":true}""",
            AtProtoJsonDefaults.Options);

        Assert.IsType<NotFoundPost>(node);
    }
}
