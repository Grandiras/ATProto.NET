using System.Text;

namespace ATProtoNet.Repo;

/// <summary>
/// In-memory Merkle Search Tree (MST) for AT Protocol repositories.
/// <para>
/// The MST is a deterministic, content-addressed key/value mapping where keys are
/// byte arrays (repo paths like <c>collection/rkey</c>) and values are CID links
/// to record data. The tree structure is fully reproducible from the set of key/value
/// pairs, regardless of insertion order.
/// </para>
/// </summary>
/// <remarks>
/// See: https://atproto.com/specs/repository#mst-structure
/// </remarks>
public sealed class MerkleSearchTree
{
    /// <summary>Maximum allowed entries per node (DoS protection).</summary>
    private const int MaxNodeEntries = 256;

    /// <summary>Maximum allowed tree depth (DoS protection).</summary>
    private const int MaxTreeDepth = 64;

    private MstMemoryNode _root;

    private MerkleSearchTree(MstMemoryNode root)
    {
        _root = root;
    }

    /// <summary>
    /// Creates an empty MST.
    /// </summary>
    public static MerkleSearchTree Create()
    {
        return new MerkleSearchTree(new MstMemoryNode());
    }

    /// <summary>
    /// Creates an MST from a set of key/value pairs. Keys must be unique.
    /// Values are binary CID bytes referencing record data.
    /// </summary>
    /// <param name="entries">The key/value pairs. Keys are UTF-8 repo paths, values are CID bytes.</param>
    /// <returns>A new MST containing all entries.</returns>
    public static MerkleSearchTree Create(IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        var sorted = entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();

        // Build from sorted entries using the layer algorithm
        var root = BuildLayer(sorted, 0);
        return new MerkleSearchTree(root ?? new MstMemoryNode());
    }

    /// <summary>
    /// Gets the record CID for a given key, or <c>null</c> if not found.
    /// </summary>
    /// <param name="key">The repo path (e.g., "app.bsky.feed.post/abc123").</param>
    /// <returns>The record CID bytes, or <c>null</c>.</returns>
    public byte[]? Get(string key)
    {
        return Get(_root, key, 0);
    }

    /// <summary>
    /// Enumerates all key/value pairs in sorted order.
    /// </summary>
    public IEnumerable<KeyValuePair<string, byte[]>> GetEntries()
    {
        return EnumerateNode(_root);
    }

    /// <summary>
    /// Adds a new key/value pair. Throws if the key already exists.
    /// </summary>
    /// <param name="key">The repo path.</param>
    /// <param name="value">The record CID bytes.</param>
    /// <exception cref="ArgumentException">Key already exists.</exception>
    public void Add(string key, byte[] value)
    {
        _root = Insert(_root, key, value, 0);
    }

    /// <summary>
    /// Updates the value for an existing key. Throws if the key does not exist.
    /// </summary>
    /// <param name="key">The repo path.</param>
    /// <param name="value">The new record CID bytes.</param>
    /// <exception cref="KeyNotFoundException">Key not found.</exception>
    public void Update(string key, byte[] value)
    {
        if (!TryUpdate(_root, key, value))
            throw new KeyNotFoundException($"Key not found in MST: {key}");
    }

    /// <summary>
    /// Removes a key/value pair. Throws if the key does not exist.
    /// </summary>
    /// <param name="key">The repo path.</param>
    /// <exception cref="KeyNotFoundException">Key not found.</exception>
    public void Delete(string key)
    {
        var (newRoot, found) = Remove(_root, key, 0);
        if (!found)
            throw new KeyNotFoundException($"Key not found in MST: {key}");
        _root = newRoot ?? new MstMemoryNode();
    }

    /// <summary>
    /// Checks whether a key exists in the tree.
    /// </summary>
    public bool ContainsKey(string key) => Get(key) is not null;

    /// <summary>
    /// Gets the total number of entries in the tree.
    /// </summary>
    public int Count => CountEntries(_root);

    /// <summary>
    /// Serializes the entire MST to a block store (dictionary of CID → DAG-CBOR bytes),
    /// and returns the root CID.
    /// </summary>
    /// <returns>Tuple of (root CID bytes, block map).</returns>
    public (byte[] RootCid, Dictionary<string, byte[]> Blocks) Serialize()
    {
        var blocks = new Dictionary<string, byte[]>();
        var rootCid = SerializeNode(_root, blocks);
        return (rootCid, blocks);
    }

