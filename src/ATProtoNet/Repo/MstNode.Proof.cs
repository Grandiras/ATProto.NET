namespace ATProtoNet.Repo;

// Covering proofs, step for step as getCoveringProof of the reference implementation
// (packages/repo/src/mst/mst.ts) computes them: the path to a key and the paths to its left and
// right neighbours, over a node's children in key order (absent subtrees left out).
internal sealed partial class MstNode
{
    public void WriteCoveringProof(ReadOnlySpan<byte> key, IWritableBlockStore proof)
    {
        ProofForKey(key, proof);
        ProofForLeftSibling(key, proof);
        ProofForRightSibling(key, proof);
    }

    public void WriteTo(IWritableBlockStore destination) => destination.Put(Cid, GetBlock());

    private void ProofForKey(ReadOnlySpan<byte> key, IWritableBlockStore proof)
    {
        var (children, index) = Find(key);
        if (index < children.Count && children[index].Key is { } found && found.AsSpan().SequenceEqual(key))
        {
            WriteTo(proof);
            return;
        }
        // A search that ends with nowhere further to look adds nothing itself; the neighbour proofs cover it.
        if (index == 0 || children[index - 1].Tree is not { } subtree) return;
        subtree.ProofForKey(key, proof);
        WriteTo(proof);
    }

    private void ProofForLeftSibling(ReadOnlySpan<byte> key, IWritableBlockStore proof)
    {
        var (children, index) = Find(key);
        if (index > 0 && children[index - 1].Tree is { } subtree) subtree.ProofForLeftSibling(key, proof);
        WriteTo(proof);
    }

    private void ProofForRightSibling(ReadOnlySpan<byte> key, IWritableBlockStore proof)
    {
        var (children, index) = Find(key);
        Child? found = index < children.Count ? children[index] : index > 0 ? children[index - 1] : null;
        if (found is { Tree: { } tree }) tree.ProofForRightSibling(key, proof);
        else if (found is { Key: { } foundKey })
        {
            var next = foundKey.AsSpan().SequenceEqual(key) ? index + 1 : index - 1;
            if (next >= 0 && next < children.Count && children[next].Tree is { } subtree) subtree.ProofForRightSibling(key, proof);
        }
        WriteTo(proof);
    }

    // The node's children in key order, and the index of the first key at or after key.
    private (List<Child> Children, int Index) Find(ReadOnlySpan<byte> key)
    {
        var (keys, _, subtrees) = Content;
        var children = new List<Child>(keys.Length * 2 + 1);
        var index = -1;
        for (var i = 0; i <= keys.Length; i++)
        {
            if (subtrees[i] is { } subtree) children.Add(new(null, subtree));
            if (i == keys.Length) break;
            if (index < 0 && keys[i].AsSpan().SequenceCompareTo(key) >= 0) index = children.Count;
            children.Add(new(keys[i], null));
        }
        return (children, index < 0 ? children.Count : index);
    }

    private readonly record struct Child(byte[]? Key, MstNode? Tree);
}
