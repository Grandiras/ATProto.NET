using System.Text.Json;
using ATProtoNet.LexiconGenerator.CodeGen;
using ATProtoNet.LexiconGenerator.Schema;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>
/// Covers the C# codegen defects found by generating the published exchange.recipe
/// Lexicons (Issue #45): brace balance, member/type name collisions, unresolved refs,
/// non-nullable JsonElement fallbacks, and token-family grouping.
/// </summary>
public class CSharpEmitterTests
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    private static LexiconDocument Parse(string json)
        => JsonSerializer.Deserialize<LexiconDocument>(json, s_options)!;

    private static Dictionary<string, string> EmitAll(params string[] documents)
        => EmitWithWarnings(documents).Files;

    private static (Dictionary<string, string> Files, IReadOnlyList<string> Warnings) EmitWithWarnings(
        params string[] documents)
    {
        var emitter = new CSharpEmitter("Mise.Core.Lexicon");
        var files = emitter.EmitAll(documents.Select(Parse))
            .ToDictionary(f => f.Path, f => f.Content.ReplaceLineEndings("\n"));
        return (files, emitter.Warnings);
    }

    private const string RecipeDefs = """
        {
          "lexicon": 1,
          "id": "exchange.recipe.defs",
          "defs": {
            "attributionPerson": {
              "type": "object",
              "required": ["name"],
              "properties": { "name": { "type": "string" } },
              "description": "Recipe shared by a specific person."
            },
            "attributionWebsite": {
              "type": "object",
              "required": ["name", "url"],
              "properties": {
                "name": { "type": "string" },
                "url": { "type": "string", "format": "uri" }
              }
            },
            "cookingMethodBaking": { "type": "token", "description": "Baked." },
            "cookingMethodFrying": { "type": "token" },
            "cookingMethodSlowCooking": { "type": "token" },
            "licenseAllRights": { "type": "token" },
            "licenseCreativeCommonsBy": { "type": "token" },
            "licenseCreativeCommonsByNcSa": { "type": "token" },
            "profileTypePersonal": { "type": "token" },
            "profileTypeBusiness": { "type": "token" },
            "standalone": { "type": "token" }
          }
        }
        """;

    private const string RecipeRecord = """
        {
          "lexicon": 1,
          "id": "exchange.recipe.recipe",
          "defs": {
            "main": {
              "type": "record",
              "key": "tid",
              "record": {
                "type": "object",
                "required": ["name", "createdAt"],
                "properties": {
                  "name": { "type": "string", "maxLength": 255 },
                  "createdAt": { "type": "string", "format": "datetime" },
                  "embed": { "type": "ref", "ref": "#imagesEmbed" },
                  "nutrition": {
                    "type": "object",
                    "properties": {
                      "calories": { "type": "integer" },
                      "fatContent": { "type": "number" }
                    }
                  },
                  "ingredients": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": { "text": { "type": "string" } }
                    }
                  },
                  "attribution": {
                    "type": "union",
                    "refs": [
                      "exchange.recipe.defs#attributionPerson",
                      "exchange.recipe.defs#attributionWebsite"
                    ]
                  },
                  "profileType": {
                    "type": "union",
                    "refs": [
                      "exchange.recipe.defs#profileTypePersonal",
                      "exchange.recipe.defs#profileTypeBusiness"
                    ]
                  }
                }
              }
            },
            "image": {
              "type": "object",
              "required": ["image", "alt"],
              "properties": {
                "alt": { "type": "string" },
                "image": { "type": "blob", "accept": ["image/*"], "maxSize": 1000000 },
                "aspectRatio": { "type": "ref", "ref": "app.bsky.embed.defs#aspectRatio" }
              }
            },
            "imagesEmbed": {
              "type": "object",
              "required": ["images"],
              "properties": {
                "images": { "type": "array", "items": { "type": "ref", "ref": "#image" } }
              }
            }
          }
        }
        """;

    [Fact]
    public void Emit_AnyDocument_ProducesBalancedBraces()
    {
        var files = EmitAll(RecipeDefs, RecipeRecord);

        Assert.NotEmpty(files);
        foreach (var (path, content) in files)
        {
            Assert.Equal(content.Count(c => c == '{'), content.Count(c => c == '}'));
            // File-scoped namespace — nothing may be appended after the last type.
            Assert.DoesNotContain("\n}\n\n}", content);
            Assert.Contains("namespace Mise.Core.Lexicon.Exchange.Recipe;", content);
            Assert.False(string.IsNullOrWhiteSpace(path));
        }
    }

    [Fact]
    public void Emit_BlobPropertyNamedLikeEnclosingType_RenamesMember()
    {
        var recipe = EmitAll(RecipeDefs, RecipeRecord)["Exchange/Recipe/Recipe.g.cs"];

        // CS0542: a member may not have the same name as its enclosing type.
        Assert.Contains("public sealed class Image", recipe);
        Assert.Contains("[JsonPropertyName(\"image\")]", recipe);
        Assert.Contains("public required BlobRef ImageBlob { get; init; }", recipe);
        Assert.DoesNotContain("BlobRef Image {", recipe);
    }

    [Fact]
    public void Emit_BlobProperty_AddsSdkModelsUsing()
    {
        var recipe = EmitAll(RecipeDefs, RecipeRecord)["Exchange/Recipe/Recipe.g.cs"];

        Assert.Contains("using ATProtoNet.Models;", recipe);
    }

    [Fact]
    public void Emit_DocumentWithoutSdkTypes_OmitsSdkUsings()
    {
        var defs = EmitAll(RecipeDefs, RecipeRecord)["Exchange/Recipe/Defs.g.cs"];

        Assert.DoesNotContain("using ATProtoNet.Models;", defs);
        Assert.DoesNotContain("using System.Text.Json;\n", defs);
    }

    [Fact]
    public void Emit_RecordDefinition_ExtendsAtProtoRecord()
    {
        var recipe = EmitAll(RecipeDefs, RecipeRecord)["Exchange/Recipe/Recipe.g.cs"];

        Assert.Contains("public sealed class RecipeRecord : AtProtoRecord", recipe);
        Assert.Contains("using ATProtoNet;", recipe);
        // The attribute must be repeated on the override or System.Text.Json writes both
        // "Type" and "$type".
        Assert.Contains("[JsonPropertyName(\"$type\")]\n    public override string Type => \"exchange.recipe.recipe\";", recipe);
        // createdAt comes from the base class.
        Assert.DoesNotContain("CreatedAt", recipe);
    }

    [Fact]
    public void Emit_UnionOfObjectDefs_GeneratesPolymorphicBaseClass()
    {
        var files = EmitAll(RecipeDefs, RecipeRecord);
        var defs = files["Exchange/Recipe/Defs.g.cs"];
        var recipe = files["Exchange/Recipe/Recipe.g.cs"];

        Assert.Contains("[JsonPolymorphic(TypeDiscriminatorPropertyName = \"$type\")]", defs);
        Assert.Contains("[JsonDerivedType(typeof(AttributionPerson), \"exchange.recipe.defs#attributionPerson\")]", defs);
        Assert.Contains("[JsonDerivedType(typeof(AttributionWebsite), \"exchange.recipe.defs#attributionWebsite\")]", defs);
        Assert.Contains("public abstract class AttributionUnion { }", defs);
        Assert.Contains("public sealed class AttributionPerson : AttributionUnion", defs);

        Assert.Contains("public AttributionUnion? Attribution { get; init; }", recipe);
    }

    [Fact]
    public void Emit_UnionOfTokens_GeneratesStringMember()
    {
        var recipe = EmitAll(RecipeDefs, RecipeRecord)["Exchange/Recipe/Recipe.g.cs"];

        Assert.Contains("public string? ProfileType { get; init; }", recipe);
    }

    [Fact]
    public void Emit_InlineObjectSchema_GeneratesNestedClass()
    {
        var recipe = EmitAll(RecipeDefs, RecipeRecord)["Exchange/Recipe/Recipe.g.cs"];

        Assert.Contains("public NutritionInfo? Nutrition { get; init; }", recipe);
        Assert.Contains("public sealed class NutritionInfo", recipe);
        Assert.Contains("public double? FatContent { get; init; }", recipe);

        // An array of inline objects names its element type in the singular.
        Assert.Contains("public List<Ingredient>? Ingredients { get; init; }", recipe);
        Assert.Contains("public sealed class Ingredient", recipe);
    }

    [Fact]
    public void Emit_KnownSdkRef_UsesSdkModelType()
    {
        var recipe = EmitAll(RecipeDefs, RecipeRecord)["Exchange/Recipe/Recipe.g.cs"];

        Assert.Contains("global::ATProtoNet.Lexicon.App.Bsky.Embed.AspectRatio? AspectRatio { get; init; }", recipe);
        Assert.DoesNotContain("Mise.Core.Lexicon.App.Bsky", recipe);
    }

    [Fact]
    public void Emit_UnresolvableRef_FallsBackToNullableJsonElementAndWarns()
    {
        const string doc = """
            {
              "lexicon": 1,
              "id": "com.example.thing",
              "defs": {
                "main": {
                  "type": "object",
                  "properties": {
                    "other": { "type": "ref", "ref": "com.elsewhere.unknown#thing" }
                  }
                }
              }
            }
            """;

        var emitter = new CSharpEmitter("My.Ns");
        var content = emitter.EmitAll([Parse(doc)])[0].Content;

        Assert.Contains("public JsonElement? Other { get; init; }", content);
        Assert.Contains("using System.Text.Json;", content);
        Assert.Contains(emitter.Warnings, w => w.Contains("com.elsewhere.unknown#thing"));
    }

    [Fact]
    public void Emit_OptionalUnionWithoutBaseClass_IsNullable()
    {
        // Two overlapping unions: only the first can be modelled by inheritance.
        const string doc = """
            {
              "lexicon": 1,
              "id": "com.example.overlap",
              "defs": {
                "a": { "type": "object", "properties": { "x": { "type": "string" } } },
                "b": { "type": "object", "properties": { "x": { "type": "string" } } },
                "holder": {
                  "type": "object",
                  "properties": {
                    "first": { "type": "union", "refs": ["#a", "#b"] },
                    "second": { "type": "union", "refs": ["#a", "#b", "com.example.overlap#holder"] }
                  }
                }
              }
            }
            """;

        var emitter = new CSharpEmitter("My.Ns");
        var content = emitter.EmitAll([Parse(doc)])[0].Content;

        Assert.Contains("public FirstUnion? First { get; init; }", content);
        // Never a bare JsonElement: serializing default(JsonElement) throws.
        Assert.Contains("public JsonElement? Second { get; init; }", content);
        Assert.DoesNotContain("public JsonElement Second", content);
    }

    [Fact]
    public void Emit_TokenFamilies_GroupsConstantsIntoOneStaticClass()
    {
        var defs = EmitAll(RecipeDefs, RecipeRecord)["Exchange/Recipe/Defs.g.cs"];

        Assert.Contains("public static class CookingMethod", defs);
        Assert.Contains("public const string Baking = \"exchange.recipe.defs#cookingMethodBaking\";", defs);
        Assert.Contains("public const string SlowCooking = \"exchange.recipe.defs#cookingMethodSlowCooking\";", defs);

        // The family name is the longest prefix all members share — "license", not
        // "licenseCreativeCommons".
        Assert.Contains("public static class License", defs);
        Assert.Contains("public const string CreativeCommonsByNcSa = \"exchange.recipe.defs#licenseCreativeCommonsByNcSa\";", defs);

        Assert.Contains("public static class ProfileType", defs);
        Assert.Contains("IReadOnlyList<string> All = new[] { Personal, Business };", defs);

        // A token with no siblings keeps its own class.
        Assert.Contains("public static class Standalone", defs);
        Assert.Contains("public const string Value = \"exchange.recipe.defs#standalone\";", defs);

        Assert.DoesNotContain("public static class CookingMethodBaking", defs);
    }

    [Fact]
    public void Emit_SiblingDocumentsWithSameDefName_DisambiguatesTypeNames()
    {
        const string first = """
            {
              "lexicon": 1,
              "id": "com.atproto.server.createAppPassword",
              "defs": {
                "appPassword": { "type": "object", "properties": { "name": { "type": "string" } } }
              }
            }
            """;
        const string second = """
            {
              "lexicon": 1,
              "id": "com.atproto.server.listAppPasswords",
              "defs": {
                "appPassword": { "type": "object", "properties": { "name": { "type": "string" } } }
              }
            }
            """;

        var files = EmitAll(first, second);

        Assert.Contains("public sealed class AppPassword", files["Com/AtProto/Server/CreateAppPassword.g.cs"]);
        Assert.Contains("public sealed class ListAppPasswordsAppPassword", files["Com/AtProto/Server/ListAppPasswords.g.cs"]);
    }

    [Fact]
    public void Emit_PropertyNamesThatAreKeywordsOrDigits_ProducesLegalIdentifiers()
    {
        const string doc = """
            {
              "lexicon": 1,
              "id": "com.example.awkward",
              "defs": {
                "main": {
                  "type": "object",
                  "properties": {
                    "class": { "type": "string" },
                    "2fa": { "type": "boolean" },
                    "kebab-case": { "type": "string" }
                  }
                }
              }
            }
            """;

        var content = EmitAll(doc)["Com/Example/Awkward.g.cs"];

        Assert.Contains("[JsonPropertyName(\"class\")]", content);
        Assert.Contains("public string? Class { get; init; }", content);
        Assert.Contains("public bool? _2fa { get; init; }", content);
        Assert.Contains("public string? KebabCase { get; init; }", content);
    }

    // ── Space type declarations ──────────────────────────────

    private const string ForumSpace = """
        {
          "lexicon": 1,
          "id": "com.atmoboards.forum",
          "defs": {
            "main": {
              "type": "space",
              "key": "any",
              "name": "AtmoBoards Forum",
              "name:lang": { "es": "Foro AtmoBoards" },
              "description": "A private discussion forum.",
              "collections": ["com.atmoboards.thread", "com.atmoboards.reply"]
            }
          }
        }
        """;

    [Fact]
    public void Emit_SpaceDeclaration_GeneratesDeclarationHolder()
    {
        var (files, warnings) = EmitWithWarnings(ForumSpace);

        var content = files["Com/Atmoboards/Forum.g.cs"];

        Assert.Empty(warnings);
        Assert.Contains("using ATProtoNet.Spaces;", content);
        Assert.Contains("public static class ForumSpace", content);
        Assert.Contains("public const string Nsid = \"com.atmoboards.forum\";", content);
        Assert.Contains("public static SpaceTypeDeclaration Declaration { get; } = new()", content);
        Assert.Contains("Key = \"any\",", content);
        Assert.Contains("Name = \"AtmoBoards Forum\",", content);
        Assert.Contains("[\"es\"] = \"Foro AtmoBoards\",", content);
        Assert.Contains("\"com.atmoboards.thread\",", content);
        Assert.Contains("/// A private discussion forum.", content);
        Assert.Equal(content.Count(c => c == '{'), content.Count(c => c == '}'));
    }

    [Fact]
    public void Emit_SpaceDeclaration_ExposesTheDeclarationsOwnMembers()
    {
        var content = EmitAll(ForumSpace)["Com/Atmoboards/Forum.g.cs"];

        // The forwarders only compile as long as the SDK model still spells them this way.
        foreach (var member in (string[])["Key", "Name", "LocalizedNames", "Collections"])
        {
            Assert.NotNull(typeof(SpaceTypeDeclaration).GetProperty(member));
            Assert.Contains($"Declaration.{member};", content);
        }
    }

    [Fact]
    public void Emit_SpaceDeclarationWithQuotesInName_EscapesTheLiteral()
    {
        var doc = """
            {
              "lexicon": 1,
              "id": "com.example.quoted",
              "defs": {
                "main": {
                  "type": "space",
                  "key": "any",
                  "name": "The \"Quoted\" Space\\Type",
                  "collections": []
                }
              }
            }
            """;

        var content = EmitAll(doc)["Com/Example/Quoted.g.cs"];

        Assert.Contains("Name = \"The \\\"Quoted\\\" Space\\\\Type\",", content);
    }

    [Fact]
    public void Emit_SpaceDeclarationMissingRequiredFields_WarnsAndSubstitutes()
    {
        var doc = """
            {
              "lexicon": 1,
              "id": "com.example.bookmarks",
              "defs": { "main": { "type": "space" } }
            }
            """;

        var (files, warnings) = EmitWithWarnings(doc);
        var content = files["Com/Example/Bookmarks.g.cs"];

        // Key, Name, and Collections are `required` on the SDK model — the file still has to compile.
        Assert.Contains("Key = \"any\",", content);
        Assert.Contains("Name = \"com.example.bookmarks\",", content);
        Assert.Contains("Collections = [],", content);

        Assert.Contains(warnings, w => w.Contains("no 'key'"));
        Assert.Contains(warnings, w => w.Contains("no 'name'"));
        Assert.Contains(warnings, w => w.Contains("no 'collections'"));
    }

    [Fact]
    public void Emit_UnsupportedDefinitionType_WarnsInsteadOfEmittingNothing()
    {
        var doc = """
            {
              "lexicon": 1,
              "id": "com.example.mystery",
              "defs": { "main": { "type": "permissionSet" } }
            }
            """;

        var (files, warnings) = EmitWithWarnings(doc);

        Assert.Empty(files);
        Assert.Contains(warnings, w => w.Contains("com.example.mystery#main")
                                    && w.Contains("unsupported definition type 'permissionSet'"));
    }

    [Fact]
    public void Emit_QueryDefinition_IsSkippedWithoutWarning()
    {
        var doc = """
            {
              "lexicon": 1,
              "id": "com.example.getThing",
              "defs": { "main": { "type": "query", "output": { "encoding": "application/json" } } }
            }
            """;

        var (files, warnings) = EmitWithWarnings(doc);

        Assert.Empty(files);
        Assert.Empty(warnings);
    }

    [Fact]
    public void SdkTypeMap_EveryMappedType_ExistsInTheSdk()
    {
        var sdkAssembly = typeof(AtProtoClient).Assembly;

        Assert.NotEmpty(SdkTypeMap.Entries);

        foreach (var (reference, typeName) in SdkTypeMap.Entries)
            Assert.True(sdkAssembly.GetType(typeName) is not null, $"{reference} → {typeName} does not exist");
    }
}