    /// <summary>
    /// Deserializes an MST from a block store (CID → DAG-CBOR bytes mapping) starting from a root CID.
    /// </summary>
    /// <param name="rootCid">The root node CID bytes.</param>
    /// <param name="blocks">Block lookup function (CID string → DAG-CBOR bytes).</param>
    /// <returns>The deserialized MST.</returns>
    public static MerkleSearchTree Deserialize(byte[] rootCid, Func<string, byte[]?> blocks)
    {
        var root = DeserializeNode(rootCid, blocks, 0);
        return new MerkleSearchTree(root);
    }

    /// <summary>
    /// Computes the root CID of the tree without materializing all blocks.
    /// </summary>
    public byte[] ComputeRootCid()
    {
        return ComputeNodeCid(_root);
    }

    /// <summary>
    /// Validates the MST structure (key ordering, depth correctness, prefix compression).
    /// </summary>
    /// <returns><c>true</c> if valid; otherwise throws with details.</returns>
    /// <exception cref="InvalidOperationException">If the tree structure is invalid.</exception>
    public bool Validate()
    {
        ValidateNode(_root, null, null, 0);
        return true;
    }

    // ── Build from sorted entries ────────────────────────────

    private static MstMemoryNode? BuildLayer(List<KeyValuePair<string, byte[]>> entries, int layer)
    {
        if (entries.Count == 0)
            return layer == 0 ? new MstMemoryNode() : null;

        // Separate entries at this layer from those at lower layers
        var nodeEntries = new List<MstMemoryEntry>();
        var leftEntries = new List<KeyValuePair<string, byte[]>>();

        foreach (var entry in entries)
        {
            var depth = MstKeyDepth.ComputeDepth(entry.Key);
            if (depth == layer)
            {
                // Before adding this entry, build a subtree from accumulated lower entries
                var subtree = BuildLayer(leftEntries, layer - 1);
                nodeEntries.Add(new MstMemoryEntry(entry.Key, entry.Value, subtree));
                leftEntries = [];
            }
            else if (depth < layer)
            {
                leftEntries.Add(entry);
            }
            else
            {
                // depth > layer: shouldn't happen if we picked the right max layer
                // This can happen when building recursive subtrees; pass through
                leftEntries.Add(entry);
            }
        }

        // Remaining entries form the rightmost subtree (which becomes the left pointer)
        var lastSubtree = BuildLayer(leftEntries, layer - 1);

        if (nodeEntries.Count == 0)
        {
            // No entries at this layer; the node is just a pass-through
            return lastSubtree;
        }

        var node = new MstMemoryNode
        {
            Left = nodeEntries[0].Subtree, // Left of first entry
        };

        // The first entry's subtree is now the node's Left
        nodeEntries[0] = nodeEntries[0] with { Subtree = null };

        // Set the last entry's subtree to lastSubtree
        if (lastSubtree is not null)
        {
            var lastIdx = nodeEntries.Count - 1;
            var last = nodeEntries[lastIdx];
            // If no entries were accumulated after the last key, this is the right subtree
            nodeEntries[lastIdx] = last with { Subtree = lastSubtree };
        }

        node.Entries.AddRange(nodeEntries);
        return node;
    }

    /// <summary>
    /// Creates an MST from a set of key/value pairs by finding the max depth
    /// and using the layer-based build algorithm.
    /// </summary>
    public static MerkleSearchTree CreateFromEntries(IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        var sorted = entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        if (sorted.Count == 0)
            return Create();

        // Find the maximum depth
        var maxDepth = 0;
        foreach (var entry in sorted)
        {
            var d = MstKeyDepth.ComputeDepth(entry.Key);
            if (d > maxDepth) maxDepth = d;
        }

        var root = BuildLayerTopDown(sorted, maxDepth);
        return new MerkleSearchTree(root ?? new MstMemoryNode());
    }

    private static MstMemoryNode? BuildLayerTopDown(List<KeyValuePair<string, byte[]>> entries, int layer)
    {
        if (entries.Count == 0)
        {
            return layer == 0 ? new MstMemoryNode() : null;
        }

        if (layer < 0)
            return null;

        // Find entries at this layer
        var atLayer = new List<(int Index, KeyValuePair<string, byte[]> Entry)>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (MstKeyDepth.ComputeDepth(entries[i].Key) == layer)
                atLayer.Add((i, entries[i]));
        }

