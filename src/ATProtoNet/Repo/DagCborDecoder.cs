using System.Formats.Cbor;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ATProtoNet.Repo;

/// <summary>
/// Decodes DRISL-CBOR (DAG-CBOR) data into JSON representation following
/// AT Protocol conventions: CID tag 42 → <c>$link</c> objects,
/// CBOR byte strings → <c>$bytes</c> objects.
/// </summary>
public static class DagCborDecoder
{
    /// <summary>CBOR tag for CID links (IPLD standard).</summary>
    private const CborTag CidTag = (CborTag)42;

    /// <summary>
    /// Decodes DRISL-CBOR bytes into a <see cref="JsonElement"/>.
    /// </summary>
    /// <param name="data">The CBOR-encoded bytes.</param>
    /// <returns>The decoded JSON element using AT Protocol conventions.</returns>
    public static JsonElement Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new CborReader(data, CborConformanceMode.Lax, allowMultipleRootLevelValues: false);
        var node = ReadValue(reader);
        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>
    /// Decodes DRISL-CBOR bytes into a <see cref="JsonNode"/>.
    /// </summary>
    /// <param name="data">The CBOR-encoded bytes.</param>
    /// <returns>The decoded JSON node using AT Protocol conventions.</returns>
    public static JsonNode? DecodeToNode(ReadOnlyMemory<byte> data)
    {
        var reader = new CborReader(data, CborConformanceMode.Lax, allowMultipleRootLevelValues: false);
        return ReadValue(reader);
    }

    /// <summary>
    /// Validates that the given bytes are valid DRISL-CBOR per AT Protocol rules.
    /// </summary>
    /// <param name="data">The CBOR-encoded bytes.</param>
    /// <param name="error">The validation error, if any.</param>
    /// <returns><c>true</c> if valid; otherwise <c>false</c>.</returns>
    public static bool TryValidate(ReadOnlyMemory<byte> data, out string? error)
    {
        try
        {
            var reader = new CborReader(data, CborConformanceMode.Lax, allowMultipleRootLevelValues: false);
            ValidateValue(reader);

            if (reader.BytesRemaining > 0)
            {
                error = "Extraneous bytes after CBOR value";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static JsonNode? ReadValue(CborReader reader)
    {
        var state = reader.PeekState();

        // Handle tags before other types
        if (state == CborReaderState.Tag)
        {
            var tag = reader.ReadTag();
            if (tag == CidTag)
            {
                return ReadCidLink(reader);
            }

            // Unknown tag: skip tag and read the inner value
            return ReadValue(reader);
        }

        return state switch
        {
            CborReaderState.StartMap => ReadMap(reader),
            CborReaderState.StartArray => ReadArray(reader),
            CborReaderState.TextString => JsonValue.Create(reader.ReadTextString()),
            CborReaderState.ByteString => ReadByteString(reader),
            CborReaderState.UnsignedInteger => JsonValue.Create(reader.ReadInt64()),
            CborReaderState.NegativeInteger => JsonValue.Create(reader.ReadInt64()),
            CborReaderState.Boolean => JsonValue.Create(reader.ReadBoolean()),
            CborReaderState.Null => ReadNull(reader),
            CborReaderState.HalfPrecisionFloat or
            CborReaderState.SinglePrecisionFloat or
            CborReaderState.DoublePrecisionFloat =>
                throw new InvalidOperationException("Floating point numbers are not allowed in the AT Protocol data model."),
            _ => throw new InvalidOperationException($"Unsupported CBOR state: {state}"),
        };
    }

    private static JsonNode? ReadNull(CborReader reader)
    {
        reader.ReadNull();
        return null;
    }

    private static JsonObject ReadCidLink(CborReader reader)
    {
        var bytes = reader.ReadByteString();

        // First byte is 0x00 (identity multibase prefix)
        if (bytes.Length < 2 || bytes[0] != 0x00)
            throw new InvalidOperationException("Invalid CID encoding: missing identity multibase prefix (0x00).");

        var cidBytes = bytes.AsSpan(1);
        var cidString = CidComputation.EncodeCidToString(cidBytes);

        var obj = new JsonObject
        {
            ["$link"] = cidString,
        };
        return obj;
    }

    private static JsonObject ReadByteString(CborReader reader)
    {
        var bytes = reader.ReadByteString();
        var base64 = Convert.ToBase64String(bytes);

        var obj = new JsonObject
        {
            ["$bytes"] = base64,
        };
        return obj;
    }

    private static JsonObject ReadMap(CborReader reader)
    {
        reader.ReadStartMap();
        var obj = new JsonObject();

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();
            var value = ReadValue(reader);
            obj[key] = value;
        }

        reader.ReadEndMap();
        return obj;
    }

    private static JsonArray ReadArray(CborReader reader)
    {
        reader.ReadStartArray();
        var array = new JsonArray();

        while (reader.PeekState() != CborReaderState.EndArray)
        {
            array.Add(ReadValue(reader));
        }

        reader.ReadEndArray();
        return array;
    }

    private static void ValidateValue(CborReader reader)
    {
        var state = reader.PeekState();

        if (state == CborReaderState.Tag)
        {
            reader.ReadTag();
            ValidateValue(reader);
            return;
        }

        switch (state)
        {
            case CborReaderState.StartMap:
                ValidateMap(reader);
                break;
            case CborReaderState.StartArray:
                ValidateArray(reader);
                break;
            case CborReaderState.TextString:
                reader.ReadTextString();
                break;
            case CborReaderState.ByteString:
                reader.ReadByteString();
                break;
            case CborReaderState.UnsignedInteger:
            case CborReaderState.NegativeInteger:
                reader.ReadInt64();
                break;
            case CborReaderState.Boolean:
                reader.ReadBoolean();
                break;
            case CborReaderState.Null:
                reader.ReadNull();
                break;
            case CborReaderState.HalfPrecisionFloat:
            case CborReaderState.SinglePrecisionFloat:
            case CborReaderState.DoublePrecisionFloat:
                throw new InvalidOperationException("Floating point numbers are not allowed in the AT Protocol data model.");
            default:
                throw new InvalidOperationException($"Unsupported CBOR state: {state}");
        }
    }

    private static void ValidateMap(CborReader reader)
    {
        reader.ReadStartMap();
        string? previousKey = null;

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();

            // Verify keys are sorted by byte value
            if (previousKey is not null &&
                string.Compare(previousKey, key, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    $"DRISL-CBOR map keys must be sorted. Key '{key}' is not sorted after '{previousKey}'.");
            }

            previousKey = key;
            ValidateValue(reader);
        }

        reader.ReadEndMap();
    }

    private static void ValidateArray(CborReader reader)
    {
        reader.ReadStartArray();

        while (reader.PeekState() != CborReaderState.EndArray)
        {
            ValidateValue(reader);
        }

        reader.ReadEndArray();
    }
}
