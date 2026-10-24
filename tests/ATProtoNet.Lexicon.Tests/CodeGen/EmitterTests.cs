using System.Text;
using ATProtoNet.Lexicon.CodeGen;

namespace ATProtoNet.Lexicon.Tests.CodeGen;

// The emitter's naming and mapping rules on small inline documents (LEX-32–LEX-47, LEX-89–LEX-92).
public sealed class EmitterTests
{
    private static GenerationResult Generate(string config = "{}", params string[] documents) =>
        LexiconGenerator.Generate([.. documents.Select((d, i) => new LexiconSource($"doc{i}.json", Encoding.UTF8.GetBytes(d)))], LexgenConfig.Parse(config));

    private static string Doc(string id, string defs) => "{\"lexicon\":1,\"id\":\"" + id + "\",\"defs\":{" + defs + "}}";

    private static string Text(GenerationResult result, string nsid) => result.Files.Single(f => f.Path.EndsWith("/" + nsid + ".g.cs", StringComparison.Ordinal)).Text;

    private static IEnumerable<string> Ids(GenerationResult result) => result.Diagnostics.Select(d => d.Id);

    [Fact]
    [Trait("Spec", "LEX-32")]
    public void Generate_Namespace_PascalCasesSegmentsWithProtocolWords()
    {
        var result = Generate("{}", Doc("com.atproto.richtext.thing", """ "main":{"type":"object","properties":{}} """));

        Assert.Contains("namespace ATProtoNet.Lexicons.Com.AtProto.RichText;", Text(result, "com.atproto.richtext.thing"));
    }

    [Fact]
    [Trait("Spec", "LEX-35")]
    public void Generate_PropertyNamesThatAreNotIdentifiers_BecomeLegalMembersKeepingTheirKeys()
    {
        var text = Text(Generate("{}", Doc("com.example.thing", """ "main":{"type":"object","properties":{"name:lang":{"type":"string"},"3d":{"type":"string"},"thing":{"type":"string"},"extensionData":{"type":"string"}}} """)), "com.example.thing");

        Assert.Contains("JsonPropertyName(\"name:lang\")]\n    public string? NameLang", text);
        Assert.Contains("public string? _3d", text);
        Assert.Contains("JsonPropertyName(\"thing\")]\n    public string? ThingValue", text);
        Assert.Contains("public string? ExtensionDataValue", text);
    }

    [Fact]
    [Trait("Spec", "LEX-37")]
    public void Generate_TwoDefinitionsWithOneName_IsAnErrorNamingBothAndTheRename()
    {
        var result = Generate("{}", Doc("com.example.defs", """ "fooBar":{"type":"object","properties":{}},"foo-bar":{"type":"object","properties":{}} """));

        var error = Assert.Single(result.Diagnostics, d => d.Id == "ATLEX001");
        Assert.Contains("com.example.defs#fooBar and com.example.defs#foo-bar", error.Message);
        Assert.Contains("\"renames\" entry for \"com.example.defs#foo-bar\"", error.Message);
        Assert.Empty(result.Files);
    }

    [Fact]
    [Trait("Spec", "LEX-91")]
    public void Generate_Rename_FixesTheCollision()
    {
        var result = Generate("""{"renames":{"com.example.defs#foo-bar":"OtherFoo"}}""", Doc("com.example.defs", """ "fooBar":{"type":"object","properties":{}},"foo-bar":{"type":"object","properties":{}} """));

        Assert.Contains("record OtherFoo", Text(result, "com.example.defs"));
        Assert.Contains("com.example.defs#foo-bar → ATProtoNet.Lexicons.Com.Example.OtherFoo", result.TypeNames);
    }

    [Fact]
    [Trait("Spec", "LEX-89")]
    public void Generate_NameBreakingTheRules_WarnsSuggestingARename()
    {
        var result = Generate("{}", Doc("com.example.defs", """ "mainView":{"type":"object","properties":{}} """));

        var warning = Assert.Single(result.Diagnostics, d => d.Id == "ATLEX002");
        Assert.Contains("\"renames\" entry for \"com.example.defs#mainView\"", warning.Message);
    }

