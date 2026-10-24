using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Data;
using ATProtoNet.Lexicon;
using ATProtoNet.Lexicon.Validation;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    private static readonly Dictionary<string, string> LexiconKnownFailures = new()
    {
        ["lexicon/lexicon-resolution.json#*"] = "Runtime Lexicon resolution is built in phase 6.",
    };

    /// <inheritdoc />
    public LexiconResolution ResolveLexicon(string nsid, string didDocumentJson, byte[] getRecordCar) => throw Later(6);

    /// <inheritdoc />
    public string LexiconDnsName(string nsid) => throw Later(6);

    /// <inheritdoc />
    public bool ValidateLexicon(string lexiconJson) => LexiconDocument.TryParse(Encoding.UTF8.GetBytes(lexiconJson), out _, out _);

    /// <inheritdoc />
    public bool ValidateRecord(IReadOnlyList<string> lexiconsJson, string rkey, string recordJson)
    {
        var catalog = LexiconCatalog.Create(lexiconsJson.Select(json => LexiconDocument.Parse(Encoding.UTF8.GetBytes(json))));
        DataModelValue record;
        try
        {
            var json = JsonNode.Parse(recordJson);
            CanonicalizeBytes(json);
            record = DataModelValue.FromJson(Encoding.UTF8.GetBytes(json?.ToJsonString() ?? "null"));
        }
        catch (JsonException)
        {
            return false;
        }
        return record is DataModelMap map && map.TryGetValue("$type", out var type) && type is DataModelString { Value: var name }
            && Nsid.TryParse(name, out var collection) && RecordKey.TryParse(rkey, out var key)
            && catalog.TryGetDocument(collection, out var document) && document.Main is LexRecord
            && new LexiconValidator(catalog).ValidateRecord(collection, key, record).IsValid;
    }

    private const string Base64Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    // The interop vectors write $bytes with non-zero spare bits ("123"), which the reference implementations decode and
    // ATProtoNet's strict base64 refuses; clear those bits before reading.
    private static void CanonicalizeBytes(JsonNode? node)
    {
        if (node is JsonObject { Count: 1 } bytes && bytes["$bytes"] is JsonValue value && value.TryGetValue<string>(out var text))
        {
            var unpadded = text.TrimEnd('=');
            var spare = (unpadded.Length % 4) switch { 2 => 0b1111, 3 => 0b11, _ => 0 };
            var last = unpadded.Length > 0 ? Base64Alphabet.IndexOf(unpadded[^1], StringComparison.Ordinal) : -1;
            if (spare != 0 && last >= 0) bytes["$bytes"] = unpadded[..^1] + Base64Alphabet[last & ~spare];
        }
        else if (node is JsonObject obj)
            foreach (var (_, child) in obj) CanonicalizeBytes(child);
        else if (node is JsonArray array)
            foreach (var child in array) CanonicalizeBytes(child);
    }
}
