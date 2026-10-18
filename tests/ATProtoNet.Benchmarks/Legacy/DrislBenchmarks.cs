using System.Text.Json;
using ATProtoNet.Repo;
using BenchmarkDotNet.Attributes;

namespace ATProtoNet.Benchmarks;

[MemoryDiagnoser]
public class DrislBenchmarks
{
    private JsonElement _document;
    private byte[] _cbor = [];

    [GlobalSetup]
    public void Setup()
    {
        _document = JsonDocument.Parse(Corpus.RecordBatchJson()).RootElement.Clone();
        _cbor = DagCborEncoder.Encode(_document);
        if (!Decode().GetProperty("records").GetArrayLength().Equals(64))
            throw new InvalidOperationException("The batch did not round-trip.");
    }

    [Benchmark]
    public byte[] Encode() => DagCborEncoder.Encode(_document);

    [Benchmark]
    public JsonElement Decode() => DagCborDecoder.Decode(_cbor);
}
