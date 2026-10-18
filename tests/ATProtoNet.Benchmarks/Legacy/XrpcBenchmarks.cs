using System.Net;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using BenchmarkDotNet.Attributes;

namespace ATProtoNet.Benchmarks;

/// <summary>Whole requests through <see cref="AtProtoClient"/> against a handler in the process: no sockets.</summary>
[MemoryDiagnoser]
public class XrpcBenchmarks
{
    private static readonly AtIdentifier Actor = AtIdentifier.Parse("user1.example.com");

    private HttpClient _http = null!;
    private AtProtoClient _client = null!;

    [GlobalSetup]
    public void Setup()
    {
        var profile = Corpus.ProfileJson();
        var timeline = Corpus.TimelineJson();
        _http = new HttpClient(new InProcessHandler(request => request.RequestUri!.AbsolutePath.EndsWith("getTimeline", StringComparison.Ordinal) ? timeline : profile));
        _client = new AtProtoClient(new AtProtoClientOptions { InstanceUrl = "https://pds.example.com" }, _http);
        if (QuerySmall().GetAwaiter().GetResult().Handle.Value != "user1.example.com" || QueryLarge().GetAwaiter().GetResult().Feed.Count != Corpus.TimelinePosts)
            throw new InvalidOperationException("The client did not decode the canned responses.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _http.Dispose();
    }

    [Benchmark]
    public Task<ProfileViewDetailed> QuerySmall() => _client.Bsky.Actor.GetProfileAsync(Actor);

    [Benchmark]
    public Task<FeedResponse> QueryLarge() => _client.Bsky.Feed.GetTimelineAsync(limit: Corpus.TimelinePosts);

    private sealed class InProcessHandler(Func<HttpRequestMessage, byte[]> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(body(request));
            content.Headers.ContentType = new("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
