using System.Text.Json;
using ATProtoNet.LexiconGenerator.CodeGen;
using ATProtoNet.LexiconGenerator.Schema;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>
/// The C# → Lexicon direction of <c>atproto-lexgen lexicon</c>. The fixtures below are shaped
/// exactly like what <c>atproto-lexgen csharp</c> emits for a space type Lexicon, so between
/// the two suites a space declaration round-trips.
/// </summary>
public sealed class LexiconEmitterTests
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    private static LexiconDocument? Emit(string nsid, out IReadOnlyList<string> warnings)
    {
        var emitter = new LexiconEmitter();
        var schemas = emitter.EmitFromAssembly(typeof(LexiconEmitterTests).Assembly);
        warnings = emitter.Warnings;

        var match = schemas.FirstOrDefault(s => s.Nsid == nsid);
        return match.JsonContent is null
            ? null
            : JsonSerializer.Deserialize<LexiconDocument>(match.JsonContent, s_options);
    }

    [Fact]
    public void EmitFromAssembly_StaticSpaceDeclaration_EmitsSpaceDefinition()
    {
        var doc = Emit("com.atmoboards.test.forum", out _);

        Assert.NotNull(doc);
        var main = doc.Defs["main"];

        Assert.Equal("space", main.Type);
        Assert.Equal("any", main.Key);
        Assert.Equal("AtmoBoards Forum", main.Name);
        Assert.Equal("A private discussion forum.", main.Description);
        Assert.Equal(["com.atmoboards.test.thread", "com.atmoboards.test.reply"], main.Collections);
        Assert.Equal("Foro AtmoBoards", main.LocalizedNames?["es"]);

        // A space declaration is not a record — none of the record fields may leak into it.
        Assert.Null(main.Record);
        Assert.Null(main.Properties);
    }

    [Fact]
    public void EmitFromAssembly_SpaceDeclaration_SerializesLexiconFieldNames()
    {
        var emitter = new LexiconEmitter();
        var json = emitter.EmitFromAssembly(typeof(LexiconEmitterTests).Assembly)
            .First(s => s.Nsid == "com.atmoboards.test.forum").JsonContent;

        Assert.Contains("\"type\": \"space\"", json);
        Assert.Contains("\"name:lang\"", json);
        Assert.Contains("\"collections\"", json);
    }

    [Fact]
    public void EmitFromAssembly_SpaceDeclarationWithoutNsid_WarnsInsteadOfGuessing()
    {
        Emit("com.atmoboards.test.forum", out var warnings);

        Assert.Contains(warnings, w => w.Contains(nameof(UnattributedSpaceFixture))
                                    && w.Contains("has no NSID"));
    }

    [Fact]
    public void EmitFromAssembly_RecordType_StillEmitsRecordDefinition()
    {
        var doc = Emit("com.atmoboards.test.thread", out _);

        Assert.NotNull(doc);
        Assert.Equal("record", doc.Defs["main"].Type);
        Assert.NotNull(doc.Defs["main"].Record);
    }
}

/// <summary>Shaped like the output of <c>atproto-lexgen csharp</c> for a space Lexicon.</summary>
public static class TestForumSpace
{
    public const string Nsid = "com.atmoboards.test.forum";

    public static SpaceTypeDeclaration Declaration { get; } = new()
    {
        Description = "A private discussion forum.",
        Key = "any",
        Name = "AtmoBoards Forum",
        LocalizedNames = new Dictionary<string, string> { ["es"] = "Foro AtmoBoards" },
        Collections = ["com.atmoboards.test.thread", "com.atmoboards.test.reply"],
    };
}

/// <summary>A declaration with no NSID constant to attribute it to.</summary>
public static class UnattributedSpaceFixture
{
    public static SpaceTypeDeclaration Declaration { get; } = new()
    {
        Key = "any",
        Name = "Nameless",
        Collections = [],
    };
}

/// <summary>A plain record type, to prove the space scan did not displace the record scan.</summary>
public sealed class TestThreadRecord
{
    [System.Text.Json.Serialization.JsonPropertyName("$type")]
    public string Type => "com.atmoboards.test.thread";

    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public required string Title { get; init; }
}
