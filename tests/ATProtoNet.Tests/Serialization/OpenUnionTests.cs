using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Lexicon.Com.AtProto.Repo;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Models;
using ATProtoNet.Serialization;
using OzoneHosting = ATProtoNet.Lexicon.Tools.Ozone.Hosting;
using OzoneModeration = ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using OzoneReport = ATProtoNet.Lexicon.Tools.Ozone.Report;
using ReportModeration = ATProtoNet.Lexicon.Com.AtProto.Moderation;

namespace ATProtoNet.Tests.Serialization;

/// <summary>
/// Lexicon unions are open unless marked closed: an unrecognized <c>$type</c> must not fail the
/// response it is part of, and must survive a read → write unchanged.
/// </summary>
public class OpenUnionTests
{
    private static readonly JsonSerializerOptions Options = AtProtoJsonDefaults.Options;

    // Deliberately awkward bytes: a non-canonical number, an escaped character and inner
    // whitespace would all change if the object were re-encoded instead of copied.
    private static string UnknownObject(string type) =>
        $$$"""{"$type":"{{{type}}}","weight":1.50,"label":"caf\u00e9", "nested":{"list":[1,2,3]}}""";

    public static TheoryData<Type, string> OpenUnionBases() => new()
    {
        { typeof(EmbedBase), "app.bsky.embed.future" },
        { typeof(EmbedView), "app.bsky.embed.future#view" },
        { typeof(GalleryItem), "app.bsky.embed.gallery#video" },
        { typeof(GalleryViewItem), "app.bsky.embed.gallery#viewVideo" },
        { typeof(FacetFeature), "app.bsky.richtext.facet#future" },
        { typeof(ThreadNode), "app.bsky.feed.defs#threadFuture" },
        { typeof(ReportModeration.ModerationSubject), "chat.bsky.convo.defs#futureRef" },
        { typeof(OzoneModeration.ModEventType), "tools.ozone.moderation.defs#futureEvent" },
        { typeof(OzoneModeration.ModerationSubjectView), "tools.ozone.moderation.defs#convoView" },
        { typeof(OzoneModeration.ScheduledAction), "tools.ozone.moderation.scheduleAction#label" },
        { typeof(OzoneReport.ReportActivity), "tools.ozone.report.defs#futureActivity" },
        { typeof(OzoneHosting.AccountHistoryDetails), "tools.ozone.hosting.getAccountHistory#futureChange" },
    };

    [Theory]
    [MemberData(nameof(OpenUnionBases))]
    public void Deserialize_UnknownVariant_ReadsAsUnknownAndWritesBackByteForByte(Type unionBase, string type)
    {
        var json = UnknownObject(type);

        var value = JsonSerializer.Deserialize(json, unionBase, Options);

        var unknown = Assert.IsAssignableFrom<IUnknownUnionVariant>(value);
        Assert.StartsWith("Unknown", value!.GetType().Name);
        Assert.Equal(type, unknown.Type);
        Assert.Equal(json, unknown.Raw.GetRawText());
        Assert.Null(((LexObject)value).ExtensionData);
        Assert.Equal(json, JsonSerializer.Serialize(value, unionBase, Options));
    }

    [Theory]
    [MemberData(nameof(OpenUnionBases))]
    public void Serialize_UnknownVariantAsItsOwnType_WritesRaw(Type unionBase, string type)
    {
        var json = UnknownObject(type);
        var value = JsonSerializer.Deserialize(json, unionBase, Options)!;

        Assert.Equal(json, JsonSerializer.Serialize(value, value.GetType(), Options));
        var reread = (IUnknownUnionVariant)JsonSerializer.Deserialize(json, value.GetType(), Options)!;
        Assert.Equal(type, reread.Type);
    }

    [Fact]
    public void Deserialize_UnknownEmbedInsidePostView_KeepsTheRestOfThePage()
    {
        var embed = UnknownObject("app.bsky.embed.future#view");
        var json =
            $$$"""
            {"feed":[
              {"post":{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm","author":{"did":"did:plc:a","handle":"a.test"},"record":{},"embed":{{{embed}}},"likeCount":3,"indexedAt":"2026-01-01T00:00:00Z"}},
              {"post":{"uri":"at://did:plc:b/app.bsky.feed.post/2","cid":"bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4","author":{"did":"did:plc:b","handle":"b.test"},"record":{},"indexedAt":"2026-01-01T00:00:00Z"}}
            ]}
            """;

        var page = JsonSerializer.Deserialize<FeedResponse>(json, Options)!;

        Assert.Equal(2, page.Feed.Count);
        Assert.Equal(3, page.Feed[0].Post.LikeCount);
        var unknown = Assert.IsType<UnknownEmbedView>(page.Feed[0].Post.Embed);
        Assert.Equal("app.bsky.embed.future#view", unknown.Type);
        Assert.Contains(embed, JsonSerializer.Serialize(page, Options));
    }

