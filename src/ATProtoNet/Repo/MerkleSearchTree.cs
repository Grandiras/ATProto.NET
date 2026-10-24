namespace ATProtoNet.Repo;

/// <summary>
/// A Merkle Search Tree: the content-addressed map from <see cref="MstKey"/> to record CID that a
/// repository commit signs. Persistent: every edit returns a new tree and leaves this one usable.
/// </summary>
/// <remarks>
/// <para>The shape depends only on the entries, never on the edits that led to them. A tree may be
/// partial: subtrees whose blocks the store lacks stay known by CID only, and an operation that needs
/// one throws <see cref="MstIncompleteException"/>. <see cref="Root"/> encodes and hashes only the nodes
/// changed since it was last computed. See <see href="https://atproto.com/specs/repository#mst-structure"/>.</para>
/// <para>Safe to share across threads: a tree never changes, and nodes load on first use.</para>
/// </remarks>
public sealed class MerkleSearchTree
{
    /// <summary>The deepest node chain a loaded tree may have.</summary>
    public const int MaxTreeDepth = 64;

    /// <summary>The most entries a loaded node may hold, far above any real fanout-4 node.</summary>
    public const int MaxEntriesPerNode = 1024;

    private readonly MstNode _root;

    private MerkleSearchTree(MstNode root) => _root = root;

    /// <summary>The CID of the root node.</summary>
    public Cid Root => _root.Cid;

    /// <summary>An empty tree.</summary>
    /// <returns>The tree.</returns>
    public static MerkleSearchTree Empty() => new(MstNode.Empty());

    /// <summary>The tree of <paramref name="entries"/>.</summary>
    /// <param name="entries">The keys and their values, in any order.</param>
    /// <returns>The tree.</returns>
    /// <exception cref="ArgumentException">A key appears twice.</exception>
    public static MerkleSearchTree Build(IEnumerable<KeyValuePair<MstKey, Cid>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sorted = entries.Select(e => (Key: Bytes(e.Key), e.Key.Layer, Value: e.Value ?? throw new ArgumentException("A value is null.", nameof(entries))))
            .OrderBy(e => e.Key, Comparer<byte[]>.Create((x, y) => x.AsSpan().SequenceCompareTo(y))).ToArray();
        for (var i = 1; i < sorted.Length; i++)
            if (sorted[i].Key.AsSpan().SequenceEqual(sorted[i - 1].Key)) throw new ArgumentException($"The key '{MstKey.FromValidBytes(sorted[i].Key)}' appears twice.", nameof(entries));
        var top = sorted.Length == 0 ? 0 : sorted.Max(e => e.Layer);
        var root = MstNode.Build([.. sorted.Select(e => e.Key)], [.. sorted.Select(e => e.Value)], [.. sorted.Select(e => e.Layer)], 0, sorted.Length, top);
        return new(root ?? MstNode.Empty());
    }

