using System.Text.Json;
using ATProtoNet.Data;
using ATProtoNet.Lexicon.Runtime;
using ATProtoNet.Lexicons;
using ATProtoNet.Lexicons.App.Bsky.Actor;
using Golden.Lexicons;
using Golden.Lexicons.Test.Golden;
using BskyPost = ATProtoNet.Lexicons.App.Bsky.Feed.Post;
using GoldenPost = Golden.Lexicons.Test.Golden.Post;

namespace ATProtoNet.Lexicon.Tests.Runtime;

// Generated records, objects, known values and optional fields (LEX-39–LEX-48, LEX-56–LEX-59, LEX-74).
public sealed class RecordTests
{
    private static T Read<T>(string json) where T : class, ILexiconType<T> => JsonSerializer.Deserialize(json, T.JsonTypeInfo)!;

    private static string Write<T>(T value) where T : class, ILexiconType<T> => JsonSerializer.Serialize(value, T.JsonTypeInfo);

    [Fact]
    [Trait("Spec", "LEX-57")]
    public void Write_Record_LeadsWithItsTypeOnce()
    {
        var json = Write(new BskyPost { Text = "hi", CreatedAt = AtDatetime.Parse("2024-01-01T00:00:00Z") });

        Assert.Equal("""{"$type":"app.bsky.feed.post","text":"hi","createdAt":"2024-01-01T00:00:00Z"}""", json);
    }

    [Fact]
    [Trait("Spec", "LEX-57")]
    public void Read_RecordWithTypeOfAnotherNsid_NeitherKeepsNorEchoesIt()
    {
        var post = Read<BskyPost>("""{"$type":"app.bsky.feed.like","text":"hi","createdAt":"2024-01-01T00:00:00Z"}""");

        Assert.Null(post.ExtensionData);
        Assert.StartsWith("""{"$type":"app.bsky.feed.post",""", Write(post));
    }

    [Fact]
    [Trait("Spec", "LEX-42")]
    public void ReadWrite_UnknownFieldsAtEveryLevel_RoundTrip()
    {
        const string json = """{"$type":"app.bsky.feed.post","text":"hi","reply":{"root":{"uri":"at://did:plc:x/app.bsky.feed.post/1","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm","n":1},"parent":{"uri":"at://did:plc:x/app.bsky.feed.post/1","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm"},"r":true},"createdAt":"2024-01-01T00:00:00Z","future":{"a":[1]}}""";

        var post = Read<BskyPost>(json);

        Assert.Equal(json, Write(post));
        Assert.Equal(1, post.Reply!.Root.ExtensionData!["n"].GetInt32());
    }

    [Fact]
    [Trait("Spec", "LEX-42")]
    public void Read_RecordWithoutUnknownFields_HasNoExtensionData() =>
        Assert.Null(Read<BskyPost>("""{"$type":"app.bsky.feed.post","text":"hi","createdAt":"2024-01-01T00:00:00Z"}""").ExtensionData);

    [Fact]
    [Trait("Spec", "LEX-42")]
    public void Write_ExtensionDataSetByTheCaller_IsWritten()
    {
        var post = new BskyPost { Text = "hi", CreatedAt = AtDatetime.Parse("2024-01-01T00:00:00Z"), ExtensionData = new Dictionary<string, JsonElement> { ["x"] = JsonDocument.Parse("1").RootElement } };

        Assert.EndsWith(""","x":1}""", Write(post));
    }

    [Fact]
    [Trait("Spec", "LEX-58")]
    public void Record_WithOptionalCreatedAt_ImplementsCreatedAtRecord()
    {
        var stamped = Stamp(new GoldenPost { Text = "x", Kind = "note" }, AtDatetime.Parse("2024-01-01T00:00:00Z"));

        Assert.Equal("2024-01-01T00:00:00Z", stamped.CreatedAt!.Value);
        Assert.DoesNotContain(typeof(BskyPost).GetInterfaces(), i => i.Name.StartsWith("ICreatedAtRecord", StringComparison.Ordinal)); // its createdAt is required

        static T Stamp<T>(T record, AtDatetime at) where T : ICreatedAtRecord<T> => T.WithCreatedAt(record, at);
    }

    [Fact]
    [Trait("Spec", "LEX-56")]
    public void Record_StaticContract_ComesFromTheLexicon()
    {
        Assert.Equal("app.bsky.actor.profile", Profile.Collection.Value);
        Assert.Equal(RecordKeyType.ForLiteral("self"), Profile.KeyType);
        Assert.Equal(RecordKeyType.Tid, BskyPost.KeyType);
    }

    [Fact]
    [Trait("Spec", "LEX-59")]
    public void As_UnknownValueOfTheRecordsType_ReadsTheRecord()
    {
        var value = DataModelValue.FromJson("""{"$type":"app.bsky.feed.post","text":"hi","createdAt":"2024-01-01T00:00:00Z"}"""u8);

        Assert.Equal("hi", value.As<BskyPost>().Text);
        Assert.False(value.TryAs<Profile>(out _));
        Assert.Throws<InvalidOperationException>(() => value.As<Profile>());
    }

