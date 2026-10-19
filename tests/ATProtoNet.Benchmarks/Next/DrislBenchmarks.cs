using System.Buffers;
using System.Text;
using System.Text.Json;
using ATProtoNet.Data;
using BenchmarkDotNet.Attributes;

namespace ATProtoNet.Benchmarks;

[MemoryDiagnoser]
public class DrislBenchmarks
{
    private readonly ArrayBufferWriter<byte> _json = new(64 * 1024);
    private byte[] _document = [];
    private byte[] _cbor = [];
    private Utf8JsonWriter _writer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _document = Encoding.UTF8.GetBytes(Corpus.RecordBatchJson());
        _cbor = Encode();
        _writer = new Utf8JsonWriter(_json);
        Decode();
        if (JsonDocument.Parse(_json.WrittenMemory).RootElement.GetProperty("records").GetArrayLength() != 64)
            throw new InvalidOperationException("The batch did not round-trip.");
    }

    [Benchmark]
    public byte[] Encode() => DrislJson.ToDrisl(_document);

    [Benchmark]
    public int Decode()
    {
        _json.ResetWrittenCount();
        _writer.Reset();
        DrislJson.WriteJson(_cbor, _writer);
        _writer.Flush();
        return _json.WrittenCount;
    }
}
