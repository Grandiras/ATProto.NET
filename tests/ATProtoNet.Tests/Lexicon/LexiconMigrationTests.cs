using System.Text.Json.Nodes;
using ATProtoNet.LexiconGenerator.Migrations;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>Tests for the Lexicon migration pipeline.</summary>
public sealed class LexiconMigrationTests
{
    // ── DelegateMigration ────────────────────────────────────

    [Fact]
    public void DelegateMigration_TransformsRecord()
    {
        var migration = new DelegateMigration(
            "com.example.post", 1, 2,
            record => record["tags"] = new JsonArray("test"),
            "Add tags field");

        var record = JsonNode.Parse("""{"text":"hello"}""")!.AsObject();
        migration.Transform(record);

        Assert.Equal("hello", record["text"]!.GetValue<string>());
        Assert.NotNull(record["tags"]);
    }

    [Fact]
    public void DelegateMigration_ValidatesParameters()
    {
        Assert.Throws<ArgumentException>(() => new DelegateMigration("", 1, 2, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new DelegateMigration("x", 1, 2, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DelegateMigration("x", 0, 2, _ => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DelegateMigration("x", 2, 1, _ => { }));
    }

    [Fact]
    public void DelegateMigration_ExposesProperties()
    {
        var m = new DelegateMigration("com.example.post", 1, 2, _ => { }, "desc");
        Assert.Equal("com.example.post", m.Nsid);
        Assert.Equal(1, m.FromRevision);
        Assert.Equal(2, m.ToRevision);
        Assert.Equal("desc", m.Description);
    }

    // ── MigrationBuilder ─────────────────────────────────────

    [Fact]
    public void MigrationBuilder_AddProperty_SetsDefault()
    {
        var migration = new MigrationBuilder("com.example.post", 1, 2)
            .AddProperty("tags", new JsonArray())
            .Build();

        var record = JsonNode.Parse("""{"text":"hello"}""")!.AsObject();
        migration.Transform(record);

        Assert.NotNull(record["tags"]);
        Assert.IsAssignableFrom<JsonArray>(record["tags"]);
    }

    [Fact]
    public void MigrationBuilder_RemoveProperty()
    {
        var migration = new MigrationBuilder("com.example.post", 1, 2)
            .RemoveProperty("legacy")
            .Build();

        var record = JsonNode.Parse("""{"text":"hello","legacy":"old"}""")!.AsObject();
        migration.Transform(record);

        Assert.Null(record["legacy"]);
        Assert.Equal("hello", record["text"]!.GetValue<string>());
    }

    [Fact]
    public void MigrationBuilder_RenameProperty()
    {
        var migration = new MigrationBuilder("com.example.post", 1, 2)
            .RenameProperty("body", "content")
            .Build();

        var record = JsonNode.Parse("""{"body":"hello world"}""")!.AsObject();
        migration.Transform(record);

        Assert.Null(record["body"]);
        Assert.Equal("hello world", record["content"]!.GetValue<string>());
    }

    [Fact]
    public void MigrationBuilder_RenameProperty_MissingIsNoOp()
    {
        var migration = new MigrationBuilder("com.example.post", 1, 2)
            .RenameProperty("nonexistent", "target")
            .Build();

        var record = JsonNode.Parse("""{"text":"hello"}""")!.AsObject();
        migration.Transform(record);

        Assert.Null(record["nonexistent"]);
        Assert.Null(record["target"]);
        Assert.Equal("hello", record["text"]!.GetValue<string>());
    }

    [Fact]
    public void MigrationBuilder_CustomApply()
    {
        var migration = new MigrationBuilder("com.example.post", 1, 2)
            .Apply(record =>
            {
                var text = record["text"]?.GetValue<string>();
                record["text"] = text?.ToUpperInvariant();
            })
            .Build();

        var record = JsonNode.Parse("""{"text":"hello"}""")!.AsObject();
        migration.Transform(record);

        Assert.Equal("HELLO", record["text"]!.GetValue<string>());
    }

    [Fact]
    public void MigrationBuilder_ChainsMultipleSteps()
    {
        var migration = new MigrationBuilder("com.example.post", 1, 2)
            .WithDescription("Multi-step migration")
            .AddProperty("version", JsonValue.Create(2))
            .RemoveProperty("legacy")
            .RenameProperty("body", "content")
            .Build();

        var record = JsonNode.Parse("""{"body":"hello","legacy":"old"}""")!.AsObject();
        migration.Transform(record);

        Assert.Equal("hello", record["content"]!.GetValue<string>());
        Assert.Equal(2, record["version"]!.GetValue<int>());
        Assert.Null(record["legacy"]);
        Assert.Null(record["body"]);
        Assert.Equal("Multi-step migration", migration.Description);
    }

    // ── LexiconMigrationRunner ───────────────────────────────

    [Fact]
    public void Runner_BuildChain_SingleStep()
    {
        var runner = new LexiconMigrationRunner();
        runner.AddMigration(new DelegateMigration("com.example.post", 1, 2, _ => { }));

        var chain = runner.BuildChain("com.example.post", 1, 2);
        Assert.Single(chain);
    }

    [Fact]
    public void Runner_BuildChain_MultipleSteps()
    {
        var runner = new LexiconMigrationRunner();
        runner.AddMigration(new DelegateMigration("com.example.post", 1, 2, _ => { }));
        runner.AddMigration(new DelegateMigration("com.example.post", 2, 3, _ => { }));
        runner.AddMigration(new DelegateMigration("com.example.post", 3, 4, _ => { }));

        var chain = runner.BuildChain("com.example.post", 1, 4);
        Assert.Equal(3, chain.Count);
        Assert.Equal(1, chain[0].FromRevision);
        Assert.Equal(2, chain[1].FromRevision);
        Assert.Equal(3, chain[2].FromRevision);
    }

    [Fact]
    public void Runner_BuildChain_ThrowsOnGap()
    {
        var runner = new LexiconMigrationRunner();
        runner.AddMigration(new DelegateMigration("com.example.post", 1, 2, _ => { }));
        // Missing 2→3
        runner.AddMigration(new DelegateMigration("com.example.post", 3, 4, _ => { }));

        Assert.Throws<InvalidOperationException>(() => runner.BuildChain("com.example.post", 1, 4));
    }

    [Fact]
    public void Runner_BuildChain_ThrowsOnInvalidRange()
    {
        var runner = new LexiconMigrationRunner();
        Assert.Throws<ArgumentException>(() => runner.BuildChain("x", 2, 1));
        Assert.Throws<ArgumentException>(() => runner.BuildChain("x", 1, 1));
    }

    [Fact]
    public void Runner_Migrate_AppliesTransforms()
    {
        var runner = new LexiconMigrationRunner();
        runner.AddMigration(new MigrationBuilder("com.example.post", 1, 2)
            .AddProperty("tags", new JsonArray())
            .Build());

        var records = new List<string>
        {
            """{"text":"hello"}""",
            """{"text":"world"}""",
        };

        var result = runner.Migrate("com.example.post", 1, 2, records);

        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.False(result.HasErrors);
        Assert.Equal(2, result.MigratedRecords.Count);

        foreach (var rec in result.MigratedRecords)
        {
            var obj = JsonNode.Parse(rec)!.AsObject();
            Assert.NotNull(obj["tags"]);
        }
    }

    [Fact]
    public void Runner_Migrate_HandlesInvalidJson()
    {
        var runner = new LexiconMigrationRunner();
        runner.AddMigration(new DelegateMigration("x", 1, 2, _ => { }));

        var records = new List<string> { "not json", """{"valid":true}""" };
        var result = runner.Migrate("x", 1, 2, records);

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Runner_Migrate_HandlesNonObjectJson()
    {
        var runner = new LexiconMigrationRunner();
        runner.AddMigration(new DelegateMigration("x", 1, 2, _ => { }));

        var records = new List<string> { """[1,2,3]""" };
        var result = runner.Migrate("x", 1, 2, records);

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Contains(result.Errors, e => e.Message.Contains("not a JSON object"));
    }

    [Fact]
    public void Runner_Migrate_ChainsMultipleMigrations()
    {
        var runner = new LexiconMigrationRunner();
        runner.AddMigration(new MigrationBuilder("com.example.post", 1, 2)
            .AddProperty("tags", new JsonArray())
            .Build());
        runner.AddMigration(new MigrationBuilder("com.example.post", 2, 3)
            .RenameProperty("text", "content")
            .Build());

        var records = new List<string> { """{"text":"hello"}""" };
        var result = runner.Migrate("com.example.post", 1, 3, records);

        Assert.Equal(1, result.SuccessCount);
        var obj = JsonNode.Parse(result.MigratedRecords[0])!.AsObject();
        Assert.Equal("hello", obj["content"]!.GetValue<string>());
        Assert.NotNull(obj["tags"]);
        Assert.Null(obj["text"]);
    }

    [Fact]
    public void Runner_CanMigrate_ReturnsTrueForValidChain()
    {
        var runner = new LexiconMigrationRunner();
        runner.AddMigration(new DelegateMigration("x", 1, 2, _ => { }));

        Assert.True(runner.CanMigrate("x", 1, 2));
        Assert.False(runner.CanMigrate("x", 1, 3));
        Assert.False(runner.CanMigrate("y", 1, 2));
    }

    [Fact]
    public void Runner_GetMigrations_ReturnsOrdered()
    {
        var runner = new LexiconMigrationRunner();
        runner.AddMigration(new DelegateMigration("x", 3, 4, _ => { }));
        runner.AddMigration(new DelegateMigration("x", 1, 2, _ => { }));
        runner.AddMigration(new DelegateMigration("x", 2, 3, _ => { }));

        var migrations = runner.GetMigrations("x");
        Assert.Equal(3, migrations.Count);
        Assert.Equal(1, migrations[0].FromRevision);
        Assert.Equal(2, migrations[1].FromRevision);
        Assert.Equal(3, migrations[2].FromRevision);
    }

    [Fact]
    public void Runner_GetRegisteredNsids()
    {
        var runner = new LexiconMigrationRunner();
        runner.AddMigration(new DelegateMigration("b.example", 1, 2, _ => { }));
        runner.AddMigration(new DelegateMigration("a.example", 1, 2, _ => { }));
        runner.AddMigration(new DelegateMigration("a.example", 2, 3, _ => { }));

        var nsids = runner.GetRegisteredNsids();
        Assert.Equal(2, nsids.Count);
        Assert.Equal("a.example", nsids[0]);
        Assert.Equal("b.example", nsids[1]);
    }

    [Fact]
    public void Runner_AddMigrations_Batch()
    {
        var runner = new LexiconMigrationRunner();
        runner.AddMigrations([
            new DelegateMigration("x", 1, 2, _ => { }),
            new DelegateMigration("x", 2, 3, _ => { }),
        ]);

        Assert.True(runner.CanMigrate("x", 1, 3));
    }

    // ── ScaffoldFromDiff ─────────────────────────────────────

    [Fact]
    public void ScaffoldFromDiff_GeneratesPropertyAddMigration()
    {
        var baseline = new List<LexiconGenerator.Schema.LexiconDocument>
        {
            MakeDoc("com.example.post", properties: new()
            {
                ["text"] = new() { Type = "string" },
            }),
        };

        var current = new List<LexiconGenerator.Schema.LexiconDocument>
        {
            MakeDoc("com.example.post", properties: new()
            {
                ["text"] = new() { Type = "string" },
                ["tags"] = new() { Type = "array" },
            }),
        };

        var differ = new LexiconGenerator.CodeGen.LexiconDiffer();
        var diff = differ.Compare(baseline, current);
        var scaffolds = LexiconMigrationRunner.ScaffoldFromDiff(diff, baseline, 2);

        Assert.Single(scaffolds);
        Assert.Equal("com.example.post", scaffolds[0].Nsid);
        Assert.Equal(1, scaffolds[0].FromRevision);
        Assert.Equal(2, scaffolds[0].ToRevision);
    }

    [Fact]
    public void ScaffoldFromDiff_GeneratesPropertyRemoveMigration()
    {
        var baseline = new List<LexiconGenerator.Schema.LexiconDocument>
        {
            MakeDoc("com.example.post", properties: new()
            {
                ["text"] = new() { Type = "string" },
                ["legacy"] = new() { Type = "string" },
            }),
        };

        var current = new List<LexiconGenerator.Schema.LexiconDocument>
        {
            MakeDoc("com.example.post", properties: new()
            {
                ["text"] = new() { Type = "string" },
            }),
        };

        var differ = new LexiconGenerator.CodeGen.LexiconDiffer();
        var diff = differ.Compare(baseline, current);
        var scaffolds = LexiconMigrationRunner.ScaffoldFromDiff(diff, baseline, 2);

        Assert.Single(scaffolds);

        // Apply the scaffolded migration
        var record = JsonNode.Parse("""{"text":"hello","legacy":"old"}""")!.AsObject();
        scaffolds[0].Transform(record);
        Assert.Null(record["legacy"]);
        Assert.Equal("hello", record["text"]!.GetValue<string>());
    }

    // ── Helpers ──────────────────────────────────────────────

    private static LexiconGenerator.Schema.LexiconDocument MakeDoc(
        string id,
        int? revision = null,
        Dictionary<string, LexiconGenerator.Schema.LexiconSchema>? properties = null)
    {
        return new LexiconGenerator.Schema.LexiconDocument
        {
            Id = id,
            Revision = revision,
            Defs = new()
            {
                ["main"] = new LexiconGenerator.Schema.LexiconSchema
                {
                    Type = "record",
                    Record = new LexiconGenerator.Schema.LexiconSchema
                    {
                        Type = "object",
                        Properties = properties ?? new()
                        {
                            ["text"] = new() { Type = "string" },
                        },
                        Required = ["text"],
                    },
                },
            },
        };
    }
}