    [Fact]
    [Trait("Spec", "LEX-91")]
    public void Generate_RenameMatchingNothing_Warns() =>
        Assert.Contains("ATLEX008", Ids(Generate("""{"renames":{"com.example.defs#nope":"X"}}""", Doc("com.example.defs", """ "a":{"type":"object","properties":{}} """))));

    [Fact]
    [Trait("Spec", "LEX-34")]
    public void Generate_UnionAtAMethodsOutput_DropsTheVerbAndRepeatedWords()
    {
        var result = Generate("{}",
            Doc("com.example.getPostThread", """ "main":{"type":"query","output":{"encoding":"application/json","schema":{"type":"object","required":["thread"],"properties":{"thread":{"type":"union","refs":["#a"]}}}}},"a":{"type":"object","properties":{}} """));

        Assert.Contains("public union PostThread(", Text(result, "com.example.getPostThread"));
    }

    [Fact]
    [Trait("Spec", "LEX-34")]
    public void Generate_Tokens_GroupIntoOneClassOfConstants()
    {
        var text = Text(Generate("{}", Doc("com.example.defs", """ "alpha":{"type":"token"},"beta":{"type":"token","description":"B."} """)), "com.example.defs");

        Assert.Contains("public static partial class Tokens", text);
        Assert.Contains("public const string Alpha = \"com.example.defs#alpha\";", text);
        Assert.Contains("/// <summary>B.</summary>\n    public const string Beta", text);
    }

    [Fact]
    [Trait("Spec", "LEX-43")]
    public void Generate_Integer_IsIntOnlyWhenBothBoundsFit()
    {
        var text = Text(Generate("{}", Doc("com.example.thing", """ "main":{"type":"object","properties":{"a":{"type":"integer","minimum":0,"maximum":10},"b":{"type":"integer","minimum":0},"c":{"type":"integer","minimum":0,"maximum":10000000000}}} """)), "com.example.thing");

        Assert.Contains("public int? A", text);
        Assert.Contains("public long? B", text);
        Assert.Contains("public long? C", text);
    }

    [Theory]
    [Trait("Spec", "LEX-43")]
    [InlineData("did", "global::ATProtoNet.Did?")]
    [InlineData("handle", "global::ATProtoNet.Handle?")]
    [InlineData("at-identifier", "global::ATProtoNet.AtIdentifier?")]
    [InlineData("at-uri", "global::ATProtoNet.AtUri?")]
    [InlineData("nsid", "global::ATProtoNet.Nsid?")]
    [InlineData("cid", "global::ATProtoNet.Cid?")]
    [InlineData("record-key", "global::ATProtoNet.RecordKey?")]
    [InlineData("tid", "global::ATProtoNet.Tid?")]
    [InlineData("datetime", "global::ATProtoNet.AtDatetime?")]
    [InlineData("language", "global::ATProtoNet.Language?")]
    [InlineData("uri", "string?")]
    public void Generate_StringFormat_MapsToItsType(string format, string type) =>
        Assert.Contains($"public {type} A", Text(Generate("{}", Doc("com.example.thing", """ "main":{"type":"object","properties":{"a":{"type":"string","format":"FORMAT"}}} """.Replace("FORMAT", format))), "com.example.thing"));

    [Fact]
    [Trait("Spec", "LEX-43")]
    public void Generate_UnknownFormat_IsAStringWithAWarning() =>
        Assert.Contains("ATLEX005", Ids(Generate("{}", Doc("com.example.thing", """ "main":{"type":"object","properties":{"a":{"type":"string","format":"color"}}} """))));

    [Fact]
    [Trait("Spec", "LEX-92")]
    public void Generate_FieldTypeOnAFormattedString_IsAnError() =>
        Assert.Contains("ATLEX004", Ids(Generate("""{"fieldTypes":{"com.example.thing#main.a":"decimal"}}""", Doc("com.example.thing", """ "main":{"type":"object","properties":{"a":{"type":"string","format":"did"}}} """))));

