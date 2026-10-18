using ATProtoNet.Repo;
using BenchmarkDotNet.Attributes;

namespace ATProtoNet.Benchmarks;

/// <summary>0.8 has no public MST diff, so the update scenario recomputes the root after changing entries.</summary>
[MemoryDiagnoser]
public class MstBenchmarks
{
    private KeyValuePair<string, byte[]>[] _entries = [];
    private MerkleSearchTree _tree = null!;
    private int _round;

    [GlobalSetup]
    public void Setup()
    {
        _entries = [.. Corpus.RepoEntries().Select(e => KeyValuePair.Create(e.Path, e.Cid))];
        _tree = MerkleSearchTree.Create(_entries);
    }

    [Benchmark]
    public byte[] Build() => MerkleSearchTree.Create(_entries).ComputeRootCid();

    [Benchmark]
    public byte[] UpdateAndRecomputeRoot()
    {
        var round = ++_round;
        for (var i = 0; i < 10; i++)
            _tree.Update(_entries[i * 97].Key, Corpus.CidBytes(Corpus.RepoSize + round * 10 + i));
        return _tree.ComputeRootCid();
    }
}
