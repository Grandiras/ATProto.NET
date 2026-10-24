namespace ATProtoNet.Repo;

// Lookup and persistent edits. Each edit returns new nodes along its path and shares every other
// node, so only the changed nodes are ever re-encoded (REP-18). The shape follows from the entries
// alone: a key sits on its layer, a gap between keys becomes a subtree one layer down, a gap whose
// keys all sit further down is a key-less node per skipped layer, and an empty gap is a null link.
internal sealed partial class MstNode
{
    public bool IsEmpty => TryGetContent(out var content) && content.Keys.Length == 0 && content.Subtrees[0] is null;

    // The index of the first key at or after key.
    public static int LowerBound(byte[][] keys, ReadOnlySpan<byte> key)
    {
        int low = 0, high = keys.Length;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (keys[middle].AsSpan().SequenceCompareTo(key) < 0) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    // Walks by key order alone, as the reference's get does, so a proof answers only from the nodes
    // on the key's path: a key whose layer is above this node's still needs the path checked.
    public Cid? Get(ReadOnlySpan<byte> key)
    {
        for (var node = this; ;)
        {
            var (keys, values, subtrees) = node.Content;
            var i = LowerBound(keys, key);
            if (i < keys.Length && keys[i].AsSpan().SequenceEqual(key)) return values[i];
            if (subtrees[i] is not { } child) return null;
            node = child;
        }
    }

    // Sets key, of a layer at or below this node's, to value in this subtree.
    public MstNode Put(byte[] key, int layer, Cid value, out Cid? previous)
    {
        var (keys, values, subtrees) = Content;
        var i = LowerBound(keys, key);
        if (layer == Layer)
        {
            if (i < keys.Length && keys[i].AsSpan().SequenceEqual(key))
            {
                previous = values[i];
                return previous == value ? this : new(Layer, keys, [.. values[..i], value, .. values[(i + 1)..]], subtrees);
            }
            previous = null;
            var (left, right) = Split(subtrees[i], key);
            return new(Layer, [.. keys[..i], key, .. keys[i..]], [.. values[..i], value, .. values[i..]], [.. subtrees[..i], left, right, .. subtrees[(i + 1)..]]);
        }
        var child = subtrees[i] ?? new MstNode(Layer - 1, [], [], [null]);
        var updated = child.Put(key, layer, value, out previous);
        return ReferenceEquals(updated, child) ? this : new(Layer, keys, values, [.. subtrees[..i], updated, .. subtrees[(i + 1)..]]);
    }

    // Removes key, of a layer at or below this node's, from this subtree; null when nothing is left.
    public MstNode? Remove(ReadOnlySpan<byte> key, int layer, out Cid? previous)
    {
        previous = null;
        var (keys, values, subtrees) = Content;
        var i = LowerBound(keys, key);
        if (layer == Layer)
        {
            if (i == keys.Length || !keys[i].AsSpan().SequenceEqual(key)) return this;
            previous = values[i];
            return Make(Layer, [.. keys[..i], .. keys[(i + 1)..]], [.. values[..i], .. values[(i + 1)..]],
                [.. subtrees[..i], Merge(subtrees[i], subtrees[i + 1]), .. subtrees[(i + 2)..]]);
        }
        if (subtrees[i] is not { } child) return this;
        var updated = child.Remove(key, layer, out previous);
        return previous is null ? this : Make(Layer, keys, values, [.. subtrees[..i], updated, .. subtrees[(i + 1)..]]);
    }

    // The parts of a subtree below and above key, which is not in it.
    private static (MstNode? Left, MstNode? Right) Split(MstNode? node, ReadOnlySpan<byte> key)
    {
        if (node is null) return (null, null);
        var (keys, values, subtrees) = node.Content;
        var i = LowerBound(keys, key);
        var (left, right) = Split(subtrees[i], key);
        return (Make(node.Layer, keys[..i], values[..i], [.. subtrees[..i], left]),
                Make(node.Layer, keys[i..], values[i..], [right, .. subtrees[(i + 1)..]]));
    }

    // Joins two adjacent subtrees of one layer, merging the subtrees that meet at the seam in turn.
    private static MstNode? Merge(MstNode? left, MstNode? right)
    {
        if (left is null) return right;
        if (right is null) return left;
        var (lk, lv, ls) = left.Content;
        var (rk, rv, rs) = right.Content;
        return new MstNode(left.Layer, [.. lk, .. rk], [.. lv, .. rv], [.. ls[..^1], Merge(ls[^1], rs[0]), .. rs[1..]]);
    }

    private static MstNode? Make(int layer, byte[][] keys, Cid[] values, MstNode?[] subtrees) =>
        keys.Length == 0 && subtrees[0] is null ? null : new(layer, keys, values, subtrees);

    // Builds the subtree of sorted entries [start, end) at layer, or null for an empty range.
    public static MstNode? Build(byte[][] keys, Cid[] values, int[] layers, int start, int end, int layer)
    {
        if (start == end) return null;
        List<byte[]> nodeKeys = [];
        List<Cid> nodeValues = [];
        List<MstNode?> subtrees = [];
        var gap = start;
        for (var i = start; i < end; i++)
        {
            if (layers[i] != layer) continue;
            subtrees.Add(Build(keys, values, layers, gap, i, layer - 1));
            nodeKeys.Add(keys[i]);
            nodeValues.Add(values[i]);
            gap = i + 1;
        }
        subtrees.Add(Build(keys, values, layers, gap, end, layer - 1));
        return new MstNode(layer, [.. nodeKeys], [.. nodeValues], [.. subtrees]);
    }
}
