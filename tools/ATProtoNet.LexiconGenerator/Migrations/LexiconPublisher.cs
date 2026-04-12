using System.Text.Json;
using ATProtoNet.LexiconGenerator.CodeGen;
using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.LexiconGenerator.Migrations;

/// <summary>
/// Result of a publish operation.
/// </summary>
public sealed class PublishResult
{
    /// <summary>Schemas that were written.</summary>
    public IReadOnlyList<string> WrittenNsids { get; init; } = [];

    /// <summary>Schemas that were skipped (unchanged).</summary>
    public IReadOnlyList<string> SkippedNsids { get; init; } = [];

    /// <summary>Errors encountered during publishing.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>The diff from baseline, if a baseline was provided.</summary>
    public DiffResult? Diff { get; init; }

    /// <summary>Suggested revision bumps, if a baseline was provided.</summary>
    public IReadOnlyDictionary<string, int>? SuggestedRevisions { get; init; }

    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Publishes Lexicon schemas to a directory with version tracking and diff-based validation.
/// Supports publishing from Lexicon JSON files, compiled assemblies, or in-memory documents.
/// Optionally auto-bumps revisions for changed schemas.
/// </summary>
public sealed class LexiconPublisher
{
    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions s_parseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Publishes Lexicon documents to an output directory.
    /// If a baseline directory exists, performs a diff and auto-bumps revisions.
    /// </summary>
    /// <param name="documents">The schemas to publish.</param>
    /// <param name="outputDir">Target directory for published schemas.</param>
    /// <param name="baselineDir">Optional existing schema directory for diff comparison.</param>
    /// <param name="autoBumpRevisions">Whether to automatically bump revisions for changed schemas.</param>
    /// <param name="failOnBreaking">If true, returns errors instead of writing breaking changes.</param>
    public async Task<PublishResult> PublishAsync(
        IReadOnlyList<LexiconDocument> documents,
        string outputDir,
        string? baselineDir = null,
        bool autoBumpRevisions = true,
        bool failOnBreaking = true)
    {
        var errors = new List<string>();
        var written = new List<string>();
        var skipped = new List<string>();
        DiffResult? diff = null;
        Dictionary<string, int>? suggestedRevisions = null;

        // Load baseline for comparison if available
        List<LexiconDocument>? baselineDocs = null;
        if (baselineDir is not null && Directory.Exists(baselineDir))
        {
            baselineDocs = await LoadFromDirectoryAsync(baselineDir);
        }

        // Run diff if baseline exists
        if (baselineDocs is not null && baselineDocs.Count > 0)
        {
            var differ = new LexiconDiffer();
            diff = differ.Compare(baselineDocs, documents.ToList());

            if (failOnBreaking && diff.HasBreakingChanges)
            {
                errors.Add("Breaking changes detected. Use --force to publish anyway.");
                foreach (var change in diff.Changes.Where(c => c.IsBreaking))
                    errors.Add($"  BREAK: {change.Nsid} — {change.Description}");

                return new PublishResult
                {
                    Errors = errors,
                    Diff = diff,
                };
            }

            if (autoBumpRevisions)
            {
                suggestedRevisions = diff.SuggestRevisions(baselineDocs);
            }
        }

        // Apply revision bumps and write schemas
        var toPublish = documents.ToList();
        if (suggestedRevisions is not null)
        {
            foreach (var doc in toPublish)
            {
                if (suggestedRevisions.TryGetValue(doc.Id, out var newRev))
                    doc.Revision = newRev;
            }
        }

        // Check for unchanged schemas (if baseline exists)
        var baselineMap = baselineDocs?.ToDictionary(d => d.Id) ?? new Dictionary<string, LexiconDocument>();

        Directory.CreateDirectory(outputDir);

        foreach (var doc in toPublish)
        {
            var json = JsonSerializer.Serialize(doc, s_writeOptions);

            // Check if content is identical to baseline
            if (baselineMap.TryGetValue(doc.Id, out var baseDoc))
            {
                var baseJson = JsonSerializer.Serialize(baseDoc, s_writeOptions);
                if (json == baseJson)
                {
                    skipped.Add(doc.Id);
                    continue;
                }
            }

            var relativePath = doc.Id.Replace('.', '/') + ".json";
            var fullPath = Path.Combine(outputDir, relativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(fullPath, json);
            written.Add(doc.Id);
        }

        return new PublishResult
        {
            WrittenNsids = written,
            SkippedNsids = skipped,
            Errors = errors,
            Diff = diff,
            SuggestedRevisions = suggestedRevisions,
        };
    }

    /// <summary>
    /// Publishes schemas from a compiled .NET assembly.
    /// </summary>
    public async Task<PublishResult> PublishFromAssemblyAsync(
        string assemblyPath,
        string outputDir,
        string? baselineDir = null,
        bool autoBumpRevisions = true,
        bool failOnBreaking = true)
    {
        var emitter = new LexiconEmitter();
        var schemas = emitter.EmitFromAssembly(assemblyPath);

        var documents = new List<LexiconDocument>();
        foreach (var (_, json) in schemas)
        {
            var doc = JsonSerializer.Deserialize<LexiconDocument>(json, s_parseOptions);
            if (doc is not null && !string.IsNullOrEmpty(doc.Id))
                documents.Add(doc);
        }

        return await PublishAsync(documents, outputDir, baselineDir, autoBumpRevisions, failOnBreaking);
    }

    /// <summary>
    /// Loads all valid Lexicon documents from a directory.
    /// </summary>
    public static async Task<List<LexiconDocument>> LoadFromDirectoryAsync(string directory)
    {
        var documents = new List<LexiconDocument>();
        var files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var doc = JsonSerializer.Deserialize<LexiconDocument>(json, s_parseOptions);

                if (doc is not null && !string.IsNullOrEmpty(doc.Id) && doc.Lexicon == 1)
                    documents.Add(doc);
            }
            catch (JsonException)
            {
                // Skip invalid files
            }
        }

        return documents;
    }
}
