using System.Net;
using System.Text;
using ATProtoNet.Identity;
using ATProtoNet.Net;
using BenchmarkDotNet.Attributes;

namespace ATProtoNet.Benchmarks;

/// <summary>Resolution with the DID document and the handle both cached; no network is reachable.</summary>
[MemoryDiagnoser]
public class IdentityBenchmarks
{
    private const string HandleName = "user1.example.com";

    private readonly Did _did = Did.Parse(Corpus.Did(1));
    private readonly Handle _handle = Handle.Parse(HandleName);
    private InMemoryDidResolver _inner = null!;
    private HandleHost _host = null!;
    private UntrustedHttp _http = null!;
    private IdentityResolver _resolver = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _http = new UntrustedHttp(handler: _host = new HandleHost(_did.Value));
        _inner = new InMemoryDidResolver(DidDocument.Parse(Encoding.UTF8.GetBytes(
            $$"""{"id":"{{_did}}","alsoKnownAs":["at://{{HandleName}}"],"service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://pds.example.com"}]}""")));
        _resolver = new IdentityResolver(new CachingDidResolver(_inner), new HandleResolver(_http, dns: null));
        await ResolveHandle();
        await ResolveDid();

        var (fetches, lookups) = (_inner.Resolves, _host.Requests);
        await ResolveHandle();
        await ResolveDid();
        if ((_inner.Resolves, _host.Requests) != (fetches, lookups))
            throw new InvalidOperationException("The resolver went past its caches.");
    }

    [GlobalCleanup]
    public void Cleanup() => _http.Dispose();

    [Benchmark]
    public Task<ResolvedIdentity> ResolveHandle() => _resolver.ResolveAsync(_handle);

    [Benchmark]
    public Task<ResolvedIdentity> ResolveDid() => _resolver.ResolveAsync(_did);

    private sealed class InMemoryDidResolver(DidDocument document) : IDidResolver
    {
        public int Resolves { get; private set; }

        public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default)
        {
            Resolves++;
            return Task.FromResult(document);
        }
    }

    private sealed class HandleHost(string did) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(did, Encoding.UTF8, "text/plain"), RequestMessage = request });
        }
    }
}
