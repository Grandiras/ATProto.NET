using ATProtoNet.Data;

namespace ATProtoNet.Repo;

// The node encoding {e: [{k, p, t, v}], l}, written and read by hand: a firehose consumer decodes
// every node of every commit it inverts, and a writer re-encodes every node an edit touches.
internal sealed partial class MstNode
{
    // A link: tag 42, a 37-byte string header, the identity multibase prefix, the binary CID.
    private const int LinkBytes = 4 + DrislEncoding.LinkLength;

    private static int MaxEncodedLength(MstContent content)
    {
        var length = 64;
        foreach (var key in content.Keys) length += 32 + key.Length + 2 * LinkBytes;
        return length;
    }

    // Writes the canonical encoding: both maps' keys are one byte long, so "e" < "l" and k < p < t < v.
    private static int Encode(MstContent content, Span<byte> destination)
    {
        var at = 0;
        destination[at++] = 0xA2;
        at += Text(destination[at..], "e"u8);
        at += DrislEncoding.WriteHeader(destination[at..], DrislEncoding.Array, (ulong)content.Keys.Length);
        ReadOnlySpan<byte> previous = [];
        for (var i = 0; i < content.Keys.Length; i++)
        {
            var key = content.Keys[i];
            var prefix = previous.CommonPrefixLength(key);
            destination[at++] = 0xA4;
            at += Text(destination[at..], "k"u8);
            at += DrislEncoding.WriteHeader(destination[at..], DrislEncoding.ByteString, (ulong)(key.Length - prefix));
            key.AsSpan(prefix).CopyTo(destination[at..]);
            at += key.Length - prefix;
            at += Text(destination[at..], "p"u8);
            at += DrislEncoding.WriteHeader(destination[at..], DrislEncoding.UnsignedInteger, (ulong)prefix);
            at += Text(destination[at..], "t"u8);
            at += Link(destination[at..], content.Subtrees[i + 1]?.Cid);
            at += Text(destination[at..], "v"u8);
            at += Link(destination[at..], content.Values[i]);
            previous = key;
        }
        at += Text(destination[at..], "l"u8);
        return at + Link(destination[at..], content.Subtrees[0]?.Cid);
    }

    private static int Text(Span<byte> destination, ReadOnlySpan<byte> key)
    {
        destination[0] = 0x61;
        destination[1] = key[0];
        return 2;
    }

    private static int Link(Span<byte> destination, Cid? cid)
    {
        if (cid is null)
        {
            destination[0] = DrislEncoding.Null;
            return 1;
        }
        destination[0] = 0xD8;
        destination[1] = (byte)DrislEncoding.LinkTag;
        destination[2] = 0x58;
        destination[3] = DrislEncoding.LinkLength;
        destination[4] = 0;
        cid.WriteBytes(destination[5..]);
        return LinkBytes;
    }

    // Reads an untrusted node and checks it against the rules of its place in the tree (REP-10..REP-14)
    // before anything uses it. expectedLayer is -1 for a root.
    private static (int Layer, MstContent Content) Decode(Cid cid, ReadOnlyMemory<byte> block, Source source, int expectedLayer)
    {
        if (source.Depth > MerkleSearchTree.MaxTreeDepth) throw Malformed(cid, $"is deeper than {MerkleSearchTree.MaxTreeDepth} layers");
        Cid? left = null;
        Entry[] entries = [];
        try
        {
            var reader = new DrislReader(block);
            var fields = reader.ReadStartMap();
            for (var i = 0; i < fields; i++)
            {
                var field = reader.ReadKeyUtf8();
                if (field.SequenceEqual("e"u8)) entries = ReadEntries(reader, cid);
                else if (field.SequenceEqual("l"u8)) left = ReadNullableLink(reader);
                else reader.SkipValue();
            }
            reader.ReadEndMap();
            reader.ReadEnd();
        }
        catch (FormatException ex)
        {
            throw Malformed(cid, $"does not decode: {ex.Message}", ex);
        }
        return Check(cid, left, entries, source, expectedLayer);
    }