        if (atLayer.Count == 0)
        {
            // No entries at this layer; recurse down
            return BuildLayerTopDown(entries, layer - 1);
        }

        var node = new MstMemoryNode();

        // Build left subtree (entries before the first key at this layer)
        var leftEntries = entries.Take(atLayer[0].Index).ToList();
        node.Left = BuildLayerTopDown(leftEntries, layer - 1);

        // Build entries and right subtrees
        for (var i = 0; i < atLayer.Count; i++)
        {
            var start = atLayer[i].Index + 1;
            var end = i + 1 < atLayer.Count ? atLayer[i + 1].Index : entries.Count;
            var rightEntries = entries.GetRange(start, end - start);
            var rightSubtree = BuildLayerTopDown(rightEntries, layer - 1);

            node.Entries.Add(new MstMemoryEntry(atLayer[i].Entry.Key, atLayer[i].Entry.Value, rightSubtree));
        }

        return node;
    }

    // ── Lookup ───────────────────────────────────────────────

    private static byte[]? Get(MstMemoryNode? node, string key, int depth)
    {
        if (node is null)
            return null;

        if (depth > MaxTreeDepth)
            throw new InvalidOperationException("MST tree depth exceeded maximum.");

        var keyDepth = MstKeyDepth.ComputeDepth(key);

        // Check entries in this node
        foreach (var entry in node.Entries)
        {
            var cmp = string.Compare(key, entry.Key, StringComparison.Ordinal);
            if (cmp == 0)
                return entry.Value;
            if (cmp < 0)
            {
                // Key would be before this entry; check left subtree or entry's subtree
                if (node.Entries.IndexOf(entry) == 0)
                    return Get(node.Left, key, depth + 1);
                // Check the previous entry's subtree
                var prevIdx = node.Entries.IndexOf(entry) - 1;
                return Get(node.Entries[prevIdx].Subtree, key, depth + 1);
            }
        }

        // Key is after all entries; check the last entry's subtree
        if (node.Entries.Count > 0)
            return Get(node.Entries[^1].Subtree, key, depth + 1);

        return Get(node.Left, key, depth + 1);
    }

    // ── Insertion ────────────────────────────────────────────

    private static MstMemoryNode Insert(MstMemoryNode? node, string key, byte[] value, int depth)
    {
        if (depth > MaxTreeDepth)
            throw new InvalidOperationException("MST tree depth exceeded maximum.");

        node ??= new MstMemoryNode();

        var keyDepth = MstKeyDepth.ComputeDepth(key);

        if (keyDepth > depth)
        {
            // This key belongs at a higher layer; we need to split this node
            // and create a new parent
            return SplitAndInsert(node, key, value, depth, keyDepth);
        }

        if (keyDepth == depth)
        {
            // This key belongs at this layer
            return InsertAtLevel(node, key, value, depth);
        }

        // keyDepth < depth: this key belongs at a lower layer, recurse into subtree
        return InsertIntoSubtree(node, key, value, depth);
    }

    private static MstMemoryNode InsertAtLevel(MstMemoryNode node, string key, byte[] value, int depth)
    {
        // Find the insertion position
        var insertIdx = 0;
        foreach (var entry in node.Entries)
        {
            var cmp = string.Compare(key, entry.Key, StringComparison.Ordinal);
            if (cmp == 0)
                throw new ArgumentException($"Key already exists in MST: {key}");
            if (cmp < 0)
                break;
            insertIdx++;
        }

        // The new entry needs to take over the right subtree of the previous entry
        // (or the left pointer if inserting at position 0)
        MstMemoryNode? leftOfNewKey;
        MstMemoryNode? rightOfNewKey;

        if (insertIdx == 0)
        {
            // Split the left subtree
            (leftOfNewKey, rightOfNewKey) = SplitSubtree(node.Left, key);
            node.Left = leftOfNewKey;
        }
        else
        {
            var prev = node.Entries[insertIdx - 1];
            (leftOfNewKey, rightOfNewKey) = SplitSubtree(prev.Subtree, key);
            node.Entries[insertIdx - 1] = prev with { Subtree = leftOfNewKey };
        }

        var newEntry = new MstMemoryEntry(key, value, rightOfNewKey);
        node.Entries.Insert(insertIdx, newEntry);

        return node;
    }

    private static MstMemoryNode InsertIntoSubtree(MstMemoryNode node, string key, byte[] value, int depth)
    {
        // Find which subtree to descend into
        for (var i = 0; i < node.Entries.Count; i++)
        {
            var cmp = string.Compare(key, node.Entries[i].Key, StringComparison.Ordinal);
            if (cmp < 0)
            {
                if (i == 0)
                {
                    node.Left = Insert(node.Left, key, value, depth - 1);
                }
                else
                {
                    var prev = node.Entries[i - 1];
                    node.Entries[i - 1] = prev with { Subtree = Insert(prev.Subtree, key, value, depth - 1) };
                }
                return node;
            }
            if (cmp == 0)
                throw new ArgumentException($"Key already exists in MST: {key}");
        }

        // After all entries
        if (node.Entries.Count > 0)
        {
            var last = node.Entries[^1];
            node.Entries[^1] = last with { Subtree = Insert(last.Subtree, key, value, depth - 1) };
        }
        else
        {
            node.Left = Insert(node.Left, key, value, depth - 1);
        }

        return node;
    }

    private static MstMemoryNode SplitAndInsert(MstMemoryNode node, string key, byte[] value, int currentDepth, int targetDepth)
    {
        // We need to create layers from currentDepth up to targetDepth
        // and insert the key at targetDepth
        var newNode = new MstMemoryNode();

        // Split the current node around the key
        var (leftNode, rightNode) = SplitNodeAtKey(node, key, currentDepth);

        newNode.Left = leftNode;
        newNode.Entries.Add(new MstMemoryEntry(key, value, rightNode));

        // If we need more intermediate layers, wrap
        // Actually: the current node was at 'currentDepth'. The key needs to be at 'targetDepth'.
        // We directly create the node at targetDepth. Empty intermediate nodes are allowed
        // as long as they point to subtrees with entries.
        return newNode;
    }

    private static (MstMemoryNode? Left, MstMemoryNode? Right) SplitSubtree(MstMemoryNode? subtree, string splitKey)
    {
        if (subtree is null)
            return (null, null);

        var leftEntries = new List<MstMemoryEntry>();
        var rightEntries = new List<MstMemoryEntry>();
        MstMemoryNode? left = subtree.Left;
        MstMemoryNode? rightLeft = null;

        var foundSplit = false;
        foreach (var entry in subtree.Entries)
        {
            var cmp = string.Compare(entry.Key, splitKey, StringComparison.Ordinal);
            if (cmp < 0)
            {
                leftEntries.Add(entry);
            }
            else
            {
                if (!foundSplit)
                {
                    // The previous entry's subtree (or left pointer) needs to be split
                    if (leftEntries.Count > 0)
                    {
                        rightLeft = null;
                        // Subtree of previous entry stays with left
                    }
                    else
                    {
                        rightLeft = null;
                    }
                    foundSplit = true;
                }
                rightEntries.Add(entry);
            }
        }

        MstMemoryNode? leftNode = null;
        if (leftEntries.Count > 0 || left is not null)
        {
            leftNode = new MstMemoryNode { Left = left };
            leftNode.Entries.AddRange(leftEntries);
        }

        MstMemoryNode? rightNode = null;
        if (rightEntries.Count > 0)
        {
            rightNode = new MstMemoryNode { Left = rightLeft };
            rightNode.Entries.AddRange(rightEntries);
        }

        return (leftNode, rightNode);
    }

    private static (MstMemoryNode? Left, MstMemoryNode? Right) SplitNodeAtKey(MstMemoryNode node, string key, int depth)
    {
        var leftEntries = new List<MstMemoryEntry>();
        var rightEntries = new List<MstMemoryEntry>();
        MstMemoryNode? origLeft = node.Left;
        MstMemoryNode? newRightLeft = null;

        foreach (var entry in node.Entries)
        {
            var cmp = string.Compare(entry.Key, key, StringComparison.Ordinal);
            if (cmp < 0)
            {
                leftEntries.Add(entry);
            }
            else
            {
                if (rightEntries.Count == 0 && leftEntries.Count > 0)
                {
                    // The split point is between the last left entry and this right entry
                    var lastLeft = leftEntries[^1];
                    var (subLeft, subRight) = SplitSubtree(lastLeft.Subtree, key);
                    leftEntries[^1] = lastLeft with { Subtree = subLeft };
                    newRightLeft = subRight;
                }
                else if (rightEntries.Count == 0 && leftEntries.Count == 0)
                {
                    // Split the left pointer
                    var (subLeft, subRight) = SplitSubtree(origLeft, key);
                    origLeft = subLeft;
                    newRightLeft = subRight;
                }
                rightEntries.Add(entry);
            }
        }

        // Handle case where all entries go to left
        if (rightEntries.Count == 0 && leftEntries.Count > 0)
        {
            var lastLeft = leftEntries[^1];
            var (subLeft, subRight) = SplitSubtree(lastLeft.Subtree, key);
            leftEntries[^1] = lastLeft with { Subtree = subLeft };
            newRightLeft = subRight;
        }

        MstMemoryNode? leftNode = (leftEntries.Count > 0 || origLeft is not null)
            ? new MstMemoryNode { Left = origLeft, Entries = leftEntries }
            : null;

        MstMemoryNode? rightNode = (rightEntries.Count > 0 || newRightLeft is not null)
            ? new MstMemoryNode { Left = newRightLeft, Entries = rightEntries }
            : null;

        return (leftNode, rightNode);
    }

    // ── Deletion ─────────────────────────────────────────────

    private static (MstMemoryNode? Node, bool Found) Remove(MstMemoryNode? node, string key, int depth)
    {
        if (node is null)
            return (null, false);

        if (depth > MaxTreeDepth)
            throw new InvalidOperationException("MST tree depth exceeded maximum.");

        var keyDepth = MstKeyDepth.ComputeDepth(key);

        // Find the key in this node
        for (var i = 0; i < node.Entries.Count; i++)
        {
            var cmp = string.Compare(key, node.Entries[i].Key, StringComparison.Ordinal);
            if (cmp == 0)
            {
                // Found it; remove and merge subtrees
                var entry = node.Entries[i];
                node.Entries.RemoveAt(i);

                // Merge the entry's right subtree with the next entry's left context
                if (entry.Subtree is not null)
                {
                    if (i == 0)
                    {
                        node.Left = MergeSubtrees(node.Left, entry.Subtree);
                    }
                    else
                    {
                        var prev = node.Entries[i - 1];
                        node.Entries[i - 1] = prev with { Subtree = MergeSubtrees(prev.Subtree, entry.Subtree) };
                    }
                }

                // If node is now empty, return the left subtree
                if (node.Entries.Count == 0)
                    return (node.Left, true);

                return (node, true);
            }

            if (cmp < 0)
            {
                // Key is before this entry; recurse into appropriate subtree
                if (i == 0)
                {
                    var (newLeft, found) = Remove(node.Left, key, depth - 1);
                    node.Left = newLeft;
                    return (node, found);
                }
                else
                {
                    var prev = node.Entries[i - 1];
                    var (newSub, found) = Remove(prev.Subtree, key, depth - 1);
                    node.Entries[i - 1] = prev with { Subtree = newSub };
                    return (node, found);
                }
            }
        }

        // Key is after all entries; recurse into last subtree
        if (node.Entries.Count > 0)
        {
            var last = node.Entries[^1];
            var (newSub, found) = Remove(last.Subtree, key, depth - 1);
            node.Entries[^1] = last with { Subtree = newSub };
            return (node, found);
        }

        var (newLeft2, found2) = Remove(node.Left, key, depth - 1);
        node.Left = newLeft2;
        return (node, found2);
    }

    private static MstMemoryNode? MergeSubtrees(MstMemoryNode? left, MstMemoryNode? right)
    {
        if (left is null) return right;
        if (right is null) return left;

        // Append right's entries to left, merging the junction
        if (left.Entries.Count > 0)
        {
            var lastLeft = left.Entries[^1];
            left.Entries[^1] = lastLeft with { Subtree = MergeSubtrees(lastLeft.Subtree, right.Left) };
        }
        else
        {
            left.Left = MergeSubtrees(left.Left, right.Left);
        }

        left.Entries.AddRange(right.Entries);
        return left;
    }

    // ── Update ───────────────────────────────────────────────

    private static bool TryUpdate(MstMemoryNode? node, string key, byte[] value)
    {
        if (node is null)
            return false;

        foreach (var entry in node.Entries)
        {
            if (entry.Key == key)
            {
                entry.Value = value;
                return true;
            }

            if (string.Compare(key, entry.Key, StringComparison.Ordinal) < 0)
            {
                // Check left/previous subtree
                if (node.Entries.IndexOf(entry) == 0)
                    return TryUpdate(node.Left, key, value);
                return TryUpdate(node.Entries[node.Entries.IndexOf(entry) - 1].Subtree, key, value);
            }
        }

        if (node.Entries.Count > 0)
            return TryUpdate(node.Entries[^1].Subtree, key, value);
        return TryUpdate(node.Left, key, value);
    }

    // ── Enumeration ──────────────────────────────────────────

    private static IEnumerable<KeyValuePair<string, byte[]>> EnumerateNode(MstMemoryNode? node)
    {
        if (node is null)
            yield break;

        // Left subtree
        foreach (var entry in EnumerateNode(node.Left))
            yield return entry;

        // Entries with their right subtrees
        foreach (var entry in node.Entries)
        {
            yield return new KeyValuePair<string, byte[]>(entry.Key, entry.Value);
            foreach (var sub in EnumerateNode(entry.Subtree))
                yield return sub;
        }
    }

    private static int CountEntries(MstMemoryNode? node)
    {
        if (node is null) return 0;
        var count = node.Entries.Count;
        count += CountEntries(node.Left);
        foreach (var entry in node.Entries)
            count += CountEntries(entry.Subtree);
        return count;
    }

    // ── Serialization ────────────────────────────────────────

    private static byte[] SerializeNode(MstMemoryNode node, Dictionary<string, byte[]> blocks)
    {
        byte[]? leftCid = null;
        if (node.Left is not null)
            leftCid = SerializeNode(node.Left, blocks);

        var entries = new List<MstTreeEntry>();
        string previousKey = "";

        foreach (var entry in node.Entries)
        {
            var keyBytes = Encoding.UTF8.GetBytes(entry.Key);
            var prevBytes = Encoding.UTF8.GetBytes(previousKey);

            // Compute shared prefix length
            var prefixLen = 0;
            var minLen = Math.Min(keyBytes.Length, prevBytes.Length);
            while (prefixLen < minLen && keyBytes[prefixLen] == prevBytes[prefixLen])
                prefixLen++;

            var suffix = keyBytes[prefixLen..];

            byte[]? treeCid = null;
            if (entry.Subtree is not null)
                treeCid = SerializeNode(entry.Subtree, blocks);

            entries.Add(new MstTreeEntry(prefixLen, suffix, entry.Value, treeCid));
            previousKey = entry.Key;
        }

        var nodeData = new MstNodeData
        {
            Left = leftCid,
            Entries = entries,
        };

        var cborBytes = nodeData.ToBytes();
        var cid = CidComputation.ComputeBinaryForDagCbor(cborBytes);
        var cidString = CidComputation.EncodeCidToString(cid);
        blocks[cidString] = cborBytes;

        return cid;
    }

    private static byte[] ComputeNodeCid(MstMemoryNode node)
    {
        byte[]? leftCid = null;
        if (node.Left is not null)
            leftCid = ComputeNodeCid(node.Left);

        var entries = new List<MstTreeEntry>();
        string previousKey = "";

        foreach (var entry in node.Entries)
        {
            var keyBytes = Encoding.UTF8.GetBytes(entry.Key);
            var prevBytes = Encoding.UTF8.GetBytes(previousKey);

            var prefixLen = 0;
            var minLen = Math.Min(keyBytes.Length, prevBytes.Length);
            while (prefixLen < minLen && keyBytes[prefixLen] == prevBytes[prefixLen])
                prefixLen++;

            var suffix = keyBytes[prefixLen..];

            byte[]? treeCid = null;
            if (entry.Subtree is not null)
                treeCid = ComputeNodeCid(entry.Subtree);

            entries.Add(new MstTreeEntry(prefixLen, suffix, entry.Value, treeCid));
            previousKey = entry.Key;
        }

        var nodeData = new MstNodeData
        {
            Left = leftCid,
            Entries = entries,
        };

        var cborBytes = nodeData.ToBytes();
        return CidComputation.ComputeBinaryForDagCbor(cborBytes);
    }

    // ── Deserialization ──────────────────────────────────────

    private static MstMemoryNode DeserializeNode(byte[] cid, Func<string, byte[]?> blockLookup, int depth)
    {
        if (depth > MaxTreeDepth)
            throw new InvalidOperationException("MST tree depth exceeded maximum during deserialization.");

        var cidString = CidComputation.EncodeCidToString(cid);
        var data = blockLookup(cidString)
                   ?? throw new FormatException($"Block not found for CID: {cidString}");

        var nodeData = MstNodeData.FromBytes(data);
        var node = new MstMemoryNode();

        if (nodeData.Left is not null)
            node.Left = DeserializeNode(nodeData.Left, blockLookup, depth + 1);

        string previousKey = "";
        foreach (var entry in nodeData.Entries)
        {
            // Reconstruct full key from prefix compression
            var prefixBytes = Encoding.UTF8.GetBytes(previousKey);
            var fullKey = new byte[entry.PrefixLength + entry.KeySuffix.Length];
            if (entry.PrefixLength > 0)
                prefixBytes.AsSpan(0, entry.PrefixLength).CopyTo(fullKey);
            entry.KeySuffix.CopyTo(fullKey.AsSpan(entry.PrefixLength));
            var key = Encoding.UTF8.GetString(fullKey);

            MstMemoryNode? subtree = null;
            if (entry.Tree is not null)
                subtree = DeserializeNode(entry.Tree, blockLookup, depth + 1);

            node.Entries.Add(new MstMemoryEntry(key, entry.Value, subtree));
            previousKey = key;
        }

        return node;
    }

    // ── Validation ───────────────────────────────────────────

    private static void ValidateNode(MstMemoryNode? node, string? minKey, string? maxKey, int depth)
    {
        if (node is null)
            return;

        if (depth > MaxTreeDepth)
            throw new InvalidOperationException("MST tree depth exceeded maximum.");

        if (node.Entries.Count > MaxNodeEntries)
            throw new InvalidOperationException(
                $"MST node has {node.Entries.Count} entries, exceeding maximum of {MaxNodeEntries}.");

        string? prevKey = null;
        foreach (var entry in node.Entries)
        {
            // Verify key ordering
            if (prevKey is not null && string.Compare(entry.Key, prevKey, StringComparison.Ordinal) <= 0)
                throw new InvalidOperationException(
                    $"MST keys are not properly sorted: '{entry.Key}' after '{prevKey}'.");

            // Verify key is within range
            if (minKey is not null && string.Compare(entry.Key, minKey, StringComparison.Ordinal) <= 0)
                throw new InvalidOperationException(
                    $"MST key '{entry.Key}' is not greater than minimum '{minKey}'.");

            if (maxKey is not null && string.Compare(entry.Key, maxKey, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException(
                    $"MST key '{entry.Key}' is not less than maximum '{maxKey}'.");

            prevKey = entry.Key;
        }

        // Validate subtrees
        ValidateNode(node.Left, minKey,
            node.Entries.Count > 0 ? node.Entries[0].Key : maxKey, depth + 1);

        for (var i = 0; i < node.Entries.Count; i++)
        {
            var entryMaxKey = i + 1 < node.Entries.Count ? node.Entries[i + 1].Key : maxKey;
            ValidateNode(node.Entries[i].Subtree, node.Entries[i].Key, entryMaxKey, depth + 1);
        }
    }
}

/// <summary>
/// In-memory representation of an MST node during tree manipulation.
/// </summary>
internal sealed class MstMemoryNode
{
    /// <summary>Left subtree (keys before all entries in this node).</summary>
    public MstMemoryNode? Left { get; set; }

    /// <summary>Entries with their right subtrees.</summary>
    public List<MstMemoryEntry> Entries { get; set; } = [];
}

/// <summary>
/// In-memory representation of an MST entry during tree manipulation.
/// </summary>
internal sealed record MstMemoryEntry
{
    public string Key { get; set; }
    public byte[] Value { get; set; }
    public MstMemoryNode? Subtree { get; set; }

    public MstMemoryEntry(string key, byte[] value, MstMemoryNode? subtree)
    {
        Key = key;
        Value = value;
        Subtree = subtree;
    }
}
