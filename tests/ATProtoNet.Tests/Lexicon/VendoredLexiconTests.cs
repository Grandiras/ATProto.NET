using ATProtoNet.Lexicon;
using ATProtoNet.TestKit;

namespace ATProtoNet.Tests.Lexicon;

public sealed class VendoredLexiconTests
{
    /// <summary>Every vendored Lexicon, parsed.</summary>
    public static IReadOnlyList<LexiconDocument> All { get; } =
        [.. Directory.EnumerateFiles(RepositoryFiles.Lexicons, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(f => LexiconDocument.Parse(File.ReadAllBytes(f)))];

    [Fact]
    [Trait("Spec", "LEX-3")]
    public void Parse_EveryVendoredLexicon_HasNoErrorsOrWarnings()
    {
        var findings = Directory.EnumerateFiles(RepositoryFiles.Lexicons, "*.json", SearchOption.AllDirectories)
            .SelectMany(f => LexiconDocument.TryParse(File.ReadAllBytes(f), out _, out var diagnostics) ? diagnostics : diagnostics)
            .Select(d => d.ToString());

        Assert.Empty(findings);
    }

    [Fact]
    [Trait("Spec", "LEX-6")]
    public void Create_VendoredCatalog_ResolvesEveryReference()
    {
        var catalog = LexiconCatalog.Create(All);

        Assert.Empty(catalog.Diagnostics.Select(d => d.ToString()));
    }

    [Fact]
    [Trait("Spec", "LEX-11")]
    public void Lint_VendoredCatalog_HasNoErrors()
    {
        var errors = LexiconLint.Run(LexiconCatalog.Create(All)).Where(d => d.Severity == LexiconSeverity.Error);

        Assert.Empty(errors.Select(d => d.ToString()));
    }

    [Fact]
    [Trait("Spec", "LEX-23")]
    public void ComputeRecordCid_EveryVendoredLexicon_MatchesTheManifest()
    {
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(RepositoryFiles.PathOf("lexicons.json")))!["lexicons"]!.AsObject();

        var mismatches = All.Where(d => d.ComputeRecordCid().ToString() != (string?)manifest[d.Id.ToString()]?["cid"]).Select(d => d.Id.ToString());

        Assert.Empty(mismatches);
    }
}
