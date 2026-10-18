using System.Runtime.CompilerServices;
using ATProtoNet.Repo;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    private const string PartialTree = "ATProtoNet.Repo.PartialMerkleSearchTree, ATProtoNet";

    /// <inheritdoc />
    public int MstKeyHeight(string key) => ComputeDepth(null, key);

    /// <inheritdoc />
    public int MstCommonPrefixLength(string left, string right) => SharedPrefixLength(null, left, right);

    /// <inheritdoc />
    public bool IsValidMstKey(string key)
    {
        try
        {
            MerkleSearchTree.Create().Add(key, CidComputation.ComputeBinaryForDagCbor([0xA0]));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public MstTree MstEdit(IReadOnlyList<KeyValuePair<string, string>> entries, IReadOnlyList<MstOperation> operations)
    {
        var tree = MerkleSearchTree.Create(entries.Select(e => KeyValuePair.Create(e.Key, CidComputation.DecodeCidString(e.Value))));
        foreach (var (key, before, after) in operations)
        {
            if (after is null) tree.Delete(key);
            else if (before is null) tree.Add(key, CidComputation.DecodeCidString(after));
            else tree.Update(key, CidComputation.DecodeCidString(after));
        }
        return Tree(tree.Serialize());
    }

    /// <inheritdoc />
    public MstTree MstCoveringProof(IReadOnlyList<KeyValuePair<string, string>> entries, IReadOnlyList<string> keys) =>
        Tree(MerkleSearchTree.Create(entries.Select(e => KeyValuePair.Create(e.Key, CidComputation.DecodeCidString(e.Value)))).SerializeProof(keys));

    /// <inheritdoc />
    public string? MstInvert(MstTree proof, IReadOnlyList<MstOperation> operations)
    {
        try
        {
            var tree = Load(null, CidComputation.DecodeCidString(proof.Root), cid => proof.Blocks.GetValueOrDefault(CidComputation.EncodeCidToString(cid)));
            // Re-inserts first, then removals, each in key order, as 0.8's own inversion tests do.
            foreach (var (key, before, after) in operations.OrderBy(o => o.After is null ? 0 : 1).ThenBy(o => o.Key, StringComparer.Ordinal))
            {
                if (before is null) Remove(tree, key);
                else Insert(tree, key, CidComputation.DecodeCidString(before));
            }
            return CidComputation.EncodeCidToString(RootCid(tree));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<KeyValuePair<string, string>>? MstLoad(string root, IReadOnlyDictionary<string, byte[]> blocks)
    {
        try
        {
            var tree = MerkleSearchTree.Deserialize(CidComputation.DecodeCidString(root), blocks.GetValueOrDefault);
            return tree.Validate() ? [.. tree.GetEntries().Select(e => KeyValuePair.Create(e.Key, CidComputation.EncodeCidToString(e.Value)))] : null;
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException) // Validate throws the latter for a non-canonical tree.
        {
            return null;
        }
    }

    private static MstTree Tree((byte[] RootCid, Dictionary<string, byte[]> Blocks) tree) =>
        new(CidComputation.EncodeCidToString(tree.RootCid), tree.Blocks);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ComputeDepth")]
    private static extern int ComputeDepth([UnsafeAccessorType("ATProtoNet.Repo.MstKeyDepth, ATProtoNet")] object? type, string key);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "SharedPrefixLength")]
    private static extern int SharedPrefixLength(MerkleSearchTree? type, string a, string b);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Load")]
    [return: UnsafeAccessorType(PartialTree)]
    private static extern object Load([UnsafeAccessorType(PartialTree)] object? type, byte[] rootCid, Func<byte[], byte[]?> blocks);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Insert")]
    private static extern byte[]? Insert([UnsafeAccessorType(PartialTree)] object tree, string key, byte[] value);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Remove")]
    private static extern byte[]? Remove([UnsafeAccessorType(PartialTree)] object tree, string key);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "RootCid")]
    private static extern byte[] RootCid([UnsafeAccessorType(PartialTree)] object tree);
}
