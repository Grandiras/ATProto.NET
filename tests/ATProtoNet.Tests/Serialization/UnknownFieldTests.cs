using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Serialization;

/// <summary>
/// Fields a model does not declare survive deserialize → serialize (Lexicon spec: do not clobber
/// data from newer schema revisions on a read-modify-write).
/// </summary>
public class UnknownFieldTests
{
    private static readonly JsonSerializerOptions Options = AtProtoJsonDefaults.Options;

    private sealed class TodoItem : AtProtoRecord
    {
        public override string Type => "com.example.todo.item";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";
    }

    private static void AssertSameJson(string expected, string actual)
    {
        using var e = JsonDocument.Parse(expected);
        using var a = JsonDocument.Parse(actual);
        Assert.True(JsonElement.DeepEquals(e.RootElement, a.RootElement), $"expected {expected}\nactual   {actual}");
    }

    [Fact]
    public void PostRecord_UnknownFieldsAtEveryLevel_RoundTrip()
    {
        const string json =
            """
            {"$type":"app.bsky.feed.post","text":"hello","createdAt":"2026-01-01T00:00:00.000Z",
             "futureTopLevel":{"nested":[1,"two",null]},
             "facets":[{"index":{"byteStart":0,"byteEnd":5,"futureIndexField":true},"features":[{"$type":"app.bsky.richtext.facet#tag","tag":"hi","futureFeatureField":1}]}],
             "embed":{"$type":"app.bsky.embed.images","futureEmbedField":"x","images":[{"alt":"a","futureImageField":2,"image":{"$type":"blob","ref":{"$link":"bafkreihbqg3ubrh6dzs2egfd3fxptl4cpn7fjrtxvutzsvtz7z4ixyzqbe"},"mimeType":"image/jpeg","size":1},"aspectRatio":{"width":1,"height":1}}]},
             "reply":{"root":{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm","futureRefField":"r"},"parent":{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm"}}}
            """;

        var post = JsonSerializer.Deserialize<PostRecord>(json, Options)!;

        Assert.Equal(["futureTopLevel"], post.ExtensionData!.Keys);
        Assert.Equal(["futureEmbedField"], Assert.IsType<ImagesEmbed>(post.Embed).ExtensionData!.Keys);
        Assert.Equal(["futureRefField"], post.Reply!.Root.ExtensionData!.Keys);
        Assert.Null(post.Reply.Parent.ExtensionData);
        AssertSameJson(json, JsonSerializer.Serialize(post, Options));
    }

    [Fact]
    public void PostRecord_TypeIsNeverCapturedAsAnUnknownField()
    {
        var post = JsonSerializer.Deserialize<PostRecord>(
            """{"text":"t","createdAt":"2026-01-01T00:00:00Z","$type":"app.bsky.feed.post"}""", Options)!;

        Assert.Null(post.ExtensionData);
    }

    [Fact]
    public void AtProtoRecord_UnknownFields_RoundTripWithoutDuplicatingType()
    {
        const string json =
            """{"$type":"com.example.todo.item","title":"Buy milk","createdAt":"2026-01-01T00:00:00.000Z","priority":3,"tags":["a"]}""";

        var todo = JsonSerializer.Deserialize<TodoItem>(json, Options)!;

        Assert.Equal(["priority", "tags"], todo.ExtensionData!.Keys.Order());
        var written = JsonSerializer.Serialize(todo, Options);
        Assert.Equal(
            """{"$type":"com.example.todo.item","title":"Buy milk","createdAt":"2026-01-01T00:00:00.000Z","priority":3,"tags":["a"]}""",
            written);
    }

    [Fact]
    public void AtProtoRecord_WithoutUnknownFields_HasNoExtensionData()
    {
        var todo = JsonSerializer.Deserialize<TodoItem>(
            """{"$type":"com.example.todo.item","title":"t","createdAt":"2026-01-01T00:00:00.000Z"}""", Options)!;

        Assert.Null(todo.ExtensionData);
    }

    [Fact]
    public void AtProtoRecord_TypeFromAnotherNsid_IsNotKeptOrEchoed()
    {
        // The record's own $type wins on write; a mismatched incoming one is dropped, not duplicated.
        var todo = JsonSerializer.Deserialize<TodoItem>(
            """{"$type":"com.example.other","title":"t","createdAt":"2026-01-01T00:00:00.000Z"}""", Options)!;

        Assert.Null(todo.ExtensionData);
        Assert.StartsWith("""{"$type":"com.example.todo.item",""", JsonSerializer.Serialize(todo, Options));
    }

    [Fact]
    public void ProfileRecord_AllUpstreamFieldsAndUnknownOnes_RoundTrip()
    {
        const string json =
            """
            {"$type":"app.bsky.actor.profile","displayName":"Alice","description":"hi","pronouns":"she/her","website":"https://alice.example.com",
             "avatar":{"$type":"blob","ref":{"$link":"bafkreihbqg3ubrh6dzs2egfd3fxptl4cpn7fjrtxvutzsvtz7z4ixyzqbe"},"mimeType":"image/jpeg","size":1},
             "labels":{"$type":"com.atproto.label.defs#selfLabels","values":[{"val":"!no-unauthenticated"}]},
             "joinedViaStarterPack":{"uri":"at://did:plc:b/app.bsky.graph.starterpack/1","cid":"bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4"},
             "pinnedPost":{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm"},
             "createdAt":"2024-01-01T00:00:00.000Z","futureField":{"x":1}}
            """;

        var profile = JsonSerializer.Deserialize<ProfileRecord>(json, Options)!;

        Assert.Equal("she/her", profile.Pronouns);
        Assert.Equal("https://alice.example.com", profile.Website);
        Assert.Equal("!no-unauthenticated", profile.Labels!.Values[0].Val);
        Assert.Equal("bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4", profile.JoinedViaStarterPack!.Cid);
        Assert.Equal(["futureField"], profile.ExtensionData!.Keys);
        AssertSameJson(json, JsonSerializer.Serialize(profile, Options));
    }

    [Fact]
    public void View_UnknownField_IsReadable()
    {
        var author = JsonSerializer.Deserialize<ProfileViewBasic>(
            """{"did":"did:plc:a","handle":"a.test","pronouns":"they/them"}""", Options)!;

        Assert.Equal("they/them", author.ExtensionData!["pronouns"].GetString());
    }

    [Fact]
    public void ExtensionData_SetByTheCaller_IsWritten()
    {
        var aspect = new AspectRatio { Width = 1, Height = 2 };
        aspect.ExtensionData = new Dictionary<string, JsonElement>
        {
            ["depth"] = JsonSerializer.SerializeToElement(3),
        };

        Assert.Equal("""{"width":1,"height":2,"depth":3}""", JsonSerializer.Serialize(aspect, Options));
    }
}
