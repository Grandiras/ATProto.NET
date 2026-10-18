namespace ATProtoNet.Conformance.Suites;

// The identifier cases 0.8 pasted into its tests (syntax/identifier-cases.json) and the TID
// generator over a scripted clock (syntax/tid-generation.json).
public abstract partial class SyntaxConformance<TAdapter>
{
    /// <summary>One row per case of identifier-cases.json.</summary>
    public static TheoryData<string, IdentifierKind, string, bool, string?> IdentifierCases()
    {
        var data = new TheoryData<string, IdentifierKind, string, bool, string?>();
        var cases = Vectors.Json("syntax/identifier-cases.json").AsArray();
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i]!;
            data.Add($"syntax/identifier-cases.json#{i}", Enum.Parse<IdentifierKind>((string)c["kind"]!), (string)c["input"]!, (bool)c["valid"]!, (string?)c["normalized"]);
        }
        return data;
    }

    /// <summary>A case gets its verdict, and a valid one its canonical text.</summary>
    [Theory]
    [MemberData(nameof(IdentifierCases))]
    [Trait("Spec", "SYN-2"), Trait("Spec", "SYN-11"), Trait("Spec", "SYN-12"), Trait("Spec", "SYN-15"), Trait("Spec", "SYN-16")]
    [Trait("Spec", "SYN-18"), Trait("Spec", "SYN-21"), Trait("Spec", "SYN-22"), Trait("Spec", "SYN-25"), Trait("Spec", "SYN-27"), Trait("Spec", "SYN-29")]
    public void ParseIdentifier_Case_MatchesExpected(string vectorId, IdentifierKind kind, string input, bool valid, string? normalized)
    {
        Vector(vectorId);
        var verdict = Adapter.ParseIdentifier(kind, input);
        Assert.Equal(valid, verdict.Valid);
        if (valid) Assert.Equal(normalized ?? input, verdict.Normalized);
    }

    /// <summary>One row per scripted clock of tid-generation.json.</summary>
    public static TheoryData<string, int, long[], string[]> TidGenerations() =>
        new(Vectors.Json("syntax/tid-generation.json").AsArray().Select(c => (
            $"syntax/tid-generation.json#{c!["name"]}",
            (int)c["clockId"]!,
            c["clockMicros"]!.AsArray().Select(m => (long)m!).ToArray(),
            c["tids"]!.AsArray().Select(t => (string)t!).ToArray())));

    /// <summary>The generator yields exactly the TIDs the scripted clock implies.</summary>
    [Theory]
    [MemberData(nameof(TidGenerations))]
    [Trait("Spec", "SYN-28"), Trait("Spec", "SYN-30"), Trait("Spec", "SYN-31"), Trait("Spec", "SYN-32"), Trait("Spec", "SYN-33"), Trait("Spec", "SYN-34")]
    public void GenerateTids_ScriptedClock_YieldsTheExpectedTids(string vectorId, int clockId, long[] clockMicros, string[] tids)
    {
        Vector(vectorId);
        Assert.Equal(tids, Adapter.GenerateTids(clockId, clockMicros));
    }
}
