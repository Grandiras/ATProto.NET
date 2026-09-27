using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Tests.TestSupport;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.Feed;

/// <summary>
/// app.bsky.feed.searchPostsV2's full parameter set and sendInteractions' proxying. Their plain
/// request shapes are rows in <see cref="EndpointRequestTests"/>.
/// </summary>
public sealed class FeedSurfaceTests : IDisposable
{
    private readonly XrpcTestClient _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task SearchPostsV2Async_SendsEveryFilterUnderItsLexiconName()
    {
        _fixture.On("app.bsky.feed.searchPostsV2", """{"posts":[]}""");

        await _fixture.Client.Bsky.Feed.SearchPostsV2Async(
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

        var request = _fixture.AssertGet(
            "app.bsky.feed.searchPostsV2",
            $"query=東京 ramen&sort=recent&authors={AliceDid}&authors=bob.test&mentions={BobDid}&domains=example.com&urls=https://example.com/a" +
            $"&embeddedAtUris={OtherPostUri}&hashtags=food&hashtags=tokyo&excludeAuthors=spam.test&excludeMentions=noise.test" +
            $"&excludeDomains=bad.example&excludeUrls=https://bad.example/x&excludeEmbeddedAtUris={PostUri}&excludeHashtags=ad" +
            "&since=2026-01-01&until=2026-09-01T00:00:00Z&allTime=true&languages=ja&languages=en&excludeLanguages=de" +
            $"&hasMedia=true&hasVideo=false&replyParentUri={PostUri}&threadRootUri={OtherPostUri}&excludeReplies=false" +
            "&repliesOnly=true&following=true&queryLanguage=ja&limit=30&cursor=c1");

        // Every upstream parameter is sent (the drift test checks the other direction for every call).
        var upstream = Upstream.UpstreamLexicons.Instance.Documents["app.bsky.feed.searchPostsV2"]
            .GetProperty("defs").GetProperty("main").GetProperty("parameters").GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal);
        Assert.Equal(upstream, request.Parameters.AllKeys.OfType<string>().Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("did:web:feeds.example.com", "did:web:feeds.example.com#bsky_fg")]
    public async Task SendInteractionsAsync_ProxiesToTheFeedGeneratorOnlyWhenGiven(string? feedGenerator, string? proxy)
    {
        _fixture.On("app.bsky.feed.sendInteractions", "{}");

        await _fixture.Client.Bsky.Feed.SendInteractionsAsync(
            [new Interaction { Item = AtUri.Parse(PostUri), Event = InteractionEvent.Like }],
            feedGenerator: feedGenerator is null ? null : Did.Parse(feedGenerator));

        Assert.Equal(proxy, Assert.Single(_fixture.Requests).Proxy);
    }
}
