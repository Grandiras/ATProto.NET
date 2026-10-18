using System.Text.Json;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Serialization;
using BenchmarkDotNet.Attributes;

namespace ATProtoNet.Benchmarks;

[MemoryDiagnoser]
public class JsonModelBenchmarks
{
    private byte[] _timeline = [];

    [GlobalSetup]
    public void Setup()
    {
        _timeline = Corpus.TimelineJson();
        if (DeserializeTimeline().Feed.Count != Corpus.TimelinePosts)
            throw new InvalidOperationException("The timeline lost posts.");
    }

    /// <summary>A getTimeline response of <see cref="Corpus.TimelinePosts"/> posts.</summary>
    [Benchmark]
    public FeedResponse DeserializeTimeline() => JsonSerializer.Deserialize<FeedResponse>(_timeline, AtProtoJsonDefaults.Options)!;
}
