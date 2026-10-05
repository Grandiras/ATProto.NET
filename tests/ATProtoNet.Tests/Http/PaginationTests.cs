using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Models;
using ATProtoNet.Spaces;
using ATProtoNet.Streaming;
using ATProtoNet.Tests.TestSupport;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;

namespace ATProtoNet.Tests.Http;

/// <summary>
/// The shared cursor loop behind every <c>Enumerate*</c> method, and the enumerators built on it.
/// </summary>
public class PaginationTests : IDisposable
{
    private const string DidText = TestIds.ModDid;

    private readonly XrpcTestClient _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private sealed record Page(IReadOnlyList<int> Items, string? Cursor) : ICursorPage<int>;

    /// <summary>
    /// The pages a server hands back in turn (the last one repeats), each <c>items</c> with the
    /// <c>cursor</c> of the same index, and what the loop should yield and ask for.
    /// </summary>
    public static TheoryData<string, int[][], string?[], int[], string?[]> Walks() => new()
    {
        { "follows cursors until null", [[1, 2], [3], [4]], ["a", "b", null], [1, 2, 3, 4], [null, "a", "b"] },
        { "an empty cursor ends it", [[1], [2]], ["a", ""], [1, 2], [null, "a"] },
        // The server hands back the same cursor forever; before the guard this never ended.
        { "a repeated cursor ends it", [[1], [2]], ["same", "same"], [1, 2], [null, "same"] },
        { "a cursor cycle ends it", [[1], [2], [3], [4]], ["a", "b", "a", "b"], [1, 2, 3], [null, "a", "b"] },
        { "an empty page with a cursor goes on", [[], [1]], ["a", null], [1], [null, "a"] },
    };

    [Theory]
    [MemberData(nameof(Walks))]
    public async Task EnumerateAsync_Walk_YieldsEveryItemAndStopsAtTheRightPage(
        string name, int[][] items, string?[] cursors, int[] expectedItems, string?[] expectedRequests)
    {
        _ = name;
        var requested = new List<string?>();
        var next = 0;

        var read = await Pagination.EnumerateAsync<Page, int>((cursor, _) =>
        {
            requested.Add(cursor);
            var i = Math.Min(next++, items.Length - 1);
            return Task.FromResult(new Page(items[i], cursors[i]));
        }).ToListAsync();

        Assert.Equal(expectedItems, read);
        Assert.Equal(expectedRequests, requested);
    }

