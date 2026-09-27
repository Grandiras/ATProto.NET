using System.Buffers;
using System.Formats.Cbor;
using System.Text;

namespace ATProtoNet.Repo;

// A Merkle Search Tree held as its nodes, where a subtree may be known only by its CID: the partial
// tree the blocks of a firehose #commit carry. Keys can be inserted and removed along the paths that
// are loaded, and the root recomputed, which is how a commit's operations are inverted and checked
// against the previous root (Sync 1.1).
//
// This mirrors the node-based tree of indigo's atproto/repo/mst (node.go, node_insert.go,
// node_remove.go), which the reference relay and Tap invert commits with, operation for operation, so
// a commit either implementation accepts this one accepts too. MerkleSearchTree is the whole-tree
// counterpart: it cannot hold a subtree it has not loaded.
//
// Loading checks the structure the blocks show: valid keys in strictly increasing order within the
// range the parent leaves, every key of a node on the node's layer, and each loaded child one layer
// below its parent.
internal sealed class PartialMerkleSearchTree
{
    private Node _root;

    // Encodes every changed node in turn when the root is recomputed.
    private CborWriter? _writer;

    private PartialMerkleSearchTree(Node root) => _root = root;

    // Loads the tree under rootCid, following every child blocks has and leaving the rest as CID-only
    // references.
    //
    // rootCid: The root node's CID.
    //
    // blocks: Looks up a block's bytes by binary CID; null when the block is absent.
    //
    // Throws FormatException: The root block is missing, a node does not decode, or the loaded nodes are
    // not a well-formed MST.
    public static PartialMerkleSearchTree Load(byte[] rootCid, Func<byte[], byte[]?> blocks)
    {
        ArgumentNullException.ThrowIfNull(rootCid);
        ArgumentNullException.ThrowIfNull(blocks);

        var data = blocks(rootCid)
            ?? throw new FormatException($"The MST root block {CidComputation.EncodeCidToString(rootCid)} is missing.");
        return new PartialMerkleSearchTree(LoadNode(rootCid, data, blocks, 0, null, null, expectedHeight: -1));
    }

    // Reads the value stored under key, or null when the key is absent.
    //
    // Throws PartialTreeException: The path to the key runs through a subtree that is not loaded.
    public byte[]? Get(string key) => _root.Get(Key(key), -1);

    // Adds or updates key and returns the value it held before, or null when it was absent. Setting the
    // value it already holds changes nothing and returns that value.
    //
    // Throws PartialTreeException: The change touches a subtree that is not loaded.
    public byte[]? Insert(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var (root, previous) = _root.Insert(Key(key), value, -1);
        _root = root;
        return previous;
    }

    // Removes key and returns the value it held, or null when it was absent.
    //
    // Throws PartialTreeException: The change touches a subtree that is not loaded.
    public byte[]? Remove(string key)
    {
        var (root, previous) = _root.Remove(Key(key), -1);
        _root = root;
        return previous;
    }

    // The root CID of the tree as it stands, encoding every node changed since loading.
    public byte[] RootCid() => _root is { Stub: true, Dirty: false, Cid: { } cid }
        ? cid
        : _root.Encode(_writer ??= new CborWriter(CborConformanceMode.Lax, initialCapacity: 4096));

