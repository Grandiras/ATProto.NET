using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.App.Bsky.Graph;
using ATProtoNet.Lexicon.App.Bsky.Labeler;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Serialization;

/// <summary>
/// The Lexicon unions that used to be raw <see cref="JsonElement"/>s (#123): each reads its known
/// variants as types, keeps an unknown one whole, and writes both back. Fixtures are
/// upstream-shaped; the appview ones are captured from public.api.bsky.app and put
/// <c>$type</c> last, as the appview does.
/// </summary>
public class LexiconUnionTests
{
    private const string Did = "did:plc:z72i7hdynmk6r22z27h6tvur";
    private const string Cid = "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm";
    private const string Author = $$"""{"did":"{{Did}}","handle":"bsky.app"}""";

    private static readonly string Post =
        $$"""{"uri":"at://{{Did}}/app.bsky.feed.post/3k2la","cid":"{{Cid}}","author":{{Author}},"record":{},"indexedAt":"2026-09-18T22:41:20.071Z"}""";

    private static readonly JsonSerializerOptions Options = AtProtoJsonDefaults.Options;

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;

    [Fact]
    public void FeedViewPost_Reasons_ReadAsRepostAndPin()
    {
        var feed = Read<FeedResponse>($$"""
            {"feed":[
              {"post":{{Post}},"reason":{"$type":"app.bsky.feed.defs#reasonPin"} },
              {"post":{{Post}},"reason":{"by":{{Author}},"uri":"at://{{Did}}/app.bsky.feed.repost/3mvtagpxpgm2j","cid":"{{Cid}}","indexedAt":"2026-09-18T22:41:20.071Z","$type":"app.bsky.feed.defs#reasonRepost"} },
              {"post":{{Post}},"reqId":"req-1"}]}
            """);

        Assert.IsType<ReasonPin>(feed.Feed[0].Reason);
        var repost = Assert.IsType<ReasonRepost>(feed.Feed[1].Reason);
        Assert.Equal("bsky.app", repost.By.Handle.Value);
        Assert.Equal("3mvtagpxpgm2j", repost.Uri?.RecordKey);
        Assert.Null(feed.Feed[2].Reason);
        Assert.Equal("req-1", feed.Feed[2].ReqId);
    }

    [Fact]
    public void FeedViewPost_UnknownReason_IsKeptAndWrittenBack()
    {
        const string Reason = """{"$type":"app.bsky.feed.defs#reasonBoost","by":"someone"}""";

        var item = Read<FeedViewPost>($$"""{"post":{{Post}},"reason":{{Reason}} }""");

        var unknown = Assert.IsType<UnknownFeedReason>(item.Reason);
        Assert.Equal("app.bsky.feed.defs#reasonBoost", unknown.Type);
        Assert.Equal(Reason, JsonSerializer.Serialize<FeedReason>(unknown, Options));
    }

    [Fact]
    public void SkeletonFeedPost_Reasons_ReadAsTypes()
    {
        var skeleton = Read<GetFeedSkeletonResponse>($$"""
            {"feed":[
              {"post":"at://{{Did}}/app.bsky.feed.post/3k2la","reason":{"$type":"app.bsky.feed.defs#skeletonReasonRepost","repost":"at://{{Did}}/app.bsky.feed.repost/3k2lb"} },
              {"post":"at://{{Did}}/app.bsky.feed.post/3k2lc","reason":{"$type":"app.bsky.feed.defs#skeletonReasonPin"} }],
             "reqId":"req-2"}
            """);

        Assert.Equal("3k2lb", Assert.IsType<SkeletonReasonRepost>(skeleton.Feed[0].Reason).Repost.RecordKey);
        Assert.IsType<SkeletonReasonPin>(skeleton.Feed[1].Reason);
        Assert.Equal("req-2", skeleton.ReqId);
    }

