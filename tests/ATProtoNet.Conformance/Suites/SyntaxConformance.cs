namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// The atproto-interop-tests <c>syntax/</c> files: every valid line parses and keeps its text,
/// every invalid line is rejected.
/// </summary>
public abstract partial class SyntaxConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    /// <summary>One row per line of every interop syntax file the suite asserts.</summary>
    public static TheoryData<string, IdentifierKind, bool, string> InteropLines()
    {
        var data = new TheoryData<string, IdentifierKind, bool, string>();
        foreach (var (file, kind, valid) in Files)
            foreach (var line in Vectors.Lines($"syntax/{file}"))
                data.Add($"syntax/{file}#{line}", kind, valid, line);
        return data;
    }

    private static readonly (string File, IdentifierKind Kind, bool Valid)[] Files =
    [
        ("did_syntax_valid.txt", IdentifierKind.Did, true),
        ("did_syntax_invalid.txt", IdentifierKind.Did, false),
        ("handle_syntax_valid.txt", IdentifierKind.Handle, true),
        ("handle_syntax_invalid.txt", IdentifierKind.Handle, false),
        ("atidentifier_syntax_valid.txt", IdentifierKind.AtIdentifier, true),
        ("atidentifier_syntax_invalid.txt", IdentifierKind.AtIdentifier, false),
        ("nsid_syntax_valid.txt", IdentifierKind.Nsid, true),
        ("nsid_syntax_invalid.txt", IdentifierKind.Nsid, false),
        ("aturi_syntax_valid.txt", IdentifierKind.AtUri, true),
        ("aturi_syntax_invalid.txt", IdentifierKind.AtUri, false),
        ("recordkey_syntax_valid.txt", IdentifierKind.RecordKey, true),
        ("recordkey_syntax_invalid.txt", IdentifierKind.RecordKey, false),
        ("tid_syntax_valid.txt", IdentifierKind.Tid, true),
        ("tid_syntax_invalid.txt", IdentifierKind.Tid, false),
        ("datetime_syntax_valid.txt", IdentifierKind.Datetime, true),
        ("datetime_syntax_invalid.txt", IdentifierKind.Datetime, false),
        ("datetime_parse_invalid.txt", IdentifierKind.Datetime, false),
        ("cid_syntax_invalid.txt", IdentifierKind.Cid, false),
        ("language_syntax_valid.txt", IdentifierKind.Language, true),
        ("language_syntax_invalid.txt", IdentifierKind.Language, false),
        ("language_parse_invalid.txt", IdentifierKind.Language, false),
        ("uri_syntax_valid.txt", IdentifierKind.Uri, true),
        ("uri_syntax_invalid.txt", IdentifierKind.Uri, false),
    ];

    /// <summary>An interop line gets the verdict its file names.</summary>
    [Theory]
    [MemberData(nameof(InteropLines))]
    public void ParseIdentifier_InteropLine_MatchesFile(string vectorId, IdentifierKind kind, bool valid, string input)
    {
        Vector(vectorId);
        var verdict = Adapter.ParseIdentifier(kind, input);
        Assert.Equal(valid, verdict.Valid);
        if (!valid) return;
        var expected = kind is IdentifierKind.Handle ? input.ToLowerInvariant() : input;
        Assert.Equal(expected, verdict.Normalized, ignoreCase: kind is IdentifierKind.AtIdentifier);
    }
}
