using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Identity;

namespace ATProtoNet.Repo;

/// <summary>
/// Deterministic DRISL-CBOR (DAG-CBOR) encoder for the AT Protocol data model.
/// Produces byte-for-byte reproducible output following AT Protocol rules:
/// map keys sorted length-first then by byte value, no floats, CIDs encoded with tag 42.
/// </summary>
public static class DagCborEncoder
{
    /// <summary>CBOR tag for CID links (IPLD standard).</summary>
    private const CborTag CidTag = (CborTag)42;

    /// <summary>
    /// Encodes a JSON element into DRISL-CBOR bytes.
    /// Handles AT Protocol JSON conventions: <c>$link</c> objects become CID tag 42,
    /// <c>$bytes</c> objects become CBOR byte strings.
    /// </summary>
    /// <param name="element">The JSON element to encode.</param>
    /// <returns>The deterministic CBOR-encoded bytes.</returns>
    public static byte[] Encode(JsonElement element)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical, allowMultipleRootLevelValues: false);
        WriteValue(writer, element);
        return writer.Encode();
    }

    /// <summary>
    /// Encodes an object (via JSON serialization) into DRISL-CBOR bytes.
    /// </summary>
    /// <param name="value">The object to encode.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>The deterministic CBOR-encoded bytes.</returns>
    public static byte[] Encode(object value, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.SerializeToElement(value, options);
        return Encode(json);
    }

    /// <summary>
    /// Computes the CID for DRISL-CBOR encoded data (CIDv1, SHA-256, dag-cbor codec).
    /// </summary>
    /// <param name="cborBytes">The DRISL-CBOR encoded bytes.</param>
    /// <returns>The CID as a base32-encoded string with 'b' prefix.</returns>
    public static Cid ComputeCid(byte[] cborBytes)
    {
        return CidComputation.ComputeForDagCbor(cborBytes);
    }

    /// <summary>
    /// Encodes a value to DRISL-CBOR and computes its CID in one step.
    /// </summary>
    /// <param name="element">The JSON element to encode.</param>
    /// <returns>A tuple of (CBOR bytes, CID).</returns>
    public static (byte[] Bytes, Cid Cid) EncodeWithCid(JsonElement element)
    {
        var bytes = Encode(element);
        var cid = ComputeCid(bytes);
        return (bytes, cid);
    }

    private static void WriteValue(CborWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(writer, element);
                break;
            case JsonValueKind.Array:
                WriteArray(writer, element);
                break;
            case JsonValueKind.String:
                writer.WriteTextString(element.GetString()!);
                break;
            case JsonValueKind.Number:
                WriteNumber(writer, element);
                break;
            case JsonValueKind.True:
                writer.WriteBoolean(true);
                break;
            case JsonValueKind.False:
                writer.WriteBoolean(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNull();
                break;
            default:
                throw new ArgumentException($"Unsupported JSON value kind: {element.ValueKind}");
        }
    }

    private static void WriteObject(CborWriter writer, JsonElement element)
    {
        // Check for special AT Protocol JSON objects
        if (TryWriteLink(writer, element))
            return;

        if (TryWriteBytes(writer, element))
            return;

        // Regular object: sort keys length-first, then by UTF-8 byte value (DRISL
        // requirement). Sorting a list in place avoids the extra buffer and comparer
        // machinery OrderBy allocates behind the scenes for what is usually a handful
        // of properties.
        var properties = new List<JsonProperty>();
        foreach (var property in element.EnumerateObject())
            properties.Add(property);

        properties.Sort(static (a, b) => CompareCanonical(a.Name, b.Name));

        writer.WriteStartMap(properties.Count);
        foreach (var property in properties)
        {
            writer.WriteTextString(property.Name);
            WriteValue(writer, property.Value);
        }
        writer.WriteEndMap();
    }

    /// <summary>
    /// Canonical DRISL/DAG-CBOR map key order: the shorter key sorts first, and keys of
    /// equal length sort by their UTF-8 bytes.
    /// </summary>
    /// <remarks>
    /// Length-first, not plain bytewise. The two agree only when no two keys differ in
    /// length, so a record carrying (say) both <c>text</c> and <c>langs</c> hashes to a
    /// different CID under each — and only the length-first ordering matches what the rest
    /// of the network computes.
    /// </remarks>
    internal static int CompareCanonical(string a, string b)
    {
        // Keys are compared by their UTF-8 length. For the ASCII keys the AT Protocol data
        // model uses in practice that equals the string length, but a non-ASCII key would
        // otherwise be ordered by a length no other implementation agrees on.
        var lengthA = Encoding.UTF8.GetByteCount(a);
        var lengthB = Encoding.UTF8.GetByteCount(b);
        if (lengthA != lengthB)
            return lengthA - lengthB;

        return string.CompareOrdinal(a, b);
    }

    private static bool TryWriteLink(CborWriter writer, JsonElement element)
    {
        // { "$link": "bafyrei..." } → CBOR tag 42 + 0x00 + CID bytes
        if (!element.TryGetProperty("$link", out var linkValue) ||
            linkValue.ValueKind != JsonValueKind.String)
            return false;

        // Only treat as a CID link if it has exactly one property
        int propertyCount = 0;
        foreach (var _ in element.EnumerateObject())
        {
            propertyCount++;
            if (propertyCount > 1) return false;
        }

        var cidString = linkValue.GetString()!;
        var cidBytes = CidComputation.DecodeCidString(cidString);

        writer.WriteTag(CidTag);
        // Prepend 0x00 identity multibase prefix for binary CID encoding
        var taggedBytes = new byte[cidBytes.Length + 1];
        taggedBytes[0] = 0x00;
        cidBytes.CopyTo(taggedBytes.AsSpan(1));
        writer.WriteByteString(taggedBytes);

        return true;
    }

    private static bool TryWriteBytes(CborWriter writer, JsonElement element)
    {
        // { "$bytes": "base64..." } → CBOR byte string
        if (!element.TryGetProperty("$bytes", out var bytesValue) ||
            bytesValue.ValueKind != JsonValueKind.String)
            return false;

        // Only treat as bytes if it has exactly one property
        int propertyCount = 0;
        foreach (var _ in element.EnumerateObject())
        {
            propertyCount++;
            if (propertyCount > 1) return false;
        }

        var base64 = bytesValue.GetString()!;
        // AT Protocol allows optional padding
        var bytes = Convert.FromBase64String(PadBase64(base64));
        writer.WriteByteString(bytes);

        return true;
    }

    private static void WriteArray(CborWriter writer, JsonElement element)
    {
        // GetArrayLength gives the count the definite-length header needs without
        // materializing the items into a list first.
        writer.WriteStartArray(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            WriteValue(writer, item);
        }
        writer.WriteEndArray();
    }

    private static void WriteNumber(CborWriter writer, JsonElement element)
    {
        // AT Protocol data model does not allow floats
        if (element.TryGetInt64(out var longValue))
        {
            writer.WriteInt64(longValue);
        }
        else
        {
            throw new InvalidOperationException(
                "Floating point numbers are not allowed in the AT Protocol data model. " +
                "Use integers, strings, or bytes instead.");
        }
    }

    private static string PadBase64(string base64)
    {
        var remainder = base64.Length % 4;
        if (remainder == 0) return base64;
        return base64 + new string('=', 4 - remainder);
    }
}
