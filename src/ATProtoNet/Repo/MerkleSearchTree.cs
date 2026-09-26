using System.Text;

namespace ATProtoNet.Repo;

/// <summary>
/// In-memory Merkle Search Tree (MST) for AT Protocol repositories.
/// <para>
/// The MST is a deterministic, content-addressed key/value mapping where keys are
/// repo paths (<c>collection/rkey</c>) and values are CID links to record data. The tree
/// structure is fully reproducible from the set of key/value pairs, regardless of insertion
/// order.
/// </para>
/// </summary>
/// <remarks>
/// <para>The tree holds its entries as a sorted set and derives the node structure from it
/// whenever a CID or a block is asked for. Because the structure is a pure function of the
/// entries, any sequence of <see cref="Add"/>, <see cref="Update"/> and <see cref="Delete"/>
/// calls lands on exactly the root a bulk build of the final entries produces — which is what
/// the specification requires and what other implementations compute.</para>
/// <para>Keys must be valid MST keys: one <c>/</c> separating two non-empty segments of
/// <c>A-Z a-z 0-9 _ ~ - : .</c>, at most 1024 characters in all.</para>
/// <para>See: https://atproto.com/specs/repository#mst-structure</para>
/// </remarks>
public sealed class MerkleSearchTree
{
    /// <summary>
    /// Deepest node chain <see cref="Deserialize"/> follows. A layer is two bits of a SHA-256
    /// leading-zero count, so a real tree of any size is a dozen layers deep at most.
    /// </summary>
    internal const int MaxTreeDepth = 64;

    /// <summary>Longest valid MST key, in characters.</summary>
    private const int MaxKeyLength = 1024;

    /// <summary>Every entry, in key order. For valid (ASCII) keys ordinal order is byte order.</summary>
    private readonly SortedDictionary<string, Leaf> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// The root CID the tree was loaded from, kept until the first edit so that
    /// <see cref="Validate"/> can compare the canonical rebuild against it.
    /// </summary>
    private byte[]? _loadedRoot;

    private MerkleSearchTree()
    {
    }

    /// <summary>
    /// Creates an empty MST.
    /// </summary>
    public static MerkleSearchTree Create() => new();

    /// <summary>
    /// Creates an MST from a set of key/value pairs. Keys must be unique.
    /// Values are binary CID bytes referencing record data.
    /// </summary>
    /// <param name="entries">The key/value pairs. Keys are repo paths, values are CID bytes.</param>
    /// <returns>A new MST containing all entries.</returns>
    /// <exception cref="ArgumentException">A key is not a valid MST key, or appears twice.</exception>
    public static MerkleSearchTree Create(IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var tree = new MerkleSearchTree();
        foreach (var (key, value) in entries)
            tree.Add(key, value);
        return tree;
    }

