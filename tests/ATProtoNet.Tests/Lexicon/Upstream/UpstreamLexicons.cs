using System.Text.Json;

namespace ATProtoNet.Tests.Lexicon.Upstream;

/// <summary>
/// The vendored snapshot of the upstream Lexicons (see <c>Lexicon/Upstream/README.md</c>).
/// </summary>
/// <remarks>
/// Like the interop fixtures, the files are read from the source tree rather than the build
/// output, so a refresh needs no project change.
/// </remarks>
internal sealed class UpstreamLexicons
{
    private static readonly Lazy<UpstreamLexicons> Loaded = new(Load);

    private readonly Dictionary<string, JsonElement> _documents;

    private UpstreamLexicons(Dictionary<string, JsonElement> documents) => _documents = documents;

    /// <summary>The snapshot, loaded once per test run.</summary>
    public static UpstreamLexicons Instance => Loaded.Value;

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Every Lexicon document in the snapshot, by NSID.</summary>
    public IReadOnlyDictionary<string, JsonElement> Documents => _documents;

    /// <summary>
    /// Every def in the snapshot as a reference (<c>nsid</c> for <c>main</c>, otherwise
    /// <c>nsid#name</c>) and its schema.
    /// </summary>
    public IEnumerable<(string Reference, JsonElement Def)> Defs =>
        _documents.SelectMany(doc => doc.Value.GetProperty("defs").EnumerateObject()
            .Select(def => (def.Name == "main" ? doc.Key : $"{doc.Key}#{def.Name}", def.Value)));

    /// <summary>
    /// Finds the def a reference names: <c>nsid</c> or <c>nsid#main</c> for the main def,
    /// <c>nsid#name</c> for another.
    /// </summary>
    public bool TryGetDef(string reference, out JsonElement def)
    {
        var (nsid, name) = Split(reference);
        def = default;
        return _documents.TryGetValue(nsid, out var doc)
            && doc.GetProperty("defs").TryGetProperty(name, out def);
    }

    /// <summary>The <c>type</c> of the def a reference names, or <see langword="null"/>.</summary>
    public string? KindOf(string reference) =>
        TryGetDef(reference, out var def) ? def.GetProperty("type").GetString() : null;

    /// <summary>
    /// Resolves a schema reference to the object schema it describes, following <c>ref</c>s.
    /// Beyond the def references <see cref="TryGetDef"/> takes, a reference may name a part of
    /// a method: <c>nsid#input</c>, <c>nsid#output</c> or <c>nsid#params</c>. A record resolves
    /// to its record object.
    /// </summary>
    public bool TryGetObject(string reference, out JsonElement schema)
    {
        schema = default;
        var (nsid, name) = Split(reference);

        JsonElement node;
        switch (name)
        {
            case "input" or "output":
                if (!TryGetDef(nsid, out var method)
                    || !method.TryGetProperty(name, out var body)
                    || !body.TryGetProperty("schema", out node))
                {
                    return false;
                }

                break;
            case "params":
                if (!TryGetDef(nsid, out method) || !method.TryGetProperty("parameters", out node))
                    return false;
                break;
            default:
                if (!TryGetDef(reference, out node))
                    return false;
                break;
        }

        for (var hops = 0; hops < 8; hops++)
        {
            switch (node.GetProperty("type").GetString())
            {
                case "record":
                    node = node.GetProperty("record");
                    continue;
                case "ref":
                    var target = node.GetProperty("ref").GetString()!;
                    var absolute = target.StartsWith('#') ? nsid + target : target;
                    if (!TryGetDef(absolute, out node))
                        return false;
                    nsid = Split(absolute).Nsid;
                    continue;
                case "object" or "params":
                    schema = node;
                    return true;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>The property names an object schema declares.</summary>
    public static IReadOnlySet<string> PropertiesOf(JsonElement schema) =>
        schema.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    private static (string Nsid, string Name) Split(string reference)
    {
        var hash = reference.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? (reference, "main") : (reference[..hash], reference[(hash + 1)..]);
    }

    private static UpstreamLexicons Load()
    {
        var directory = Path.Combine(RepositoryRoot, "tests", "ATProtoNet.Tests", "Lexicon", "Upstream", "lexicons");
        var documents = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
        {
            var root = JsonDocument.Parse(File.ReadAllBytes(file)).RootElement;
            documents.Add(root.GetProperty("id").GetString()!, root);
        }

        return new UpstreamLexicons(documents);
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ATProto.NET.slnx")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException(
            $"The repository root (ATProto.NET.slnx) was not found above '{AppContext.BaseDirectory}'.");
    }
}
