using ATProtoNet.Streaming;
using BenchmarkDotNet.Attributes;

namespace ATProtoNet.Benchmarks;

/// <summary>Decodes v1 frames; each operation is one frame. The captured v2 frames of the conformance vectors are not covered.</summary>
[MemoryDiagnoser]
public class JetstreamBenchmarks
{
    private ReadOnlyMemory<byte>[] _frames = [];

    [GlobalSetup]
    public void Setup()
    {
        _frames = [.. Corpus.JetstreamFrames().Select(f => (ReadOnlyMemory<byte>)f)];
        if (Decode() != _frames.Length)
            throw new InvalidOperationException("The corpus holds a frame the parser drops.");
    }

    [Benchmark(OperationsPerInvoke = Corpus.JetstreamFrameCount)]
    public int Decode()
    {
        var decoded = 0;
        foreach (var frame in _frames)
            decoded += JetstreamEventParser.ParseFrame(frame, JetstreamProtocol.V1).Event is null ? 0 : 1;
        return decoded;
    }
}