    [Fact]
    public void ThreadgateRecord_Rules_RoundTripWithTheirTypes()
    {
        var json = $$"""
            {"$type":"app.bsky.feed.threadgate","post":"at://{{Did}}/app.bsky.feed.post/3k2la","allow":[
              {"$type":"app.bsky.feed.threadgate#mentionRule"},
              {"$type":"app.bsky.feed.threadgate#followerRule"},
              {"$type":"app.bsky.feed.threadgate#followingRule"},
              {"$type":"app.bsky.feed.threadgate#listRule","list":"at://{{Did}}/app.bsky.graph.list/3k2ld"},
              {"$type":"app.bsky.feed.threadgate#verifiedRule"}],
             "createdAt":"2026-09-21T18:08:17.808Z"}
            """;

        var gate = Read<ThreadgateRecord>(json);

        Assert.Collection(
            gate.Allow!,
            rule => Assert.IsType<ThreadgateMentionRule>(rule),
            rule => Assert.IsType<ThreadgateFollowerRule>(rule),
            rule => Assert.IsType<ThreadgateFollowingRule>(rule),
            rule => Assert.Equal("3k2ld", Assert.IsType<ThreadgateListRule>(rule).List.RecordKey),
            rule => Assert.Equal("app.bsky.feed.threadgate#verifiedRule", Assert.IsType<UnknownThreadgateRule>(rule).Type));
        AssertSameJson(json, JsonSerializer.Serialize(gate, Options));
    }

    [Fact]
    public void PostgateRecord_DisableRule_WritesItsType()
    {
        var gate = new PostgateRecord
        {
            Post = AtUri.Parse($"at://{Did}/app.bsky.feed.post/3k2la"),
            EmbeddingRules = [new PostgateDisableRule()],
            CreatedAt = AtDatetime.Parse("2026-09-21T18:08:17.808Z"),
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(gate, Options));

        var rule = Assert.Single(json.RootElement.GetProperty("embeddingRules").EnumerateArray());
        Assert.Equal("app.bsky.feed.postgate#disableRule", rule.GetProperty("$type").GetString());
    }

    [Fact]
    public void RecordView_EveryVariant_ReadsAsItsType()
    {
        var views = Read<List<EmbeddedRecordView>>($$"""
            [
              {"uri":"at://{{Did}}/app.bsky.feed.post/3muphbj35cs2h","cid":"{{Cid}}","author":{{Author}},"value":{"$type":"app.bsky.feed.post","text":"hi","createdAt":"2026-09-04T17:07:50.559Z"},"labels":[],"likeCount":910,"replyCount":380,"repostCount":309,"quoteCount":2075,"indexedAt":"2026-09-04T17:07:51.868Z","embeds":[{"external":{"uri":"https://bsky38.com/","title":"t","description":"d"},"$type":"app.bsky.embed.external#view"}],"$type":"app.bsky.embed.record#viewRecord"},
              {"$type":"app.bsky.embed.record#viewNotFound","uri":"at://{{Did}}/app.bsky.feed.post/3k2la","notFound":true},
              {"$type":"app.bsky.embed.record#viewBlocked","uri":"at://{{Did}}/app.bsky.feed.post/3k2la","blocked":true,"author":{"did":"{{Did}}","viewer":{"blockedBy":true} } },
              {"$type":"app.bsky.embed.record#viewDetached","uri":"at://{{Did}}/app.bsky.feed.post/3k2la","detached":true},
              {"$type":"app.bsky.feed.defs#generatorView","uri":"at://{{Did}}/app.bsky.feed.generator/whats-hot","cid":"{{Cid}}","did":"did:web:feed.example.com","creator":{{Author}},"displayName":"Hot","contentMode":"app.bsky.feed.defs#contentModeVideo","indexedAt":"2026-09-01T00:00:00.000Z"},
              {"$type":"app.bsky.graph.defs#listView","uri":"at://{{Did}}/app.bsky.graph.list/3k2ld","cid":"{{Cid}}","creator":{{Author}},"name":"L","purpose":"app.bsky.graph.defs#curatelist","indexedAt":"2026-09-01T00:00:00.000Z"},
              {"$type":"app.bsky.labeler.defs#labelerView","uri":"at://{{Did}}/app.bsky.labeler.service/self","cid":"{{Cid}}","creator":{{Author}},"indexedAt":"2026-09-01T00:00:00.000Z"},
              {"$type":"app.bsky.graph.defs#starterPackViewBasic","uri":"at://{{Did}}/app.bsky.graph.starterpack/3k2le","cid":"{{Cid}}","record":{},"creator":{{Author}},"indexedAt":"2026-09-01T00:00:00.000Z"},
              {"$type":"app.bsky.embed.record#viewSomethingNew","uri":"at://{{Did}}/app.bsky.feed.post/3k2la"}
            ]
            """);

        var post = Assert.IsType<EmbeddedRecord>(views[0]);
        Assert.Equal(2075, post.QuoteCount);
        Assert.IsType<ExternalView>(Assert.Single(post.Embeds!));
        Assert.Equal("hi", post.Value.GetProperty("text").GetString());
        Assert.IsType<EmbeddedRecordNotFound>(views[1]);
        Assert.True(Assert.IsType<EmbeddedRecordBlocked>(views[2]).Author.Viewer?.BlockedBy);
        Assert.IsType<EmbeddedRecordDetached>(views[3]);
        Assert.Equal(FeedContentMode.Video, Assert.IsType<GeneratorView>(views[4]).ContentMode);
        Assert.IsType<ListView>(views[5]);
        Assert.Equal("bsky.app", Assert.IsType<LabelerView>(views[6]).Creator.Handle.Value);
        Assert.IsType<StarterPackViewBasic>(views[7]);
        Assert.IsType<UnknownEmbeddedRecordView>(views[8]);
    }

