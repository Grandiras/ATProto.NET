using System.Formats.Cbor;
using ATProtoNet.Repo;

namespace ATProtoNet.Spaces;

/// <summary>
/// Checks that a record block from a space repo is one map in the strict DAG-CBOR subset
/// (DRISL) records are hashed in.
/// </summary>
/// <remarks>
/// <para>The block's CID already matched, so its bytes are exactly what the author wrote; this
/// decides whether they are a record at all. A lenient decoder would accept encodings that no
/// conforming writer produces — an indefinite length, an integer in a longer form than it needs,
/// an arbitrary tag — and each is a second byte form of the same value, which a record's content
/// address exists to rule out.</para>
/// <para>The reader runs in <see cref="CborConformanceMode.Canonical"/> mode, which rejects
/// indefinite lengths, non-shortest integers and lengths, invalid UTF-8, and map keys that repeat
/// or are out of the length-first order DAG-CBOR sorts them in. On top of that it allows only
/// text-string keys, tag 42 over a byte string that starts with the <c>0x00</c> multibase
/// prefix (a CID link), and no floats or simple values other than <c>true</c>, <c>false</c> and
/// <c>null</c>.</para>
/// </remarks>
internal static class SpaceRecordCbor
{
    /// <summary>The CBOR tag DAG-CBOR marks a CID link with.</summary>
    private const ulong CidTag = 42;

    /// <summary>
    /// Validates <paramref name="block"/> as a single record map.
    /// </summary>
    /// <param name="block">The record block.</param>
    /// <param name="error">Why the block is not a record, when it is not.</param>
    /// <returns>Whether the block is a record map.</returns>
    public static bool TryValidateRecord(ReadOnlyMemory<byte> block, out string? error)
    {
        // Major type 5 is a map; the reader rejects an indefinite-length one below, but a record
        // that is not a map at all gets the plainer message.
        if (block.IsEmpty || (block.Span[0] >> 5) != 5)
        {
            error = "a record must be a map.";
            return false;
        }

        try
        {
            var reader = new CborReader(block, CborConformanceMode.Canonical, allowMultipleRootLevelValues: false);
            ReadValue(reader, depth: 0);

            if (reader.BytesRemaining > 0)
            {
                error = "extraneous bytes after the record.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is CborContentException or InvalidOperationException or FormatException or OverflowException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void ReadValue(CborReader reader, int depth)
    {
        if (depth > DagCborJson.MaxDepth)
            throw new FormatException($"the record nests deeper than {DagCborJson.MaxDepth} levels.");

        switch (reader.PeekState())
        {
            case CborReaderState.StartMap:
                reader.ReadStartMap();
                while (reader.PeekState() != CborReaderState.EndMap)
                {
                    if (reader.PeekState() != CborReaderState.TextString)
                        throw new FormatException("map keys must be text strings.");
                    reader.ReadTextString();
                    ReadValue(reader, depth + 1);
                }

                reader.ReadEndMap();
                break;

            case CborReaderState.StartArray:
                reader.ReadStartArray();
                while (reader.PeekState() != CborReaderState.EndArray)
                    ReadValue(reader, depth + 1);
                reader.ReadEndArray();
                break;

            case CborReaderState.Tag:
                var tag = (ulong)reader.ReadTag();
                if (tag != CidTag)
                    throw new FormatException($"tag {tag} is not allowed; the only tag is 42, a CID link.");
                if (reader.PeekState() != CborReaderState.ByteString)
                    throw new FormatException("tag 42 must wrap a byte string.");
                var link = reader.ReadByteString();
                if (link.Length < 2 || link[0] != 0x00)
                    throw new FormatException("a CID link must be a byte string starting with the 0x00 multibase prefix.");
                break;

            case CborReaderState.TextString:
                reader.ReadTextString();
                break;

            case CborReaderState.ByteString:
                reader.ReadByteString();
                break;

            case CborReaderState.UnsignedInteger:
            case CborReaderState.NegativeInteger:
                // DAG-CBOR integers are 64-bit signed; this throws past that range.
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
                throw new FormatException("floating-point numbers are not allowed in a record.");

            case var state:
                throw new FormatException($"a CBOR {state} is not allowed in a record.");
        }
    }
}
