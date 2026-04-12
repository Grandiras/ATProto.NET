using ATProtoNet.LexiconGenerator.Migrations;
using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>Tests for <see cref="LexiconPublisher"/>.</summary>
public sealed class LexiconPublisherTests : IDisposable
{
    private readonly string _tempDir;

    public LexiconPublisherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lexicon-publisher-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Publish_WritesSchemas_NoBaseline()
    {
        var outputDir = Path.Combine(_tempDir, "output");
        var documents = new List<LexiconDocument>
        {
            MakeDoc("com.example.post"),
            MakeDoc("com.example.like"),
        };

        var publisher = new LexiconPublisher();
        var result = await publisher.PublishAsync(documents, outputDir);

        Assert.False(result.HasErrors);
        Assert.Equal(2, result.WrittenNsids.Count);
        Assert.Contains("com.example.post", result.WrittenNsids);
        Assert.Contains("com.example.like", result.WrittenNsids);

        // Verify files exist on disk
        Assert.True(File.Exists(Path.Combine(outputDir, "com/example/post.json")));
        Assert.True(File.Exists(Path.Combine(outputDir, "com/example/like.json")));
    }

    [Fact]
    public async Task Publish_SkipsUnchangedSchemas()
    {
        // Write baseline
        var baselineDir = Path.Combine(_tempDir, "baseline");
        var outputDir = Path.Combine(_tempDir, "output");

        var documents = new List<LexiconDocument>
        {
            MakeDoc("com.example.post"),
        };

        var publisher = new LexiconPublisher();

        // Publish initial
        await publisher.PublishAsync(documents, baselineDir);

        // Publish same schemas against baseline
        var result = await publisher.PublishAsync(documents, outputDir, baselineDir);

        Assert.False(result.HasErrors);
        Assert.Empty(result.WrittenNsids);
        Assert.Single(result.SkippedNsids);
        Assert.Contains("com.example.post", result.SkippedNsids);
    }

    [Fact]
    public async Task Publish_DetectsBreakingChanges()
    {
        var baselineDir = Path.Combine(_tempDir, "baseline");
        var outputDir = Path.Combine(_tempDir, "output");

        var baseline = new List<LexiconDocument>
        {
            MakeDoc("com.example.post", properties: new()
            {
                ["text"] = new() { Type = "string" },
                ["author"] = new() { Type = "string" },
            }),
        };

        // Remove a property = breaking
        var current = new List<LexiconDocument>
        {
            MakeDoc("com.example.post", properties: new()
            {
                ["text"] = new() { Type = "string" },
            }),
        };

        var publisher = new LexiconPublisher();
        await publisher.PublishAsync(baseline, baselineDir);

        var result = await publisher.PublishAsync(current, outputDir, baselineDir, failOnBreaking: true);

        Assert.True(result.HasErrors);
        Assert.NotNull(result.Diff);
        Assert.True(result.Diff!.HasBreakingChanges);
    }

    [Fact]
    public async Task Publish_ForcePublishesBreakingChanges()
    {
        var baselineDir = Path.Combine(_tempDir, "baseline");
        var outputDir = Path.Combine(_tempDir, "output");

        var baseline = new List<LexiconDocument>
        {
            MakeDoc("com.example.post", properties: new()
            {
                ["text"] = new() { Type = "string" },
                ["author"] = new() { Type = "string" },
            }),
        };

        var current = new List<LexiconDocument>
        {
            MakeDoc("com.example.post", properties: new()
            {
                ["text"] = new() { Type = "string" },
            }),
        };

        var publisher = new LexiconPublisher();
        await publisher.PublishAsync(baseline, baselineDir);

        // failOnBreaking = false → force
        var result = await publisher.PublishAsync(current, outputDir, baselineDir, failOnBreaking: false);

        Assert.False(result.HasErrors);
        Assert.Single(result.WrittenNsids);
    }

    [Fact]
    public async Task Publish_AutoBumpsRevisions()
    {
        var baselineDir = Path.Combine(_tempDir, "baseline");
        var outputDir = Path.Combine(_tempDir, "output");

        var baseline = new List<LexiconDocument>
        {
            MakeDoc("com.example.post", revision: 1, properties: new()
            {
                ["text"] = new() { Type = "string" },
            }),
        };

        // Add an optional property = non-breaking
        var current = new List<LexiconDocument>
        {
            MakeDoc("com.example.post", revision: 1, properties: new()
            {
                ["text"] = new() { Type = "string" },
                ["tags"] = new() { Type = "array" },
            }),
        };

        var publisher = new LexiconPublisher();
        await publisher.PublishAsync(baseline, baselineDir);

        var result = await publisher.PublishAsync(current, outputDir, baselineDir, autoBumpRevisions: true);

        Assert.False(result.HasErrors);
        Assert.NotNull(result.SuggestedRevisions);
        Assert.True(result.SuggestedRevisions!.ContainsKey("com.example.post"));
        Assert.Equal(2, result.SuggestedRevisions["com.example.post"]);
    }

    [Fact]
    public async Task Publish_NoBump_KeepsRevision()
    {
        var baselineDir = Path.Combine(_tempDir, "baseline");
        var outputDir = Path.Combine(_tempDir, "output");

        var baseline = new List<LexiconDocument>
        {
            MakeDoc("com.example.post", revision: 1, properties: new()
            {
                ["text"] = new() { Type = "string" },
            }),
        };

        var current = new List<LexiconDocument>
        {
            MakeDoc("com.example.post", revision: 1, properties: new()
            {
                ["text"] = new() { Type = "string" },
                ["tags"] = new() { Type = "array" },
            }),
        };

        var publisher = new LexiconPublisher();
        await publisher.PublishAsync(baseline, baselineDir);

        var result = await publisher.PublishAsync(current, outputDir, baselineDir, autoBumpRevisions: false);

        Assert.False(result.HasErrors);
        Assert.Null(result.SuggestedRevisions);
    }

    [Fact]
    public async Task LoadFromDirectory_ParsesValidDocuments()
    {
        var dir = Path.Combine(_tempDir, "schemas");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "com", "example"));

        var doc = MakeDoc("com.example.post");
        var json = System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        await File.WriteAllTextAsync(Path.Combine(dir, "com", "example", "post.json"), json);

        // Also write an invalid file
        await File.WriteAllTextAsync(Path.Combine(dir, "invalid.json"), "not json");

        var loaded = await LexiconPublisher.LoadFromDirectoryAsync(dir);
        Assert.Single(loaded);
        Assert.Equal("com.example.post", loaded[0].Id);
    }

    // ── Helpers ──────────────────────────────────────────────

    private static LexiconDocument MakeDoc(
        string id,
        int? revision = null,
        Dictionary<string, LexiconSchema>? properties = null)
    {
        return new LexiconDocument
        {
            Id = id,
            Revision = revision,
            Defs = new()
            {
                ["main"] = new LexiconSchema
                {
                    Type = "record",
                    Record = new LexiconSchema
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