    [Fact]
    public void GetRelationshipsResponse_Entries_ReadAsRelationshipOrNotFound()
    {
        // Captured from public.api.bsky.app (getRelationships, 2026-09-25), plus the block fields.
        var response = Read<GetRelationshipsResponse>($$"""
            {"actor":"{{Did}}","relationships":[
              {"actor":"jay.bsky.team","notFound":true,"$type":"app.bsky.graph.defs#notFoundActor"},
              {"did":"did:plc:aaaaaaaaaaaaaaaaaaaaaaaa","blocking":"at://{{Did}}/app.bsky.graph.block/3k2lf","blockedByList":"at://did:plc:aaaaaaaaaaaaaaaaaaaaaaaa/app.bsky.graph.listblock/3k2lg","$type":"app.bsky.graph.defs#relationship"}]}
            """);

        Assert.Equal("jay.bsky.team", Assert.IsType<NotFoundActor>(response.Relationships[0]).Actor.Value);
        var relationship = Assert.IsType<Relationship>(response.Relationships[1]);
        Assert.Equal("3k2lf", relationship.Blocking?.RecordKey);
        Assert.Equal("3k2lg", relationship.BlockedByList?.RecordKey);
    }

    [Fact]
    public void StarterPackView_Feeds_ReadAsGeneratorViews()
    {
        var pack = Read<StarterPackView>($$"""
            {"uri":"at://{{Did}}/app.bsky.graph.starterpack/3k2le","cid":"{{Cid}}","record":{},"creator":{{Author}},"indexedAt":"2026-09-01T00:00:00.000Z",
             "feeds":[{"uri":"at://{{Did}}/app.bsky.feed.generator/whats-hot","cid":"{{Cid}}","did":"did:web:feed.example.com","creator":{{Author}},"displayName":"Hot","indexedAt":"2026-09-01T00:00:00.000Z"}]}
            """);

        Assert.Equal("Hot", Assert.Single(pack.Feeds!).DisplayName);
    }

