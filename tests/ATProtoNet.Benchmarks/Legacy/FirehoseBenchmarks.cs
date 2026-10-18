using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Serialization;
using ATProtoNet.Streaming;
using BenchmarkDotNet.Attributes;
using System.Text.Json;

namespace ATProtoNet.Benchmarks;

/// <summary>The six captured frames of the conformance vectors; each operation is one frame.</summary>
[MemoryDiagnoser]
public class FirehoseBenchmarks
{
    private byte[][] _frames = [];
    private FirehoseMessage[] _messages = [];
    private InMemoryDidResolver _resolver = null!;

    [GlobalSetup]
    public void Setup()
    {
        var vectors = FirehoseFrames.Load();
        _frames = [.. vectors.Frames.Select(f => f.Frame)];
        _messages = [.. _frames.Select(f => FirehoseEventParser.Parse(f)!)];
        _resolver = new(vectors.SigningKeys.ToDictionary(
            k => Did.Parse(k.Key),
            k => Document(k.Key, vectors.Pds.GetValueOrDefault(k.Key) ?? "https://pds.example.com", k.Value)));
    }

    [Benchmark(OperationsPerInvoke = 6)]
    public int Decode()
    {
        var count = 0;
        foreach (var frame in _frames)
            count += FirehoseEventParser.Parse(frame) is null ? 0 : 1;
        return count;
    }

    /// <summary>Decode, then verify each commit or sync event: signature, inductive proof and chain, against fresh state.</summary>
    [Benchmark(OperationsPerInvoke = 6)]
    public async Task<int> DecodeAndVerify()
    {
        using var verifier = new RepoSyncVerifier(new RepoSyncVerifierOptions { DidResolver = _resolver });
        var valid = 0;
        foreach (var frame in _frames)
        {
            var result = FirehoseEventParser.Parse(frame) switch
            {
                CommitEvent commit => await verifier.VerifyCommitAsync(commit),
                SyncEvent sync => await verifier.VerifySyncAsync(sync),
                _ => throw new InvalidOperationException("Not a repository event."),
            };
            await verifier.ApplyAsync(result);
            valid += result.IsValid ? 1 : 0;
        }
        return valid == _frames.Length ? valid : throw new InvalidOperationException($"{valid} of {_frames.Length} frames verified.");
    }

    internal static DidDocument Document(string did, string pds, string? signingKey = null, string? handle = null) =>
        JsonSerializer.Deserialize<DidDocument>(
            Corpus.DidDocumentJson(did, handle ?? "user.example.com", pds, signingKey?["did:key:".Length..]), AtProtoJsonDefaults.Options)!;
}
