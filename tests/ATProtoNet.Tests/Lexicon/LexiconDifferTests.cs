using ATProtoNet.LexiconGenerator.CodeGen;
using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>Tests for <see cref="LexiconDiffer"/>.</summary>
public sealed class LexiconDifferTests
{
    private readonly LexiconDiffer _differ = new();

    /// <summary>
    /// A record Lexicon with a <c>main</c> def whose properties are written <c>name:type,…</c> and
    /// whose required ones are written <c>name,…</c>, plus any extra (empty object) defs.
    /// </summary>
    private static LexiconDocument Record(
        string id, string properties = "text:string", string required = "text", int? revision = null, params string[] extraDefs)
    {
        var defs = new Dictionary<string, LexiconSchema>
        {
            ["main"] = new()
            {
                Type = "record",
                Record = new LexiconSchema
                {
                    Type = "object",
                    Properties = properties.Split(',').Select(p => p.Split(':'))
                        .ToDictionary(p => p[0], p => new LexiconSchema { Type = p[1] }),
                    Required = [.. required.Split(',', StringSplitOptions.RemoveEmptyEntries)],
                },
            },
        };
        foreach (var def in extraDefs)
            defs[def] = new LexiconSchema { Type = "object" };
        return new LexiconDocument { Id = id, Revision = revision, Defs = defs };
    }

    private static LexiconDocument MainDef(string id, string type) =>
        new() { Id = id, Defs = new Dictionary<string, LexiconSchema> { ["main"] = new() { Type = type } } };

    private static LexiconDocument Space(
        string key = "any", string name = "AtmoBoards Forum", List<string>? collections = null, Dictionary<string, string>? localizedNames = null) =>
        new()
        {
            Id = "com.atmoboards.forum",
            Defs = new Dictionary<string, LexiconSchema>
            {
                ["main"] = new()
                {
                    Type = "space",
                    Key = key,
                    Name = name,
                    LocalizedNames = localizedNames,
                    Collections = collections ?? ["com.atmoboards.thread"],
                },
            },
        };

    private static readonly LexiconDocument Post = Record("com.example.post");
    private static readonly LexiconDocument Like = Record("com.example.like");

    [Fact]
    public void Compare_IdenticalSchemas_NoChanges()
    {
        LexiconDocument[] docs = [Post, Space()];

        var result = _differ.Compare(docs, docs);

        Assert.False(result.HasChanges);
        Assert.Empty(result.Changes);
    }

    /// <summary>
    /// Baseline, current, the change expected among the reported ones, whether it (and so the diff)
    /// is breaking, and text its description or path must mention, if any.
    /// </summary>
    public static TheoryData<string, LexiconDocument[], LexiconDocument[], ChangeKind, bool, string?> Changes() => new()
    {
        { "schema added", [Post], [Post, Like], ChangeKind.SchemaAdded, false, "com.example.like" },
        { "schema removed", [Post, Like], [Post], ChangeKind.SchemaRemoved, true, "com.example.like" },
        { "definition added", [Post], [Record("com.example.post", extraDefs: "viewRecord")], ChangeKind.DefinitionAdded, false, null },
        { "definition removed", [Record("com.example.post", extraDefs: "viewRecord")], [Post], ChangeKind.DefinitionRemoved, true, null },
        { "optional property added", [Post], [Record("com.example.post", "text:string,lang:string")], ChangeKind.PropertyAdded, false, null },
        { "required property added", [Post], [Record("com.example.post", "text:string,lang:string", "text,lang")], ChangeKind.PropertyAdded, true, null },
        { "property removed", [Record("com.example.post", "text:string,lang:string")], [Post], ChangeKind.PropertyRemoved, true, null },
        { "property type changed", [Post], [Record("com.example.post", "text:integer")], ChangeKind.PropertyTypeChanged, true, null },
        {
            "property became required", [Record("com.example.post", "text:string,lang:string")],
            [Record("com.example.post", "text:string,lang:string", "text,lang")], ChangeKind.PropertyBecameRequired, true, null
        },
        { "definition type changed", [MainDef("com.example.post", "record")], [MainDef("com.example.post", "query")], ChangeKind.TypeChanged, true, null },
        // The default collection set is resolved at grant-evaluation time, so this widens grants the
        // user already consented to: the point of reporting it at all.
        {
            "space collection added", [Space()], [Space(collections: ["com.atmoboards.thread", "com.atmoboards.reply"])],
            ChangeKind.SpaceCollectionAdded, false, "widens"
        },
        {
            "space collection removed", [Space(collections: ["com.atmoboards.thread", "com.atmoboards.reply"])], [Space()],
            ChangeKind.SpaceCollectionRemoved, true, "com.atmoboards.reply"
        },
        { "space key changed", [Space()], [Space(key: "tid")], ChangeKind.SpaceKeyChanged, true, null },
        { "space name changed", [Space()], [Space(name: "AtmoBoards Forums")], ChangeKind.SpaceNameChanged, false, "consent" },
        {
            "space localized name added", [Space()], [Space(localizedNames: new Dictionary<string, string> { ["es"] = "Foro" })],
            ChangeKind.SpaceNameChanged, false, "main.name:lang.es"
        },
        { "space replaced by a record", [Space()], [Record("com.atmoboards.forum")], ChangeKind.TypeChanged, true, null },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Compare_OneChange_ReportsItsKindAndSeverity(
        string name, LexiconDocument[] baseline, LexiconDocument[] current, ChangeKind kind, bool breaking, string? mentions)
    {
        _ = name;

        var result = _differ.Compare(baseline, current);

        var change = Assert.Single(result.Changes, c => c.Kind == kind);
        Assert.Equal(breaking, change.IsBreaking);
        Assert.Equal(breaking, result.HasBreakingChanges);
        if (mentions is not null)
            Assert.Contains(mentions, $"{change.Nsid} {change.Path} {change.Description}", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, "No schema changes")]
    [InlineData(true, false, "backwards-compatible")]
    [InlineData(false, true, "Breaking changes detected", "BREAK")]
    public void ToReport_SaysWhetherTheChangesAreCompatible(bool add, bool remove, params string[] expected)
    {
        LexiconDocument[] baseline = remove ? [Post, Like] : [Post];
        LexiconDocument[] current = add ? [Post, Like] : [Post];

        var report = _differ.Compare(baseline, current).ToReport();
        Assert.All(expected, text => Assert.Contains(text, report, StringComparison.Ordinal));
    }

    [Fact]
    public void SuggestRevisions_BumpsNonBreakingSchemas()
    {
        LexiconDocument[] baseline = [Record("com.example.post", revision: 2)];

        var result = _differ.Compare(baseline, [Record("com.example.post", "text:string,lang:string", revision: 2)]);

        Assert.Equal(3, result.SuggestRevisions(baseline)["com.example.post"]);
    }

    [Fact]
    public void BreakingCount_ReturnsCorrectCount()
    {
        var result = _differ.Compare([Post, Like], []);

        Assert.Equal(2, result.BreakingCount);
        Assert.Equal(0, result.NonBreakingCount);
    }
}
