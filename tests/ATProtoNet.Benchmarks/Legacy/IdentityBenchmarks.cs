using System.Net;
using System.Text;
using ATProtoNet.Identity;
using BenchmarkDotNet.Attributes;

namespace ATProtoNet.Benchmarks;

/// <summary>Resolution with the DID document and the handle both cached; no network is reachable.</summary>
[MemoryDiagnoser]
public class IdentityBenchmarks
{
    private const string HandleName = "user1.example.com";

    private readonly Did _did = Did.Parse(Corpus.Did(1));
    private InMemoryDidResolver _inner = null!;
    private HandleHost _host = null!;
    private HttpClient _http = null!;
    private HandleResolver _handles = null!;
    private CachingDidResolver _documents = null!;
    private IdentityResolver _resolver = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _http = new HttpClient(_host = new HandleHost(_did.Value));
        _handles = new HandleResolver(_http, new IdentityResolverOptions { DnsOverHttpsUrl = null });
        _inner = new InMemoryDidResolver(new Dictionary<Did, DidDocument>
        {
            [_did] = FirehoseBenchmarks.Document(_did.Value, "https://pds.example.com", handle: HandleName),
        });
        _documents = new CachingDidResolver(_inner);
        _resolver = new IdentityResolver(_documents, _handles);
        await _resolver.ResolveAsync(Handle.Parse(HandleName));
        await _resolver.ResolveAsync(_did);

        var (fetches, lookups) = (_inner.Resolves, _host.Requests);
        await ResolveHandle();
        await ResolveDid();
        if ((_inner.Resolves, _host.Requests) != (fetches, lookups))
            throw new InvalidOperationException("The resolver went past its caches.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _documents.Dispose();
        _handles.Dispose();
        _http.Dispose();
    }

    [Benchmark]
    public Task<ResolvedIdentity> ResolveHandle() => _resolver.ResolveAsync(Handle.Parse(HandleName));

    [Benchmark]
    public Task<ResolvedIdentity> ResolveDid() => _resolver.ResolveAsync(_did);

    private sealed class HandleHost(string did) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(did, Encoding.UTF8, "text/plain") });
        }
    }
}
