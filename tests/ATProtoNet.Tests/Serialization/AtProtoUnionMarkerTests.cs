using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Serialization;

/// <summary>
/// Regression for #46: <see cref="IAtProtoUnion"/> is a plain marker interface, so implementing
/// it on a custom open-union base must not affect (de)serialization. The old
/// <c>UnionJsonConverterFactory</c> claimed every <see cref="IAtProtoUnion"/> implementor via
/// <c>CanConvert</c> but returned <c>null</c> from <c>CreateConverter</c>, which
/// System.Text.Json rejects with
/// "The converter '…' cannot return a null value".
/// </summary>
public class AtProtoUnionMarkerTests
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(AuthorAttribution), "exchange.recipe.defs#attributionAuthor")]
    [JsonDerivedType(typeof(SourceAttribution), "exchange.recipe.defs#attributionSource")]
    public abstract class RecipeAttribution : IAtProtoUnion;

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

    public sealed class Recipe
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("attribution")]
        public RecipeAttribution? Attribution { get; set; }
    }

    [Fact]
    public void Serialize_RecordContainingUnionMarkerType_WritesDiscriminator()
    {
        var recipe = new Recipe
        {
            Title = "Carrot soup",
            Attribution = new SourceAttribution { Url = "https://recipe.exchange/r/1" },
        };

        var json = JsonSerializer.Serialize(recipe, AtProtoJsonDefaults.Options);

        Assert.Contains("\"$type\":\"exchange.recipe.defs#attributionSource\"", json);
        Assert.Contains("\"url\":\"https://recipe.exchange/r/1\"", json);
    }

    [Fact]
    public void Deserialize_RecordContainingUnionMarkerType_ResolvesVariant()
    {
        const string json =
            """
            {
              "title": "Carrot soup",
              "attribution": {
                "$type": "exchange.recipe.defs#attributionAuthor",
                "did": "did:plc:tkjlutt3jh2aqkkxliitff6k"
              }
            }
            """;

        var recipe = JsonSerializer.Deserialize<Recipe>(json, AtProtoJsonDefaults.Options);

        Assert.NotNull(recipe);
        var author = Assert.IsType<AuthorAttribution>(recipe!.Attribution);
        Assert.Equal("did:plc:tkjlutt3jh2aqkkxliitff6k", author.Did);
    }

    [Fact]
    public void Serialize_UnionMarkerTypeDirectly_DoesNotThrow()
    {
        RecipeAttribution attribution = new AuthorAttribution { Did = "did:plc:abc" };

        var json = JsonSerializer.Serialize(attribution, AtProtoJsonDefaults.Options);

        Assert.Contains("\"$type\":\"exchange.recipe.defs#attributionAuthor\"", json);
    }

    [Fact]
    public void RegistryOptions_AlsoHandleUnionMarkerType()
    {
        var options = new LexiconTypeRegistry().CreateOptions();

        var json = JsonSerializer.Serialize(
            new Recipe { Title = "t", Attribution = new AuthorAttribution { Did = "did:plc:abc" } },
            options);
        var round = JsonSerializer.Deserialize<Recipe>(json, options);

        Assert.IsType<AuthorAttribution>(round!.Attribution);
    }

    /// <summary>
    /// The open-union flow documented in docs/custom-records.md: extra arms come from
    /// <see cref="LexiconTypeRegistry.RegisterUnionVariant{TBase, TDerived}(string)"/>, not from
    /// the marker interface.
    /// </summary>
    [Fact]
    public void RegistryOptions_RuntimeRegisteredVariantOnUnionMarkerBase_RoundTrips()
    {
        var registry = new LexiconTypeRegistry();
        registry.RegisterUnionVariant<RecipeAttribution, ImportedAttribution>(
            "exchange.recipe.defs#attributionImported");
        var options = registry.CreateOptions();

        var json = JsonSerializer.Serialize(
            new Recipe { Title = "t", Attribution = new ImportedAttribution { From = "mise" } },
            options);

        Assert.Contains("\"$type\":\"exchange.recipe.defs#attributionImported\"", json);
        var round = JsonSerializer.Deserialize<Recipe>(json, options);
        Assert.Equal("mise", Assert.IsType<ImportedAttribution>(round!.Attribution).From);
    }

    public sealed class ImportedAttribution : RecipeAttribution
    {
        [JsonPropertyName("from")]
        public string From { get; set; } = string.Empty;
    }

    [Fact]
    public void Deserialize_UnionMarkerTypeWithTrailingDiscriminator_ResolvesVariant()
    {
        const string json =
            """
            {"title":"t","attribution":{"did":"did:plc:abc","$type":"exchange.recipe.defs#attributionAuthor"}}
            """;

        var recipe = JsonSerializer.Deserialize<Recipe>(json, AtProtoJsonDefaults.Options);

        Assert.IsType<AuthorAttribution>(recipe!.Attribution);
    }
}