    [Fact]
    public async Task EnumerateAsync_Cancelled_StopsBetweenPages()
    {
        using var cts = new CancellationTokenSource();
        var seen = new List<int>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in Pagination.EnumerateAsync<Page, int>(
                (cursor, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(new Page([cursor is null ? 1 : 2], cursor is null ? "a" : null));
                },
                cts.Token))
            {
                seen.Add(item);
                cts.Cancel();
            }
        });

        Assert.Equal([1], seen);
    }

    [Fact]
    public async Task EnumerateAsync_OverAnEndpointWithNoDedicatedEnumerator_WalksEveryPage()
    {
        // The public one-liner every removed `Enumerate*` wrapper is replaced by.
        _fixture.Fallback(request => HttpStub.JsonResponse(request.Query.Contains("cursor=")
            ? $$"""{"cids":["{{PostCid}}"]}"""
            : $$"""{"cursor":"next","cids":["{{PostCid}}","{{PostCid}}"]}"""));

        var cids = await Pagination.EnumerateAsync<ATProtoNet.Lexicon.Com.AtProto.Sync.ListBlobsResponse, Cid>(
            (cursor, ct) => _fixture.Client.Sync.ListBlobsAsync(Did.Parse(DidText), cursor: cursor, cancellationToken: ct)).ToListAsync();

        Assert.Equal(3, cids.Count);
        Assert.All(cids, cid => Assert.Equal(PostCid, cid.Value));
        Assert.Equal(2, _fixture.Requests.Count);
    }

    [Fact]
    public async Task EnumerateAsync_OnARecordCollection_UsesThePaginator()
    {
        _fixture.Fallback(request => HttpStub.JsonResponse(request.Nsid == "com.atproto.server.createSession"
            ? $$"""{"did":"{{DidText}}","handle":"alice.test","accessJwt":"a","refreshJwt":"r"}"""
            : $$$"""{"cursor":"same","records":[{"uri":"at://{{{DidText}}}/com.example.note/1","cid":"{{{PostCid}}}","value":{"text":"hi"}}]}"""));
        await _fixture.Client.LoginAsync("alice.test", "password");

        var notes = await _fixture.Client.GetCollection<Note>(Nsid.Parse("com.example.note")).EnumerateAsync().ToListAsync();

        Assert.Equal(2, notes.Count);
        Assert.Equal("1", notes[0].RecordKey.Value);
    }

    /// <summary>
    /// Every dedicated <c>Enumerate*</c> helper, as (the method it pages, a page of one item, the
    /// query of its first request, the enumeration with a page size of 2 over a client or the
    /// stub's <see cref="HttpClient"/>).
    /// </summary>
    public static TheoryData<string, string, string, Func<AtProtoClient, HttpClient, IAsyncEnumerable<object>>> Enumerators()
    {
        var alice = Did.Parse(DidText);
        var space = SpaceUri.Parse($"at://{DidText}/space/com.atmoboards.forum/default");
        var feed = $$"""{"feed":[{"post":{{PostViewJson}}}]}""";

        return new()
        {
            { "app.bsky.feed.getTimeline", feed, "limit=2", (c, _) => c.Bsky.Feed.EnumerateTimelineAsync(pageSize: 2) },
            { "app.bsky.feed.getAuthorFeed", feed, $"actor={DidText}&limit=2", (c, _) => c.Bsky.Feed.EnumerateAuthorFeedAsync(alice, pageSize: 2) },
            { "app.bsky.feed.getActorLikes", feed, $"actor={DidText}&limit=2", (c, _) => c.Bsky.Feed.EnumerateActorLikesAsync(alice, pageSize: 2) },
            {
                "app.bsky.graph.getFollowers", $$"""{"subject":{{BobProfileJson}},"followers":[{{BobProfileJson}}]}""",
                $"actor={DidText}&limit=2", (c, _) => c.Bsky.Graph.EnumerateFollowersAsync(alice, pageSize: 2)
            },
            {
                "app.bsky.graph.getFollows", $$"""{"subject":{{BobProfileJson}},"follows":[{{BobProfileJson}}]}""",
                $"actor={DidText}&limit=2", (c, _) => c.Bsky.Graph.EnumerateFollowsAsync(alice, pageSize: 2)
            },
            {
                "app.bsky.graph.getList", $$"""{"list":{{ListViewJson}},"items":[{{ListItemViewJson}}]}""",
                $"list={ListUri}&limit=2", (c, _) => c.Bsky.Graph.EnumerateListMembersAsync(AtUri.Parse(ListUri), pageSize: 2)
            },
            {
                "app.bsky.notification.listNotifications", $$"""
                {"notifications":[{"uri":"at://{{DidText}}/app.bsky.feed.like/3k2lb","cid":"{{PostCid}}","author":{{AliceBasicJson}},
                  "reason":"reply","record":{},"isRead":false,"indexedAt":"2024-05-01T12:00:00.000Z"}]}
                """,
                "reasons=reply&limit=2", (c, _) => c.Bsky.Notification.EnumerateNotificationsAsync(["reply"], pageSize: 2)
            },
            {
                "app.bsky.bookmark.getBookmarks", $$$"""{"bookmarks":[{"subject":{"uri":"{{{PostUri}}}","cid":"{{{PostCid}}}"},"item":{{{PostViewJson}}}}]}""",
                "limit=2", (c, _) => c.Bsky.Bookmark.EnumerateBookmarksAsync(pageSize: 2)
            },
            {
                "com.atproto.repo.listRecords", $$$"""{"records":[{"uri":"at://{{{DidText}}}/com.example.note/1","cid":"{{{PostCid}}}","value":{}}]}""",
                $"repo={DidText}&collection=com.example.note&limit=2", (c, _) => c.Repo.EnumerateRecordsAsync(alice, Nsid.Parse("com.example.note"), pageSize: 2)
            },
            {
                "com.atproto.space.listRepos", $$"""{"repos":[{"did":"{{DidText}}","repoRev":"3l6oveex3ii2l","hash":{"$bytes":"AQID"},"spaceRev":"3l6oveex3ii2m"}]}""",
                $"space={space}&limit=2", (c, _) => c.Space.EnumerateReposAsync(space, pageSize: 2)
            },
            {
                "com.atproto.space.listRecords", $$"""{"records":[{"collection":"com.example.n","rkey":"a","cid":"{{PostCid}}"}]}""",
                $"space={space}&repo={DidText}&limit=2", (c, _) => c.Space.EnumerateRecordsAsync(space, alice, pageSize: 2)
            },
            {
                "com.atproto.simplespace.listMembers", $$"""{"members":[{"did":"{{DidText}}","read":true,"write":true}]}""",
                $"space={space}&limit=2", (c, _) => c.SimpleSpace.EnumerateMembersAsync(space, pageSize: 2)
            },
            {
                "chat.bsky.convo.getMessages", """
                {"messages":[{"$type":"chat.bsky.convo.defs#deletedMessageView","id":"m","rev":"r","sender":{"did":"did:plc:user1"},"sentAt":"2026-06-01T12:00:00.000Z"}]}
                """,
                "convoId=convo-1&limit=2", (c, _) => c.Chat.Convo.EnumerateMessagesAsync("convo-1", pageSize: 2)
            },
            {
                "network.bsky.jetstream.listSegments", """{"segments":[{"name":"seg_0.jss","index":0,"checksum":"0123456789abcdef"}]}""",
                "limit=2", (_, http) => new JetstreamArchiveClient(JetstreamEndpoints.UsEast, apiKey: null, http).EnumerateSegmentsAsync(pageSize: 2)
            },
        };
    }

    [Theory]
    [MemberData(nameof(Enumerators))]
    public async Task Enumerator_PassesItsCursor_ReadsItsItems_StopsOnARepeatedCursor(
        string nsid, string page, string firstQuery, Func<AtProtoClient, HttpClient, IAsyncEnumerable<object>> enumerate)
    {
        // Every page carries the same cursor. Regression: some of these loops used to stop only on
        // an empty page, so such a host was asked for the same page forever; the cap turns that
        // into a failure rather than a hang.
        _fixture.On(nsid, "{\"cursor\":\"c2\"," + page.TrimStart()[1..]);

        var read = await enumerate(_fixture.Client, _fixture.Http).Take(10).ToListAsync();

        Assert.Equal(2, read.Count);
        Assert.Equal([firstQuery, $"{firstQuery}&cursor=c2"], _fixture.To(nsid).Select(r => Uri.UnescapeDataString(r.Query)));
    }

    private sealed class Note
    {
        public string? Text { get; set; }
    }
}
