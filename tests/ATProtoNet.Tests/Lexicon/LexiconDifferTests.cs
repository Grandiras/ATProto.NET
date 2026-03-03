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