    private static Entry[] ReadEntries(DrislReader reader, Cid cid)
    {
        var count = reader.ReadStartArray();
        if (count > MerkleSearchTree.MaxEntriesPerNode) throw Malformed(cid, $"has more than {MerkleSearchTree.MaxEntriesPerNode} entries");
        var entries = new Entry[count];
        ReadOnlySpan<byte> previous = [];
        for (var i = 0; i < count; i++)
        {
            long prefix = 0;
            ReadOnlyMemory<byte>? suffix = null;
            Cid? value = null, right = null;
            var fields = reader.ReadStartMap();
            for (var j = 0; j < fields; j++)
            {
                var field = reader.ReadKeyUtf8();
                if (field.SequenceEqual("p"u8)) prefix = reader.ReadInt64();
                else if (field.SequenceEqual("k"u8)) suffix = reader.ReadBytes();
                else if (field.SequenceEqual("v"u8)) value = reader.ReadLink();
                else if (field.SequenceEqual("t"u8)) right = ReadNullableLink(reader);
                else reader.SkipValue();
            }
            reader.ReadEndMap();
            if (suffix is not { } k || value is null) throw Malformed(cid, "has an entry without 'k' or 'v'");
            // Checked before slicing: a hostile prefix must not size an allocation (indigo SEC-4).
            if (prefix < 0 || prefix > previous.Length) throw Malformed(cid, $"has a prefix length {prefix} beyond the previous key's {previous.Length}");
            var key = new byte[(int)prefix + k.Length];
            previous[..(int)prefix].CopyTo(key);
            k.Span.CopyTo(key.AsSpan((int)prefix));
            entries[i] = new(key, value, right);
            previous = key;
        }
        reader.ReadEndArray();
        return entries;
    }

    private static Cid? ReadNullableLink(DrislReader reader)
    {
        if (reader.PeekKind() != DrislValueKind.Null) return reader.ReadLink();
        reader.ReadNull();
        return null;
    }

    private static (int Layer, MstContent Content) Check(Cid cid, Cid? left, Entry[] entries, Source source, int expectedLayer)
    {
        int layer;
        if (entries.Length == 0)
        {
            // Only an empty tree's root holds nothing; any other key-less node is a step between layers.
            if (expectedLayer < 0 && left is not null) throw Malformed(cid, "is a root holding only a link");
            if (expectedLayer >= 0 && left is null) throw Malformed(cid, "is empty");
            layer = Math.Max(expectedLayer, 0);
        }
        else layer = MstKey.LayerOf(entries[0].Key);

        var keys = new byte[entries.Length][];
        var values = new Cid[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            var key = entries[i].Key;
            if (!MstKey.IsValid<byte>(key)) throw Malformed(cid, "holds an invalid key");
            var floor = i > 0 ? keys[i - 1] : source.Lower;
            if (floor is not null && key.AsSpan().SequenceCompareTo(floor) <= 0 || source.Upper is not null && key.AsSpan().SequenceCompareTo(source.Upper) >= 0)
                throw Malformed(cid, "holds keys out of order or outside its range");
            if (MstKey.LayerOf(key) != layer) throw Malformed(cid, "holds keys of different layers");
            (keys[i], values[i]) = (key, entries[i].Value);
        }
        if (expectedLayer >= 0 && layer != expectedLayer) throw Malformed(cid, "sits on the wrong layer");

        var subtrees = new MstNode?[entries.Length + 1];
        for (var i = 0; i <= entries.Length; i++)
        {
            if ((i == 0 ? left : entries[i - 1].Right) is not { } link) continue;
            if (layer == 0) throw Malformed(cid, "links below layer 0");
            var range = new Source(source.Store, i > 0 ? keys[i - 1] : source.Lower, i < keys.Length ? keys[i] : source.Upper, source.Depth + 1);
            subtrees[i] = new MstNode(link, layer - 1, range);
        }
        return (layer, new(keys, values, subtrees));
    }

    private static RepoVerificationException Malformed(Cid cid, string problem, Exception? inner = null) =>
        new(RepoVerificationError.MalformedTree, $"MST node {cid} {problem}.", inner);

    private readonly record struct Entry(byte[] Key, Cid Value, Cid? Right);
}
