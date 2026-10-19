using System.Buffers;
using System.Text;
using System.Text.Json;
using ATProtoNet.Data;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    private static readonly Dictionary<string, string> DataModelKnownFailures = new()
    {
        // EncodeDataModel converts any data-model value; drisl-encode.json encodes top-level strings, integers,
        // arrays, links and bytes through the same member, so one of the two verdicts has to give.
        ["data-model/data-model-invalid.json#top-level not an object"] =
            "A record (not a value) must be an object: record-level validation, phase 3 (Lexicon record validation, lexicon/interop/record-data-*).",
        // The MST rows of the hostile corpus run on the MST adapter members.
        ["hostile/mst.json#*"] = "MST loading is built in phase 2 (ATProtoNet.Repo).",
    };

    /// <inheritdoc />
    public byte[]? EncodeDataModel(string json)
    {
        try
        {
            return DrislJson.ToDrisl(Encoding.UTF8.GetBytes(json));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string? DecodeDataModel(ReadOnlySpan<byte> cbor, DrislMode mode)
    {
        var json = new ArrayBufferWriter<byte>();
        try
        {
            using (var writer = new Utf8JsonWriter(json))
                DrislJson.WriteJson(cbor.ToArray(), writer, new() { Mode = mode is DrislMode.Strict ? Data.DrislMode.Strict : Data.DrislMode.Lenient });
            return Encoding.UTF8.GetString(json.WrittenSpan);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
