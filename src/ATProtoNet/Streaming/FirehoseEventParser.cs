using System.Formats.Cbor;
using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;
using ATProtoNet.Serialization;

namespace ATProtoNet.Streaming;

/// <summary>
/// Parses raw firehose WebSocket frames (CBOR-encoded) into typed <see cref="FirehoseMessage"/> objects.
/// AT Protocol firehose frames are encoded as two concatenated CBOR values: a header map and a body map.
/// The header contains <c>op</c> (operation: 1=regular, -1=error) and <c>t</c> (type discriminator like "#commit").
/// </summary>
public static class FirehoseEventParser
{
    /// <summary>
    /// Parses a raw firehose frame into a typed <see cref="FirehoseMessage"/>.
    /// </summary>
    /// <param name="frame">The raw frame received from the WebSocket.</param>
    /// <returns>The parsed message, or <c>null</c> if the frame could not be parsed.</returns>
    public static FirehoseMessage? Parse(FirehoseFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.RawData.Length == 0)
            return null;

        return Parse(frame.RawData);
    }

    /// <summary>
    /// Parses raw CBOR bytes (header + body) into a typed <see cref="FirehoseMessage"/>.
    /// </summary>
    /// <param name="data">The raw CBOR-encoded firehose frame.</param>
    /// <returns>The parsed message, or <c>null</c> if the frame could not be parsed.</returns>
    public static FirehoseMessage? Parse(ReadOnlyMemory<byte> data)
    {
        try
        {
            var reader = new CborReader(data, CborConformanceMode.Lax, allowMultipleRootLevelValues: true);

            // Read the header map
            var (op, type) = ReadHeader(reader);
            if (op != 1 || string.IsNullOrEmpty(type))
                return null; // Error frames or unknown ops

            // Read the body - use remaining bytes after header
            int headerBytesConsumed = data.Length - reader.BytesRemaining;
            var bodyData = data[headerBytesConsumed..];
            var bodyJson = DagCborDecoder.Decode(bodyData);

            // Inject $type for polymorphic deserialization
            return DeserializeEvent(type, bodyJson);
        }
        catch
        {
            return null; // Malformed frames are silently dropped (AT Protocol convention)
        }
    }

    /// <summary>
    /// Attempts to parse a raw firehose frame, returning success/failure.
    /// </summary>
    /// <param name="frame">The raw frame.</param>
    /// <param name="message">The parsed message on success.</param>
    /// <param name="error">Error information on failure.</param>
    /// <returns><c>true</c> if parsing succeeded.</returns>
    public static bool TryParse(FirehoseFrame frame, out FirehoseMessage? message, out string? error)
    {
        try
        {
            message = Parse(frame);
            if (message is null)
            {
                error = "Frame could not be parsed (empty, error frame, or unknown type)";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            message = null;
            error = ex.Message;
            return false;
        }
    }

    private static (int op, string? type) ReadHeader(CborReader reader)
    {
        int op = 0;
        string? type = null;

        int mapLength = (int)reader.ReadStartMap()!;
        for (int i = 0; i < mapLength; i++)
        {
            var key = reader.ReadTextString();
            switch (key)
            {
                case "op":
                    op = reader.ReadInt32();
                    break;
                case "t":
                    type = reader.ReadTextString();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }

        reader.ReadEndMap();
        return (op, type);
    }

    private static FirehoseMessage? DeserializeEvent(string type, JsonElement bodyJson)
    {
        // DagCborDecoder produces AT Protocol JSON with $bytes and $link wrappers.
        // Convert to standard JSON that System.Text.Json can deserialize into C# models.
        var node = NormalizeDagCborJson(JsonSerializer.SerializeToNode(bodyJson));
        if (node is not JsonObject bodyObj)
            return null;

        // Build a new object with $type FIRST — System.Text.Json requires the discriminator
        // to appear before any other properties for polymorphic deserialization.
        var obj = new JsonObject { ["$type"] = type };
        foreach (var prop in bodyObj)
        {
            obj[prop.Key] = prop.Value?.DeepClone();
        }

        return obj.Deserialize<FirehoseMessage>(AtProtoJsonDefaults.Options);
    }

    /// <summary>
    /// Converts DAG-CBOR JSON conventions to standard JSON:
    /// - <c>{"$bytes":"base64"}</c> → <c>"base64"</c>
    /// - <c>{"$link":"cid"}</c> → <c>"cid"</c>
    /// </summary>
    private static JsonNode? NormalizeDagCborJson(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                // Check for $bytes wrapper → flatten to base64 string
                if (obj.Count == 1 && obj.TryGetPropertyValue("$bytes", out var bytesVal))
                    return JsonValue.Create(bytesVal?.GetValue<string>() ?? "");

                // Check for $link wrapper → flatten to CID string
                if (obj.Count == 1 && obj.TryGetPropertyValue("$link", out var linkVal))
                    return JsonValue.Create(linkVal?.GetValue<string>() ?? "");

                // Recursively normalize all properties
                var normalized = new JsonObject();
                foreach (var prop in obj)
                {
                    normalized[prop.Key] = NormalizeDagCborJson(prop.Value?.DeepClone());
                }
                return normalized;

            case JsonArray arr:
                var normalizedArr = new JsonArray();
                foreach (var item in arr)
                {
                    normalizedArr.Add(NormalizeDagCborJson(item?.DeepClone()));
                }
                return normalizedArr;

            default:
                return node?.DeepClone();
        }
    }
}