    [Fact]
    [Trait("Spec", "LEX-59")]
    public void AsKnownRecord_RegisteredCollection_ReadsTheRecordItsTypeNames()
    {
        var registry = new LexiconTypeRegistry();
        BlueskyLexicons.Register(registry);
        var value = DataModelValue.FromJson("""{"$type":"app.bsky.actor.profile","displayName":"Alice"}"""u8);

        Assert.Equal("Alice", Assert.IsType<Profile>(value.AsKnownRecord(registry)).DisplayName);
        Assert.Null(DataModelValue.FromJson("""{"$type":"com.example.other"}"""u8).AsKnownRecord(registry));
        Assert.True(registry.TryGetRecordType(Nsid.Parse("app.bsky.feed.post"), out _));
    }

    [Fact]
    [Trait("Spec", "LEX-40")]
    public void Optional_AbsentNullAndValue_AreThreeStates()
    {
        var reference = new GoldenPost.ReplyRef { Root = Ref(), Parent = Ref() };

        Assert.DoesNotContain("pinned", Write(new GoldenPost { Text = "x", Kind = "note" }));
        Assert.Contains("\"pinned\":null", Write(new GoldenPost { Text = "x", Kind = "note", Pinned = new(null) }));
        Assert.Contains("\"pinned\":{\"root\"", Write(new GoldenPost { Text = "x", Kind = "note", Pinned = reference }));
        Assert.False(Read<GoldenPost>("""{"text":"x","kind":"note"}""").Pinned.HasValue);
        Assert.True(Read<GoldenPost>("""{"text":"x","kind":"note","pinned":null}""").Pinned is { HasValue: true, Value: null });
    }

    [Fact]
    [Trait("Spec", "LEX-40")]
    public void RequiredNullable_Null_IsWritten() =>
        Assert.Equal("""{"seq":1,"since":null}""", JsonSerializer.Serialize(new Subscribe.Commit { Seq = 1, Since = null }, Subscribe.Commit.JsonTypeInfo));

    [Fact]
    [Trait("Spec", "LEX-47")]
    public void KnownValues_AnyString_RoundTripsAndKnowsItsValues()
    {
        var post = Read<GoldenPost>("""{"text":"x","kind":"poem"}""");

        Assert.False(post.Kind.IsKnown);
        Assert.Equal("poem", (string)post.Kind);
        Assert.True(GoldenPost.KindValue.Article.IsKnown);
        Assert.Equal("test.golden.defs#reply", GoldenPost.KindValue.Reply.Value);
        Assert.Contains("\"kind\":\"poem\"", Write(post));
        Assert.Throws<JsonException>(() => Write(new GoldenPost { Text = "x", Kind = default }));
    }

    [Fact]
    [Trait("Spec", "LEX-48")]
    public void KnownValues_NamedStringDef_IsOneTypeForEveryRef() =>
        Assert.Equal(typeof(ReasonType), typeof(Image).GetProperty(nameof(Image.Reason))!.PropertyType.GetGenericArguments()[0]);

    [Fact]
    [Trait("Spec", "LEX-43")]
    public void Read_InvalidIdentifier_FailsWithItsPath()
    {
        var exception = Assert.Throws<JsonException>(() => Read<GoldenPost>("""{"text":"x","kind":"note","author":"not-a-did"}"""));

        Assert.Equal("$.author", exception.Path);
        Assert.IsType<FormatException>(exception.InnerException);
    }

    [Theory]
    [Trait("Spec", "LEX-44")]
    [InlineData("""{"text":"x","kind":"note","count":"5"}""")]
    [InlineData("""{"Text":"x","kind":"note"}""")]
    public void Read_NumberFromStringOrWrongCase_Throws(string json) => Assert.Throws<JsonException>(() => Read<GoldenPost>(json));

    [Fact]
    [Trait("Spec", "LEX-92")]
    public void FieldType_Decimal_IsADecimalStringKeptAsWritten()
    {
        var location = Read<GoldenPost.Location>("""{"latitude":"52.520"}""");

        Assert.Equal("52.520", location.Latitude.Value);
        Assert.Equal("""{"latitude":"52.520"}""", JsonSerializer.Serialize(location, GoldenPost.Location.JsonTypeInfo));
    }

    [Fact]
    [Trait("Spec", "LEX-74")]
    public void ToString_PrintsNoMembers()
    {
        var text = new GoldenPost { Text = "secret", Kind = "note" }.ToString();

        Assert.Equal("Post { $type = test.golden.post }", text);
        Assert.Equal("CreateThing.Input { $type = test.golden.createThing#input }", new CreateThing.Input { Name = "secret" }.ToString());
    }

    [Fact]
    [Trait("Spec", "LEX-30")]
    public void Lexicons_Resolver_HasEveryContract() =>
        Assert.NotNull(GoldenLexicons.Resolver.GetTypeInfo(typeof(GoldenPost), new JsonSerializerOptions()));

    private static StrongRef Ref() => new() { Uri = AtUri.Parse("at://did:plc:x/test.golden.post/1"), Cid = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm") };
}
