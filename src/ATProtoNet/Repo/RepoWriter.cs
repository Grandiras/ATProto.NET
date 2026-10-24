using ATProtoNet.Crypto;
using ATProtoNet.Data;

namespace ATProtoNet.Repo;

/// <summary>A change to one record: its new block, or <see langword="null"/> to delete it.</summary>
/// <param name="Key">The record's key.</param>
/// <param name="Record">The record as a DRISL map, or <see langword="null"/> to delete it.</param>
public sealed record RecordWrite(MstKey Key, ReadOnlyMemory<byte>? Record);

/// <summary>A key a commit changed, with its value before and after (<see langword="null"/> when absent).</summary>
/// <param name="Key">The key.</param>
/// <param name="Before">The record CID before the commit.</param>
/// <param name="After">The record CID after the commit.</param>
public sealed record RepoOperation(MstKey Key, Cid? Before, Cid? After);

/// <summary>A commit <see cref="RepoWriter"/> made.</summary>
/// <param name="Commit">The signed commit.</param>
/// <param name="Operations">The keys it changed, in the order written.</param>
/// <param name="Blocks">What a <c>#commit</c> event carries: the commit, the new and proof tree nodes and the new record blocks.</param>
public sealed record RepoWriteResult(SignedCommit Commit, IReadOnlyList<RepoOperation> Operations, MemoryBlockStore Blocks);

/// <summary>Applies record writes to a repository and signs each batch as a commit. Not safe for concurrent use.</summary>
/// <remarks>Every block a commit needs goes into the writer's store, which also backs its tree.</remarks>
public sealed class RepoWriter
{
    private readonly Did _did;
    private readonly SigningKey _key;
    private readonly IWritableBlockStore _blocks;

    /// <summary>Creates a writer for a repository.</summary>
    /// <param name="did">The repository's DID.</param>
    /// <param name="key">The repository's signing key. The caller keeps owning it.</param>
    /// <param name="blocks">The repository's blocks: the head's tree is read from it, and every new block written to it.</param>
    /// <param name="head">The current commit, or <see langword="null"/> for a new, empty repository.</param>
    public RepoWriter(Did did, SigningKey key, IWritableBlockStore blocks, SignedCommit? head = null)
    {
        ArgumentNullException.ThrowIfNull(did);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(blocks);
        if (head is not null && head.Commit.Did != did) throw new ArgumentException($"The head is a commit of {head.Commit.Did}, not {did}.", nameof(head));
        (_did, _key, _blocks, Head) = (did, key, blocks, head);
        Tree = head is null ? MerkleSearchTree.Empty() : MerkleSearchTree.Load(head.Commit.Data, blocks);
    }

    /// <summary>The latest commit, or <see langword="null"/> before the first.</summary>
    public SignedCommit? Head { get; private set; }

    /// <summary>The tree of <see cref="Head"/>.</summary>
    public MerkleSearchTree Tree { get; private set; }

    /// <summary>Applies <paramref name="writes"/> and signs the result as one commit.</summary>
    /// <param name="writes">The changes; each key at most once. Deleting an absent record is an error.</param>
    /// <param name="rev">The commit's revision, after the head's.</param>
    /// <returns>The commit, its operations and its blocks.</returns>
    /// <exception cref="ArgumentException">A key repeats, a record is not a DRISL map, or <paramref name="rev"/> is not after the head's.</exception>
    /// <exception cref="InvalidOperationException">A delete names an absent record.</exception>
    public RepoWriteResult Commit(IEnumerable<RecordWrite> writes, Tid rev)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (Head is not null && rev.CompareTo(Head.Commit.Rev) <= 0) throw new ArgumentException($"The revision {rev} is not after the head's {Head.Commit.Rev}.", nameof(rev));

        var tree = Tree;
        var diff = new MemoryBlockStore();
        List<RepoOperation> operations = [];
        HashSet<MstKey> keys = [];
        foreach (var (key, record) in writes)
        {
            if (!keys.Add(key)) throw new ArgumentException($"The key '{key}' is written twice.", nameof(writes));
            Cid? cid = null;
            if (record is { } block)
            {
                if (!IsMap(block)) throw new ArgumentException($"The record '{key}' is not a DRISL map.", nameof(writes));
                cid = Cid.Compute(CidCodec.DagCbor, block.Span);
                diff.Put(cid, block);
            }
            tree = tree.Apply(key, cid, out var previous);
            if (cid is null && previous is null) throw new InvalidOperationException($"The record '{key}' to delete does not exist.");
            if (previous != cid) operations.Add(new(key, previous, cid));
        }

        var commit = new RepoCommit { Did = _did, Rev = rev, Data = tree.Root }.Sign(_key);
        diff.Put(commit.Cid, commit.Block);
        tree.WriteDiffBlocks(Tree, diff);
        foreach (var block in diff.Blocks) _blocks.Put(block.Cid, block.Data);
        (Head, Tree) = (commit, tree);
        return new(commit, operations, diff);
    }

    private static bool IsMap(ReadOnlyMemory<byte> block)
    {
        try
        {
            return DataModelValue.FromDrisl(block) is DataModelMap;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
