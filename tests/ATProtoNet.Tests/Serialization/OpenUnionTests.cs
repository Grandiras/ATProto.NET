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
    private const string FutureType = "xrpc.doesNotExist.defs#futureVariant";

    private static string UnknownObject(string type) =>
        $$$"""{"$type":"{{{type}}}","weight":1.50,"label":"caf\u00e9", "nested":{"list":[1,2,3]}}""";

    /// <summary>Every open union base in the SDK, found by <see cref="AtProtoUnionAttribute"/> rather than a hand-picked list.</summary>
    public static TheoryData<Type> OpenUnionBases()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(AtProtoClient).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<AtProtoUnionAttribute>() is { UnknownVariant: not null })
            .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(OpenUnionBases))]
    public void Deserialize_UnknownVariant_ReadsAsUnknownAndWritesBackByteForByte(Type unionBase)
    {
        var json = UnknownObject(FutureType);

        var value = JsonSerializer.Deserialize(json, unionBase, Options);

        var unknown = Assert.IsAssignableFrom<IUnknownUnionVariant>(value);
        Assert.StartsWith("Unknown", value!.GetType().Name);
        Assert.Equal(FutureType, unknown.Type);
        Assert.Equal(json, unknown.Raw.GetRawText());
        Assert.Null(((LexObject)value).ExtensionData);
        Assert.Equal(json, JsonSerializer.Serialize(value, unionBase, Options));
    }

    [Theory]
    [MemberData(nameof(OpenUnionBases))]
    public void Serialize_UnknownVariantAsItsOwnType_WritesRaw(Type unionBase)
    {
        var json = UnknownObject(FutureType);
        var value = JsonSerializer.Deserialize(json, unionBase, Options)!;

        Assert.Equal(json, JsonSerializer.Serialize(value, value.GetType(), Options));
        var reread = (IUnknownUnionVariant)JsonSerializer.Deserialize(json, value.GetType(), Options)!;
        Assert.Equal(FutureType, reread.Type);
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
    /// exceptions are the firehose and label stream messages,
    /// whose variant is named by the event-stream frame header and which the stream parsers
    /// deserialize by that type, dropping unknown ones themselves, and
    /// <see cref="ATProtoNet.Auth.AtProtoSession"/>, the SDK's own persisted form rather than a
    /// Lexicon union.
    /// </summary>
    [Fact]
    public void SdkUnionBases_AreAllAtProtoUnionsWithAValidShape()
    {
        var polymorphic = typeof(AtProtoClient).Assembly.GetTypes()
            .Where(t => t.IsDefined(typeof(JsonPolymorphicAttribute), inherit: false)
                || t.IsDefined(typeof(JsonDerivedTypeAttribute), inherit: false))
            .Where(t => t != typeof(FirehoseMessage)
                && t != typeof(ATProtoNet.Lexicon.Com.AtProto.Label.LabelStreamMessage)
                && t != typeof(ATProtoNet.Auth.AtProtoSession))
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
        Assert.Equal(
            [typeof(ApplyWriteOperation), typeof(ATProtoNet.Lexicon.Com.AtProto.Space.SpaceWriteOp)],
            closed.OrderBy(t => t.Name, StringComparer.Ordinal));
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

    [Theory]
    [InlineData("""{"$type":"app.bsky.richtext.facet#mention","did":"not-a-did"}""", "$.did", nameof(ATProtoNet.Identity.Did))]
    [InlineData("""{"did":"not-a-did","$type":"app.bsky.richtext.facet#mention"}""", "$.did", nameof(ATProtoNet.Identity.Did))]
    [InlineData("""{"$type":"app.bsky.richtext.facet#link","uri":7}""", "$.uri", nameof(String))]
    public void Deserialize_InvalidMemberOfAVariant_NamesItsPathWithinTheVariant(string json, string path, string type)
    {
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FacetFeature>(json, Options));

        Assert.Equal(path, ex.Path);
        Assert.Contains(type, ex.Message, StringComparison.Ordinal);
    }

    // Unions nested nearly as deep as the reader allows, around a member no variant accepts. The expected
    // errors are what origin/main threw, where every union member went through JsonSerializer.Deserialize.
    [Theory]
    [InlineData("embed", false)]
    [InlineData("embed", true)]
    [InlineData("post embed", false)]
    [InlineData("post embed", true)]
    [InlineData("thread parent", false)]
    [InlineData("thread parent", true)]
    public async Task Deserialize_InvalidMemberUnderDeeplyNestedUnions_FailsFastWithTheErrorOfOneDeserialize(
        string nesting, bool onThreadPool)
    {
        const int depth = 60;
        const string embed = """{"$type":"app.bsky.embed.recordWithMedia","media":""";
        const string badExternal = """{"$type":"app.bsky.embed.external","external":5}""";
        var embeds = string.Concat(Enumerable.Repeat(embed, depth)) + badExternal + new string('}', depth);

        var (type, json, path, message) = nesting switch
        {
            "embed" => (typeof(EmbedBase), embeds, "$.external",
                "The JSON value could not be converted to ATProtoNet.Lexicon.App.Bsky.Embed.ExternalInfo. Path: $.external | LineNumber: 0 | BytePositionInLine: 47."),
            "post embed" => (typeof(PostRecord), $$"""{"text":"t","createdAt":"2024-01-01T00:00:00Z","embed":{{embeds}}}""", "$.external",
                "The JSON value could not be converted to ATProtoNet.Lexicon.App.Bsky.Embed.ExternalInfo. Path: $.external | LineNumber: 0 | BytePositionInLine: 47."),
            _ => (typeof(ThreadNode),
                string.Concat(Enumerable.Repeat("""{"$type":"app.bsky.feed.defs#threadViewPost","parent":""", depth))
                    + """{"$type":"app.bsky.feed.defs#notFoundPost","uri":"bad","notFound":true}""" + new string('}', depth),
                "$.uri",
                "The JSON value could not be converted to ATProtoNet.Identity.AtUri. Path: $.uri | LineNumber: 0 | BytePositionInLine: 54."),
        };

        // A thread-pool thread has the smallest stack a read gets; the other thread's stack is large
        // enough that only the time the read takes can fail it.
        var failure = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Read() => failure.SetResult(Record.Exception(() => JsonSerializer.Deserialize(json, type, Options)));
        if (onThreadPool)
            ThreadPool.QueueUserWorkItem(_ => Read());
        else
            new Thread(Read, maxStackSize: 256 << 20) { IsBackground = true }.Start();

        var ex = Assert.IsType<JsonException>(
            await failure.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(path, ex.Path);
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void Deserialize_EscapedDiscriminatorValue_IsMatchedUnescaped()
    {
        var tag = JsonSerializer.Deserialize<FacetFeature>(
            """{"$type":"app.bsky.richtext.facet\u0023tag","tag":"dotnet"}""", Options);

        Assert.Equal("dotnet", Assert.IsType<TagFeature>(tag).Tag);
    }

    [Fact]
    public void Deserialize_UtfEightDiscriminatorWithEscapes_IsMatchedUnescaped()
    {
        // "$type" written with a JSON escape still names the discriminator.
        var json = Encoding.UTF8.GetBytes("""{"\u0024type":"app.bsky.richtext.facet#tag","tag":"dotnet"}""");

        var tag = JsonSerializer.Deserialize<FacetFeature>(json, Options);

        Assert.Equal("dotnet", Assert.IsType<TagFeature>(tag).Tag);
    }

    // A mention and a tag, the mention read by a custom converter. origin/main confined that converter to
    // the mention's value and checked that it read all of it.
    private const string MentionThenTagPost = """
        {"text":"t","createdAt":"2024-01-01T00:00:00Z","facets":[{"index":{"byteStart":0,"byteEnd":1},"features":[
        {"$type":"app.bsky.richtext.facet#mention","did":"did:plc:a"},{"$type":"app.bsky.richtext.facet#tag","tag":"x"}]}]}
        """;

    [Fact]
    public void Deserialize_VariantConverterThatReadsPastItsValue_CannotReachTheNextMember()
    {
        var options = new JsonSerializerOptions(Options);
        options.Converters.Insert(0, new OverReadingConverter<MentionFeature>());

        var post = JsonSerializer.Deserialize<PostRecord>(MentionThenTagPost, options)!;

        Assert.Collection(
            Assert.Single(post.Facets!).Features,
            Assert.Null,
            feature => Assert.Equal("x", Assert.IsType<TagFeature>(feature).Tag));
    }

    [Fact]
    public void Deserialize_VariantConverterThatStopsShortOfItsValue_FailsAsDeserializeWould()
    {
        var options = new JsonSerializerOptions(Options);
        options.Converters.Insert(0, new UnderReadingConverter<MentionFeature>());

        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PostRecord>(MentionThenTagPost, options));

        Assert.Equal("$", ex.Path);
        Assert.Equal(
            $"The converter '{typeof(UnderReadingConverter<MentionFeature>)}' read too much or not enough. Path: $ | LineNumber: 0 | BytePositionInLine: 1.",
            ex.Message);
    }

    // A converter that catches an invalid union member and skips it, as origin/main left the reader at the
    // member's start: it reads on after the member, whether the union is the outermost one read or is
    // nested in a record-with-media embed, and whatever the error.
    [Theory]
    [InlineData(false, "\"bad\"", false)]
    [InlineData(false, "\"bad\"", true)]
    [InlineData(false, "5", false)]
    [InlineData(false, "5", true)]
    [InlineData(true, "\"bad\"", false)]
    [InlineData(true, "\"bad\"", true)]
    [InlineData(true, "5", false)]
    [InlineData(true, "5", true)]
    public void Deserialize_ConverterThatSkipsAnInvalidUnionMember_ReadsOnAfterTheMember(
        bool nested, string did, bool tokenByToken)
    {
        var images = $$"""
            {"$type":"app.bsky.embed.images",
             "feature":{"$type":"app.bsky.richtext.facet#mention","did":{{did}},"alt":"inside the feature"},
             "alt":"after the feature","images":[]}
            """;
        var json = nested
            ? $$$"""
                {"$type":"app.bsky.embed.recordWithMedia","record":{"record":{"uri":"at://did:plc:a/app.bsky.feed.post/3k2la",
                 "cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm"}},"media":{{{images}}}}
                """
            : images;
        var options = new JsonSerializerOptions(Options);
        options.Converters.Insert(0, new LenientImagesConverter(tokenByToken));

        var embed = JsonSerializer.Deserialize<EmbedBase>(json, options);

        var read = Assert.IsType<ImagesEmbed>(nested ? Assert.IsType<RecordWithMediaEmbed>(embed).Media : embed);
        Assert.Equal("after the feature", read.ExtensionData!["alt"].GetString());
    }

    // Reads an images embed's "feature" with the facet-feature union's converter, skipping it when it is
    // invalid, and keeps the first "alt" it meets. It reads member by member, or every token up to its
    // end, which ends in the right place even when a skip did not.
    private sealed class LenientImagesConverter(bool tokenByToken) : JsonConverter<ImagesEmbed>
    {
        public override ImagesEmbed Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var features = (JsonConverter<FacetFeature>)options.GetConverter(typeof(FacetFeature));
            var depth = reader.CurrentDepth;
            string? alt = null;
            while (reader.Read() && (tokenByToken
                ? reader.TokenType != JsonTokenType.EndObject || reader.CurrentDepth != depth
                : reader.TokenType == JsonTokenType.PropertyName))
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                var name = reader.GetString();
                reader.Read();
                if (name == "feature")
                {
                    try
                    {
                        features.Read(ref reader, typeof(FacetFeature), options);
                    }
                    catch (JsonException)
                    {
                        reader.Skip();
                    }
                }
                else if (name == "alt")
                {
                    alt ??= reader.GetString();
                }
                else if (!tokenByToken)
                {
                    reader.Skip();
                }
            }

            return new ImagesEmbed
            {
                Images = [],
                ExtensionData = new Dictionary<string, JsonElement> { ["alt"] = JsonSerializer.SerializeToElement(alt) },
            };
        }

        public override void Write(Utf8JsonWriter writer, ImagesEmbed value, JsonSerializerOptions options) => writer.WriteNullValue();
    }

    // Skips its value, then the one after it if the reader has one.
    private sealed class OverReadingConverter<T> : JsonConverter<T>
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            if (reader.Read())
                reader.Skip();

            return default;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteNullValue();
    }

    // Returns without reading its value.
    private sealed class UnderReadingConverter<T> : JsonConverter<T>
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => default;

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteNullValue();
    }
}