    /// <summary>
    /// Gets the record CID for a given key, or <c>null</c> if not found.
    /// </summary>
    /// <param name="key">The repo path (e.g., "app.bsky.feed.post/abc123").</param>
    /// <returns>The record CID bytes, or <c>null</c>.</returns>
    public byte[]? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _entries.TryGetValue(key, out var leaf) ? leaf.Value : null;
    }

    /// <summary>
    /// Enumerates all key/value pairs in sorted order.
    /// </summary>
    public IEnumerable<KeyValuePair<string, byte[]>> GetEntries()
    {
        foreach (var (key, leaf) in _entries)
            yield return new KeyValuePair<string, byte[]>(key, leaf.Value);
    }

    /// <summary>
    /// Adds a new key/value pair. Throws if the key already exists.
    /// </summary>
    /// <param name="key">The repo path.</param>
    /// <param name="value">The record CID bytes.</param>
    /// <exception cref="ArgumentException">The key is not a valid MST key, or already exists.</exception>
    public void Add(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        if (!IsValidKey(key))
            throw new ArgumentException($"Not a valid MST key: '{key}'.", nameof(key));
        if (_entries.ContainsKey(key))
            throw new ArgumentException($"Key already exists in MST: {key}", nameof(key));

        _entries.Add(key, new Leaf(value, MstKeyDepth.ComputeDepth(key)));
        _loadedRoot = null;
    }

    /// <summary>
    /// Updates the value for an existing key. Throws if the key does not exist.
    /// </summary>
    /// <param name="key">The repo path.</param>
    /// <param name="value">The new record CID bytes.</param>
    /// <exception cref="KeyNotFoundException">Key not found.</exception>
    public void Update(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        if (!_entries.TryGetValue(key, out var leaf))
            throw new KeyNotFoundException($"Key not found in MST: {key}");

        _entries[key] = leaf with { Value = value };
        _loadedRoot = null;
    }

    /// <summary>
    /// Removes a key/value pair. Throws if the key does not exist.
    /// </summary>
    /// <param name="key">The repo path.</param>
    /// <exception cref="KeyNotFoundException">Key not found.</exception>
    public void Delete(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_entries.Remove(key))
            throw new KeyNotFoundException($"Key not found in MST: {key}");

        _loadedRoot = null;
    }

    /// <summary>
    /// Checks whether a key exists in the tree.
    /// </summary>
    public bool ContainsKey(string key) => Get(key) is not null;

    /// <summary>
    /// Gets the total number of entries in the tree.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Serializes the entire MST to a block store (dictionary of CID → DAG-CBOR bytes),
    /// and returns the root CID.
    /// </summary>
    /// <returns>Tuple of (root CID bytes, block map).</returns>
    public (byte[] RootCid, Dictionary<string, byte[]> Blocks) Serialize()
    {
        var root = Build();
        var blocks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        CollectAll(root, blocks);
        return (root.Cid, blocks);
    }

    /// <summary>
    /// Serializes only the nodes a firehose <c>#commit</c> needs to prove the operations on
    /// <paramref name="keys"/>: for each key, the covering proof the reference implementation
    /// computes — the nodes on the path to the key and on the paths to its immediate left and
    /// right neighbours — plus the root. A relay replays the operations in reverse against these
    /// blocks to check the commit, so the set is logarithmic in the repository size rather than
    /// linear like <see cref="Serialize"/>.
    /// </summary>
    /// <param name="keys">
    /// The MST keys the commit touched. Keys absent from the tree (deletions) contribute the
    /// nodes around where they were, which is what proves the absence.
    /// </param>
    /// <returns>Tuple of (root CID bytes, block map covering the requested keys).</returns>
    public (byte[] RootCid, Dictionary<string, byte[]> Blocks) SerializeProof(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var root = Build();
        var proof = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        // The root is always included: it is what the signed commit points at.
        AddBlock(root, proof);

        foreach (var key in keys)
        {
            ArgumentNullException.ThrowIfNull(key);
            ProofForKey(root, key, proof);
            ProofForLeftSibling(root, key, proof);
            ProofForRightSibling(root, key, proof);
        }

        return (root.Cid, proof);
    }

    /// <summary>
    /// Deserializes an MST from a block store (CID → DAG-CBOR bytes mapping) starting from a root CID.
    /// </summary>
    /// <param name="rootCid">The root node CID bytes.</param>
    /// <param name="blocks">Block lookup function (CID string → DAG-CBOR bytes).</param>
    /// <returns>The deserialized MST.</returns>
    /// <exception cref="FormatException">
    /// A block is missing or is not a well-formed MST node, a key is not a valid MST key, keys are
    /// out of order, or the tree is deeper than any real tree can be.
    /// </exception>
    /// <remarks>
    /// This reads the entries without re-hashing the blocks. Pass blocks whose CIDs were already
    /// checked (for example <see cref="CarReader.FromBytes"/> with <c>verifyBlockCids</c>), and call
    /// <see cref="Validate"/> to confirm the blocks are the canonical tree for
    /// <paramref name="rootCid"/>.
    /// </remarks>
    public static MerkleSearchTree Deserialize(byte[] rootCid, Func<string, byte[]?> blocks)
    {
        ArgumentNullException.ThrowIfNull(rootCid);
        ArgumentNullException.ThrowIfNull(blocks);

        var tree = new MerkleSearchTree();
        string? lastKey = null;
        tree.Load(rootCid, blocks, 0, ref lastKey);
        tree._loadedRoot = rootCid.ToArray();
        return tree;
    }

    /// <summary>
    /// Computes the root CID of the tree without materializing all blocks.
    /// </summary>
    public byte[] ComputeRootCid() => Build().Cid;

    /// <summary>
    /// Validates that the tree is the canonical MST for its entries: rebuilds it from the entries
    /// and compares the root CID with the one it was loaded from by <see cref="Deserialize"/>.
    /// </summary>
    /// <remarks>
    /// A tree built or edited in memory is canonical by construction, so this only has something
    /// to check between <see cref="Deserialize"/> and the first edit.
    /// </remarks>
    /// <returns><c>true</c> if valid; otherwise throws with details.</returns>
    /// <exception cref="InvalidOperationException">If the tree structure is invalid.</exception>
    public bool Validate()
    {
        if (_loadedRoot is null)
            return true;

        var rebuilt = Build().Cid;
        if (!rebuilt.AsSpan().SequenceEqual(_loadedRoot))
        {
            throw new InvalidOperationException(
                $"MST loaded from {CidComputation.EncodeCidToString(_loadedRoot)} is not the canonical tree " +
                $"for its entries, which is {CidComputation.EncodeCidToString(rebuilt)}.");
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="key"/> is a valid MST key: <c>collection/rkey</c>, both non-empty,
    /// drawn from <c>A-Z a-z 0-9 _ ~ - : .</c>, at most 1024 characters in all.
    /// </summary>
    /// <remarks>Mirrors <c>isValidMstKey</c> in the reference implementation.</remarks>
    internal static bool IsValidKey(string key)
    {
        if (key.Length == 0 || key.Length > MaxKeyLength)
            return false;

        var separator = -1;
        for (var i = 0; i < key.Length; i++)
        {
            if (!IsKeyChar(key[i], i, ref separator))
                return false;
        }

        return separator > 0 && separator < key.Length - 1;
    }

    /// <summary><see cref="IsValidKey(string)"/> over the bytes a tree node holds, without decoding them.</summary>
    internal static bool IsValidKey(ReadOnlySpan<byte> key)
    {
        if (key.Length == 0 || key.Length > MaxKeyLength)
            return false;

        var separator = -1;
        for (var i = 0; i < key.Length; i++)
        {
            if (!IsKeyChar((char)key[i], i, ref separator))
                return false;
        }

        return separator > 0 && separator < key.Length - 1;
    }

    private static bool IsKeyChar(char c, int index, ref int separator)
    {
        if (c == '/')
        {
            if (separator >= 0)
                return false;
            separator = index;
            return true;
        }

        return c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '~' or '-' or ':' or '.';
    }

    // ── Building ─────────────────────────────────────────────

    /// <summary>
    /// Builds the canonical node structure for the current entries, with every node's bytes and
    /// CID computed.
    /// </summary>
    /// <remarks>
    /// Each key sits at the layer given by its hash (<see cref="MstKeyDepth"/>); the root is at the
    /// highest layer present. A node holds the keys of its layer within its range, and the gaps
    /// between them become subtrees one layer down. An empty gap is a null link rather than an
    /// empty node, and a gap whose keys all sit further down is wrapped in an entry-less node per
    /// skipped layer. The only empty node is the root of an empty tree.
    /// </remarks>
    private Node Build()
    {
        var count = _entries.Count;
        var keys = new string[count];
        var values = new byte[count][];
        var heights = new int[count];
        var maxHeight = 0;

        var i = 0;
        foreach (var (key, leaf) in _entries)
        {
            keys[i] = key;
            values[i] = leaf.Value;
            heights[i] = leaf.Height;
            maxHeight = Math.Max(maxHeight, leaf.Height);
            i++;
        }

        return BuildRange(keys, values, heights, 0, count, maxHeight) ?? Seal(new Node());
    }

    /// <summary>
    /// Builds the node for <c>[start, end)</c> at <paramref name="layer"/>, or <c>null</c> for an
    /// empty range. Every key in the range sits at <paramref name="layer"/> or below.
    /// </summary>
    private static Node? BuildRange(string[] keys, byte[][] values, int[] heights, int start, int end, int layer)
    {
        if (start == end)
            return null;

        var node = new Node();
        var gapStart = start;
        for (var i = start; i < end; i++)
        {
            if (heights[i] != layer)
                continue;

            node.Attach(BuildRange(keys, values, heights, gapStart, i, layer - 1));
            node.Entries.Add(new NodeEntry(keys[i], values[i]));
            gapStart = i + 1;
        }

        node.Attach(BuildRange(keys, values, heights, gapStart, end, layer - 1));
        return Seal(node);
    }

    /// <summary>Encodes <paramref name="node"/>, whose children are already sealed, and records its CID.</summary>
    private static Node Seal(Node node)
    {
        var entries = new List<MstTreeEntry>(node.Entries.Count);
        var previous = string.Empty;

        foreach (var entry in node.Entries)
        {
            // Keys are ASCII, so a character offset is a byte offset.
            var prefix = SharedPrefixLength(previous, entry.Key);
            var suffix = Encoding.ASCII.GetBytes(entry.Key, prefix, entry.Key.Length - prefix);
            entries.Add(new MstTreeEntry(prefix, suffix, entry.Value, entry.Right?.Cid));
            previous = entry.Key;
        }

        node.Bytes = new MstNodeData { Left = node.Left?.Cid, Entries = entries }.ToBytes();
        node.Cid = CidComputation.ComputeBinaryForDagCbor(node.Bytes);
        return node;
    }

    internal static int SharedPrefixLength(string a, string b)
    {
        var max = Math.Min(a.Length, b.Length);
        var length = 0;
        while (length < max && a[length] == b[length])
            length++;
        return length;
    }

    private static void CollectAll(Node node, Dictionary<string, byte[]> blocks)
    {
        if (node.Left is not null)
            CollectAll(node.Left, blocks);

        foreach (var entry in node.Entries)
        {
            if (entry.Right is not null)
                CollectAll(entry.Right, blocks);
        }

        AddBlock(node, blocks);
    }

    private static void AddBlock(Node node, Dictionary<string, byte[]> blocks)
        => blocks[CidComputation.EncodeCidToString(node.Cid)] = node.Bytes;

    // ── Covering proofs ──────────────────────────────────────
    //
    // These mirror getCoveringProof in the reference implementation (packages/repo/src/mst/mst.ts)
    // step for step, over the same view of a node: its children in key order, a subtree before
    // each entry and after the last, with absent subtrees left out.

    private static void ProofForKey(Node node, string key, Dictionary<string, byte[]> proof)
    {
        var children = node.Children();
        var index = FindGreaterOrEqualEntry(children, key);

        if (index < children.Count && children[index].Key == key)
        {
            AddBlock(node, proof);
            return;
        }

        // A search that ends in a node with nowhere further to look contributes nothing itself;
        // the neighbour proofs cover that node.
        if (index == 0 || children[index - 1].Tree is not { } subtree)
            return;

        ProofForKey(subtree, key, proof);
        AddBlock(node, proof);
    }

    private static void ProofForLeftSibling(Node node, string key, Dictionary<string, byte[]> proof)
    {
        var children = node.Children();
        var index = FindGreaterOrEqualEntry(children, key);

        if (index > 0 && children[index - 1].Tree is { } subtree)
            ProofForLeftSibling(subtree, key, proof);

        AddBlock(node, proof);
    }

    private static void ProofForRightSibling(Node node, string key, Dictionary<string, byte[]> proof)
    {
        var children = node.Children();
        var index = FindGreaterOrEqualEntry(children, key);

        Child? found = index < children.Count ? children[index]
            : index > 0 ? children[index - 1]
            : null;

        if (found is { Tree: { } foundTree })
        {
            ProofForRightSibling(foundTree, key, proof);
        }
        else if (found is { Key: { } foundKey })
        {
            var next = foundKey == key ? index + 1 : index - 1;
            if (next >= 0 && next < children.Count && children[next].Tree is { } subtree)
                ProofForRightSibling(subtree, key, proof);
        }

        AddBlock(node, proof);
    }

    /// <summary>Index of the first entry whose key is ≥ <paramref name="key"/>, or the child count.</summary>
    private static int FindGreaterOrEqualEntry(List<Child> children, string key)
    {
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Key is { } entryKey && string.CompareOrdinal(entryKey, key) >= 0)
                return i;
        }

        return children.Count;
    }

    // ── Loading ──────────────────────────────────────────────

    private void Load(byte[] cid, Func<string, byte[]?> blocks, int depth, ref string? lastKey)
    {
        if (depth > MaxTreeDepth)
            throw new FormatException($"MST is deeper than the maximum of {MaxTreeDepth} layers.");

        var cidString = CidComputation.EncodeCidToString(cid);
        var data = blocks(cidString)
                   ?? throw new FormatException($"Block not found for CID: {cidString}");

        var node = MstNodeData.FromBytes(data);

        // Only the root of an empty tree has neither entries nor a left link. Refusing any other
        // such node means every subtree holds at least one key, so the ordering check below also
        // stops a crafted tree from linking one subtree many times over.
        if (depth > 0 && node.Entries.Count == 0 && node.Left is null)
            throw new FormatException($"MST node {cidString} is empty.");

        if (node.Left is not null)
            Load(node.Left, blocks, depth + 1, ref lastKey);

        byte[] previous = [];
        foreach (var entry in node.Entries)
        {
            // MstNodeData has already bounded the prefix by the previous key's length.
            var keyBytes = new byte[entry.PrefixLength + entry.KeySuffix.Length];
            previous.AsSpan(0, entry.PrefixLength).CopyTo(keyBytes);
            entry.KeySuffix.CopyTo(keyBytes.AsSpan(entry.PrefixLength));

            // Latin-1 maps each byte to one char, so anything outside the ASCII key alphabet
            // survives decoding and is refused by the key check rather than silently replaced.
            var key = Encoding.Latin1.GetString(keyBytes);
            if (!IsValidKey(key))
                throw new FormatException($"MST node {cidString} holds an invalid key: '{key}'.");

            // An in-order walk must see strictly increasing keys. Checking that here also means a
            // subtree linked twice fails on its first repeated key rather than being walked again.
            // Ordinal order is byte order for the ASCII keys the check above lets through.
            if (lastKey is not null && string.CompareOrdinal(key, lastKey) <= 0)
                throw new FormatException($"MST keys are out of order: '{key}' follows '{lastKey}'.");

            _entries.Add(key, new Leaf(entry.Value, MstKeyDepth.ComputeDepth(key)));
            lastKey = key;
            previous = keyBytes;

            if (entry.Tree is not null)
                Load(entry.Tree, blocks, depth + 1, ref lastKey);
        }
    }

    // ── Types ────────────────────────────────────────────────

    /// <summary>A record CID and the layer its key hashes to.</summary>
    private readonly record struct Leaf(byte[] Value, int Height);

    /// <summary>An entry of a built node.</summary>
    private sealed class NodeEntry(string key, byte[] value)
    {
        public string Key { get; } = key;

        public byte[] Value { get; } = value;

        /// <summary>The subtree between this entry and the next one.</summary>
        public Node? Right { get; set; }
    }

    /// <summary>A node's child in key order: either an entry or a subtree.</summary>
    private readonly record struct Child(string? Key, Node? Tree);

    /// <summary>A node of the built tree, with its encoding.</summary>
    private sealed class Node
    {
        /// <summary>The subtree before the first entry.</summary>
        public Node? Left { get; private set; }

        public List<NodeEntry> Entries { get; } = [];

        public byte[] Bytes { get; set; } = [];

        public byte[] Cid { get; set; } = [];

        /// <summary>Links <paramref name="subtree"/> after the last entry so far (or as the left subtree).</summary>
        public void Attach(Node? subtree)
        {
            if (Entries.Count == 0)
                Left = subtree;
            else
                Entries[^1].Right = subtree;
        }

        public List<Child> Children()
        {
            var children = new List<Child>(Entries.Count * 2 + 1);
            if (Left is not null)
                children.Add(new Child(null, Left));

            foreach (var entry in Entries)
            {
                children.Add(new Child(entry.Key, null));
                if (entry.Right is not null)
                    children.Add(new Child(null, entry.Right));
            }

            return children;
        }
    }
}