    [Fact]
    [Trait("Spec", "LEX-6")]
    public void Generate_UnresolvableRef_IsAnErrorNotAFallback()
    {
        var result = Generate("{}", Doc("com.example.thing", """ "main":{"type":"object","properties":{"a":{"type":"ref","ref":"com.example.missing#x"}}} """));

        Assert.Contains("LXL010", Ids(result));
        Assert.Empty(result.Files);
    }

    [Fact]
    [Trait("Spec", "LEX-8")]
    public void Generate_RefToAnExternalType_UsesIt()
    {
        var result = LexiconGenerator.Generate(
            [new("doc.json", Encoding.UTF8.GetBytes(Doc("com.example.thing", """ "main":{"type":"object","properties":{"a":{"type":"ref","ref":"com.atproto.repo.strongRef"}}} """)))],
            new LexgenConfig(), new External());

        Assert.Contains("public global::Sdk.StrongRef? A", Text(result, "com.example.thing"));
    }

    [Fact]
    [Trait("Spec", "LEX-46")]
    public void Generate_DeprecatedDescription_IsObsoleteExceptOnRequiredMembers()
    {
        var text = Text(Generate("{}", Doc("com.example.thing", """ "main":{"type":"object","required":["b"],"properties":{"a":{"type":"string","description":"Deprecated: gone."},"b":{"type":"string","description":"DEPRECATED too."}}} """)), "com.example.thing");

        Assert.Contains("[global::System.Obsolete(\"Deprecated: gone.\")]", text);
        Assert.DoesNotContain("Obsolete(\"DEPRECATED too.\")", text);
    }

    [Fact]
    [Trait("Spec", "LEX-46")]
    public void Generate_Description_IsXmlEscapedAndEveryTypeIsDocumented()
    {
        var text = Text(Generate("{}", Doc("com.example.thing", """ "main":{"type":"object","description":"a < b & c","properties":{"a":{"type":"string"}}} """)), "com.example.thing");

        Assert.Contains("/// <summary>a &lt; b &amp; c</summary>", text);
        Assert.Contains("/// <summary>The <c>a</c> property.</summary>", text);
    }

    [Fact]
    [Trait("Spec", "LEX-31")]
    public void Generate_InternalVisibility_MakesTypesInternal() =>
        Assert.Contains("internal sealed partial record Thing", Text(Generate("""{"visibility":"internal"}""", Doc("com.example.thing", """ "main":{"type":"object","properties":{}} """)), "com.example.thing"));

    [Fact]
    [Trait("Spec", "LEX-31")]
    public void Parse_UnknownOption_Fails() => Assert.Throws<FormatException>(() => LexgenConfig.Parse("""{"colour":"blue"}"""));

    private sealed class External : IExternalLexiconTypes
    {
        public IReadOnlyDictionary<string, string> FormatTypes { get; } = new Dictionary<string, string>();

        public bool TryResolve(string reference, out string typeName, out ExternalTypeKind kind)
        {
            (typeName, kind) = ("Sdk.StrongRef", ExternalTypeKind.Object);
            return reference == "com.atproto.repo.strongRef";
        }
    }

    [Fact]
    [Trait("Spec", "LEX-86")]
    public void Generate_UnionExtensionOfAnUnknownUnion_IsAnError() =>
        Assert.Contains("ATLEX011", Ids(Generate("""{"unionExtensions":{"com.example.thing":["com.example.other#main.embed"]}}""", Doc("com.example.thing", """ "main":{"type":"object","properties":{}} """))));

    [Fact]
    [Trait("Spec", "LEX-6")]
    public void Generate_UnionMemberThatIsAToken_IsAnErrorNotUncompilableCode()
    {
        var result = Generate("{}", Doc("com.example.thing", """ "main":{"type":"object","properties":{"a":{"type":"union","refs":["#t"]}}},"t":{"type":"token"} """));

        Assert.Contains("LXL011", Ids(result));
        Assert.Empty(result.Files);
    }
}
