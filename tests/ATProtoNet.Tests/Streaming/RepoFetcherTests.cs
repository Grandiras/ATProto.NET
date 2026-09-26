using System.Net;
using System.Net.Http.Headers;
using ATProtoNet.Identity;
using ATProtoNet.Streaming;
using ATProtoNet.Tests.Identity;

namespace ATProtoNet.Tests.Streaming;

/// <summary>Fetching repository exports for a resync: relay first, redirects, limits.</summary>
public sealed class RepoFetcherTests
{
    private static readonly Did Repo = Did.Parse("did:plc:synctestrepoaaaaaaaaaaaa");
    private static readonly byte[] Car = [0x0a, 0x0b, 0x0c];

    private static HttpResponseMessage CarResponse(byte[] body, long? declared = null)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.ipld.car");
        if (declared is { } length)
            content.Headers.ContentLength = length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location) }, Content = new StringContent("") };

    [Fact]
    public async Task HostRepoFetcher_AsksGetRepoForTheDid()
    {
        var handler = new ScriptedHandler(_ => CarResponse(Car));
        var fetcher = new HostRepoFetcher(new Uri("https://bsky.network"), new HttpClient(handler));

        var car = await fetcher.FetchAsync(Repo, 1024);

        Assert.Equal(Car, car);
        Assert.Equal(
            "https://bsky.network/xrpc/com.atproto.sync.getRepo?did=did%3Aplc%3Asynctestrepoaaaaaaaaaaaa",
            Assert.Single(handler.Requests).AbsoluteUri);
    }

    [Fact]
    public async Task HostRepoFetcher_RelayRedirect_IsFollowedToThePds()
    {
        var handler = new ScriptedHandler(request => request.RequestUri!.Host == "bsky.network"
            ? Redirect("https://pds.example.com/xrpc/com.atproto.sync.getRepo?did=" + Repo)
            : CarResponse(Car));
        var fetcher = new HostRepoFetcher(new Uri("https://bsky.network"), new HttpClient(handler));

        Assert.Equal(Car, await fetcher.FetchAsync(Repo, 1024));
        Assert.Equal(["bsky.network", "pds.example.com"], handler.Requests.Select(r => r.Host));
    }

    [Fact]
    public async Task HostRepoFetcher_EndlessRedirects_Throw()
    {
        var handler = new ScriptedHandler(_ => Redirect("https://bsky.network/xrpc/com.atproto.sync.getRepo"));
        var fetcher = new HostRepoFetcher(new Uri("https://bsky.network"), new HttpClient(handler));

        await Assert.ThrowsAsync<RepoFetchException>(() => fetcher.FetchAsync(Repo, 1024));
        Assert.Equal(4, handler.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task HostRepoFetcher_HostWithoutTheRepository_ReturnsNull(HttpStatusCode status)
    {
        var fetcher = new HostRepoFetcher(new Uri("https://bsky.network"), new HttpClient(new ScriptedHandler(_ => ScriptedHandler.Status(status))));

        Assert.Null(await fetcher.FetchAsync(Repo, 1024));
    }

    [Fact]
    public async Task HostRepoFetcher_ServerError_Throws()
    {
        var fetcher = new HostRepoFetcher(new Uri("https://bsky.network"),
            new HttpClient(new ScriptedHandler(_ => ScriptedHandler.Status(HttpStatusCode.BadGateway))));

        var ex = await Assert.ThrowsAsync<RepoFetchException>(() => fetcher.FetchAsync(Repo, 1024));
        Assert.Contains("502", ex.Message);
    }

    [Fact]
    public async Task HostRepoFetcher_DeclaredLengthOverTheLimit_Throws()
    {
        var fetcher = new HostRepoFetcher(new Uri("https://bsky.network"),
            new HttpClient(new ScriptedHandler(_ => CarResponse(new byte[100], declared: 100))));

        await Assert.ThrowsAsync<RepoFetchException>(() => fetcher.FetchAsync(Repo, 99));
    }

    [Fact]
    public async Task HostRepoFetcher_UndeclaredBodyOverTheLimit_Throws()
    {
        var content = new StreamContent(new MemoryStream(new byte[200_000]));
        var fetcher = new HostRepoFetcher(new Uri("https://bsky.network"),
            new HttpClient(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content })));

        await Assert.ThrowsAsync<RepoFetchException>(() => fetcher.FetchAsync(Repo, 100_000));
    }

    [Fact]
    public async Task HostRepoFetcher_BodyTricklingPastTheTimeout_Throws()
    {
        // The headers arrive at once; the body then trickles for five seconds.
        var client = new HttpClient(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new TricklingStream(bytes: 50, delay: TimeSpan.FromMilliseconds(100))),
        }))
        {
            Timeout = TimeSpan.FromMilliseconds(300),
        };
        var fetcher = new HostRepoFetcher(new Uri("https://bsky.network"), client);

        var started = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<RepoFetchException>(() => fetcher.FetchAsync(Repo, 1024));

        Assert.True(started.Elapsed < TimeSpan.FromSeconds(3), $"The download ran for {started.Elapsed}.");
        Assert.Contains("longer than", ex.Message);
    }

    [Fact]
    public async Task HostRepoFetcher_CallerCancels_IsCancellationNotAFailure()
    {
        var client = new HttpClient(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new TricklingStream(bytes: 50, delay: TimeSpan.FromMilliseconds(100))),
        }))
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var fetcher = new HostRepoFetcher(new Uri("https://bsky.network"), client);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.FetchAsync(Repo, 1024, cts.Token));
    }

    [Fact]
    public async Task HostRepoFetcher_HugeDeclaredLength_DoesNotReserveIt()
    {
        // A length past 2 GiB used to be cast to an int buffer size and throw; any claim is now
        // only believed as the bytes arrive.
        var content = new ByteArrayContent(Car);
        content.Headers.ContentLength = 3L * 1024 * 1024 * 1024;
        var fetcher = new HostRepoFetcher(new Uri("https://bsky.network"),
            new HttpClient(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content })));

        Assert.Equal(Car, await fetcher.FetchAsync(Repo, 4L * 1024 * 1024 * 1024));
    }

    /// <summary>A body that yields one byte per <paramref name="delay"/>.</summary>
    private sealed class TricklingStream(int bytes, TimeSpan delay) : Stream
    {
        private int _left = bytes;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_left == 0 || buffer.Length == 0)
                return 0;

            await Task.Delay(delay, cancellationToken);
            buffer.Span[0] = 0x2a;
            _left--;
            return 1;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task HostRepoFetcher_DefaultClient_RefusesPlainHttp()
    {
        var fetcher = new HostRepoFetcher(new Uri("http://relay.example.com"));

        var ex = await Assert.ThrowsAsync<RepoFetchException>(() => fetcher.FetchAsync(Repo, 1024));
        Assert.Contains("http", ex.Message);
    }

    [Fact]
    public async Task PdsRepoFetcher_AsksThePdsTheDidDocumentNames()
    {
        var resolver = new StubDidResolver().Add(Repo, "did:key:zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w", "https://pds.example.com");
        var handler = new ScriptedHandler(_ => CarResponse(Car));
        var fetcher = new PdsRepoFetcher(resolver, new HttpClient(handler));

        Assert.Equal(Car, await fetcher.FetchAsync(Repo, 1024));
        Assert.Equal("pds.example.com", Assert.Single(handler.Requests).Host);
    }

    [Fact]
    public async Task PdsRepoFetcher_NoPds_Throws()
    {
        var resolver = new StubDidResolver().Add(Repo, "did:key:zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w", pds: null);
        var fetcher = new PdsRepoFetcher(resolver, new HttpClient(new ScriptedHandler(_ => CarResponse(Car))));

        await Assert.ThrowsAsync<RepoFetchException>(() => fetcher.FetchAsync(Repo, 1024));
    }

    [Fact]
    public async Task PdsRepoFetcher_UnresolvableDid_Throws()
    {
        var fetcher = new PdsRepoFetcher(new StubDidResolver(), new HttpClient(new ScriptedHandler(_ => CarResponse(Car))));

        await Assert.ThrowsAsync<RepoFetchException>(() => fetcher.FetchAsync(Repo, 1024));
    }
}
