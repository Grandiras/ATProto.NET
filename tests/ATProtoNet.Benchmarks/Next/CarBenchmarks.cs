using System.Text;
using ATProtoNet.Data;
using ATProtoNet.Data.Car;
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
        var (root, nodes) = MstNodes.Build(entries.Select(e => (e.Path, Cid.FromBytes(e.Cid))));
        var records = entries.Select((e, i) =>
        {
            var bytes = DrislJson.ToDrisl(Encoding.UTF8.GetBytes(Corpus.PostRecordJson(i)));
            return new CarBlock(Cid.Compute(CidCodec.DagCbor, bytes), bytes);
        });
        var car = new System.Buffers.ArrayBufferWriter<byte>();
        CarWriter.Write(car, [root], nodes.Concat(records));
        _car = car.WrittenSpan.ToArray();
        if (ReadVerifyingCids().Blocks.Count < Corpus.RepoSize)
            throw new InvalidOperationException("The CAR lost blocks.");
    }

    [Benchmark]
    public CarFile Read() => CarReader.Read(_car);

    [Benchmark]
    public CarFile ReadVerifyingCids() => CarReader.Read(_car, new() { VerifyBlockCids = true });
}
