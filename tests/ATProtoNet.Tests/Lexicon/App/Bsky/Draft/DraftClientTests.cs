using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Draft;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Models;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;
using DraftModel = ATProtoNet.Lexicon.App.Bsky.Draft.Draft;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.Draft;

public sealed class DraftClientTests : IDisposable
{
    private const string DraftId = "3lwinfmsd2k2h";

    private readonly ScriptedXrpcHandler _handler = new();
    private readonly AtProtoClient _client;

    public DraftClientTests() => _client = _handler.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task CreateDraftAsync_PostsTheDraft_ReturnsItsId()
    {
        _handler.On("app.bsky.draft.createDraft", $$"""{"id":"{{DraftId}}"}""");

        var id = await _client.Bsky.Draft.CreateDraftAsync(new DraftModel
        {
            DeviceId = "4f1d0c1e-7c8b-4a57-9d0e-2b6f0c4e9a11",
            DeviceName = "iPhone",
            Langs = ["en"],
            Posts =
            [
                new DraftPost
                {
                    Text = "first",
                    EmbedGallery = new DraftEmbedGallery
                    {
                        Items = [new DraftEmbedImage { LocalRef = new DraftEmbedLocalRef { Path = "file:///a.jpg" }, Alt = "a" }],
                    },
                },
                new DraftPost
                {
                    Text = "second",
                    EmbedRecords = [new DraftEmbedRecord { Record = new StrongRef { Uri = AtUri.Parse(PostUri), Cid = Cid.Parse(PostCid) } }],
                },
            ],
            ThreadgateAllow = [new ThreadgateFollowingRule()],
            PostgateEmbeddingRules = [new PostgateDisableRule()],
        });

        Assert.Equal(Tid.Parse(DraftId), id);
        Assert.Equal(
            """{"draft":{"deviceId":"4f1d0c1e-7c8b-4a57-9d0e-2b6f0c4e9a11","deviceName":"iPhone","posts":[{"text":"first","embedGallery":{"items":[{"$type":"app.bsky.draft.defs#draftEmbedImage","localRef":{"path":"file:///a.jpg"},"alt":"a"}]}},{"text":"second","embedRecords":[{"record":{"uri":"POST_URI","cid":"POST_CID"}}]}],"langs":["en"],"postgateEmbeddingRules":[{"$type":"app.bsky.feed.postgate#disableRule"}],"threadgateAllow":[{"$type":"app.bsky.feed.threadgate#followingRule"}]}}"""
                .Replace("POST_URI", PostUri, StringComparison.Ordinal)
                .Replace("POST_CID", PostCid, StringComparison.Ordinal),
            Assert.Single(_handler.Requests).BodyText);
    }

    [Fact]
    public async Task UpdateDraftAsync_WrapsTheDraftWithItsId()
    {
        _handler.On("app.bsky.draft.updateDraft", "{}");

        await _client.Bsky.Draft.UpdateDraftAsync(Tid.Parse(DraftId), new DraftModel { Posts = [new DraftPost { Text = "edited" }] });

        Assert.Equal(
            """{"draft":{"id":"3lwinfmsd2k2h","draft":{"posts":[{"text":"edited"}]}}}""",
            Assert.Single(_handler.Requests).BodyText);
    }

    [Fact]
    public async Task DeleteDraftAsync_PostsTheId()
    {
        _handler.On("app.bsky.draft.deleteDraft", "{}");

        await _client.Bsky.Draft.DeleteDraftAsync(Tid.Parse(DraftId));

        Assert.Equal($$"""{"id":"{{DraftId}}"}""", Assert.Single(_handler.Requests).BodyText);
    }

    [Fact]
    public async Task GetDraftsAsync_BindsDraftViews()
    {
        _handler.On("app.bsky.draft.getDrafts", """
            {"cursor":"n","drafts":[{"id":"3lwinfmsd2k2h","createdAt":"2026-09-20T10:00:00.000Z","updatedAt":"2026-09-21T10:00:00.000Z",
              "draft":{"posts":[{"text":"a video","embedVideos":[{"localRef":{"path":"file:///v.mp4"},"alt":"clip","captions":[{"lang":"en","content":"WEBVTT"}]}],
                "embedExternals":[{"uri":"https://example.com"}],
                "labels":{"$type":"com.atproto.label.defs#selfLabels","values":[{"val":"graphic-media"}]},
                "embedGallery":{"items":[{"$type":"app.bsky.draft.defs#draftEmbedImage","localRef":{"path":"file:///g.jpg"}},{"$type":"app.bsky.draft.defs#draftEmbedFutureThing","x":1}]}}]}}]}
            """);

        var page = await _client.Bsky.Draft.GetDraftsAsync(limit: 1);

        Assert.Equal("limit=1", Assert.Single(_handler.Requests).Query);
        var view = Assert.Single(page.Drafts);
        Assert.Equal(DraftId, view.Id.Value);
        Assert.Equal("2026-09-21T10:00:00.000Z", view.UpdatedAt.ToString());
        var post = Assert.Single(view.Draft.Posts);
        var video = Assert.Single(post.EmbedVideos!);
        Assert.Equal("file:///v.mp4", video.LocalRef.Path);
        Assert.Equal("en", Assert.Single(video.Captions!).Lang);
        Assert.Equal("https://example.com", Assert.Single(post.EmbedExternals!).Uri);
        Assert.Equal("graphic-media", Assert.Single(post.Labels!.Values).Val);
        Assert.IsType<DraftEmbedImage>(post.EmbedGallery!.Items[0]);
        Assert.Equal("app.bsky.draft.defs#draftEmbedFutureThing", Assert.IsType<UnknownDraftGalleryItem>(post.EmbedGallery.Items[1]).Type);
    }

    [Fact]
    public async Task EnumerateDraftsAsync_FollowsTheCursor()
    {
        const string view = """{"id":"3lwinfmsd2k2h","createdAt":"2026-09-20T10:00:00.000Z","updatedAt":"2026-09-20T10:00:00.000Z","draft":{"posts":[{"text":"x"}]}}""";
        _handler
            .On("app.bsky.draft.getDrafts", $$"""{"cursor":"n","drafts":[{{view}}]}""")
            .On("app.bsky.draft.getDrafts", $$"""{"drafts":[{{view}}]}""");

        var drafts = await _client.Bsky.Draft.EnumerateDraftsAsync(pageSize: 1).ToListAsync();

        Assert.Equal(2, drafts.Count);
        Assert.Equal(["limit=1", "limit=1&cursor=n"], _handler.Requests.Select(r => r.Query));
    }
}
