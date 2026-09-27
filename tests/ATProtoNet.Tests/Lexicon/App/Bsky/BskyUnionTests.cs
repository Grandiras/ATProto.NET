using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.AgeAssurance;
using ATProtoNet.Lexicon.App.Bsky.Draft;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.App.Bsky.Unspecced;
using ATProtoNet.Models;
using ATProtoNet.Serialization;
using ATProtoNet.Tests.TestSupport;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;
using DraftModel = ATProtoNet.Lexicon.App.Bsky.Draft.Draft;

namespace ATProtoNet.Tests.Lexicon.App.Bsky;

/// <summary>
/// The app.bsky unions the newer endpoints added, one representative call per union, against
/// appview-shaped responses. The plain request/response calls are rows in
/// <see cref="EndpointRequestTests"/>; the older unions are in
/// <see cref="ATProtoNet.Tests.Serialization.LexiconUnionTests"/>.
/// </summary>
public sealed class BskyUnionTests : IDisposable
{
    private readonly XrpcTestClient _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task GetBookmarksAsync_BindsEveryItemVariant()
    {
        _fixture.On("app.bsky.bookmark.getBookmarks", $$$"""
            {"cursor":"c2","bookmarks":[
              {"subject":{"uri":"{{{PostUri}}}","cid":"{{{PostCid}}}"},"createdAt":"2026-09-21T08:00:00.000Z","item":{{{PostViewJson}}}},
              {"subject":{"uri":"{{{OtherPostUri}}}","cid":"{{{OtherCid}}}"},"item":{"$type":"app.bsky.feed.defs#notFoundPost","uri":"{{{OtherPostUri}}}","notFound":true}},
              {"subject":{"uri":"{{{OtherPostUri}}}","cid":"{{{OtherCid}}}"},"item":{"$type":"app.bsky.feed.defs#blockedPost","uri":"{{{OtherPostUri}}}","blocked":true,"author":{"did":"{{{BobDid}}}","viewer":{"blocking":"at://{{{AliceDid}}}/app.bsky.graph.block/3lwinfmsd2k2g"} } } }
            ]}
            """);

        var page = await _fixture.Client.Bsky.Bookmark.GetBookmarksAsync(limit: 10, cursor: "c1");

        _fixture.AssertGet("app.bsky.bookmark.getBookmarks", "limit=10&cursor=c1");
        var post = Assert.IsType<PostView>(page.Bookmarks[0].Item);
        Assert.Equal(3, post.BookmarkCount);
        Assert.True(post.Viewer!.Bookmarked);
        Assert.Equal(OtherPostUri, Assert.IsType<NotFoundPost>(page.Bookmarks[1].Item).Uri.Value);
        Assert.Equal(BobDid, Assert.IsType<BlockedPost>(page.Bookmarks[2].Item).Author.Did.Value);
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

    [Fact]
    public async Task CreateDraftAsync_PostsTheDraft_ReturnsItsId()
    {
        _fixture.On("app.bsky.draft.createDraft", """{"id":"3lwinfmsd2k2h"}""");

        var id = await _fixture.Client.Bsky.Draft.CreateDraftAsync(new DraftModel
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

        Assert.Equal(Tid.Parse("3lwinfmsd2k2h"), id);
        _fixture.AssertPost(
            "app.bsky.draft.createDraft",
            $$$"""
            {"draft":{"deviceId":"4f1d0c1e-7c8b-4a57-9d0e-2b6f0c4e9a11","deviceName":"iPhone","langs":["en"],
              "posts":[{"text":"first","embedGallery":{"items":[{"$type":"app.bsky.draft.defs#draftEmbedImage","localRef":{"path":"file:///a.jpg"},"alt":"a"}]}},
                       {"text":"second","embedRecords":[{"record":{"uri":"{{{PostUri}}}","cid":"{{{PostCid}}}"}}]}],
              "postgateEmbeddingRules":[{"$type":"app.bsky.feed.postgate#disableRule"}],
              "threadgateAllow":[{"$type":"app.bsky.feed.threadgate#followingRule"}]}}
            """);
    }

    [Fact]
    public async Task GetPostThreadV2Async_SendsTheShape_BindsAFlatThread()
    {
        _fixture.On("app.bsky.unspecced.getPostThreadV2", $$$"""
            {"hasOtherReplies":true,
             "threadgate":{"uri":"at://{{{AliceDid}}}/app.bsky.feed.threadgate/3lwinfmsd2k2a","cid":"{{{OtherCid}}}","record":{"$type":"app.bsky.feed.threadgate","post":"{{{PostUri}}}","allow":[],"createdAt":"2026-09-20T12:00:00.000Z"},"lists":[]},
             "thread":[
              {"uri":"{{{OtherPostUri}}}","depth":-1,"value":{"$type":"app.bsky.unspecced.defs#threadItemBlocked","author":{"did":"{{{BobDid}}}","viewer":{"blocking":"at://{{{AliceDid}}}/app.bsky.graph.block/3lwinfmsd2k2g"} } } },
              {"uri":"{{{PostUri}}}","depth":0,"value":{"post":{{{PostViewJson}}},"moreParents":false,"moreReplies":0,"opThread":true,"opThreadPostIndex":1,"opThreadPostCount":3,"hiddenByThreadgate":false,"mutedByViewer":false,"$type":"app.bsky.unspecced.defs#threadItemPost"}},
              {"uri":"{{{OtherPostUri}}}","depth":1,"value":{"$type":"app.bsky.unspecced.defs#threadItemNoUnauthenticated"}},
              {"uri":"{{{OtherPostUri}}}","depth":1,"value":{"$type":"app.bsky.unspecced.defs#threadItemNotFound"}}
             ]}
            """);

        var response = await _fixture.Client.Bsky.Unspecced.GetPostThreadV2Async(
            AtUri.Parse(PostUri), above: false, below: 3, branchingFactor: 5, sort: PostThreadSort.Top);

        _fixture.AssertGet("app.bsky.unspecced.getPostThreadV2", $"anchor={PostUri}&above=false&below=3&branchingFactor=5&sort=top");
        Assert.Equal([-1, 0, 1, 1], response.Thread.Select(i => i.Depth));
        Assert.Equal(BobDid, Assert.IsType<ThreadItemBlocked>(response.Thread[0].Value).Author.Did.Value);
        var anchor = Assert.IsType<ThreadItemPost>(response.Thread[1].Value);
        Assert.Equal(PostUri, anchor.Post.Uri.Value);
        Assert.Equal((true, 1, 3), (anchor.OpThread, anchor.OpThreadPostIndex, anchor.OpThreadPostCount));
        Assert.IsType<ThreadItemNoUnauthenticated>(response.Thread[2].Value);
        Assert.IsType<ThreadItemNotFound>(response.Thread[3].Value);
    }

    [Fact]
    public async Task AgeAssuranceGetConfigAsync_BindsRegionsAndEveryRuleKind()
    {
        _fixture.On("app.bsky.ageassurance.getConfig", """
            {"regions":[
              {"countryCode":"GB","minAccessAge":13,"rules":[
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfAssuredOverAge","age":18,"access":"full"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfAssuredUnderAge","age":18,"access":"safe"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfDeclaredOverAge","age":18,"access":"full"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfDeclaredUnderAge","age":13,"access":"none"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfAccountNewerThan","date":"2025-07-25T00:00:00.000Z","access":"safe"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleIfAccountOlderThan","date":"2025-07-25T00:00:00.000Z","access":"full"},
                {"$type":"app.bsky.ageassurance.defs#configRegionRuleDefault","access":"safe"}]},
              {"countryCode":"US","regionCode":"TX","platforms":["ios","android"],"minAccessAge":13,"additionalVerificationMethods":["device"],"rules":[]}
            ]}
            """);

        var config = await _fixture.Client.Bsky.AgeAssurance.GetConfigAsync();

        _fixture.AssertGet("app.bsky.ageassurance.getConfig");
        var rules = config.Regions[0].Rules;
        Assert.Equal(18, Assert.IsType<AssuredOverAgeRule>(rules[0]).Age);
        Assert.Equal(AgeAssuranceAccess.Safe, Assert.IsType<AssuredUnderAgeRule>(rules[1]).Access);
        Assert.IsType<DeclaredOverAgeRule>(rules[2]);
        Assert.Equal(AgeAssuranceAccess.None, Assert.IsType<DeclaredUnderAgeRule>(rules[3]).Access);
        Assert.Equal("2025-07-25T00:00:00.000Z", Assert.IsType<AccountNewerThanRule>(rules[4]).Date.ToString());
        Assert.IsType<AccountOlderThanRule>(rules[5]);
        Assert.Equal(AgeAssuranceAccess.Safe, Assert.IsType<DefaultAgeRule>(rules[6]).Access);
        Assert.Equal(["ios", "android"], config.Regions[1].Platforms);
    }
}