    /// <summary>The tree under <paramref name="root"/>, loading nodes from <paramref name="blocks"/> as operations reach them.</summary>
    /// <remarks>Each node is checked when it is loaded. Subtrees the store lacks make the tree partial.</remarks>
    /// <param name="root">The root node's CID.</param>
    /// <param name="blocks">The store to read nodes from; it must stay available while the tree is used.</param>
    /// <returns>The tree.</returns>
    /// <exception cref="MstIncompleteException">The store lacks the root node.</exception>
    /// <exception cref="RepoVerificationException">The root node is malformed (<see cref="RepoVerificationError.MalformedTree"/>).</exception>
    public static MerkleSearchTree Load(Cid root, IBlockStore blocks)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(blocks);
        return new(MstNode.Read(root, blocks));
    }

    /// <summary>The complete tree under <paramref name="root"/>, every node loaded and checked.</summary>
    /// <param name="root">The root node's CID.</param>
    /// <param name="blocks">The store to read nodes from.</param>
    /// <returns>The tree.</returns>
    /// <exception cref="RepoVerificationException">A node is missing (<see cref="RepoVerificationError.MissingBlock"/>), or
    /// the blocks are not exactly a well-formed, canonical tree (<see cref="RepoVerificationError.MalformedTree"/>).</exception>
    public static MerkleSearchTree LoadComplete(Cid root, IBlockStore blocks)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(blocks);
        try
        {
            var tree = MstNode.Read(root, blocks);
            HashSet<Cid> seen = [];
            Stack<MstNode> pending = new([tree]);
            while (pending.TryPop(out var node))
            {
                var content = node.Content;
                if (!seen.Add(node.Cid)) throw new RepoVerificationException(RepoVerificationError.MalformedTree, $"MST node {node.Cid} is referenced twice.");
                if (node.ComputeCid() != node.Cid) throw new RepoVerificationException(RepoVerificationError.MalformedTree, $"MST node {node.Cid} is not in canonical form.");
                foreach (var subtree in content.Subtrees) if (subtree is not null) pending.Push(subtree);
            }
            return new(tree);
        }
        catch (MstIncompleteException ex)
        {
            throw new RepoVerificationException(RepoVerificationError.MissingBlock, $"The MST block {ex.Cid} is missing.", ex);
        }
    }

    /// <summary>The value of a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/> when the tree has no such key.</returns>
    /// <exception cref="MstIncompleteException">The path to the key runs through a subtree the store lacks.</exception>
    public Cid? Get(MstKey key) => _root.Get(Bytes(key));

    /// <summary>Adds a key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">Its value.</param>
    /// <returns>The new tree.</returns>
    /// <exception cref="InvalidOperationException">The tree already has the key.</exception>
    /// <exception cref="MstIncompleteException">The edit reaches a subtree the store lacks.</exception>
    public MerkleSearchTree Insert(MstKey key, Cid value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var tree = Apply(key, value, out var previous);
        return previous is null ? tree : throw new InvalidOperationException($"The tree already has the key '{key}'.");
    }

    /// <summary>Changes the value of a key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">Its new value.</param>
    /// <returns>The new tree.</returns>
    /// <exception cref="InvalidOperationException">The tree has no such key.</exception>
    /// <exception cref="MstIncompleteException">The edit reaches a subtree the store lacks.</exception>
    public MerkleSearchTree Update(MstKey key, Cid value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var tree = Apply(key, value, out var previous);
        return previous is not null ? tree : throw new InvalidOperationException($"The tree has no key '{key}'.");
    }

    /// <summary>Removes a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The new tree.</returns>
    /// <exception cref="InvalidOperationException">The tree has no such key.</exception>
    /// <exception cref="MstIncompleteException">The edit reaches a subtree the store lacks.</exception>
    public MerkleSearchTree Delete(MstKey key)
    {
        var tree = Apply(key, null, out var previous);
        return previous is not null ? tree : throw new InvalidOperationException($"The tree has no key '{key}'.");
    }

    /// <summary>Sets or removes a key, whatever it held: the primitive that inverting a commit uses.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">Its new value, or <see langword="null"/> to remove it.</param>
    /// <param name="previous">The value it held, or <see langword="null"/> when it was absent.</param>
    /// <returns>The new tree; this tree when nothing changed.</returns>
    /// <exception cref="MstIncompleteException">The edit reaches a subtree the store lacks.</exception>
    public MerkleSearchTree Apply(MstKey key, Cid? value, out Cid? previous)
    {
        var bytes = Bytes(key);
        if (value is null)
        {
            previous = null;
            if (key.Layer > _root.Layer) return this;
            var removed = _root.Remove(bytes, key.Layer, out previous);
            if (previous is null) return this;
            // A root left holding only a link gives way to that subtree, unless the store lacks it.
            while (removed is not null && removed.TryGetContent(out var content) && content.Keys.Length == 0) removed = content.Subtrees[0];
            return new(removed ?? MstNode.Empty());
        }

        var root = _root;
        if (key.Layer > root.Layer)
        {
            if (root.IsEmpty) root = new MstNode(key.Layer, [], [], [null]);
            while (root.Layer < key.Layer) root = new MstNode(root.Layer + 1, [], [], [root]);
        }
        var updated = root.Put(bytes, key.Layer, value, out previous);
        return previous == value ? this : new(updated);
    }

    /// <summary>The entries in key order.</summary>
    /// <param name="from">The first key to return, or <see langword="null"/> to start at the beginning.</param>
    /// <returns>The entries at or after <paramref name="from"/>.</returns>
    /// <exception cref="MstIncompleteException">The walk reaches a subtree the store lacks.</exception>
    public IEnumerable<KeyValuePair<MstKey, Cid>> Enumerate(MstKey? from = null)
    {
        var start = from is { } key ? Bytes(key) : null;
        for (var walker = new MstWalker(_root); !walker.Done;)
        {
            if (walker.Tree is { } tree)
            {
                if (start is not null && walker.Bound is { } bound && bound.AsSpan().SequenceCompareTo(start) <= 0) walker.Advance();
                else walker.Descend();
                continue;
            }
            if (start is null || walker.Key.AsSpan().SequenceCompareTo(start) >= 0)
                yield return new(MstKey.FromValidBytes(walker.Key), walker.Value);
            walker.Advance();
        }
    }

    /// <summary>Writes every node of the tree.</summary>
    /// <param name="destination">Where the nodes go.</param>
    /// <exception cref="MstIncompleteException">The tree is partial.</exception>
    public void WriteBlocks(IWritableBlockStore destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        for (Stack<MstNode> pending = new([_root]); pending.TryPop(out var node);)
        {
            node.WriteTo(destination);
            foreach (var subtree in node.Content.Subtrees) if (subtree is not null) pending.Push(subtree);
        }
    }

    /// <summary>Writes the nodes that prove each key present or absent: the root, and the paths to each key and its neighbours.</summary>
    /// <param name="keys">The keys to prove.</param>
    /// <param name="destination">Where the nodes go.</param>
    /// <exception cref="MstIncompleteException">A path runs through a subtree the store lacks.</exception>
    public void WriteCoveringProof(IEnumerable<MstKey> keys, IWritableBlockStore destination)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(destination);
        _root.WriteTo(destination);
        foreach (var key in keys) _root.WriteCoveringProof(Bytes(key), destination);
    }

    /// <summary>
    /// Writes what a <c>#commit</c> event carries for the change from <paramref name="previous"/> to this tree:
    /// every node that is new, and the covering proof of every changed key, so that a holder of only
    /// these blocks and the previous root can invert the change.
    /// </summary>
    /// <param name="previous">The tree before the change.</param>
    /// <param name="destination">Where the nodes go.</param>
    /// <exception cref="MstIncompleteException">The changed paths run through a subtree a store lacks.</exception>
    public void WriteDiffBlocks(MerkleSearchTree previous, IWritableBlockStore destination)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(destination);
        // Every new node lies on the path to a changed key, which the key's covering proof holds.
        _root.WriteTo(destination);
        foreach (var (key, _, _) in MstWalker.Diff(previous._root, _root)) _root.WriteCoveringProof(key, destination);
    }

    internal IEnumerable<(byte[] Key, Cid? Before, Cid? After)> ChangesSince(MerkleSearchTree previous) => MstWalker.Diff(previous._root, _root);

    private static byte[] Bytes(MstKey key) => key.ToString().Length > 0 ? key.ToBytes() : throw new ArgumentException("The key is a default MstKey.", nameof(key));
}