    [Fact]
    public void Deserialize_UnknownThreadNodeAmongReplies_KeepsTheKnownNodes()
    {
        const string json =
            """
            {"thread":{"$type":"app.bsky.feed.defs#threadViewPost",
              "post":{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm","author":{"did":"did:plc:a","handle":"a.test"},"record":{},"indexedAt":"2026-01-01T00:00:00Z"},
              "replies":[
                {"$type":"app.bsky.feed.defs#threadFuture","uri":"at://did:plc:b/app.bsky.feed.post/2"},
                {"uri":"at://did:plc:c/app.bsky.feed.post/3","notFound":true,"$type":"app.bsky.feed.defs#notFoundPost"}
              ]}}
            """;

        var thread = JsonSerializer.Deserialize<GetPostThreadResponse>(json, Options)!;

        var root = Assert.IsType<ThreadViewPost>(thread.Thread);
        Assert.IsType<UnknownThreadNode>(root.Replies![0]);
        Assert.Equal("at://did:plc:c/app.bsky.feed.post/3", Assert.IsType<NotFoundPost>(root.Replies[1]).Uri);
    }

    [Fact]
    public void Deserialize_OzoneEventTypeTheSdkDoesNotModel_ReadsAsUnknownModEvent()
    {
        // Shape of a real Ozone queryEvents entry for an event type added after this SDK's models.
        const string json =
            """
            {"id":7,"event":{"$type":"tools.ozone.moderation.defs#futureEvent","handle":"new.example.com","timestamp":"2026-01-01T00:00:00Z"},
             "subject":{"$type":"com.atproto.admin.defs#repoRef","did":"did:plc:a"},
             "subjectBlobCids":[],"createdBy":"did:plc:mod","createdAt":"2026-01-01T00:00:00Z"}
            """;

        var view = JsonSerializer.Deserialize<OzoneModeration.ModEventView>(json, Options)!;

        var unknown = Assert.IsType<OzoneModeration.UnknownModEvent>(view.Event);
        Assert.Equal("tools.ozone.moderation.defs#futureEvent", unknown.Type);
        Assert.Equal("did:plc:a", Assert.IsType<ReportModeration.RepoSubject>(view.Subject).Did);
    }

