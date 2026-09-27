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
    /// <summary>Decodes DRISL-CBOR bytes into a <see cref="JsonElement"/>.</summary>
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
        catch (Exception ex) when (IsMalformed(ex))
        {
            throw new FormatException($"Invalid DAG-CBOR: {ex.Message}", ex);
        }

        // ParseValue copies into an element that owns its memory, so nothing needs disposing.
        var jsonReader = new Utf8JsonReader(buffer.WrittenSpan, new JsonReaderOptions { MaxDepth = DagCborJson.MaxDepth });
        return JsonElement.ParseValue(ref jsonReader);
    }

    /// <summary>
    /// What <see cref="CborReader"/>, and the readers built on it, throw for input that is not
    /// well-formed or not the shape being read: never a bug in the caller.
    /// </summary>
    internal static bool IsMalformed(Exception ex) =>
        ex is CborContentException or InvalidOperationException or FormatException or OverflowException
            // Inside a parse, an ArgumentException comes from the bytes (an identifier that does not
            // parse, a length the reader refuses); a null argument is a bug, not input.
            or (ArgumentException and not ArgumentNullException);
}
