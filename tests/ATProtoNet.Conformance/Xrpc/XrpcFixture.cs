using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ATProtoNet.Conformance;

/// <summary>
/// One golden XRPC call from <c>vectors/xrpc/*.json</c>: what the caller passes, the request that
/// must reach the wire, the response the service sends back, and what the client must make of it.
/// </summary>
public sealed class XrpcFixture
{
    /// <summary>The service every fixture talks to.</summary>
    public const string ServiceUrl = "https://pds.example.com";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, List<XrpcFixture>> Files = [];

    /// <summary>The vector ID, <c>xrpc/{file}#{case}</c>.</summary>
    [JsonIgnore]
    public string Id { get; private set; } = "";

    /// <summary>The case name, unique within its file.</summary>
    public required string Case { get; init; }

    /// <summary>The method called.</summary>
    public required string Nsid { get; init; }

    /// <summary>What the caller passes (see <see cref="XrpcFixtureCall"/>).</summary>
    public JsonObject Params { get; init; } = [];

    /// <summary>The proxy configured on the whole client.</summary>
    public string? ClientProxy { get; init; }

    /// <summary>The request that must reach the wire.</summary>
    public required ExpectedRequest Request { get; init; }

    /// <summary>The response the service answers with.</summary>
    public ScriptedResponse Response { get; init; } = new();

    /// <summary>What the client must return or raise.</summary>
    public Expectation Expect { get; init; } = new();

    /// <summary>The call, as handed to the adapter.</summary>
    public XrpcFixtureCall ToCall() => new(Nsid, Params, ServiceUrl, ClientProxy);

    /// <summary>The IDs of every fixture in the files whose names start with <paramref name="filePrefix"/>.</summary>
    public static IReadOnlyList<string> Ids(string filePrefix) =>
    [
        .. Directory.EnumerateFiles(Path.Combine(Vectors.Directory, "xrpc"), $"{filePrefix}*.json")
            .Order(StringComparer.Ordinal)
            .SelectMany(file => Load(Path.GetFileName(file)).Select(fixture => fixture.Id)),
    ];

    /// <summary>The fixture with the given vector ID.</summary>
    public static XrpcFixture Get(string id) => Load(id["xrpc/".Length..id.IndexOf('#')]).Single(f => f.Id == id);

    private static List<XrpcFixture> Load(string file)
    {
        lock (Files)
        {
            if (Files.TryGetValue(file, out var loaded)) return loaded;
            var cases = Vectors.Json($"xrpc/{file}")["cases"]!.Deserialize<List<XrpcFixture>>(Options)!;
            foreach (var fixture in cases)
                fixture.Id = $"xrpc/{file}#{fixture.Case}";
            if (cases.Select(f => f.Id).Distinct().Count() != cases.Count)
                throw new InvalidDataException($"{file} repeats a case name.");
            return Files[file] = cases;
        }
    }

    /// <summary>The request a client must send.</summary>
    public sealed class ExpectedRequest
    {
        /// <summary>The HTTP method.</summary>
        public required string Method { get; init; }

        /// <summary>The query string as ordered, unescaped key/value pairs (a repeated key is repeated).</summary>
        public string[][] Query { get; init; } = [];

        /// <summary>The query string exactly as sent (percent-encoded), when the encoding is what the case asserts.</summary>
        public string? RawQuery { get; init; }

        /// <summary>The JSON body, compared as JSON (property order is free); absent means not asserted for a procedure.</summary>
        public JsonNode? Body { get; init; }

        /// <summary>A binary body, base64.</summary>
        public string? BodyBase64 { get; init; }

        /// <summary>The body's media type, when asserted.</summary>
        public string? ContentType { get; init; }

        /// <summary>Headers that must be present (names case-insensitive); <c>atproto-proxy</c> must be absent unless listed.</summary>
        public Dictionary<string, string> Headers { get; init; } = [];

        /// <summary>
        /// Dot paths ("" is the root) of body objects that write exactly one <c>$type</c>, as
        /// their first property.
        /// </summary>
        public string[] TypeFirst { get; init; } = [];
    }

    /// <summary>A scripted response; the body is <see cref="Body"/>, else <see cref="Text"/>, else <see cref="Base64"/>.</summary>
    public sealed class ScriptedResponse
    {
        /// <summary>The HTTP status.</summary>
        public int Status { get; init; } = 200;

        /// <summary>The response media type.</summary>
        public string ContentType { get; init; } = "application/json";

        /// <summary>Further response headers.</summary>
        public Dictionary<string, string> Headers { get; init; } = [];

        /// <summary>A JSON body.</summary>
        public JsonNode? Body { get; init; }

        /// <summary>A text body (empty is a real, empty body).</summary>
        public string? Text { get; init; }

        /// <summary>A binary body, base64.</summary>
        public string? Base64 { get; init; }
    }

    /// <summary>What the client must make of the response.</summary>
    public sealed class Expectation
    {
        /// <summary>The decoded output: every property and element listed here must be present with that value.</summary>
        public JsonNode? Output { get; init; }

        /// <summary>The decoded output must hold everything the scripted JSON response body has (the common case).</summary>
        public bool OutputIsResponse { get; init; }

        /// <summary>The binary output, when the method has one.</summary>
        public BinaryExpectation? Binary { get; init; }

        /// <summary>The error the client must raise; absent means the call must succeed.</summary>
        public ErrorExpectation? Error { get; init; }
    }

    /// <summary>A binary output.</summary>
    public sealed class BinaryExpectation
    {
        /// <summary>The content as UTF-8 text.</summary>
        public string? Text { get; init; }

        /// <summary>The content, base64.</summary>
        public string? Base64 { get; init; }

        /// <summary>The media type the client reports.</summary>
        public string? ContentType { get; init; }
    }

    /// <summary>An expected XRPC error.</summary>
    public sealed class ErrorExpectation
    {
        /// <summary>The error name.</summary>
        public required string Name { get; init; }

        /// <summary>The HTTP status.</summary>
        public required int Status { get; init; }

        /// <summary>The server's message, when asserted.</summary>
        public string? Message { get; init; }
    }
}
