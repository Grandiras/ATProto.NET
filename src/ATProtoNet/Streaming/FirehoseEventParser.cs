using System.Buffers;
using System.Formats.Cbor;
using System.Text.Json;
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

            // Read the body — the remaining bytes after the header.
            int headerBytesConsumed = data.Length - reader.BytesRemaining;
            var bodyData = data[headerBytesConsumed..];

            // Inject $type for polymorphic deserialization
            return DeserializeEvent(type, bodyData);
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

    /// <summary>
    /// Transcodes a DAG-CBOR event body straight into the JSON the models bind from, then
    /// deserializes it.
    /// </summary>
    /// <remarks>
    /// The obvious route — decode to a <c>JsonNode</c> tree, rewrite the AT Protocol
    /// wrappers, then hand the tree to the serializer — walks the payload four times and
    /// materializes every value twice. It is especially costly for <c>#commit</c>, whose
    /// <c>blocks</c> CAR becomes a base64 <see cref="string"/> (two bytes per character) only to
    /// be re-encoded and parsed straight back to <see cref="byte"/>[]. The shared one-pass
    /// transcoder writes the CBOR into a <see cref="Utf8JsonWriter"/> instead, flattening CIDs to
    /// their string form and byte strings to base64 the way the models expect.
    /// </remarks>
    private static FirehoseMessage? DeserializeEvent(string type, ReadOnlyMemory<byte> bodyCbor)
    {
        var reader = new CborReader(bodyCbor, CborConformanceMode.Lax, allowMultipleRootLevelValues: false);
        if (reader.PeekState() != CborReaderState.StartMap)
            return null;

        // JSON is bulkier than the CBOR it came from — mostly the base64 blow-up on `blocks`
        // — so start the buffer above the source size to avoid a regrow on the common frame.
        var buffer = new ArrayBufferWriter<byte>(Math.Max(256, bodyCbor.Length * 2));

        // SkipValidation: the writer's structure comes from the transcoder, not from the frame.
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("$type", type);

            // The discriminator is supplied by the frame header and already written; a body
            // that carries its own would otherwise emit the property twice.
            DagCborJson.WriteMapBody(
                reader, writer, DagCborJsonForm.Flattened, depth: 1, static key => key == "$type");
            writer.WriteEndObject();
        }

        return JsonSerializer.Deserialize<FirehoseMessage>(buffer.WrittenSpan, AtProtoJsonDefaults.Options);
    }
}
