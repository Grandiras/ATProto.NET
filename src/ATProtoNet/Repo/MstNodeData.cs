using System.Formats.Cbor;

namespace ATProtoNet.Repo;

/// <summary>
/// Represents a single entry within an MST node, corresponding to the CBOR TreeEntry schema.
/// </summary>
/// <param name="PrefixLength">Count of bytes shared with the previous entry's key in this node.</param>
/// <param name="KeySuffix">Remainder of the key after removing the shared prefix.</param>
/// <param name="Value">CID link (binary) to the record data.</param>
/// <param name="Tree">Optional CID link to a right sub-tree node.</param>
public sealed record MstTreeEntry(int PrefixLength, byte[] KeySuffix, byte[] Value, byte[]? Tree);

/// <summary>
/// Represents a serialized MST node as stored in DAG-CBOR, with fields
/// <c>l</c> (left subtree link) and <c>e</c> (entries array).
/// </summary>
public sealed class MstNodeData
{
    /// <summary>Link to the left sub-tree node (nullable).</summary>
    public byte[]? Left { get; init; }

    /// <summary>Ordered list of tree entries.</summary>
    public required List<MstTreeEntry> Entries { get; init; }

    /// <summary>
    /// Serializes this node to deterministic DAG-CBOR bytes.
    /// </summary>
    public byte[] ToBytes()
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        var entryCount = Entries.Count;
        var fieldCount = Left is not null ? 2 : 1;

        writer.WriteStartMap(fieldCount);

        // Fields must be sorted by key byte value: "e" < "l"
        // "e" (0x65) comes before "l" (0x6C) in UTF-8
        writer.WriteTextString("e");
        writer.WriteStartArray(entryCount);
        foreach (var entry in Entries)
        {
            var innerFieldCount = entry.Tree is not null ? 4 : 3;
            writer.WriteStartMap(innerFieldCount);

            // Fields sorted: "k" < "p" < "t" < "v"
            writer.WriteTextString("k");
            writer.WriteByteString(entry.KeySuffix);

            writer.WriteTextString("p");
            writer.WriteInt32(entry.PrefixLength);

            if (entry.Tree is not null)
            {
                writer.WriteTextString("t");
                WriteCidLink(writer, entry.Tree);
            }

            writer.WriteTextString("v");
            WriteCidLink(writer, entry.Value);

            writer.WriteEndMap();
        }
        writer.WriteEndArray();

        if (Left is not null)
        {
            writer.WriteTextString("l");
            WriteCidLink(writer, Left);
        }

        writer.WriteEndMap();
        return writer.Encode();
    }

    /// <summary>
    /// Deserializes an MST node from DAG-CBOR bytes.
    /// </summary>
    public static MstNodeData FromBytes(ReadOnlyMemory<byte> data)
    {
        var reader = new CborReader(data, CborConformanceMode.Lax);

        byte[]? left = null;
        List<MstTreeEntry>? entries = null;

        var mapLen = reader.ReadStartMap()
                    ?? throw new FormatException("MST node must be a definite-length map.");

        for (var i = 0; i < mapLen; i++)
        {
            var key = reader.ReadTextString();
            switch (key)
            {
                case "e":
                    entries = ReadEntries(reader);
                    break;
                case "l":
                    left = ReadCidLink(reader);
                    break;
                default:
                    // Skip unknown fields
                    reader.SkipValue();
                    break;
            }
        }

        reader.ReadEndMap();

        return new MstNodeData
        {
            Left = left,
            Entries = entries ?? [],
        };
    }

    private static List<MstTreeEntry> ReadEntries(CborReader reader)
    {
        var arrLen = reader.ReadStartArray()
                    ?? throw new FormatException("MST entries must be a definite-length array.");

        var entries = new List<MstTreeEntry>((int)arrLen);

        for (var i = 0; i < arrLen; i++)
        {
            int prefixLen = 0;
            byte[]? keySuffix = null;
            byte[]? value = null;
            byte[]? tree = null;

            var entryMapLen = reader.ReadStartMap()
                             ?? throw new FormatException("MST entry must be a definite-length map.");

            for (var j = 0; j < entryMapLen; j++)
            {
                var field = reader.ReadTextString();
                switch (field)
                {
                    case "p":
                        prefixLen = reader.ReadInt32();
                        break;
                    case "k":
                        keySuffix = reader.ReadByteString();
                        break;
                    case "v":
                        value = ReadCidLink(reader);
                        break;
                    case "t":
                        tree = ReadCidLink(reader);
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }

            reader.ReadEndMap();

            if (keySuffix is null || value is null)
                throw new FormatException("MST entry missing required field 'k' or 'v'.");

            entries.Add(new MstTreeEntry(prefixLen, keySuffix, value, tree));
        }

        reader.ReadEndArray();
        return entries;
    }

    private static void WriteCidLink(CborWriter writer, byte[] cidBytes)
    {
        writer.WriteTag((CborTag)42);
        // Prepend 0x00 identity multibase prefix
        var tagged = new byte[cidBytes.Length + 1];
        tagged[0] = 0x00;
        cidBytes.CopyTo(tagged.AsSpan(1));
        writer.WriteByteString(tagged);
    }

    private static byte[] ReadCidLink(CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 42)
            throw new FormatException($"Expected CID tag 42, got {(int)tag}.");

        var bytes = reader.ReadByteString();
        // Strip 0x00 identity multibase prefix
        if (bytes.Length < 2 || bytes[0] != 0x00)
            throw new FormatException("Invalid CID encoding: missing identity multibase prefix.");

        return bytes[1..];
    }
}
