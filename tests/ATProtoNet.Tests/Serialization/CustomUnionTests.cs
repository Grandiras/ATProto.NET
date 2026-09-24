using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Serialization;

/// <summary>
/// The open-union pattern documented in docs/custom-records.md, applied to a consumer's own
/// Lexicon, and runtime registration of variants on both consumer and SDK unions.
/// </summary>
public class CustomUnionTests
{
    [AtProtoUnion(typeof(UnknownAttribution))]
    [JsonDerivedType(typeof(AuthorAttribution), "exchange.recipe.defs#attributionAuthor")]
    [JsonDerivedType(typeof(SourceAttribution), "exchange.recipe.defs#attributionSource")]
    public abstract class RecipeAttribution : LexObject;

    public sealed class AuthorAttribution : RecipeAttribution
    {
        [JsonPropertyName("did")]
        public string Did { get; set; } = string.Empty;
    }

    public sealed class SourceAttribution : RecipeAttribution
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    public sealed class UnknownAttribution(string type, JsonElement raw) : RecipeAttribution, IUnknownUnionVariant
    {
        public string Type { get; } = type;

        public JsonElement Raw { get; } = raw;
    }

    // Registered at runtime; see RegisteredVariant_* below.
    public sealed class ImportedAttribution : RecipeAttribution
    {
        [JsonPropertyName("from")]
        public string From { get; set; } = string.Empty;
    }

    public sealed class Recipe : AtProtoRecord
    {
        public override string Type => "exchange.recipe.recipe";

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("attribution")]
        public RecipeAttribution? Attribution { get; set; }
    }

    // A plain System.Text.Json union, which keeps STJ's own (strict) polymorphism.
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(PlainKnown), "exchange.recipe.defs#plainKnown")]
    public abstract class PlainUnion;

    public sealed class PlainKnown : PlainUnion
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    public sealed class PlainRegistered : PlainUnion
    {
        [JsonPropertyName("extra")]
        public string Extra { get; set; } = string.Empty;
    }

    public sealed class CustomEmbed : EmbedBase
    {
        [JsonPropertyName("note")]
        public string Note { get; set; } = string.Empty;
    }

    public sealed class LateEmbed : EmbedBase
    {
        [JsonPropertyName("late")]
        public bool Late { get; set; }
    }

    private static readonly JsonSerializerOptions Options = AtProtoJsonDefaults.Options;

    [Fact]
    public void Serialize_RecordContainingCustomUnion_WritesDiscriminatorFirst()
    {
        var recipe = new Recipe
        {
            Title = "Carrot soup",
            CreatedAt = AtDatetime.Parse("2026-01-01T00:00:00.000Z"),
            Attribution = new SourceAttribution { Url = "https://recipe.exchange/r/1" },
        };

        var json = JsonSerializer.Serialize(recipe, Options);

        Assert.Equal(
            """{"$type":"exchange.recipe.recipe","title":"Carrot soup","attribution":{"$type":"exchange.recipe.defs#attributionSource","url":"https://recipe.exchange/r/1"},"createdAt":"2026-01-01T00:00:00.000Z"}""",
            json);
    }

    [Fact]
    public void Deserialize_CustomUnionWithTrailingDiscriminator_ResolvesVariant()
    {
        const string json =
            """{"title":"t","attribution":{"did":"did:plc:abc","$type":"exchange.recipe.defs#attributionAuthor"}}""";

        var recipe = JsonSerializer.Deserialize<Recipe>(json, Options)!;

        Assert.Equal("did:plc:abc", Assert.IsType<AuthorAttribution>(recipe.Attribution).Did);
    }

    [Fact]
    public void Deserialize_CustomUnionUnknownVariant_RoundTripsThroughTheRecord()
    {
        const string attribution = """{"$type":"exchange.recipe.defs#attributionBook","isbn":"978-0"}""";
        var json = $$"""{"$type":"exchange.recipe.recipe","title":"t","attribution":{{attribution}},"createdAt":"2026-01-01T00:00:00.000Z"}""";

        var recipe = JsonSerializer.Deserialize<Recipe>(json, Options)!;

        Assert.Equal("exchange.recipe.defs#attributionBook", Assert.IsType<UnknownAttribution>(recipe.Attribution).Type);
        Assert.Equal(json, JsonSerializer.Serialize(recipe, Options));
    }

    [Fact]
    public void RegisteredVariant_OnCustomUnion_RoundTripsThroughDefaultOptions()
    {
        LexiconTypeRegistry.Instance.RegisterUnionVariant<RecipeAttribution, ImportedAttribution>(
            "exchange.recipe.defs#attributionImported");

        var json = JsonSerializer.Serialize(
            new Recipe { Title = "t", Attribution = new ImportedAttribution { From = "mise" } }, Options);

        Assert.Contains("""{"$type":"exchange.recipe.defs#attributionImported","from":"mise"}""", json);
        var round = JsonSerializer.Deserialize<Recipe>(json, Options)!;
        Assert.Equal("mise", Assert.IsType<ImportedAttribution>(round.Attribution).From);
    }

    [Fact]
    public void RegisteredVariant_OnSdkUnion_IsUsedByTheOptionsTheClientsUse()
    {
        LexiconTypeRegistry.Instance.RegisterUnionVariant<EmbedBase, CustomEmbed>("com.example.test.customEmbed");
        const string json =
            """{"$type":"app.bsky.feed.post","text":"hi","embed":{"note":"n","$type":"com.example.test.customEmbed"},"createdAt":"2026-01-01T00:00:00Z"}""";

        var post = JsonSerializer.Deserialize<PostRecord>(json, AtProtoJsonDefaults.Options)!;

        Assert.Equal("n", Assert.IsType<CustomEmbed>(post.Embed).Note);
        Assert.Contains("""{"$type":"com.example.test.customEmbed","note":"n"}""", JsonSerializer.Serialize(post, Options));
    }

    [Fact]
    public void RegisteredVariant_AfterTheUnionWasFirstUsed_IsStillPickedUp()
    {
        const string json = """{"$type":"com.example.test.lateEmbed","late":true}""";
        Assert.IsType<UnknownEmbed>(JsonSerializer.Deserialize<EmbedBase>(json, Options));

        LexiconTypeRegistry.Instance.RegisterUnionVariant<EmbedBase, LateEmbed>("com.example.test.lateEmbed");

        Assert.True(Assert.IsType<LateEmbed>(JsonSerializer.Deserialize<EmbedBase>(json, Options)).Late);
    }

    [Fact]
    public void RegisteredVariant_OnPlainPolymorphicUnion_IsAddedToItsContract()
    {
        LexiconTypeRegistry.Instance.RegisterUnionVariant<PlainUnion, PlainRegistered>("exchange.recipe.defs#plainRegistered");

        var round = JsonSerializer.Deserialize<PlainUnion>(
            JsonSerializer.Serialize<PlainUnion>(new PlainRegistered { Extra = "x" }, Options), Options);

        Assert.Equal("x", Assert.IsType<PlainRegistered>(round).Extra);
    }

    [Fact]
    public void Serialize_VariantThatIsNeitherDeclaredNorRegistered_Throws()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => JsonSerializer.Serialize<RecipeAttribution>(new UnregisteredAttribution(), Options));

        Assert.Contains(nameof(LexiconTypeRegistry.RegisterUnionVariant), ex.Message);
    }

    public sealed class UnregisteredAttribution : RecipeAttribution;
}
