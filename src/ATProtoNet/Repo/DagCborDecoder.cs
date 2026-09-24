using System.Buffers;
using System.Formats.Cbor;
using System.Text.Json;

namespace ATProtoNet.Repo;

/// <summary>
/// Decodes DRISL-CBOR (DAG-CBOR) data into JSON representation following
/// AT Protocol conventions: CID tag 42 → <c>$link</c> objects,
/// CBOR byte strings → <c>$bytes</c> objects.
/// </summary>
public static class DagCborDecoder
{
    /// <summary>
    /// Decodes DRISL-CBOR bytes into a <see cref="JsonElement"/>.
    /// </summary>
    /// <param name="data">The CBOR-encoded bytes.</param>
    /// <returns>The decoded JSON element using AT Protocol conventions.</returns>
    /// <exception cref="FormatException">
    /// The data is not well-formed CBOR, falls outside the AT Protocol data model (a float, a
    /// non-string map key, a malformed CID link), or nests deeper than 64 levels.
    /// </exception>
    public static JsonElement Decode(ReadOnlyMemory<byte> data)
    {
        // JSON is bulkier than the CBOR it came from (base64, quoting, `$link` wrappers).
        var buffer = new ArrayBufferWriter<byte>(Math.Max(256, data.Length * 2));

        try
        {
            var reader = new CborReader(data, CborConformanceMode.Lax, allowMultipleRootLevelValues: false);

            // SkipValidation: the JSON structure comes from the transcoder, not from the input.
            using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true });
            DagCborJson.WriteValue(reader, writer, DagCborJsonForm.Wrapped, depth: 0);
        }
        catch (Exception ex) when (ex is CborContentException or InvalidOperationException or OverflowException)
        {
            throw new FormatException($"Invalid DAG-CBOR: {ex.Message}", ex);
        }

        // ParseValue copies into an element that owns its memory, so nothing needs disposing.
        var jsonReader = new Utf8JsonReader(buffer.WrittenSpan, new JsonReaderOptions { MaxDepth = DagCborJson.MaxDepth });
        return JsonElement.ParseValue(ref jsonReader);
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
            ValidateValue(reader, depth: 0);

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

    private static void ValidateValue(CborReader reader, int depth)
    {
        var state = reader.PeekState();

        // Iterate, not recurse, over a chain of tags: each costs one byte of input.
        while (state == CborReaderState.Tag)
        {
            reader.ReadTag();
            state = reader.PeekState();
        }

        switch (state)
        {
            case CborReaderState.StartMap:
                ValidateMap(reader, depth + 1);
                break;
            case CborReaderState.StartArray:
                ValidateArray(reader, depth + 1);
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

    private static void ValidateMap(CborReader reader, int depth)
    {
        EnsureDepth(depth);
        reader.ReadStartMap();
        string? previousKey = null;

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();

            // Verify keys are in canonical order: length first, then byte value. Plain
            // bytewise ordering would reject valid blocks whose keys differ in length.
            if (previousKey is not null &&
                DagCborEncoder.CompareCanonical(previousKey, key) >= 0)
            {
                throw new InvalidOperationException(
                    $"DRISL-CBOR map keys must be sorted. Key '{key}' is not sorted after '{previousKey}'.");
            }

            previousKey = key;
            ValidateValue(reader, depth);
        }

        reader.ReadEndMap();
    }

    private static void ValidateArray(CborReader reader, int depth)
    {
        EnsureDepth(depth);
        reader.ReadStartArray();

        while (reader.PeekState() != CborReaderState.EndArray)
        {
            ValidateValue(reader, depth);
        }

        reader.ReadEndArray();
    }

    private static void EnsureDepth(int depth)
    {
        if (depth > DagCborJson.MaxDepth)
            throw new InvalidOperationException($"DRISL-CBOR value nests deeper than the maximum of {DagCborJson.MaxDepth} levels.");
    }
}
