using System.Runtime.CompilerServices;
using System.Text;
using ATProtoNet.Repo;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    /// <inheritdoc />
    public int MstKeyHeight(string key) => LayerOf(default, Encoding.UTF8.GetBytes(key));

    // The interop heights include strings that are not MST keys, so this calls the layer function itself.
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "LayerOf")]
    private static extern int LayerOf(MstKey type, ReadOnlySpan<byte> key);

    /// <inheritdoc />
    public int MstCommonPrefixLength(string left, string right) => left.AsSpan().CommonPrefixLength(right);

    /// <inheritdoc />
    public bool IsValidMstKey(string key) => MstKey.TryParse(key, out _);

    /// <inheritdoc />
    public MstTree MstEdit(IReadOnlyList<KeyValuePair<string, string>> entries, IReadOnlyList<MstOperation> operations)
    {
        var tree = MerkleSearchTree.Build(entries.Select(e => KeyValuePair.Create(MstKey.Parse(e.Key), Cid.Parse(e.Value))));
        foreach (var (key, before, after) in operations)
        {
            var k = MstKey.Parse(key);
            tree = after is null ? tree.Delete(k) : before is null ? tree.Insert(k, Cid.Parse(after)) : tree.Update(k, Cid.Parse(after));
        }
        var blocks = new MemoryBlockStore();
        tree.WriteBlocks(blocks);
        return Tree(tree.Root, blocks);
    }

    /// <inheritdoc />
    public MstTree MstCoveringProof(IReadOnlyList<KeyValuePair<string, string>> entries, IReadOnlyList<string> keys)
    {
        var tree = MerkleSearchTree.Build(entries.Select(e => KeyValuePair.Create(MstKey.Parse(e.Key), Cid.Parse(e.Value))));
        var blocks = new MemoryBlockStore();
        tree.WriteCoveringProof(keys.Select(MstKey.Parse), blocks);
        return Tree(tree.Root, blocks);
    }

    /// <inheritdoc />
    public string? MstInvert(MstTree proof, IReadOnlyList<MstOperation> operations)
    {
        try
        {
            var tree = MerkleSearchTree.Load(Cid.Parse(proof.Root), Store(proof.Blocks));
            foreach (var (key, before, after) in operations)
            {
                tree = tree.Apply(MstKey.Parse(key), before is null ? null : Cid.Parse(before), out var previous);
                if (previous?.Value != after) return null;
            }
            return tree.Root.Value;
        }
        catch (Exception e) when (e is MstIncompleteException or RepoVerificationException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<KeyValuePair<string, string>>? MstLoad(string root, IReadOnlyDictionary<string, byte[]> blocks)
    {
        try
        {
            return [.. MerkleSearchTree.LoadComplete(Cid.Parse(root), Store(blocks)).Enumerate().Select(e => KeyValuePair.Create(e.Key.Value, e.Value.Value))];
        }
        catch (RepoVerificationException)
        {
            return null;
        }
    }

    private static MemoryBlockStore Store(IReadOnlyDictionary<string, byte[]> blocks)
    {
        var store = new MemoryBlockStore();
        foreach (var (cid, block) in blocks) store.Put(Cid.Parse(cid), block);
        return store;
    }

    private static MstTree Tree(Cid root, MemoryBlockStore blocks) =>
        new(root.Value, blocks.Blocks.ToDictionary(b => b.Cid.Value, b => b.Data.ToArray()));
}