    [Fact]
    public void Preferences_KnownAndUnknown_ReadAsTypesAndWriteBackUnchanged()
    {
        const string Unknown = """{"$type":"app.bsky.actor.defs#somethingPref","keep":[1,2.50,"x"]}""";
        var json = $$"""
            {"preferences":[
              {"$type":"app.bsky.actor.defs#adultContentPref","enabled":true},
              {"$type":"app.bsky.actor.defs#contentLabelPref","labelerDid":"{{Did}}","label":"porn","visibility":"hide"},
              {"$type":"app.bsky.actor.defs#savedFeedsPrefV2","items":[{"id":"3k2lh","type":"timeline","value":"following","pinned":true}]},
              {"$type":"app.bsky.actor.defs#interestsPref","tags":["tech"],"updatedAt":"2026-09-03T00:00:00.000Z"},
              {"$type":"app.bsky.actor.defs#mutedWordsPref","items":[{"value":"spoiler","targets":["content","tag"],"actorTarget":"exclude-following"}]},
              {"$type":"app.bsky.actor.defs#bskyAppStatePref","isBetaUser":true,"nuxs":[{"id":"intro","completed":true}]},
              {"$type":"app.bsky.actor.defs#postInteractionSettingsPref","threadgateAllowRules":[{"$type":"app.bsky.feed.threadgate#followingRule"}],"postgateEmbeddingRules":[{"$type":"app.bsky.feed.postgate#disableRule"}]},
              {{Unknown}}]}
            """;

        var preferences = Read<GetPreferencesResponse>(json).Preferences;

        Assert.True(Assert.IsType<AdultContentPreference>(preferences[0]).Enabled);
        Assert.Equal("hide", Assert.IsType<ContentLabelPreference>(preferences[1]).Visibility);
        Assert.Equal("following", Assert.Single(Assert.IsType<SavedFeedsPreferenceV2>(preferences[2]).Items).Value);
        Assert.Equal("2026-09-03T00:00:00.000Z", Assert.IsType<InterestsPreference>(preferences[3]).UpdatedAt?.ToString());
        Assert.Equal(new[] { "content", "tag" }, Assert.Single(Assert.IsType<MutedWordsPreference>(preferences[4]).Items).Targets);
        Assert.True(Assert.IsType<BskyAppStatePreference>(preferences[5]).IsBetaUser);
        var interaction = Assert.IsType<PostInteractionSettingsPreference>(preferences[6]);
        Assert.IsType<ThreadgateFollowingRule>(Assert.Single(interaction.ThreadgateAllowRules!));
        Assert.IsType<PostgateDisableRule>(Assert.Single(interaction.PostgateEmbeddingRules!));
        Assert.Equal(Unknown, JsonSerializer.Serialize(preferences[7], Options));

        // putPreferences replaces the whole set, so a read-modify-write must keep every entry.
        AssertSameJson(json, JsonSerializer.Serialize(new { preferences }, Options));
    }

    [Fact]
    public void SubjectStatusView_HostingAndBlobDetails_ReadAsTypes()
    {
        var detail = Read<RecordViewDetail>($$"""
            {"uri":"at://{{Did}}/app.bsky.feed.post/3k2la","cid":"{{Cid}}","value":{},"indexedAt":"2026-09-01T00:00:00.000Z",
             "blobs":[
               {"cid":"{{Cid}}","mimeType":"image/jpeg","size":1024,"createdAt":"2026-09-01T00:00:00.000Z","details":{"$type":"tools.ozone.moderation.defs#imageDetails","width":640,"height":480} },
               {"cid":"{{Cid}}","mimeType":"video/mp4","size":2048,"createdAt":"2026-09-01T00:00:00.000Z","details":{"$type":"tools.ozone.moderation.defs#videoDetails","width":1920,"height":1080,"length":12} }],
             "moderation":{"subjectStatus":{"id":1,"subject":{"$type":"chat.bsky.convo.defs#messageRef","did":"{{Did}}","convoId":"c1","messageId":"m1"},"hosting":{"$type":"tools.ozone.moderation.defs#recordHosting","status":"deleted"},"createdAt":"2026-09-01T00:00:00.000Z","updatedAt":"2026-09-01T00:00:00.000Z","reviewState":"tools.ozone.moderation.defs#reviewOpen"} },
             "repo":{"did":"{{Did}}","handle":"bsky.app","relatedRecords":[],"indexedAt":"2026-09-01T00:00:00.000Z","moderation":{} } }
            """);

        Assert.Equal(480, Assert.IsType<ImageDetails>(detail.Blobs[0].Details).Height);
        Assert.Equal(12, Assert.IsType<VideoDetails>(detail.Blobs[1].Details).Length);
        var status = detail.Moderation.SubjectStatus!;
        Assert.Equal("m1", Assert.IsType<MessageSubject>(status.Subject).MessageId);
        Assert.Equal("deleted", Assert.IsType<RecordHosting>(status.Hosting).Status);
    }

    private static void AssertSameJson(string expected, string actual) =>
        Assert.True(
            JsonElement.DeepEquals(JsonDocument.Parse(expected).RootElement, JsonDocument.Parse(actual).RootElement),
            $"Expected {expected}\nActual   {actual}");
}
