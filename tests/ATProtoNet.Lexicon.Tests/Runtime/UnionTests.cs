using System.Text.Json;
using ATProtoNet.Lexicon.Runtime;
using ATProtoNet.Lexicons.App.Bsky.Feed;
using Golden.Lexicons.Test.Golden;
using Golden.Lexicons.Test.Golden.Chat;
using BskyExternal = ATProtoNet.Lexicons.App.Bsky.Embed.External;
using External = Golden.Lexicons.Test.Golden.External;
using GoldenPost = Golden.Lexicons.Test.Golden.Post;
using Images = Golden.Lexicons.Test.Golden.Images;
using PostEmbed = Golden.Lexicons.Test.Golden.PostEmbed;

namespace ATProtoNet.Lexicon.Tests.Runtime;

// Generated unions read and write atproto's $type format (LEX-49–LEX-54, LEX-84–LEX-86), against generated types.
public sealed class UnionTests
{
    private const string PostView = """
        {"uri":"at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/app.bsky.feed.post/3kq5wcfxw4k2a","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
         "author":{"did":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","handle":"alice.test"},"record":{"$type":"app.bsky.feed.post","text":"hi","createdAt":"2024-01-01T00:00:00Z"},
         "indexedAt":"2024-01-01T00:00:00Z"%EMBED%}
        """;

    private static T Read<T>(string json) where T : class, ILexiconType<T> => JsonSerializer.Deserialize(json, T.JsonTypeInfo)!;

    private static string Write<T>(T value) where T : class, ILexiconType<T> => JsonSerializer.Serialize(value, T.JsonTypeInfo);

    private static SendMessage.Input ReadInput(string json) => JsonSerializer.Deserialize(json, SendMessage.Descriptor.Input!)!;

    [Fact]
    [Trait("Spec", "LEX-50")]
    public void Read_UnknownVariant_ReadsAsUnknownAndWritesBackUnchanged()
    {
        const string embed = """{"$type":"app.bsky.embed.poll#view","options":[1,2],"nested":{"a":"b"}}""";

        var view = Read<PostView>(PostView.Replace("%EMBED%", ",\"embed\":" + embed));

        var unknown = Assert.IsType<LexiconUnknown>(view.Embed.Value);
        Assert.Equal("app.bsky.embed.poll#view", unknown.Type);
        Assert.Contains(embed, Write(view));
    }

    [Fact]
    [Trait("Spec", "LEX-52")]
    public void Read_UnknownThreadNodeAmongReplies_KeepsTheKnownNodes()
    {
        var post = PostView.Replace("%EMBED%", "");
        var json = $$"""{"$type":"app.bsky.feed.defs#threadViewPost","post":{{post}},"replies":[{"$type":"app.bsky.feed.defs#notFoundPost","uri":"at://did:plc:x/app.bsky.feed.post/1","notFound":true},{"$type":"app.bsky.feed.defs#futureNode","x":1}]}""";

        var thread = Read<ThreadViewPost>(json);

        Assert.Collection(thread.Replies!, r => Assert.IsType<NotFoundPost>(r.Value), r => Assert.Equal("app.bsky.feed.defs#futureNode", Assert.IsType<LexiconUnknown>(r.Value).Type));
    }

    [Fact]
    [Trait("Spec", "LEX-51")]
    public void Read_TrailingTypeDiscriminator_ResolvesTheVariant()
    {
        var view = Read<PostView>(PostView.Replace("%EMBED%", ""","embed":{"external":{"uri":"https://example.com","title":"t","description":"d"},"$type":"app.bsky.embed.external#view"}"""));

        Assert.Equal("https://example.com", Assert.IsType<BskyExternal.View>(view.Embed.Value).External.Uri);
    }

    [Fact]
    [Trait("Spec", "LEX-51")]
    public void Read_EscapedDiscriminator_IsMatchedUnescaped()
    {
        var view = Read<PostView>(PostView.Replace("%EMBED%", ""","embed":{"$type":"app.bsky.embed.external#view","external":{"uri":"u","title":"t","description":"d"}}"""));

        Assert.IsType<BskyExternal.View>(view.Embed.Value);
    }

    [Fact]
    [Trait("Spec", "LEX-51")]
    public void Read_UnknownVariantOfClosedUnion_Throws()
    {
        var exception = Assert.Throws<JsonException>(() => ReadInput("""{"message":{"$type":"test.golden.embed#video"}}"""));

        Assert.Contains("closed union", exception.Message);
        Assert.Equal("$.message", exception.Path);
    }

    [Fact]
    [Trait("Spec", "LEX-51")]
    public void Read_KnownVariantOfClosedUnion_Works() =>
        Assert.IsType<External>(ReadInput("""{"message":{"$type":"test.golden.embed#external","uri":"u"}}""").Message.Value);

    [Theory]
    [Trait("Spec", "LEX-51")]
    [InlineData("""{"external":{"uri":"u","title":"t","description":"d"}}""")]
    [InlineData("""{"$type":5}""")]
    [InlineData("\"app.bsky.embed.external#view\"")]
    [InlineData("[]")]
    public void Read_MemberWithoutTypeOrNotAnObject_Throws(string embed) =>
        Assert.Throws<JsonException>(() => Read<PostView>(PostView.Replace("%EMBED%", ",\"embed\":" + embed)));

    [Fact]
    [Trait("Spec", "LEX-84")]
    public void Read_NullOptionalUnion_IsNoActiveCase()
    {
        var view = Read<PostView>(PostView.Replace("%EMBED%", ",\"embed\":null"));

        Assert.True(view.Embed is null);
        Assert.DoesNotContain("embed", Write(view));
    }

