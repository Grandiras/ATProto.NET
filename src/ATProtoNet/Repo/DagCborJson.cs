using System.Buffers;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Text.Json;

namespace ATProtoNet.Repo;

// How DagCborJson renders the two DAG-CBOR kinds JSON has no type for.
internal enum DagCborJsonForm
{
    // The AT Protocol JSON data model: a CID becomes {"$link": "bafy…"} and a byte string becomes
    // {"$bytes": "<base64>"}, unpadded as the data model specifies.
    Wrapped,

    // The shape the firehose models bind from: a CID becomes its base32 string and a byte string becomes a
    // plain base64 string.
    Flattened,
}

// Transcodes DAG-CBOR straight into a Utf8JsonWriter in one pass.
//
// This is the one DAG-CBOR→JSON walker in the SDK, shared by DagCborDecoder.Decode and the firehose
// frame parser. Writing into the JSON writer directly skips the intermediate JsonNode tree, and
// Utf8JsonWriter.WriteBase64StringValue puts byte strings on the wire without an intermediate string.
//
// The walk recurses once per container, and its input comes from relays and other untrusted peers, so
// nesting is capped at MaxDepth: without the cap a few kilobytes of nested arrays overflow the stack,
// which no catch can recover from.
internal static class DagCborJson
{
    // The deepest JSON nesting the transcoder emits, counting the $link/$bytes wrapper objects. Matches
    // the JsonSerializerOptions.MaxDepth default, which every consumer of the output parses with, so
    // anything deeper could not be read anyway.
    internal const int MaxDepth = 64;

    // Writes the next DAG-CBOR value as JSON.
    //
    // Malformed CBOR also surfaces from CborReader itself, as CborContentException,
    // InvalidOperationException or OverflowException; callers normalize those at their own boundary.
    //
    // reader: Positioned at the value.
    //
    // writer: Receives the value.
    //
    // form: How CIDs and byte strings are rendered.
    //
    // depth: The JSON nesting depth of the container the value sits in: 0 for a root value, 1 for a
    // property of the root object, and so on.
    //
    // Throws FormatException: The value is not in the AT Protocol data model (a float, a non-string map
    // key, a malformed CID link) or nests deeper than MaxDepth.
    internal static void WriteValue(CborReader reader, Utf8JsonWriter writer, DagCborJsonForm form, int depth)
    {
        var state = reader.PeekState();

        if (state == CborReaderState.Tag)
        {
            // DAG-CBOR has exactly one tag. Any other is skipped and the value it wraps written
            // in its place; the loop, not recursion, keeps a long chain of tags off the stack.
            while (state == CborReaderState.Tag)
            {
                if (reader.ReadTag() == DagCborLink.Tag)
                {
                    WriteLink(reader, writer, form, depth);
                    return;
                }

                state = reader.PeekState();
            }
        }

        switch (state)
        {
            case CborReaderState.StartMap:
                WriteMap(reader, writer, form, depth + 1);
                break;
            case CborReaderState.StartArray:
                WriteArray(reader, writer, form, depth + 1);
                break;
            case CborReaderState.TextString:
                writer.WriteStringValue(reader.ReadTextString());
                break;
            case CborReaderState.ByteString:
                WriteBytes(reader, writer, form, depth);
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
                throw new FormatException("Floating point numbers are not allowed in the AT Protocol data model.");
            default:
                throw new FormatException($"Unsupported CBOR value in the AT Protocol data model: {state}.");
        }
    }

    // Writes the entries of the map at the reader as JSON properties of an object the caller has already
    // opened. The firehose parser uses this to write its $type discriminator ahead of the body.
    //
    // reader: Positioned at the map.
    //
    // writer: Receives the properties.
    //
    // form: How CIDs and byte strings are rendered.
    //
    // depth: The depth of the object the properties are written into (1 for the root).
    //
    // skip: Returns true for a key whose entry is left out.
    internal static void WriteMapBody(
        CborReader reader, Utf8JsonWriter writer, DagCborJsonForm form, int depth, Func<string, bool>? skip = null)
    {
        EnsureDepth(depth);
        reader.ReadStartMap();

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            if (reader.PeekState() != CborReaderState.TextString)
                throw new FormatException("DAG-CBOR map keys must be text strings.");

            var key = reader.ReadTextString();
            if (skip is not null && skip(key))
            {
                reader.SkipValue();
                continue;
            }

            writer.WritePropertyName(key);
            WriteValue(reader, writer, form, depth);
        }

        reader.ReadEndMap();
    }

    private static void WriteMap(CborReader reader, Utf8JsonWriter writer, DagCborJsonForm form, int depth)
    {
        writer.WriteStartObject();
        WriteMapBody(reader, writer, form, depth);
        writer.WriteEndObject();
    }

    private static void WriteArray(CborReader reader, Utf8JsonWriter writer, DagCborJsonForm form, int depth)
    {
        EnsureDepth(depth);
        reader.ReadStartArray();
        writer.WriteStartArray();

        while (reader.PeekState() != CborReaderState.EndArray)
            WriteValue(reader, writer, form, depth);

        reader.ReadEndArray();
        writer.WriteEndArray();
    }

    private static void WriteLink(CborReader reader, Utf8JsonWriter writer, DagCborJsonForm form, int depth)
    {
        if (form == DagCborJsonForm.Flattened)
        {
            writer.WriteStringValue(DagCborLink.ReadPayloadAsString(reader));
            return;
        }

        EnsureDepth(depth + 1);
        writer.WriteStartObject();
        writer.WriteString("$link", DagCborLink.ReadPayloadAsString(reader));
        writer.WriteEndObject();
    }

    private static void WriteBytes(CborReader reader, Utf8JsonWriter writer, DagCborJsonForm form, int depth)
    {
        if (form == DagCborJsonForm.Flattened)
        {
            writer.WriteBase64StringValue(reader.ReadByteString());
            return;
        }

        EnsureDepth(depth + 1);
        writer.WriteStartObject();
        writer.WritePropertyName("$bytes");
        WriteUnpaddedBase64(writer, reader.ReadByteString());
        writer.WriteEndObject();
    }

    // Writes bytes as a base64 string without trailing =: the data model's $bytes form.
    // Utf8JsonWriter.WriteBase64StringValue always pads.
    private static void WriteUnpaddedBase64(Utf8JsonWriter writer, byte[] bytes)
    {
        // Two quotes around the encoding; the base64 alphabet needs no JSON escaping.
        var length = Base64.GetMaxEncodedToUtf8Length(bytes.Length) + 2;
        byte[]? rented = null;
        Span<byte> buffer = length <= 256 ? stackalloc byte[256] : (rented = ArrayPool<byte>.Shared.Rent(length));

        try
        {
            Base64.EncodeToUtf8(bytes, buffer[1..], out _, out var written);
            while (written > 0 && buffer[written] == (byte)'=')
                written--;

            buffer[0] = (byte)'"';
            buffer[written + 1] = (byte)'"';
            writer.WriteRawValue(buffer[..(written + 2)], skipInputValidation: true);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void EnsureDepth(int depth)
    {
        if (depth > MaxDepth)
            throw new FormatException($"DAG-CBOR value nests deeper than the maximum of {MaxDepth} levels.");
    }
}
