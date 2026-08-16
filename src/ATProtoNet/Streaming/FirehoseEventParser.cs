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
    /// <summary>CBOR tag for CID links (IPLD standard).</summary>
    private const CborTag CidTag = (CborTag)42;

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
    /// be re-encoded and parsed straight back to <see cref="byte"/>[]. Writing the CBOR into a
    /// <see cref="Utf8JsonWriter"/> in one pass skips the tree entirely, and
    /// <see cref="Utf8JsonWriter.WriteBase64StringValue"/> puts byte strings on the wire without
    /// an intermediate string at all.
    /// </remarks>
    private static FirehoseMessage? DeserializeEvent(string type, ReadOnlyMemory<byte> bodyCbor)
    {
        var reader = new CborReader(bodyCbor, CborConformanceMode.Lax, allowMultipleRootLevelValues: false);
        if (reader.PeekState() != CborReaderState.StartMap)
            return null;

        // JSON is bulkier than the CBOR it came from — mostly the base64 blow-up on `blocks`
        // — so start the buffer above the source size to avoid a regrow on the common frame.
        var buffer = new ArrayBufferWriter<byte>(Math.Max(256, bodyCbor.Length * 2));

        // SkipValidation: the writer's structure comes from this method, not from the frame.
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("$type", type);
            WriteMapBody(reader, writer);
            writer.WriteEndObject();
        }

        return JsonSerializer.Deserialize<FirehoseMessage>(buffer.WrittenSpan, AtProtoJsonDefaults.Options);
    }

    /// <summary>
    /// Writes a CBOR map's entries as JSON properties, without the enclosing braces, so the
    /// caller can prepend the <c>$type</c> discriminator.
    /// </summary>
    private static void WriteMapBody(CborReader reader, Utf8JsonWriter writer)
    {
        reader.ReadStartMap();

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();

            // The discriminator is supplied by the frame header and already written; a body
            // that carries its own would otherwise emit the property twice.
            if (key == "$type")
            {
                reader.SkipValue();
                continue;
            }

            writer.WritePropertyName(key);
            WriteValue(reader, writer);
        }

        reader.ReadEndMap();
    }

    /// <summary>
    /// Writes one DAG-CBOR value as JSON, flattening the AT Protocol wrappers the models expect
    /// to see unwrapped: a CID (tag 42) becomes its base32 string, and a byte string becomes
    /// base64.
    /// </summary>
    private static void WriteValue(CborReader reader, Utf8JsonWriter writer)
    {
        var state = reader.PeekState();

        if (state == CborReaderState.Tag)
        {
            if (reader.ReadTag() == CidTag)
            {
                writer.WriteStringValue(ReadCidLink(reader));
                return;
            }

            // Unknown tag: ignore it and write the value it wraps.
            WriteValue(reader, writer);
            return;
        }

        switch (state)
        {
            case CborReaderState.StartMap:
                WriteMap(reader, writer);
                break;
            case CborReaderState.StartArray:
                WriteArray(reader, writer);
                break;
            case CborReaderState.TextString:
                writer.WriteStringValue(reader.ReadTextString());
                break;
            case CborReaderState.ByteString:
                writer.WriteBase64StringValue(reader.ReadByteString());
                break;
            case CborReaderState.UnsignedInteger:
            case CborReaderState.NegativeInteger:
                writer.WriteNumberValue(reader.ReadInt64());
                break;
            case CborReaderState.Boolean:
                writer.WriteBooleanValue(reader.ReadBoolean());
                break;
            case CborReaderState.Null:
                reader.ReadNull();
                writer.WriteNullValue();
                break;
            case CborReaderState.HalfPrecisionFloat:
            case CborReaderState.SinglePrecisionFloat:
            case CborReaderState.DoublePrecisionFloat:
                throw new InvalidOperationException(
                    "Floating point numbers are not allowed in the AT Protocol data model.");
            default:
                throw new InvalidOperationException($"Unsupported CBOR state: {state}");
        }
    }

    private static void WriteMap(CborReader reader, Utf8JsonWriter writer)
    {
        reader.ReadStartMap();
        writer.WriteStartObject();

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            writer.WritePropertyName(reader.ReadTextString());
            WriteValue(reader, writer);
        }

        reader.ReadEndMap();
        writer.WriteEndObject();
    }

    private static void WriteArray(CborReader reader, Utf8JsonWriter writer)
    {
        reader.ReadStartArray();
        writer.WriteStartArray();

        while (reader.PeekState() != CborReaderState.EndArray)
            WriteValue(reader, writer);

        reader.ReadEndArray();
        writer.WriteEndArray();
    }

    /// <summary>Reads a tag-42 CID and returns its base32 string form.</summary>
    private static string ReadCidLink(CborReader reader)
    {
        var bytes = reader.ReadByteString();

        // First byte is 0x00 (identity multibase prefix)
        if (bytes.Length < 2 || bytes[0] != 0x00)
            throw new InvalidOperationException(
                "Invalid CID encoding: missing identity multibase prefix (0x00).");

        return CidComputation.EncodeCidToString(bytes.AsSpan(1));
    }
}
