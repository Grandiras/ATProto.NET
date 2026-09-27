using System.Formats.Cbor;
using System.Text;

namespace ATProtoNet.Repo;

// Represents a single entry within an MST node, corresponding to the CBOR TreeEntry schema.
//
// PrefixLength: Count of bytes shared with the previous entry's key in this node.
//
// KeySuffix: Remainder of the key after removing the shared prefix.
//
// Value: CID link (binary) to the record data.
//
// Tree: Optional CID link to a right sub-tree node.
internal sealed record MstTreeEntry(int PrefixLength, byte[] KeySuffix, byte[] Value, byte[]? Tree);

// Represents a serialized MST node as stored in DAG-CBOR, with fields l (left subtree link) and e
// (entries array).
//
// The wire schema is {e: [{k, p, t, v}], l}, with l and every t always present and written as CBOR
// null when there is no subtree. Omitting them instead changes every node's CID, so the tree no longer
// matches the one any other implementation computes for the same records. See:
// https://atproto.com/specs/repository#mst-structure
internal sealed class MstNodeData
{
    // Link to the left sub-tree node (nullable).
    public byte[]? Left { get; init; }

    // Ordered list of tree entries.
    public required List<MstTreeEntry> Entries { get; init; }

    // Serializes this node to deterministic DAG-CBOR bytes.
    public byte[] ToBytes()
    {
        // Sized up front, so encoding a node does not grow the buffer step by step.
        var size = 48;
        foreach (var entry in Entries)
            size += entry.KeySuffix.Length + 96;
        var writer = new CborWriter(CborConformanceMode.Lax, initialCapacity: size);

        WriteStart(writer, Entries.Count);
        foreach (var entry in Entries)
            WriteEntry(writer, entry.PrefixLength, entry.KeySuffix, entry.Tree, entry.Value);
        WriteEnd(writer, Left);

        return writer.Encode();
    }

    // A node is written in three steps, so a tree can encode its nodes straight from its own
    // entries. Every map is in canonical key order by hand ("e" < "l"; "k" < "p" < "t" < "v"), so
    // the writer need not buffer and re-sort it.

    // Starts a node with the given number of entries.
    internal static void WriteStart(CborWriter writer, int entries)
    {
        writer.WriteStartMap(2);
        writer.WriteTextString("e");
        writer.WriteStartArray(entries);
    }

    // Writes one entry: its key after the prefix it shares with the previous one, its right subtree and
    // its value.
    internal static void WriteEntry(CborWriter writer, int prefixLength, ReadOnlySpan<byte> keySuffix, byte[]? tree, byte[] value)
    {
        writer.WriteStartMap(4);
        writer.WriteTextString("k");
        writer.WriteByteString(keySuffix);
        writer.WriteTextString("p");
        writer.WriteInt32(prefixLength);
        writer.WriteTextString("t");
        DagCborLink.WriteNullable(writer, tree);
        writer.WriteTextString("v");
        DagCborLink.Write(writer, value);
        writer.WriteEndMap();
    }

    // Ends a node with its left subtree.
    internal static void WriteEnd(CborWriter writer, byte[]? left)
    {
        writer.WriteEndArray();
        writer.WriteTextString("l");
        DagCborLink.WriteNullable(writer, left);
        writer.WriteEndMap();
    }

    // Rebuilds the entries' keys from their prefix compression, checking that each is a valid MST key and
    // that they increase strictly between lower and upper: the range the parent leaves this node, when it
    // has one.
    //
    // Throws FormatException: A key is not valid, or out of order.
    public byte[][] ReadKeys(byte[]? lower, byte[]? upper)
    {
        var keys = new byte[Entries.Count][];
        for (var i = 0; i < keys.Length; i++)
        {
            // FromBytes has already bounded the prefix by the previous key's length.
            var entry = Entries[i];
            var key = new byte[entry.PrefixLength + entry.KeySuffix.Length];
            if (i > 0)
                keys[i - 1].AsSpan(0, entry.PrefixLength).CopyTo(key);
            entry.KeySuffix.CopyTo(key.AsSpan(entry.PrefixLength));

            if (!MerkleSearchTree.IsValidKey<byte>(key))
                throw new FormatException($"The MST node holds an invalid key: '{Encoding.Latin1.GetString(key)}'.");

            var floor = i > 0 ? keys[i - 1] : lower;
            if ((floor is not null && key.AsSpan().SequenceCompareTo(floor) <= 0)
                || (upper is not null && key.AsSpan().SequenceCompareTo(upper) >= 0))
            {
                throw new FormatException($"The MST node holds keys out of order: '{Encoding.ASCII.GetString(key)}'.");
            }

            keys[i] = key;
        }

        return keys;
    }

    // Deserializes an MST node from DAG-CBOR bytes.
    //
    // Throws FormatException: The bytes are not a well-formed MST node, including an entry whose prefix
    // length is negative or longer than the key before it.
    public static MstNodeData FromBytes(ReadOnlyMemory<byte> data)
    {
        try
        {
            return Read(new CborReader(data, CborConformanceMode.Lax));
        }
        catch (Exception ex) when (DagCborDecoder.IsMalformed(ex))
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

        // Keys are compared as bytes rather than read as strings: a firehose consumer decodes
        // every node of every commit it inverts, and a string per field adds up.
        for (var i = 0; i < mapLen; i++)
        {
            var key = reader.ReadDefiniteLengthTextStringBytes().Span;
            if (key.SequenceEqual("e"u8))
                entries = ReadEntries(reader);
            else if (key.SequenceEqual("l"u8))
                left = DagCborLink.ReadNullable(reader);
            else
                reader.SkipValue();
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
                var field = reader.ReadDefiniteLengthTextStringBytes().Span;
                if (field.SequenceEqual("p"u8))
                    prefixLen = reader.ReadInt64();
                else if (field.SequenceEqual("k"u8))
                    keySuffix = reader.ReadByteString();
                else if (field.SequenceEqual("v"u8))
                    value = DagCborLink.Read(reader);
                else if (field.SequenceEqual("t"u8))
                    tree = DagCborLink.ReadNullable(reader);
                else
                    reader.SkipValue();
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
