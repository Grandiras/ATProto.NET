using ATProtoNet.Repo;
using BenchmarkDotNet.Attributes;

namespace ATProtoNet.Benchmarks;

/// <summary>The MST of a repository of <see cref="Corpus.RepoSize"/> entries: building it, and editing it as a commit does.</summary>
[MemoryDiagnoser]
public class MstBenchmarks
{
    private KeyValuePair<MstKey, Cid>[] _entries = [];
    private MerkleSearchTree _tree = null!, _base = null!, _edited = null!;
    private int _round;

    [GlobalSetup]
    public void Setup()
    {
        _entries = [.. Corpus.RepoEntries().Select(e => KeyValuePair.Create(MstKey.Parse(e.Path), Cid.FromBytes(e.Cid)))];
        _tree = _base = MerkleSearchTree.Build(_entries);
        _edited = Edit(_base, 0);
        if (_edited.Root == _base.Root) throw new InvalidOperationException("The edits changed nothing.");
    }

    [Benchmark]
    public Cid Build() => MerkleSearchTree.Build(_entries).Root;

    [Benchmark]
    public Cid UpdateAndRecomputeRoot()
    {
        _tree = Edit(_tree, ++_round);
        return _tree.Root;
    }

    /// <summary>The blocks of a 10-update commit: the new nodes and the covering proofs of the changed keys.</summary>
    [Benchmark]
    public int Diff()
    {
        var blocks = new MemoryBlockStore();
        _edited.WriteDiffBlocks(_base, blocks);
        return blocks.Count;
    }

    private MerkleSearchTree Edit(MerkleSearchTree tree, int round)
    {
        for (var i = 0; i < 10; i++)
            tree = tree.Update(_entries[i * 97].Key, Cid.FromBytes(Corpus.CidBytes(Corpus.RepoSize + round * 10 + i)));
        return tree;
    }
}
