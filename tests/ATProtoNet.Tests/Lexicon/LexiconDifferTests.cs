using ATProtoNet.LexiconGenerator.CodeGen;
using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>Tests for <see cref="LexiconDiffer"/>.</summary>
public sealed class LexiconDifferTests
{
    private readonly LexiconDiffer _differ = new();

    private static LexiconDocument MakeDoc(string id, int? revision = null, Dictionary<string, LexiconSchema>? defs = null)
    {
        return new LexiconDocument
        {
            Id = id,
            Revision = revision,
            Defs = defs ?? new Dictionary<string, LexiconSchema>
            {
                ["main"] = new LexiconSchema
                {
                    Type = "record",
                    Record = new LexiconSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, LexiconSchema>
                        {
                            ["text"] = new LexiconSchema { Type = "string" },
                        },
                        Required = ["text"],
                    }
                }
            }
        };
    }

    // ── No Changes ───────────────────────────────────────────

    [Fact]
    public void Compare_IdenticalSchemas_NoChanges()
    {
        var docs = new[] { MakeDoc("com.example.post") };
        var result = _differ.Compare(docs, docs);
        Assert.False(result.HasChanges);
        Assert.Empty(result.Changes);
    }

    // ── Schema-Level Changes ─────────────────────────────────

    [Fact]
    public void Compare_SchemaAdded_NonBreaking()
    {
        var baseline = new[] { MakeDoc("com.example.post") };
        var current = new[] { MakeDoc("com.example.post"), MakeDoc("com.example.like") };

        var result = _differ.Compare(baseline, current);

        Assert.True(result.HasChanges);
        Assert.False(result.HasBreakingChanges);
        Assert.Single(result.Changes);
        Assert.Equal(ChangeKind.SchemaAdded, result.Changes[0].Kind);
        Assert.Equal("com.example.like", result.Changes[0].Nsid);
    }

    [Fact]
    public void Compare_SchemaRemoved_Breaking()
    {
        var baseline = new[] { MakeDoc("com.example.post"), MakeDoc("com.example.like") };
        var current = new[] { MakeDoc("com.example.post") };

        var result = _differ.Compare(baseline, current);

        Assert.True(result.HasBreakingChanges);
        Assert.Contains(result.Changes, c => c.Kind == ChangeKind.SchemaRemoved && c.Nsid == "com.example.like");
    }

    // ── Definition-Level Changes ─────────────────────────────

    [Fact]
    public void Compare_DefinitionAdded_NonBreaking()
    {
        var baseline = new[] { MakeDoc("com.example.post") };
        var current = new[]
        {
            new LexiconDocument
            {
                Id = "com.example.post",
                Defs = new Dictionary<string, LexiconSchema>
                {
                    ["main"] = baseline[0].Defs["main"],
                    ["viewRecord"] = new LexiconSchema { Type = "object" },
                }
            }
        };

        var result = _differ.Compare(baseline, current);

        Assert.True(result.HasChanges);
        Assert.False(result.HasBreakingChanges);
        Assert.Contains(result.Changes, c => c.Kind == ChangeKind.DefinitionAdded);
    }

    [Fact]
    public void Compare_DefinitionRemoved_Breaking()
    {
        var baseline = new[]
        {
            new LexiconDocument
            {
                Id = "com.example.post",
                Defs = new Dictionary<string, LexiconSchema>
                {
                    ["main"] = new LexiconSchema { Type = "record", Record = new LexiconSchema { Type = "object", Properties = new() } },
                    ["viewRecord"] = new LexiconSchema { Type = "object" },
                }
            }
        };
        var current = new[] { MakeDoc("com.example.post") };

        var result = _differ.Compare(baseline, current);

        Assert.True(result.HasBreakingChanges);
        Assert.Contains(result.Changes, c => c.Kind == ChangeKind.DefinitionRemoved);
    }

    // ── Property Changes ─────────────────────────────────────

    [Fact]
    public void Compare_OptionalPropertyAdded_NonBreaking()
    {
        var baseline = new[] { MakeDoc("com.example.post") };
        var current = new[]
        {
            new LexiconDocument
            {
                Id = "com.example.post",
                Defs = new Dictionary<string, LexiconSchema>
                {
                    ["main"] = new LexiconSchema
                    {
                        Type = "record",
                        Record = new LexiconSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconSchema>
                            {
                                ["text"] = new LexiconSchema { Type = "string" },
                                ["lang"] = new LexiconSchema { Type = "string" },
                            },
                            Required = ["text"], // lang is optional
                        }
                    }
                }
            }
        };

        var result = _differ.Compare(baseline, current);

        Assert.True(result.HasChanges);
        Assert.False(result.HasBreakingChanges);
        Assert.Contains(result.Changes, c => c.Kind == ChangeKind.PropertyAdded && !c.IsBreaking);
    }

    [Fact]
    public void Compare_RequiredPropertyAdded_Breaking()
    {
        var baseline = new[] { MakeDoc("com.example.post") };
        var current = new[]
        {
            new LexiconDocument
            {
                Id = "com.example.post",
                Defs = new Dictionary<string, LexiconSchema>
                {
                    ["main"] = new LexiconSchema
                    {
                        Type = "record",
                        Record = new LexiconSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconSchema>
                            {
                                ["text"] = new LexiconSchema { Type = "string" },
                                ["lang"] = new LexiconSchema { Type = "string" },
                            },
                            Required = ["text", "lang"], // lang is required!
                        }
                    }
                }
            }
        };

        var result = _differ.Compare(baseline, current);

        Assert.True(result.HasBreakingChanges);
        Assert.Contains(result.Changes, c => c.Kind == ChangeKind.PropertyAdded && c.IsBreaking);
    }

    [Fact]
    public void Compare_PropertyRemoved_Breaking()
    {
        var baseline = new[]
        {
            new LexiconDocument
            {
                Id = "com.example.post",
                Defs = new Dictionary<string, LexiconSchema>
                {
                    ["main"] = new LexiconSchema
                    {
                        Type = "record",
                        Record = new LexiconSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconSchema>
                            {
                                ["text"] = new LexiconSchema { Type = "string" },
                                ["lang"] = new LexiconSchema { Type = "string" },
                            },
                            Required = ["text"],
                        }
                    }
                }
            }
        };
        var current = new[] { MakeDoc("com.example.post") }; // only "text"

        var result = _differ.Compare(baseline, current);

        Assert.True(result.HasBreakingChanges);
        Assert.Contains(result.Changes, c => c.Kind == ChangeKind.PropertyRemoved);
    }

    [Fact]
    public void Compare_PropertyTypeChanged_Breaking()
    {
        var baseline = new[] { MakeDoc("com.example.post") };
        var current = new[]
        {
            new LexiconDocument
            {
                Id = "com.example.post",
                Defs = new Dictionary<string, LexiconSchema>
                {
                    ["main"] = new LexiconSchema
                    {
                        Type = "record",
                        Record = new LexiconSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconSchema>
                            {
                                ["text"] = new LexiconSchema { Type = "integer" }, // was string
                            },
                            Required = ["text"],
                        }
                    }
                }
            }
        };

        var result = _differ.Compare(baseline, current);

        Assert.True(result.HasBreakingChanges);
        Assert.Contains(result.Changes, c => c.Kind == ChangeKind.PropertyTypeChanged);
    }

    [Fact]
    public void Compare_PropertyBecameRequired_Breaking()
    {
        var baseline = new[]
        {
            new LexiconDocument
            {
                Id = "com.example.post",
                Defs = new Dictionary<string, LexiconSchema>
                {
                    ["main"] = new LexiconSchema
                    {
                        Type = "record",
                        Record = new LexiconSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconSchema>
                            {
                                ["text"] = new LexiconSchema { Type = "string" },
                                ["lang"] = new LexiconSchema { Type = "string" },
                            },
                            Required = ["text"], // lang is optional
                        }
                    }
                }
            }
        };
        var current = new[]
        {
            new LexiconDocument
            {
                Id = "com.example.post",
                Defs = new Dictionary<string, LexiconSchema>
                {
                    ["main"] = new LexiconSchema
                    {
                        Type = "record",
                        Record = new LexiconSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconSchema>
                            {
                                ["text"] = new LexiconSchema { Type = "string" },
                                ["lang"] = new LexiconSchema { Type = "string" },
                            },
                            Required = ["text", "lang"], // lang now required
                        }
                    }
                }
            }
        };

        var result = _differ.Compare(baseline, current);

        Assert.True(result.HasBreakingChanges);
        Assert.Contains(result.Changes, c => c.Kind == ChangeKind.PropertyBecameRequired);
    }

    // ── Type-Level Changes ───────────────────────────────────

    [Fact]
    public void Compare_DefinitionTypeChanged_Breaking()
    {
        var baseline = new[]
        {
            new LexiconDocument
            {
                Id = "com.example.post",
                Defs = new Dictionary<string, LexiconSchema>
                {
                    ["main"] = new LexiconSchema { Type = "record" },
                }
            }
        };
        var current = new[]
        {
            new LexiconDocument
            {
                Id = "com.example.post",
                Defs = new Dictionary<string, LexiconSchema>
                {
                    ["main"] = new LexiconSchema { Type = "query" },
                }
            }
        };

        var result = _differ.Compare(baseline, current);

        Assert.True(result.HasBreakingChanges);
        Assert.Contains(result.Changes, c => c.Kind == ChangeKind.TypeChanged);
    }

    // ── Report ───────────────────────────────────────────────

    [Fact]
    public void ToReport_NoChanges_ReturnsMessage()
    {
        var docs = new[] { MakeDoc("com.example.post") };
        var result = _differ.Compare(docs, docs);

        Assert.Contains("No schema changes", result.ToReport());
    }

    [Fact]
    public void ToReport_BreakingChanges_IncludesWarning()
    {
        var baseline = new[] { MakeDoc("com.example.post"), MakeDoc("com.example.like") };
        var current = new[] { MakeDoc("com.example.post") };

        var result = _differ.Compare(baseline, current);

        var report = result.ToReport();
        Assert.Contains("Breaking changes detected", report);
        Assert.Contains("BREAK", report);
    }

    [Fact]
    public void ToReport_NonBreakingChanges_ShowsCompatible()
    {
        var baseline = new[] { MakeDoc("com.example.post") };
        var current = new[] { MakeDoc("com.example.post"), MakeDoc("com.example.like") };

        var result = _differ.Compare(baseline, current);

        Assert.Contains("backwards-compatible", result.ToReport());
    }

    // ── Revision Suggestions ─────────────────────────────────

    [Fact]
    public void SuggestRevisions_BumpsNonBreakingSchemas()
    {
        var baseline = new[] { MakeDoc("com.example.post", revision: 2) };
        var current = new[]
        {
            new LexiconDocument
            {
                Id = "com.example.post",
                Revision = 2,
                Defs = new Dictionary<string, LexiconSchema>
                {
                    ["main"] = new LexiconSchema
                    {
                        Type = "record",
                        Record = new LexiconSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconSchema>
                            {
                                ["text"] = new LexiconSchema { Type = "string" },
                                ["lang"] = new LexiconSchema { Type = "string" },
                            },
                            Required = ["text"],
                        }
                    }
                }
            }
        };

        var result = _differ.Compare(baseline, current);
        var revisions = result.SuggestRevisions(baseline);

        Assert.Contains("com.example.post", revisions.Keys);
        Assert.Equal(3, revisions["com.example.post"]);
    }


    // ── Space Type Declarations ──────────────────────────────

    private static LexiconDocument MakeSpaceDoc(
        string id,
        string key = "any",
        string name = "AtmoBoards Forum",
        List<string>? collections = null,
        Dictionary<string, string>? localizedNames = null)
    {
        return new LexiconDocument
        {
            Id = id,
            Defs = new Dictionary<string, LexiconSchema>
            {
                ["main"] = new LexiconSchema
                {
                    Type = "space",
                    Key = key,
                    Name = name,
                    LocalizedNames = localizedNames,
                    Collections = collections ?? ["com.atmoboards.thread"],
                }
            }
        };
    }

    [Fact]
    public void Compare_IdenticalSpaceDeclarations_NoChanges()
    {
        var docs = new[] { MakeSpaceDoc("com.atmoboards.forum") };

        var result = _differ.Compare(docs, docs);

        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Compare_SpaceCollectionAdded_NonBreakingButReported()
    {
        var baseline = new[] { MakeSpaceDoc("com.atmoboards.forum") };
        var current = new[]
        {
            MakeSpaceDoc("com.atmoboards.forum", collections: ["com.atmoboards.thread", "com.atmoboards.reply"])
        };

        var result = _differ.Compare(baseline, current);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ChangeKind.SpaceCollectionAdded, change.Kind);
        Assert.False(change.IsBreaking);
        // The default collection set is resolved at grant-evaluation time, so this widens
        // grants the user already consented to — the point of reporting it at all.
        Assert.Contains("com.atmoboards.reply", change.Description);
        Assert.Contains("widens", change.Description);
    }

    [Fact]
    public void Compare_SpaceCollectionRemoved_Breaking()
    {
        var baseline = new[]
        {
            MakeSpaceDoc("com.atmoboards.forum", collections: ["com.atmoboards.thread", "com.atmoboards.reply"])
        };
        var current = new[] { MakeSpaceDoc("com.atmoboards.forum") };

        var result = _differ.Compare(baseline, current);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ChangeKind.SpaceCollectionRemoved, change.Kind);
        Assert.True(change.IsBreaking);
        Assert.Contains("com.atmoboards.reply", change.Description);
    }

    [Fact]
    public void Compare_SpaceKeyChanged_Breaking()
    {
        var baseline = new[] { MakeSpaceDoc("com.atmoboards.forum") };
        var current = new[] { MakeSpaceDoc("com.atmoboards.forum", key: "tid") };

        var result = _differ.Compare(baseline, current);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ChangeKind.SpaceKeyChanged, change.Kind);
        Assert.True(change.IsBreaking);
    }

    [Fact]
    public void Compare_SpaceNameChanged_NonBreaking()
    {
        var baseline = new[] { MakeSpaceDoc("com.atmoboards.forum") };
        var current = new[] { MakeSpaceDoc("com.atmoboards.forum", name: "AtmoBoards Forums") };

        var result = _differ.Compare(baseline, current);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ChangeKind.SpaceNameChanged, change.Kind);
        Assert.False(change.IsBreaking);
        Assert.Contains("consent", change.Description);
    }

    [Fact]
    public void Compare_SpaceLocalizedNameAdded_NonBreaking()
    {
        var baseline = new[] { MakeSpaceDoc("com.atmoboards.forum") };
        var current = new[]
        {
            MakeSpaceDoc("com.atmoboards.forum", localizedNames: new Dictionary<string, string> { ["es"] = "Foro" })
        };

        var result = _differ.Compare(baseline, current);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ChangeKind.SpaceNameChanged, change.Kind);
        Assert.False(change.IsBreaking);
        Assert.Equal("main.name:lang.es", change.Path);
    }

    [Fact]
    public void Compare_SpaceReplacedByRecord_Breaking()
    {
        var baseline = new[] { MakeSpaceDoc("com.atmoboards.forum") };
        var current = new[] { MakeDoc("com.atmoboards.forum") };

        var result = _differ.Compare(baseline, current);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ChangeKind.TypeChanged, change.Kind);
        Assert.True(change.IsBreaking);
    }

    // ── Counts ───────────────────────────────────────────────

    [Fact]
    public void BreakingCount_ReturnsCorrectCount()
    {
        var baseline = new[] { MakeDoc("com.example.post"), MakeDoc("com.example.like") };
        var current = Array.Empty<LexiconDocument>();

        var result = _differ.Compare(baseline, current);

        Assert.Equal(2, result.BreakingCount);
        Assert.Equal(0, result.NonBreakingCount);
    }
}
