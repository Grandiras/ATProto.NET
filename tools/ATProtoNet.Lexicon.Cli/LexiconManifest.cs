using System.Text.Json;
using System.Text.Json.Nodes;

namespace ATProtoNet.Lexicon.Cli;

/// <summary><c>lexicons.json</c>: where each vendored Lexicon came from, and its CID (LEX-22, LEX-23).</summary>
/// <remarks>
/// Shape: <c>{"version": 1, "sources": {name: {type, …}}, "lexicons": {nsid: {source, cid}}}</c>. Sources are <c>git</c>
/// (repository, commit, path, namespaces), <c>directory</c> (path, namespaces) or <c>network</c> (did, prefix). An optional
/// <c>provided</c> array lists NSID patterns that references supply. Unknown members are kept as written.
/// </remarks>
public sealed class LexiconManifest
{
    private readonly JsonObject _root;

    private LexiconManifest(JsonObject root) => _root = root;

    /// <summary>The sources, by name.</summary>
    public JsonObject Sources => _root["sources"] as JsonObject ?? throw new FormatException("lexicons.json has no \"sources\" object.");

    /// <summary>The Lexicons, by NSID.</summary>
    public JsonObject Lexicons => _root["lexicons"] as JsonObject ?? throw new FormatException("lexicons.json has no \"lexicons\" object.");

    /// <summary>The <c>provided</c> patterns: NSIDs the project's references define, which <c>install</c> never vendors (TOO-17).</summary>
    public IEnumerable<string> Provided => (_root["provided"] as JsonArray)?.Select(n => (string?)n).OfType<string>() ?? [];

    /// <summary>Parses a manifest.</summary>
    /// <param name="utf8Json">The file.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="FormatException">The file is not a manifest.</exception>
    public static LexiconManifest Parse(byte[] utf8Json)
    {
        try
        {
            var manifest = new LexiconManifest(JsonNode.Parse(utf8Json) as JsonObject ?? throw new FormatException("lexicons.json is a JSON object."));
            _ = manifest.Sources;
            // Keys become file paths (PathOf): only NSIDs, so none can leave the Lexicons directory.
            if (manifest.Lexicons.Select(e => e.Key).FirstOrDefault(key => !Nsid.TryParse(key, out _)) is { } invalid)
                throw new FormatException($"\"{invalid}\" in \"lexicons\" is not an NSID.");
            return manifest;
        }
        catch (JsonException e)
        {
            throw new FormatException("lexicons.json is not valid JSON: " + e.Message, e);
        }
    }

    /// <summary>An empty manifest.</summary>
    /// <returns>The manifest.</returns>
    public static LexiconManifest Empty() => new(new JsonObject { ["version"] = 1, ["sources"] = new JsonObject(), ["lexicons"] = new JsonObject() });

    /// <summary>The path of a Lexicon in the vendored layout: <c>{segments}/{name}.json</c> (LEX-22).</summary>
    /// <param name="directory">The Lexicons directory.</param>
    /// <param name="nsid">The NSID.</param>
    /// <returns>The path.</returns>
    public static string PathOf(string directory, string nsid) => Path.Combine([directory, .. nsid.Split('.')[..^1], nsid.Split('.')[^1] + ".json"]);

    /// <summary>The CID recorded for an NSID.</summary>
    public string? CidOf(string nsid) => (string?)Lexicons[nsid]?["cid"];

    /// <summary>The source recorded for an NSID.</summary>
    public string? SourceOf(string nsid) => (string?)Lexicons[nsid]?["source"];

    /// <summary>The header pin of an NSID's generated file: source, short commit or DID, and CID.</summary>
    public string? PinOf(string nsid)
    {
        if (SourceOf(nsid) is not { } name || CidOf(nsid) is not { } cid) return null;
        var source = Sources[name];
        var at = (string?)source?["commit"] is { Length: >= 8 } commit ? commit[..8] : (string?)source?["did"];
        return $"{name}{(at is null ? "" : "@" + at)} · {cid}";
    }

    /// <summary>Records an NSID's source and CID, keeping the map sorted.</summary>
    public void Set(string nsid, string source, string cid)
    {
        var entries = Lexicons.Select(e => (e.Key, Value: e.Value!.DeepClone())).Where(e => e.Key != nsid).Append((nsid, new JsonObject { ["source"] = source, ["cid"] = cid }))
            .OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        var map = new JsonObject();
        foreach (var (key, value) in entries) map[key] = value;
        _root["lexicons"] = map;
    }

    /// <summary>Removes an NSID.</summary>
    public void Remove(string nsid) => Lexicons.Remove(nsid);

    /// <summary>The manifest as written: two-space indented, with a final newline.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToUtf8() =>
        [.. JsonSerializer.SerializeToUtf8Bytes(_root, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), (byte)'\n'];
}
