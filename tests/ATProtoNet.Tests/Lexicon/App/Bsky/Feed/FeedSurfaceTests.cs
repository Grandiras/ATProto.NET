using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.Feed;

/// <summary>
/// app.bsky.feed.searchPostsV2 and app.bsky.feed.sendInteractions.
/// </summary>
public sealed class FeedSurfaceTests : IDisposable
{
    private readonly ScriptedXrpcHandler _handler = new();
    private readonly AtProtoClient _client;

    public FeedSurfaceTests() => _client = _handler.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task SearchPostsV2Async_SendsEveryFilterUnderItsLexiconName()
    {
        _handler.On("app.bsky.feed.searchPostsV2", """{"posts":[]}""");

        await _client.Bsky.Feed.SearchPostsV2Async(
            "東京 ramen",
            new PostSearchFilters
            {
                Authors = [Did.Parse(AliceDid), Handle.Parse("bob.test")],
                Mentions = [Did.Parse(BobDid)],
                Domains = ["example.com"],
                Urls = ["https://example.com/a"],
                EmbeddedAtUris = [AtUri.Parse(OtherPostUri)],
                Hashtags = ["food", "tokyo"],
                ExcludeAuthors = [Handle.Parse("spam.test")],
                ExcludeMentions = [Handle.Parse("noise.test")],
                ExcludeDomains = ["bad.example"],
                ExcludeUrls = ["https://bad.example/x"],
                ExcludeEmbeddedAtUris = [AtUri.Parse(PostUri)],
                ExcludeHashtags = ["ad"],
                Since = "2026-01-01",
                Until = "2026-09-01T00:00:00Z",
                AllTime = true,
                Languages = ["ja", "en"],
                ExcludeLanguages = ["de"],
                HasMedia = true,
                HasVideo = false,
                ReplyParentUri = AtUri.Parse(PostUri),
                ThreadRootUri = AtUri.Parse(OtherPostUri),
                ExcludeReplies = false,
                RepliesOnly = true,
                Following = true,
                QueryLanguage = SearchQueryLanguage.Japanese,
            },
            sort: PostSearchSort.Recent,
            limit: 30,
            cursor: "c1");

        var request = Assert.Single(_handler.Requests);
        var query = request.Parameters;
        Assert.Equal("東京 ramen", query["query"]);
        Assert.Equal("recent", query["sort"]);
        Assert.Equal([AliceDid, "bob.test"], request.ValuesOf("authors"));
        Assert.Equal([BobDid], request.ValuesOf("mentions"));
        Assert.Equal(["example.com"], request.ValuesOf("domains"));
        Assert.Equal(["https://example.com/a"], request.ValuesOf("urls"));
        Assert.Equal([OtherPostUri], request.ValuesOf("embeddedAtUris"));
        Assert.Equal(["food", "tokyo"], request.ValuesOf("hashtags"));
        Assert.Equal(["spam.test"], request.ValuesOf("excludeAuthors"));
        Assert.Equal(["noise.test"], request.ValuesOf("excludeMentions"));
        Assert.Equal(["bad.example"], request.ValuesOf("excludeDomains"));
        Assert.Equal(["https://bad.example/x"], request.ValuesOf("excludeUrls"));
        Assert.Equal([PostUri], request.ValuesOf("excludeEmbeddedAtUris"));
        Assert.Equal(["ad"], request.ValuesOf("excludeHashtags"));
        Assert.Equal("2026-01-01", query["since"]);
        Assert.Equal("2026-09-01T00:00:00Z", query["until"]);
        Assert.Equal("true", query["allTime"]);
        Assert.Equal(["ja", "en"], request.ValuesOf("languages"));
        Assert.Equal(["de"], request.ValuesOf("excludeLanguages"));
        Assert.Equal("true", query["hasMedia"]);
        Assert.Equal("false", query["hasVideo"]);
        Assert.Equal(PostUri, query["replyParentUri"]);
        Assert.Equal(OtherPostUri, query["threadRootUri"]);
        Assert.Equal("false", query["excludeReplies"]);
        Assert.Equal("true", query["repliesOnly"]);
        Assert.Equal("true", query["following"]);
        Assert.Equal("ja", query["queryLanguage"]);
        Assert.Equal("30", query["limit"]);
        Assert.Equal("c1", query["cursor"]);

        // Every upstream parameter is sent, and nothing else.
        var upstream = Upstream.UpstreamLexicons.Instance.Documents["app.bsky.feed.searchPostsV2"]
            .GetProperty("defs").GetProperty("main").GetProperty("parameters").GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal);
        Assert.Equal(upstream, query.AllKeys.OfType<string>().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SearchPostsV2Async_FiltersOnly_SendsNoQuery()
    {
        _handler.On("app.bsky.feed.searchPostsV2", """{"posts":[]}""");

        await _client.Bsky.Feed.SearchPostsV2Async(filters: new PostSearchFilters { Hashtags = ["atproto"] });

        Assert.Equal("hashtags=atproto", Assert.Single(_handler.Requests).Query);
    }

    [Fact]
    public async Task SearchPostsV2Async_BindsHitsAndDetectedLanguages()
    {
        _handler.On("app.bsky.feed.searchPostsV2", $$"""{"cursor":"25","hitsTotal":1200,"posts":[{{PostViewJson}}],"detectedQueryLanguages":["ja"]}""");

        var page = await _client.Bsky.Feed.SearchPostsV2Async("東京");

        Assert.Equal("25", page.Cursor);
        Assert.Equal(1200, page.HitsTotal);
        Assert.Equal(PostUri, Assert.Single(page.Posts).Uri.Value);
        Assert.Equal([SearchQueryLanguage.Japanese], page.DetectedQueryLanguages);
    }

    [Fact]
    public async Task EnumerateSearchPostsV2Async_KeepsFiltersAcrossPages()
    {
        _handler
            .On("app.bsky.feed.searchPostsV2", $$"""{"cursor":"p2","posts":[{{PostViewJson}}]}""")
            .On("app.bsky.feed.searchPostsV2", $$"""{"posts":[{{PostViewJson}}]}""");

        var posts = await _client.Bsky.Feed.EnumerateSearchPostsV2Async(
            "q", new PostSearchFilters { HasVideo = true }, pageSize: 1).ToListAsync();

        Assert.Equal(2, posts.Count);
        Assert.Equal(
            ["query=q&hasVideo=true&limit=1", "query=q&hasVideo=true&limit=1&cursor=p2"],
            _handler.Requests.Select(r => r.Query));
    }

    [Fact]
    public async Task SendInteractionsAsync_PostsTheInteractions()
    {
        _handler.On("app.bsky.feed.sendInteractions", "{}");

        await _client.Bsky.Feed.SendInteractionsAsync(
            [
                new Interaction
                {
                    Item = AtUri.Parse(PostUri),
                    Event = InteractionEvent.RequestLess,
                    FeedContext = "ctx-1",
                    ReqId = "req-1",
                },
                new Interaction { Item = AtUri.Parse(OtherPostUri), Event = InteractionEvent.Seen },
            ],
            feed: AtUri.Parse($"at://{AliceDid}/app.bsky.feed.generator/discover"));

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Null(request.Proxy);
        Assert.Equal(
            $$"""{"feed":"at://{{AliceDid}}/app.bsky.feed.generator/discover","interactions":[{"item":"{{PostUri}}","event":"app.bsky.feed.defs#requestLess","feedContext":"ctx-1","reqId":"req-1"},{"item":"{{OtherPostUri}}","event":"app.bsky.feed.defs#interactionSeen"}]}""",
            request.BodyText);
    }

    [Fact]
    public async Task SendInteractionsAsync_WithFeedGenerator_ProxiesToIt()
    {
        _handler.On("app.bsky.feed.sendInteractions", "{}");

        await _client.Bsky.Feed.SendInteractionsAsync(
            [new Interaction { Item = AtUri.Parse(PostUri), Event = InteractionEvent.Like }],
            feedGenerator: Did.Parse("did:web:feeds.example.com"));

        Assert.Equal("did:web:feeds.example.com#bsky_fg", Assert.Single(_handler.Requests).Proxy);
    }
}