    [Theory]
    [Trait("Spec", "LEX-84")]
    [InlineData("{}")]
    [InlineData("""{"message":null}""")]
    public void Read_MissingOrNullRequiredUnion_Throws(string json) => Assert.Throws<JsonException>(() => ReadInput(json));

    [Fact]
    [Trait("Spec", "LEX-84")]
    public void Write_RequiredUnionLeftDefault_ThrowsNamingTheUnion()
    {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new SendMessage.Input { Message = default }, SendMessage.Descriptor.Input!));

        Assert.Contains(nameof(ChatMessage), exception.Message);
    }

    [Fact]
    [Trait("Spec", "LEX-54")]
    public void Write_KnownVariant_LeadsWithTypeInUnionPositionOnly()
    {
        var reference = new StrongRef { Uri = AtUri.Parse("at://did:plc:x/test.golden.post/1"), Cid = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm") };
        var post = new GoldenPost { Text = "x", Kind = "note", Embed = reference, Reply = new() { Root = reference, Parent = reference } };

        var json = Write(post);

        Assert.StartsWith("""{"$type":"test.golden.post","text":"x","kind":"note","embed":{"$type":"test.golden.defs#strongRef","uri":""", json);
        Assert.Contains("""reply":{"root":{"uri""", json);
    }

    [Fact]
    [Trait("Spec", "LEX-42")]
    public void Read_KnownVariant_DoesNotKeepTypeAsAnUnknownField()
    {
        var view = Read<PostView>(PostView.Replace("%EMBED%", ""","embed":{"$type":"app.bsky.embed.external#view","external":{"uri":"u","title":"t","description":"d"}}"""));

        Assert.Null(Assert.IsType<BskyExternal.View>(view.Embed.Value).ExtensionData);
    }

    [Fact]
    [Trait("Spec", "LEX-53")]
    public void Read_InvalidMemberOfAVariant_NamesItsPathWithinTheVariant()
    {
        var exception = Assert.Throws<JsonException>(() => Read<PostView>(PostView.Replace("%EMBED%", ""","embed":{"$type":"app.bsky.embed.external#view","external":{"uri":5}}""")));

        Assert.Equal("$.external.uri", exception.Path);
    }

    [Fact]
    [Trait("Spec", "LEX-53")]
    public void Read_InvalidMemberUnderNestedUnions_FailsWithTheInnermostPath()
    {
        var inner = """{"$type":"app.bsky.embed.record#viewRecord","uri":"at://did:plc:x/app.bsky.feed.post/1","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm","author":{"did":"bad","handle":"a.test"},"value":{},"indexedAt":"2024-01-01T00:00:00Z"}""";

        var exception = Assert.Throws<JsonException>(() => Read<PostView>(PostView.Replace("%EMBED%", $$""","embed":{"$type":"app.bsky.embed.record#view","record":{{inner}}}""")));

        Assert.Equal("$.author.did", exception.Path);
    }

    [Fact]
    [Trait("Spec", "LEX-50")]
    public void Unknown_Constructor_RejectsNonObjectsAndDetachesItsCopy()
    {
        Assert.Throws<ArgumentException>(() => new LexiconUnknown(JsonDocument.Parse("[]").RootElement));
        Assert.Throws<ArgumentException>(() => new LexiconUnknown(JsonDocument.Parse("""{"a":1}""").RootElement));
        LexiconUnknown unknown;
        using (var document = JsonDocument.Parse("""{"$type":"x.y.z","a":1}""")) unknown = new(document.RootElement);

        Assert.Equal("""{"$type":"x.y.z","a":1}""", unknown.Raw.GetRawText());
    }

    [Fact]
    [Trait("Spec", "LEX-86")]
    public void Unknown_FromAndTryAs_CarryAUserTypeThroughAnSdkUnion()
    {
        var external = new External { Uri = "https://example.com", Title = "t" };
        var embed = new PostEmbed(LexiconUnknown.From(external));
        var post = new GoldenPost { Text = "x", Kind = "note", Embed = embed };

        var read = Read<GoldenPost>(Write(post));

        // test.golden.embed#external is a case of PostEmbed, so it reads typed; the unknown wrapper wrote it unchanged.
        Assert.Equal("https://example.com", Assert.IsType<External>(read.Embed.Value).Uri);
        Assert.True(LexiconUnknown.From(external).TryAs<External>(out var back));
        Assert.Equal(external, back);
        Assert.False(LexiconUnknown.From(external).TryAs<Images>(out _));
    }

    [Fact]
    [Trait("Spec", "LEX-85")]
    public void Switch_OverEveryCase_NeedsNoDiscardForAClosedUnion()
    {
        static string Kind(ChatMessage message) => message switch
        {
            Images => "images",
            External => "external",
            null => "none",
        };

        Assert.Equal("external", Kind(new External { Uri = "u" }));
        Assert.Equal("none", Kind(default));
    }

    [Fact]
    [Trait("Spec", "LEX-86")]
    public void UnionExtensions_UserTypeInAForeignUnion_HasTypedHelpers()
    {
        var post = new GoldenPost { Text = "x", Kind = "note", Embed = PostEmbed.FromPoll(new Poll { Options = ["a", "b"] }) };

        var read = Read<GoldenPost>(Write(post));

        Assert.IsType<LexiconUnknown>(read.Embed.Value);
        Assert.True(read.Embed.TryGetPoll(out var poll));
        Assert.Equal(["a", "b"], poll.Options);
        Assert.False(new PostEmbed(new External { Uri = "u" }).TryGetPoll(out _));
    }
}
