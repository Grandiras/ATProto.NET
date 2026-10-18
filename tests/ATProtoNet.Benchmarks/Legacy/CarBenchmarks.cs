using System.Text.Json;
using ATProtoNet.Repo;
using BenchmarkDotNet.Attributes;

namespace ATProtoNet.Benchmarks;

/// <summary>The CAR export of a repository of <see cref="Corpus.RepoSize"/> mixed records.</summary>
[MemoryDiagnoser]
public class CarBenchmarks
{
    private byte[] _car = [];

    [GlobalSetup]
    public void Setup()
    {
        var entries = Corpus.RepoEntries();
        var tree = MerkleSearchTree.Create(entries.Select(e => KeyValuePair.Create(e.Path, e.Cid)));
        var (root, nodes) = tree.Serialize();
        var records = entries.Select((e, i) =>
        {
            var bytes = DagCborEncoder.Encode(JsonDocument.Parse(Corpus.PostRecordJson(i)).RootElement);
            return new CarBlock(CidComputation.ComputeBinaryForDagCbor(bytes), bytes);
        });
        _car = CarWriter.Write(root, nodes.Select(n => new CarBlock(CidComputation.DecodeCidString(n.Key), n.Value)).Concat(records));
        if (ReadVerifyingCids().Blocks.Count < Corpus.RepoSize)
            throw new InvalidOperationException("The CAR lost blocks.");
    }

    [Benchmark]
    public CarReader Read() => CarReader.FromBytes(_car);

    [Benchmark]
    public CarReader ReadVerifyingCids() => CarReader.FromBytes(_car, verifyBlockCids: true);
}
