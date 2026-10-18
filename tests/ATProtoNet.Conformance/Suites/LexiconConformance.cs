using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// Lexicon resolution against the reference resolver's verdicts (<c>lexicon/lexicon-resolution.json</c>),
/// and the atproto-interop-tests <c>lexicon/</c> suites (<c>lexicon/interop/</c>): schema validity
/// and record data validation against the catalog schemas.
/// </summary>
public abstract class LexiconConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    private const string ResolutionFile = "lexicon/lexicon-resolution.json";

    private static readonly JsonNode Resolution = Vectors.Json(ResolutionFile);
    private static readonly string[] Catalog =
        [.. Directory.GetFiles(Vectors.PathOf("lexicon/interop/catalog"), "*.json").Order(StringComparer.Ordinal).Select(File.ReadAllText)];

    /// <summary>The resolution vectors, by ID.</summary>
    public static TheoryData<string> Resolutions()
    {
        var data = new TheoryData<string>();
        foreach (var (nsid, _) in Resolution["lexicons"]!.AsObject())
            data.Add($"{ResolutionFile}#{nsid}");
        data.Add($"{ResolutionFile}#foreignKey");
        return data;
    }

    /// <summary>The DNS-name vectors, by ID.</summary>
    public static TheoryData<string> DnsNames() => new(Resolution["dnsNames"]!.AsArray().Select(n => $"{ResolutionFile}#dns:{n!["nsid"]}"));

    /// <summary>The interop schema documents, by ID, with whether each is valid.</summary>
    public static TheoryData<string, bool> Lexicons() => Interop([("lexicon-valid.json", true), ("lexicon-invalid.json", false)]);

    /// <summary>The interop records, by ID, with whether each is valid.</summary>
    public static TheoryData<string, bool> Records() => Interop([("record-data-valid.json", true), ("record-data-invalid.json", false)]);

    /// <summary>The resolver reaches the reference resolver's verdict on each published schema.</summary>
    [Theory]
    [Trait("Spec", "LEX-81")]
    [MemberData(nameof(Resolutions))]
    public void ResolveLexicon_ReferenceVector_ReachesTheReferenceVerdict(string vectorId)
    {
        Vector(vectorId);
        var name = vectorId[(vectorId.IndexOf('#') + 1)..];
        var (nsid, document, entry) = name == "foreignKey"
            ? ((string)Resolution["foreignKey"]!["nsid"]!, Resolution["foreignKey"]!["didDocument"]!, Resolution["lexicons"]![(string)Resolution["foreignKey"]!["nsid"]!]!)
            : (name, Resolution["didDocument"]!, Resolution["lexicons"]![name]!);
        var reference = name == "foreignKey" ? Resolution["foreignKey"]!["reference"]! : entry["reference"]!;

        var resolved = Adapter.ResolveLexicon(nsid, document.ToJsonString(), Vectors.Base64(entry, "car"));

        Assert.Equal(
            new LexiconResolution((bool)reference["ok"]!, (string?)reference["uri"], (string?)reference["cid"], (string?)reference["lexiconId"]),
            resolved);
    }

    /// <summary>An NSID's authority is looked up under <c>_lexicon.</c> and its reversed domain.</summary>
    [Theory]
    [Trait("Spec", "LEX-81")]
    [MemberData(nameof(DnsNames))]
    public void LexiconDnsName_SpecExample_MatchesTheReference(string vectorId)
    {
        Vector(vectorId);
        var nsid = vectorId[(vectorId.IndexOf("#dns:", StringComparison.Ordinal) + 5)..];
        var expected = (string)Resolution["dnsNames"]!.AsArray().First(n => (string)n!["nsid"]! == nsid)!["name"]!;

        Assert.Equal(expected, Adapter.LexiconDnsName(nsid), ignoreCase: true);
    }

    /// <summary>An interop schema document is accepted or refused as its file says.</summary>
    [Theory]
    [Trait("Spec", "LEX-3")]
    [MemberData(nameof(Lexicons))]
    public void ValidateLexicon_InteropDocument_MatchesFile(string vectorId, bool valid)
    {
        Vector(vectorId);

        Assert.Equal(valid, Adapter.ValidateLexicon(InteropCase(vectorId)["lexicon"]!.ToJsonString()));
    }

    /// <summary>An interop record is valid or invalid against the catalog as its file says.</summary>
    [Theory]
    [Trait("Spec", "LEX-18")]
    [MemberData(nameof(Records))]
    public void ValidateRecord_InteropRecord_MatchesFile(string vectorId, bool valid)
    {
        Vector(vectorId);
        var vector = InteropCase(vectorId);

        Assert.Equal(valid, Adapter.ValidateRecord(Catalog, (string)vector["rkey"]!, vector["data"]!.ToJsonString()));
    }

    private static TheoryData<string, bool> Interop((string File, bool Valid)[] files)
    {
        var data = new TheoryData<string, bool>();
        foreach (var (file, valid) in files)
            foreach (var (_, index) in Vectors.Json($"lexicon/interop/{file}").AsArray().Select((c, i) => (c, i)))
                data.Add($"lexicon/interop/{file}#{index}", valid);
        return data;
    }

    private static JsonNode InteropCase(string vectorId)
    {
        var (file, index) = (vectorId[..vectorId.IndexOf('#')], int.Parse(vectorId[(vectorId.IndexOf('#') + 1)..]));
        return Vectors.Json(file).AsArray()[index]!;
    }
}