    private static byte[] Key(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!MerkleSearchTree.IsValidKey(key.AsSpan()))
            throw new ArgumentException($"Not a valid MST key: '{key}'.", nameof(key));
        return Encoding.ASCII.GetBytes(key);
    }

    private static int Height(byte[] key) => MstKeyDepth.ComputeDepth(key);

    // ── Loading ──────────────────────────────────────────────

    private static Node LoadNode(
        byte[] cid, byte[] data, Func<byte[], byte[]?> blocks, int depth, byte[]? lower, byte[]? upper, int expectedHeight)
    {
        if (depth > MerkleSearchTree.MaxTreeDepth)
            throw new FormatException($"The MST is deeper than the maximum of {MerkleSearchTree.MaxTreeDepth} layers.");

        var nodeData = MstNodeData.FromBytes(data);
        var node = new Node { Cid = cid };

        if (nodeData.Left is not null)
            node.Entries.Add(new Entry { ChildCid = nodeData.Left });

        var keys = nodeData.ReadKeys(lower, upper);
        var height = -1;
        for (var i = 0; i < keys.Length; i++)
        {
            var keyHeight = Height(keys[i]);
            if (height >= 0 && keyHeight != height)
                throw new FormatException($"MST node {Name()} holds keys of different layers.");
            height = keyHeight;

            node.Entries.Add(new Entry { Key = keys[i], Value = nodeData.Entries[i].Value });
            if (nodeData.Entries[i].Tree is { } tree)
                node.Entries.Add(new Entry { ChildCid = tree });
        }

        if (height < 0)
        {
            // A node without keys is either the root of an empty tree, or a step between layers
            // that links on to one subtree.
            if (expectedHeight < 0)
            {
                if (node.Entries.Count > 0)
                    throw new FormatException($"The MST root {Name()} holds no keys.");
                height = 0;
            }
            else
            {
                if (node.Entries.Count != 1)
                    throw new FormatException($"MST node {Name()} is empty.");
                height = expectedHeight;
            }
        }
        else if (expectedHeight >= 0 && height != expectedHeight)
        {
            throw new FormatException($"MST node {Name()} sits on the wrong layer.");
        }

        node.Height = height;

        for (var i = 0; i < node.Entries.Count; i++)
        {
            var entry = node.Entries[i];
            if (entry.ChildCid is not { } childCid)
                continue;

            if (height == 0)
                throw new FormatException($"MST node {Name()} links below layer 0.");

            // A subtree lies strictly between the keys around it.
            var childLower = i > 0 ? node.Entries[i - 1].Key : lower;
            var childUpper = i + 1 < node.Entries.Count ? node.Entries[i + 1].Key : upper;

            // A block the diff does not carry stays a reference by CID: that is what makes the tree partial.
            if (blocks(childCid) is { } childData)
                entry.Child = LoadNode(childCid, childData, blocks, depth + 1, childLower, childUpper, height - 1);
        }

        return node;

        string Name() => CidComputation.EncodeCidToString(cid);
    }

    // ── Types ────────────────────────────────────────────────

    // One item of a node, in key order: a key and its value, or a link to the subtree between the keys
    // around it. At most one link sits between two keys.
    private sealed class Entry
    {
        public byte[]? Key { get; set; }

        public byte[]? Value { get; set; }

        // The subtree's CID, as loaded or last encoded.
        public byte[]? ChildCid { get; set; }

        // The subtree, when it is loaded or was built here.
        public Node? Child { get; set; }

        // Whether the link changed since ChildCid was computed.
        public bool Dirty { get; set; }

        public bool IsValue => Key is not null && Value is not null;

        public bool IsChild => Child is not null || ChildCid is not null;
    }

    private sealed class Node
    {
        public List<Entry> Entries { get; init; } = [];

        // The node's layer, counted from zero at the bottom of the tree.
        public int Height { get; set; }

        // Whether Cid is out of date.
        public bool Dirty { get; set; }

        // The node's CID, as loaded or last encoded.
        public byte[]? Cid { get; set; }

        // A node known only by its CID: what is left at the top of the tree when a removal empties the root
        // down to a link to a subtree that is not loaded.
        public bool Stub { get; init; }

        public bool IsEmpty => Entries.Count == 0;

        // ── Lookup ───────────────────────────────────────────

        public byte[]? Get(byte[] key, int height)
        {
            if (Stub)
                throw new PartialTreeException();
            if (height < 0)
                height = Height(key);

            // A key of a higher layer would sit above this node.
            if (height > Height)
                return null;

            if (height < Height)
            {
                var child = FindExistingChild(key);
                if (child < 0)
                    return null;
                return (Entries[child].Child ?? throw new PartialTreeException()).Get(key, height);
            }

            var index = FindExistingEntry(key);
            return index >= 0 ? Entries[index].Value : null;
        }

        // The index of the value entry holding exactly key, or -1.
        private int FindExistingEntry(byte[] key)
        {
            for (var i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].IsValue && Entries[i].Key.AsSpan().SequenceEqual(key))
                    return i;
            }

            return -1;
        }

        // The index of the child entry whose range covers key, or -1.
        private int FindExistingChild(byte[] key)
        {
            var index = -1;
            for (var i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (entry.IsChild)
                {
                    index = i;
                    continue;
                }

                if (entry.IsValue)
                {
                    if (key.AsSpan().SequenceCompareTo(entry.Key) <= 0)
                        break;
                    index = -1;
                }
            }

            return index;
        }

        // Where a new entry for key goes: the index to insert at, and whether the key falls inside the child
        // entry at that index, which must then be split.
        private (int Index, bool Split) FindInsertionIndex(byte[] key)
        {
            if (Stub)
                throw new PartialTreeException();

            for (var i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (entry.IsValue && key.AsSpan().SequenceCompareTo(entry.Key) < 0)
                    return (i, false);

                if (entry.IsChild)
                {
                    // When the next value already sorts below the key, this child cannot hold it.
                    if (i + 1 < Entries.Count && Entries[i + 1] is { IsValue: true } next
                        && key.AsSpan().SequenceCompareTo(next.Key) > 0)
                    {
                        continue;
                    }

                    var order = (entry.Child ?? throw new PartialTreeException()).CompareKey(key);
                    if (order < 0)
                        return (i, false);
                    if (order > 0)
                        continue;
                    return (i, true);
                }
            }

            return (Entries.Count, false);
        }

        // Whether key sorts below every key under this node (-1), above every one (1), or within their range
        // (0).
        private int CompareKey(byte[] key)
        {
            if (Stub)
                throw new PartialTreeException();
            if (IsEmpty)
                throw new FormatException("The key range of an empty MST node is undefined.");

            if (Entries[0] is { IsValue: true } first && key.AsSpan().SequenceCompareTo(first.Key) < 0)
                return -1;
            if (Entries[^1] is { IsValue: true } last && key.AsSpan().SequenceCompareTo(last.Key) > 0)
                return 1;

            for (var i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (entry.IsValue && key.AsSpan().SequenceCompareTo(entry.Key) < 0)
                    return 0;

                if (entry.IsChild)
                {
                    if (i + 1 < Entries.Count && Entries[i + 1] is { IsValue: true } next
                        && key.AsSpan().SequenceCompareTo(next.Key) > 0)
                    {
                        continue;
                    }

                    var order = (entry.Child ?? throw new PartialTreeException()).CompareKey(key);
                    if (i == 0 && order < 0)
                        return -1;
                    if (i == Entries.Count - 1 && order > 0)
                        return 1;
                    return 0;
                }
            }

            return 0;
        }

        // ── Insertion ────────────────────────────────────────

        public (Node Node, byte[]? Previous) Insert(byte[] key, byte[] value, int height)
        {
            if (Stub)
                throw new PartialTreeException();
            if (height < 0)
                height = Height(key);

            // A key of a higher layer needs a new parent above this node, which may split it.
            if (height > Height)
                return InsertParent(key, value, height);

            if (height < Height)
                return InsertChild(key, value, height);

            var existing = FindExistingEntry(key);
            if (existing >= 0)
            {
                var entry = Entries[existing];
                if (entry.Value.AsSpan().SequenceEqual(value))
                    return (this, value);

                var previous = entry.Value;
                entry.Value = value;
                entry.Dirty = true;
                Dirty = true;
                return (this, previous);
            }

            var (index, split) = FindInsertionIndex(key);
            Dirty = true;
            var added = new Entry { Key = key, Value = value, Dirty = true };

            if (!split)
            {
                Entries.Insert(index, added);
                return (this, null);
            }

            var (left, right) = Entries[index].Child!.Split(key);
            Entries.RemoveAt(index);
            Entries.InsertRange(index,
            [
                new Entry { Child = left, Dirty = true },
                added,
                new Entry { Child = right, Dirty = true },
            ]);
            return (this, null);
        }

        // Splits this node's entries at index.
        private (Node Left, Node Right) SplitEntries(int index)
        {
            if (index == 0 || index >= Entries.Count)
                throw new FormatException("An MST node cannot be split at either end.");

            var left = new Node { Height = Height, Dirty = true, Entries = Entries.GetRange(0, index) };
            var right = new Node { Height = Height, Dirty = true, Entries = Entries.GetRange(index, Entries.Count - index) };
            return (left, right);
        }

        // Splits this subtree into the parts below and above key.
        private (Node Left, Node Right) Split(byte[] key)
        {
            if (IsEmpty)
                throw new FormatException("An empty MST node cannot be split.");

            var (index, split) = FindInsertionIndex(key);
            if (!split)
                return SplitEntries(index);

            var (lowerLeft, lowerRight) = Entries[index].Child!.Split(key);

            var left = new Node { Height = Height, Dirty = true, Entries = Entries.GetRange(0, index) };
            left.Entries.Add(new Entry { Child = lowerLeft, Dirty = true });

            var right = new Node { Height = Height, Dirty = true };
            right.Entries.Add(new Entry { Child = lowerRight, Dirty = true });
            if (index + 1 < Entries.Count)
                right.Entries.AddRange(Entries.GetRange(index + 1, Entries.Count - index - 1));

            return (left, right);
        }

        // Puts a new node above this one and inserts into that.
        private (Node Node, byte[]? Previous) InsertParent(byte[] key, byte[] value, int height)
        {
            var parent = IsEmpty
                ? new Node { Height = height, Dirty = true }
                : new Node { Height = Height + 1, Dirty = true, Entries = [new Entry { Child = this, Dirty = true }] };

            // An ordinary insertion then climbs the rest of the way, splitting as it goes.
            return parent.Insert(key, value, height);
        }

        // Inserts into the child that covers the key, or a new child when none does.
        private (Node Node, byte[]? Previous) InsertChild(byte[] key, byte[] value, int height)
        {
            var existing = FindExistingChild(key);
            if (existing >= 0)
            {
                var entry = Entries[existing];
                var (child, previous) = (entry.Child ?? throw new PartialTreeException()).Insert(key, value, height);
                if (previous is not null && previous.AsSpan().SequenceEqual(value))
                    return (this, value);

                Dirty = true;
                entry.Child = child;
                entry.Dirty = true;
                return (this, previous);
            }

            var (index, split) = FindInsertionIndex(key);
            if (split)
                throw new FormatException("An MST insertion found a child to split where it expected none.");

            Dirty = true;
            var (created, _) = new Node { Height = Height - 1, Dirty = true }.Insert(key, value, height);
            Entries.Insert(index, new Entry { Child = created, Dirty = true });
            return (this, null);
        }

        // ── Removal ──────────────────────────────────────────

        public (Node Node, byte[]? Previous) Remove(byte[] key, int height)
        {
            if (Stub)
                throw new PartialTreeException();

            var top = false;
            if (height < 0)
            {
                top = true;
                height = Height(key);
            }

            // A key of a higher layer would sit above this node, so it is not in the tree.
            if (height > Height)
                return (this, null);

            if (height < Height)
                return RemoveChild(key, height);

            var index = FindExistingEntry(key);
            if (index < 0)
                return (this, null);

            Dirty = true;
            var previous = Entries[index].Value;

            // Removing a key between two subtrees merges them.
            if (index > 0 && index + 1 < Entries.Count && Entries[index - 1].IsChild && Entries[index + 1].IsChild)
            {
                var merged = Merge(
                    Entries[index - 1].Child ?? throw new PartialTreeException(),
                    Entries[index + 1].Child ?? throw new PartialTreeException());
                Entries.RemoveRange(index, 2);
                Entries[index - 1] = new Entry { Child = merged, Dirty = true };
            }
            else
            {
                Entries.RemoveAt(index);
            }

            if (!top)
                return (this, previous);

            // A root left holding nothing but a link is trimmed down to that subtree. One that is
            // not loaded becomes a stub, known only by its CID: all the inversion needs of it.
            var node = this;
            while (node.Entries.Count == 1 && node.Entries[0].IsChild)
            {
                var link = node.Entries[0];
                node = link.Child ?? new Node
                {
                    Height = node.Height - 1,
                    Stub = true,
                    Cid = link.ChildCid ?? throw new PartialTreeException(),
                };

                if (node.Stub)
                    break;
            }

            return (node, previous);
        }

        private static Node Merge(Node left, Node right)
        {
            var index = left.Entries.Count;
            var merged = new Node { Height = left.Height, Dirty = true, Entries = [.. left.Entries, .. right.Entries] };

            // Two subtrees meeting at the seam merge in turn, down the layers.
            if (merged.Entries[index - 1].IsChild && merged.Entries[index].IsChild)
            {
                var lower = Merge(
                    merged.Entries[index - 1].Child ?? throw new PartialTreeException(),
                    merged.Entries[index].Child ?? throw new PartialTreeException());
                merged.Entries[index - 1] = new Entry { Child = lower, Dirty = true };
                merged.Entries.RemoveAt(index);
            }

            return merged;
        }

        private (Node Node, byte[]? Previous) RemoveChild(byte[] key, int height)
        {
            var index = FindExistingChild(key);
            if (index < 0)
                return (this, null);

            var entry = Entries[index];
            var (child, previous) = (entry.Child ?? throw new PartialTreeException()).Remove(key, height);
            if (previous is null)
                return (this, null);

            Dirty = true;
            if (!child.IsEmpty)
            {
                entry.Child = child;
                entry.Dirty = true;
                return (this, previous);
            }

            // The subtree emptied: drop the link. This node may now be empty itself.
            Entries.RemoveAt(index);
            return (this, previous);
        }

        // ── Encoding ─────────────────────────────────────────

        // Encodes this node, after any changed subtree, and returns its CID. The bytes are those
        // MstNodeData.ToBytes writes, written straight from the entries into one reused writer: only the CID
        // is kept.
        public byte[] Encode(CborWriter writer)
        {
            if (Stub)
                throw new FormatException("A stub MST node cannot be encoded.");
            if (!Dirty && Cid is not null)
                return Cid;

            var values = 0;
            foreach (var entry in Entries)
            {
                if (entry.Child is { } child && (entry.Dirty || child.Dirty))
                {
                    entry.ChildCid = child.Encode(writer);
                    entry.Dirty = false;
                }

                if (entry.IsValue)
                    values++;
            }

            writer.Reset();
            MstNodeData.WriteStart(writer, values);

            byte[] previous = [];
            for (var i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (!entry.IsValue)
                {
                    // The left link is written last; any other link follows its key, below.
                    if (i == 0)
                        continue;
                    throw new FormatException("An MST node holds two subtrees in a row.");
                }

                var key = entry.Key!;
                var prefix = previous.AsSpan().CommonPrefixLength(key);
                var tree = i + 1 < Entries.Count && Entries[i + 1].IsChild ? Entries[++i].ChildCid : null;

                MstNodeData.WriteEntry(writer, prefix, key.AsSpan(prefix), tree, entry.Value!);
                previous = key;
            }

            MstNodeData.WriteEnd(writer, Entries is [{ IsChild: true } first, ..] ? first.ChildCid : null);

            var buffer = ArrayPool<byte>.Shared.Rent(writer.BytesWritten);
            try
            {
                var length = writer.Encode(buffer);
                Cid = CidComputation.ComputeBinaryForDagCbor(buffer.AsSpan(0, length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            Dirty = false;
            return Cid;
        }
    }
}

// An operation on a PartialMerkleSearchTree needed a subtree the diff did not carry: the commit's
// blocks do not cover its operations.
internal sealed class PartialTreeException() : FormatException("The MST is not complete: an operation reaches a subtree whose block is missing.");
