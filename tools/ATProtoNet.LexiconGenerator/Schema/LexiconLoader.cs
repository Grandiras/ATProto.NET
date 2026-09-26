using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.LexiconGenerator.CodeGen;

namespace ATProtoNet.LexiconGenerator.Schema;

/// <summary>The serializer options Lexicon JSON is read and written with.</summary>
public static class LexiconJson
{
    /// <summary>Reading: comments and trailing commas tolerated, property names matched case-insensitively.</summary>
    public static JsonSerializerOptions ReadOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Writing: indented, without null members.</summary>
    public static JsonSerializerOptions WriteOptions { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

/// <summary>
/// A Lexicon document and the JSON it was read from. The JSON is what gets published: the
/// document model does not carry every field a schema may hold.
/// </summary>
/// <param name="Origin">The file, or the assembly and NSID, the document came from.</param>
/// <param name="Document">The parsed document.</param>
/// <param name="Json">The document as JSON.</param>
public sealed record LexiconSource(string Origin, LexiconDocument Document, JsonElement Json);

/// <summary>The documents a load produced, and what it passed over.</summary>
public sealed class LexiconLoadResult
{
    /// <summary>The Lexicon documents, ordered by NSID.</summary>
    public List<LexiconSource> Documents { get; } = [];

    /// <summary>Files that are not Lexicon version 1 documents, with the reason.</summary>
    public List<string> Skipped { get; } = [];

    /// <summary>Files that are not valid JSON, or whose fields have the wrong shape.</summary>
    public List<string> Errors { get; } = [];
}

/// <summary>
/// Reads Lexicon documents from a directory of <c>.json</c> files or from a compiled assembly —
/// the one place every command gets its input from.
/// </summary>
public static class LexiconLoader
{
    /// <summary>Reads every <c>.json</c> file under <paramref name="directory"/>, recursively.</summary>
    /// <param name="directory">The directory.</param>
    public static LexiconLoadResult LoadDirectory(string directory)
    {
        var result = new LexiconLoadResult();
        var files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        foreach (var file in files)
        {
            try
            {
                Add(result, file, File.ReadAllBytes(file));
            }
            catch (JsonException ex)
            {
                result.Errors.Add($"{file}: {ex.Message}");
            }
        }

        Sort(result);
        return result;
    }

    /// <summary>
    /// Derives Lexicon documents from the record and space types of a compiled assembly, with
    /// <see cref="LexiconEmitter"/>.
    /// </summary>
    /// <param name="assemblyPath">The assembly.</param>
    /// <param name="emitter">The emitter, whose warnings the caller reports.</param>
    public static LexiconLoadResult LoadAssembly(string assemblyPath, LexiconEmitter emitter)
    {
        var result = new LexiconLoadResult();
        foreach (var (nsid, json) in emitter.EmitFromAssembly(assemblyPath))
            Add(result, $"{assemblyPath} ({nsid})", System.Text.Encoding.UTF8.GetBytes(json));

        Sort(result);
        return result;
    }

    private static void Add(LexiconLoadResult result, string origin, byte[] utf8)
    {
        using var json = JsonDocument.Parse(utf8, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var document = json.RootElement.ValueKind == JsonValueKind.Object
            ? json.RootElement.Deserialize<LexiconDocument>(LexiconJson.ReadOptions)
            : null;

        if (document is null || string.IsNullOrEmpty(document.Id))
        {
            result.Skipped.Add($"{origin} (not a Lexicon document)");
            return;
        }

        if (document.Lexicon != 1)
        {
            result.Skipped.Add($"{origin} (unsupported lexicon version: {document.Lexicon})");
            return;
        }

        result.Documents.Add(new LexiconSource(origin, document, json.RootElement.Clone()));
    }

    private static void Sort(LexiconLoadResult result) =>
        result.Documents.Sort((a, b) => string.CompareOrdinal(a.Document.Id, b.Document.Id));
}
