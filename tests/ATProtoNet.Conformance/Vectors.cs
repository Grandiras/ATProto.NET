using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance;

/// <summary>
/// Reads the files under <c>vectors/</c>, which the build copies next to the test assembly.
/// </summary>
public static class Vectors
{
    /// <summary>The vectors directory in the test output.</summary>
    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "vectors");

    /// <summary>The full path of a vector file, relative to <c>vectors/</c>.</summary>
    public static string PathOf(string relativePath) => Path.Combine(Directory, relativePath);

    /// <summary>A vector file's bytes.</summary>
    public static byte[] Bytes(string relativePath) => File.ReadAllBytes(PathOf(relativePath));

    /// <summary>A vector file parsed as JSON.</summary>
    public static JsonNode Json(string relativePath) =>
        JsonNode.Parse(File.ReadAllText(PathOf(relativePath)), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })
        ?? throw new InvalidDataException($"{relativePath} is JSON null.");

    /// <summary>
    /// The cases of a JSON vector file (its <paramref name="member"/> array), keyed by vector ID
    /// <c>{file}#{name}</c>, the name being each case's <c>name</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, JsonNode> Cases(string relativePath, string member = "cases") =>
        Json(relativePath)[member]!.AsArray().ToDictionary(c => $"{relativePath}#{(string)c!["name"]!}", c => c!);

    /// <summary>
    /// The bytes a JSON vector carries under <paramref name="name"/> in base64, or gzipped under
    /// <c>{name}Gzip</c> (large frames are stored compressed).
    /// </summary>
    public static byte[] Base64(JsonNode holder, string name)
    {
        if (holder[name] is { } plain)
            return Convert.FromBase64String(plain.GetValue<string>());
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(holder[name + "Gzip"]!.GetValue<string>())), CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        return bytes.ToArray();
    }

    /// <summary>
    /// The test lines of a line-oriented vector file, as the upstream harnesses read them:
    /// blank lines and lines starting with <c>#</c> are skipped and nothing is trimmed, since
    /// leading and trailing whitespace is part of several invalid cases.
    /// </summary>
    public static IReadOnlyList<string> Lines(string relativePath) =>
    [
        .. File.ReadLines(PathOf(relativePath))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>
    /// Bytes written as a hex string, or as an array of segments (hex strings and
    /// <c>{"repeat": hex, "count": n}</c> objects) concatenated in order, for inputs too large to
    /// spell out.
    /// </summary>
    public static byte[] Hex(JsonNode? node)
    {
        if (node is not JsonArray segments) return Convert.FromHexString((string?)node ?? "");
        var bytes = new List<byte>();
        foreach (var segment in segments)
        {
            if (segment is JsonObject run)
                for (var i = (int)run["count"]!; i > 0; i--)
                    bytes.AddRange(Convert.FromHexString((string)run["repeat"]!));
            else
                bytes.AddRange(Convert.FromHexString((string)segment!));
        }
        return [.. bytes];
    }

    /// <summary>
    /// The row of a JSON array file that a vector ID names: <c>{file}#{name}</c>, matched against
    /// the rows' <c>name</c>.
    /// </summary>
    public static JsonNode Case(string vectorId)
    {
        var hash = vectorId.IndexOf('#');
        var name = vectorId[(hash + 1)..];
        return Json(vectorId[..hash]).AsArray().Single(c => (string?)c!["name"] == name)!;
    }

    /// <summary>A row's JSON-valued field as text: <c>{field}Text</c> verbatim, else the value serialized.</summary>
    public static string Text(JsonNode row, string field) => (string?)row[field + "Text"] ?? row[field]!.ToJsonString();

    /// <summary>The vector IDs (<c>{file}#{name}</c>) of a JSON array file's rows, in order.</summary>
    public static TheoryData<string> Ids(string file) => new(Json(file).AsArray().Select(c => $"{file}#{c!["name"]}"));
}
