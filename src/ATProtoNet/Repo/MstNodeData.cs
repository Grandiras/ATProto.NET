using System.Formats.Cbor;

namespace ATProtoNet.Repo;

/// <summary>
/// Represents a single entry within an MST node, corresponding to the CBOR TreeEntry schema.
/// </summary>
/// <param name="PrefixLength">Count of bytes shared with the previous entry's key in this node.</param>
/// <param name="KeySuffix">Remainder of the key after removing the shared prefix.</param>
/// <param name="Value">CID link (binary) to the record data.</param>
/// <param name="Tree">Optional CID link to a right sub-tree node.</param>
internal sealed record MstTreeEntry(int PrefixLength, byte[] KeySuffix, byte[] Value, byte[]? Tree);

/// <summary>
/// Represents a serialized MST node as stored in DAG-CBOR, with fields
/// <c>l</c> (left subtree link) and <c>e</c> (entries array).
/// </summary>
/// <remarks>
/// The wire schema is <c>{e: [{k, p, t, v}], l}</c>, with <c>l</c> and every <c>t</c> always
/// present and written as CBOR <c>null</c> when there is no subtree. Omitting them instead
/// changes every node's CID, so the tree no longer matches the one any other implementation
/// computes for the same records.
/// See: https://atproto.com/specs/repository#mst-structure
/// </remarks>
internal sealed class MstNodeData
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
        // Every map is written in canonical key order by hand ("e" < "l"; "k" < "p" < "t" < "v"),
        // so the writer need not buffer and re-sort it.
        var writer = new CborWriter(CborConformanceMode.Lax);

        writer.WriteStartMap(2);
        writer.WriteTextString("e");
        writer.WriteStartArray(Entries.Count);
        foreach (var entry in Entries)
        {
            writer.WriteStartMap(4);
            writer.WriteTextString("k");
            writer.WriteByteString(entry.KeySuffix);
            writer.WriteTextString("p");
            writer.WriteInt32(entry.PrefixLength);
            writer.WriteTextString("t");
            DagCborLink.WriteNullable(writer, entry.Tree);
            writer.WriteTextString("v");
            DagCborLink.Write(writer, entry.Value);
            writer.WriteEndMap();
        }
        writer.WriteEndArray();

        writer.WriteTextString("l");
        DagCborLink.WriteNullable(writer, Left);
        writer.WriteEndMap();

        return writer.Encode();
    }

    /// <summary>
    /// Deserializes an MST node from DAG-CBOR bytes.
    /// </summary>
    /// <exception cref="FormatException">
    /// The bytes are not a well-formed MST node, including an entry whose prefix length is
    /// negative or longer than the key before it.
    /// </exception>
    public static MstNodeData FromBytes(ReadOnlyMemory<byte> data)
    {
        try
        {
            return Read(new CborReader(data, CborConformanceMode.Lax));
        }
        catch (Exception ex) when (ex is CborContentException or InvalidOperationException or OverflowException)
        {
            throw new FormatException($"Invalid MST node: {ex.Message}", ex);
        }
    }

    private static MstNodeData Read(CborReader reader)
    {
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
                    left = DagCborLink.ReadNullable(reader);
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
        // CborReader rejects a declared length longer than the remaining input, so the
        // capacity below is bounded by the size of the block.
        var arrLen = reader.ReadStartArray()
                    ?? throw new FormatException("MST entries must be a definite-length array.");

        var entries = new List<MstTreeEntry>(arrLen);

        // Length of the key the previous entry reconstructs to: the only bytes a prefix can share.
        var previousKeyLength = 0;

        for (var i = 0; i < arrLen; i++)
        {
            long prefixLen = 0;
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
                        prefixLen = reader.ReadInt64();
                        break;
                    case "k":
                        keySuffix = reader.ReadByteString();
                        break;
                    case "v":
                        value = DagCborLink.Read(reader);
                        break;
                    case "t":
                        tree = DagCborLink.ReadNullable(reader);
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }

            reader.ReadEndMap();

            if (keySuffix is null || value is null)
                throw new FormatException("MST entry missing required field 'k' or 'v'.");

            // The prefix is sliced off the previous key before anything else touches it, so an
            // out-of-range value from an untrusted node must be refused here (indigo SEC-4).
            if (prefixLen < 0 || prefixLen > previousKeyLength)
            {
                throw new FormatException(
                    $"MST entry prefix length {prefixLen} is outside the previous key's length {previousKeyLength}.");
            }

            entries.Add(new MstTreeEntry((int)prefixLen, keySuffix, value, tree));
            previousKeyLength = (int)prefixLen + keySuffix.Length;
        }

        reader.ReadEndArray();
        return entries;
    }
}
