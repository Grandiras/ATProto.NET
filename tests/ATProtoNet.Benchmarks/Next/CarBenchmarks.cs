using System.Text;
using ATProtoNet.Data;
using ATProtoNet.Data.Car;
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
        var tree = MerkleSearchTree.Build(entries.Select(e => KeyValuePair.Create(MstKey.Parse(e.Path), Cid.FromBytes(e.Cid))));
        var nodes = new MemoryBlockStore();
        tree.WriteBlocks(nodes);
        var records = entries.Select((e, i) =>
        {
            var bytes = DrislJson.ToDrisl(Encoding.UTF8.GetBytes(Corpus.PostRecordJson(i)));
            return new CarBlock(Cid.Compute(CidCodec.DagCbor, bytes), bytes);
        });
        var car = new System.Buffers.ArrayBufferWriter<byte>();
        CarWriter.Write(car, [tree.Root], nodes.Blocks.Concat(records));
        _car = car.WrittenSpan.ToArray();
        if (ReadVerifyingCids().Blocks.Count < Corpus.RepoSize)
            throw new InvalidOperationException("The CAR lost blocks.");
    }

    [Benchmark]
    public CarFile Read() => CarReader.Read(_car);

    [Benchmark]
    public CarFile ReadVerifyingCids() => CarReader.Read(_car, new() { VerifyBlockCids = true });
}