    [Fact]
    public void Deserialize_UnknownVariantOfClosedUnion_Throws()
    {
        const string json = """{"$type":"com.atproto.repo.applyWrites#future","collection":"a.b.c"}""";

        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApplyWriteOperation>(json, Options));
        Assert.Contains("closed", ex.Message);
    }

    [Fact]
    public void Deserialize_KnownVariantOfClosedUnion_Works()
    {
        const string json = """{"collection":"a.b.c","rkey":"k","$type":"com.atproto.repo.applyWrites#delete"}""";

        var op = Assert.IsType<ApplyWriteDelete>(JsonSerializer.Deserialize<ApplyWriteOperation>(json, Options));

        Assert.Equal("k", op.Rkey);
        Assert.Equal("""{"$type":"com.atproto.repo.applyWrites#delete","collection":"a.b.c","rkey":"k"}""",
            JsonSerializer.Serialize<ApplyWriteOperation>(op, Options));
    }

    [Fact]
    public void Deserialize_UnionMemberWithoutType_Throws()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmbedBase>("""{"images":[]}""", Options));
    }

    [Fact]
    public void Deserialize_UnionMemberThatIsNotAnObject_Throws()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmbedBase>("\"app.bsky.embed.images\"", Options));
    }

    [Fact]
    public void Deserialize_NullUnionMember_ReadsAsNull()
    {
        var post = JsonSerializer.Deserialize<PostRecord>(
            """{"text":"t","createdAt":"2026-01-01T00:00:00Z","embed":null}""", Options)!;

        Assert.Null(post.Embed);
    }

    [Fact]
    public void Serialize_KnownVariant_LeadsWithTypeWhetherWrittenAsBaseOrOnItsOwn()
    {
        var link = new LinkFeature { Uri = "https://example.com" };
        const string expected = """{"$type":"app.bsky.richtext.facet#link","uri":"https://example.com"}""";

        Assert.Equal(expected, JsonSerializer.Serialize<FacetFeature>(link, Options));
        Assert.Equal(expected, JsonSerializer.Serialize(link, Options));
    }

    [Fact]
    public void Deserialize_KnownVariant_DoesNotKeepTypeAsAnUnknownField()
    {
        var link = JsonSerializer.Deserialize<FacetFeature>(
            """{"uri":"https://example.com","$type":"app.bsky.richtext.facet#link"}""", Options);

        Assert.Null(Assert.IsType<LinkFeature>(link).ExtensionData);
    }

    [Fact]
    public void UnknownVariant_Constructor_RejectsNonObjects()
    {
        using var doc = JsonDocument.Parse("[1]");

        Assert.Throws<ArgumentException>(() => new UnknownEmbed("app.bsky.embed.future", doc.RootElement));
        Assert.Throws<ArgumentException>(() => new UnknownEmbed("", default));
    }

    [Fact]
    public void UnknownVariant_Constructor_DetachesRawFromTheCallersDocument()
    {
        UnknownEmbed embed;
        using (var doc = JsonDocument.Parse("""{"$type":"app.bsky.embed.future","x":1}"""))
            embed = new UnknownEmbed("app.bsky.embed.future", doc.RootElement);

        Assert.Equal("""{"$type":"app.bsky.embed.future","x":1}""", JsonSerializer.Serialize<EmbedBase>(embed, Options));
    }

    /// <summary>
    /// Guards the rule the SDK's models follow: every <c>$type</c>-discriminated union is an
    /// <see cref="AtProtoUnionAttribute"/> base, open unless the Lexicon marks it closed. The
    /// exceptions are Spaces (owned by the Spaces work), the firehose message frame, which is
    /// transcoded from CBOR by a parser that drops unknown frame types itself, and
    /// <see cref="ATProtoNet.Auth.AtProtoSession"/>, the SDK's own persisted form rather than a
    /// Lexicon union.
    /// </summary>
    [Fact]
    public void SdkUnionBases_AreAllAtProtoUnionsWithAValidShape()
    {
        var polymorphic = typeof(AtProtoClient).Assembly.GetTypes()
            .Where(t => t.IsDefined(typeof(JsonPolymorphicAttribute), inherit: false)
                || t.IsDefined(typeof(JsonDerivedTypeAttribute), inherit: false))
            .Where(t => t != typeof(FirehoseMessage) && t != typeof(ATProtoNet.Auth.AtProtoSession)
                && !t.Namespace!.StartsWith("ATProtoNet.Lexicon.Com.AtProto.Space", StringComparison.Ordinal)
                && !t.Namespace!.StartsWith("ATProtoNet.Lexicon.Com.AtProto.SimpleSpace", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(polymorphic);
        Assert.All(polymorphic, t =>
        {
            var attribute = t.GetCustomAttribute<AtProtoUnionAttribute>();
            Assert.True(attribute is not null, $"{t.FullName} is a union base without [AtProtoUnion]");
            Assert.NotNull(AtProtoUnionShape.Get(t));
            Assert.False(t.IsDefined(typeof(JsonPolymorphicAttribute), inherit: false),
                $"{t.FullName} keeps [JsonPolymorphic], which [AtProtoUnion] replaces");
        });

        var closed = polymorphic.Where(t => t.GetCustomAttribute<AtProtoUnionAttribute>()!.Closed).ToList();
        Assert.Equal([typeof(ApplyWriteOperation)], closed);
    }

    [Fact]
    public void Options_AreReadOnly()
    {
        Assert.True(AtProtoJsonDefaults.Options.IsReadOnly);
        Assert.Throws<InvalidOperationException>(
            () => AtProtoJsonDefaults.Options.Converters.Add(new JsonStringEnumConverter()));
    }

    [Fact]
    public void Options_CopyKeepsUnionAndUnknownFieldHandling()
    {
        var indented = new JsonSerializerOptions(AtProtoJsonDefaults.Options) { WriteIndented = true };

        var value = JsonSerializer.Deserialize<EmbedBase>(UnknownObject("app.bsky.embed.future"), indented);

        Assert.IsType<UnknownEmbed>(value);
    }

    [Fact]
    public void PlainOptions_StillReadKnownVariantsThroughJsonDerivedType()
    {
        // Options built from scratch have no union converter; STJ's own polymorphism then reads
        // the declared variants, strictly.
        var plain = new JsonSerializerOptions { AllowOutOfOrderMetadataProperties = true };

        var link = JsonSerializer.Deserialize<FacetFeature>(
            """{"uri":"https://example.com","$type":"app.bsky.richtext.facet#link"}""", plain);

        Assert.IsType<LinkFeature>(link);
    }

    [Fact]
    public void Deserialize_UtfEightDiscriminatorWithEscapes_IsMatchedUnescaped()
    {
        // "$type" written with a JSON escape still names the discriminator.
        var json = Encoding.UTF8.GetBytes("""{"\u0024type":"app.bsky.richtext.facet#tag","tag":"dotnet"}""");

        var tag = JsonSerializer.Deserialize<FacetFeature>(json, Options);

        Assert.Equal("dotnet", Assert.IsType<TagFeature>(tag).Tag);
    }
}
