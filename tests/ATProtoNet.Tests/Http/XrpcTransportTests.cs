using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Http;

/// <summary>
/// The transport's behaviour under shared use: absolute URIs instead of a mutated
/// <see cref="HttpClient.BaseAddress"/>, per-request headers, per-call options, streaming
/// reads, uploads that respect the caller's stream, and failures mapped to typed exceptions.
/// </summary>
public class XrpcTransportTests : IDisposable
{
    private readonly RecordingHandler _handler = new();
    private readonly HttpClient _httpClient;

    public XrpcTransportTests()
    {
        _httpClient = new HttpClient(_handler);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    private AtProtoClient CreateClient(string instanceUrl = "https://pds.example.com", Action<AtProtoClientOptions>? configure = null)
    {
        var options = new AtProtoClientOptions { InstanceUrl = instanceUrl, AutoRefreshSession = false };
        configure?.Invoke(options);
        return new AtProtoClient(options, _httpClient, null, null);
    }

    // ──────────────────────────────────────────────────────────
    //  Service URL: never through HttpClient.BaseAddress
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task InstanceUrl_WinsOverASuppliedClientsBaseAddress()
    {
        using var httpClient = new HttpClient(_handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://elsewhere.example.com/"),
        };
        using var client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            httpClient, null, null);

        await client.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));

        Assert.Equal("https://pds.example.com/xrpc/com.example.ping", _handler.Single().Uri);
        Assert.Equal(new Uri("https://elsewhere.example.com/"), httpClient.BaseAddress);
    }

    [Fact]
    public async Task SetServiceUrl_AfterARequestOnASharedClient_TakesEffect()
    {
        // HttpClient.BaseAddress throws once a client has sent a request; the service URL is
        // now the transport's own, so switching PDS (or re-applying an OAuth session) works.
        using var first = CreateClient();
        using var second = CreateClient("https://other.example.com");

        await first.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));
        first.SetServiceUrl(new Uri("https://pds2.example.com"));
        await first.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));
        await second.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));

        Assert.Equal(
            new[]
            {
                "https://pds.example.com/xrpc/com.example.ping",
                "https://pds2.example.com/xrpc/com.example.ping",
                "https://other.example.com/xrpc/com.example.ping",
            },
            _handler.Requests.Select(r => r.Uri));
        Assert.Null(_httpClient.BaseAddress);
    }

    [Fact]
    public async Task ApplySessionAsync_AfterARequest_PointsAtTheSessionsPds()
    {
        using var client = CreateClient("https://entryway.example.com");
        await client.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));

        using var key = new DPoPProofGenerator();
        await client.ApplySessionAsync(new OAuthSession
        {
            Did = Did.Parse("did:plc:alice"),
            Handle = Handle.Parse("alice.example.com"),
            ServiceEndpoint = new Uri("https://pds.alice.example.com"),
            AccessToken = "access",
            RefreshToken = "refresh",
            DPoPKey = key.ExportPrivateKey(),
            Issuer = "https://entryway.example.com",
            TokenEndpoint = new Uri("https://entryway.example.com/oauth/token"),
        });
        await client.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));

        Assert.Equal(new Uri("https://pds.alice.example.com/"), client.ServiceUrl);
        Assert.Equal("https://pds.alice.example.com/xrpc/com.example.ping", _handler.Requests[^1].Uri);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://pds.example.com")]
    [InlineData("https://pds.example.com/base?x=1")]
    [InlineData("https://pds.example.com/#frag")]
    public void Constructor_WithAnInvalidInstanceUrl_Throws(string url)
    {
        Assert.Throws<ArgumentException>(() => CreateClient(url));
    }

    [Theory]
    [InlineData("https://pds.example.com/base?x=1")]
    [InlineData("https://pds.example.com/#frag")]
    public void SetServiceUrl_WithAQueryOrFragment_Throws(string url)
    {
        using var client = CreateClient();

        var ex = Assert.Throws<ArgumentException>(() => client.SetServiceUrl(new Uri(url)));

        Assert.Equal("url", ex.ParamName);
        Assert.Equal(new Uri("https://pds.example.com/"), client.ServiceUrl);
    }

    [Fact]
    public void Constructor_TrustsAConfiguredPlainHttpHost()
    {
        // A container network (http://pds:3000) is configuration, not attacker input.
        using var client = CreateClient("http://pds:3000");

        Assert.Equal(new Uri("http://pds:3000/"), client.ServiceUrl);
    }

    // ──────────────────────────────────────────────────────────
    //  User-Agent: per request, never appended to shared headers
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ManyClientsOnOneHttpClient_SendOneUserAgentEachAndLeaveTheClientUntouched()
    {
        var clients = Enumerable.Range(0, 100).Select(_ => CreateClient()).ToList();
        try
        {
            await clients[^1].QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));

            // Before: 100 product tokens accumulated here, a 1,899-character header.
            Assert.Empty(_httpClient.DefaultRequestHeaders.UserAgent);
            Assert.Equal(AtProtoHttp.DefaultUserAgent, _handler.Single().UserAgent);
            Assert.StartsWith("ATProtoNet/", AtProtoHttp.DefaultUserAgent);
        }
        finally
        {
            clients.ForEach(c => c.Dispose());
        }
    }

    [Fact]
    public async Task UserAgent_IsConfigurablePerClient()
    {
        using var mine = CreateClient(configure: o => o.UserAgent = "MyApp/2.1 (+https://myapp.example)");
        using var theirs = CreateClient(configure: o => o.UserAgent = "OtherApp/1.0");

        await mine.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));
        await theirs.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));

        Assert.Equal(
            new[] { "MyApp/2.1 (+https://myapp.example)", "OtherApp/1.0" },
            _handler.Requests.Select(r => r.UserAgent));
    }

    [Fact]
    public async Task UserAgent_Null_LeavesTheHttpClientsDefault()
    {
        using var httpClient = new HttpClient(_handler, disposeHandler: false);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Host/1.0");
        using var client = new AtProtoClient(
            new AtProtoClientOptions { UserAgent = null, AutoRefreshSession = false }, httpClient, null, null);

        await client.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));

        Assert.Equal("Host/1.0", _handler.Single().UserAgent);
    }

    // ──────────────────────────────────────────────────────────
    //  Proxy and labeler headers: with or without a session
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ClientWideProxyAndLabelers_AreSentOnUnauthenticatedCalls()
    {
        using var client = CreateClient();
        client.SetProxy(ServiceProxy.BskyAppViewHeader);
        client.SetLabelers("did:plc:labeler1", "did:plc:labeler2;redact");

        await client.QueryAsync<JsonElement>(Nsid.Parse("app.bsky.feed.getPostThread"));

        var request = _handler.Single();
        Assert.Null(request.Authorization);
        Assert.Equal(ServiceProxy.BskyAppViewHeader, request.Header("atproto-proxy"));
        Assert.Equal("did:plc:labeler1, did:plc:labeler2;redact", request.Header("atproto-accept-labelers"));
    }

    [Fact]
    public async Task CallOptions_OverrideTheClientDefaultsForThatCallOnly()
    {
        using var client = CreateClient();
        client.SetProxy(ServiceProxy.BskyAppViewHeader);
        client.SetLabelers("did:plc:default");

        await client.QueryAsync<JsonElement>(
            Nsid.Parse("com.atproto.label.queryLabels"),
            options: new XrpcCallOptions { Proxy = "did:plc:labeler#atproto_labeler", AcceptLabelers = [] });
        await client.QueryAsync<JsonElement>(Nsid.Parse("app.bsky.feed.getTimeline"));

        Assert.Equal("did:plc:labeler#atproto_labeler", _handler.Requests[0].Header("atproto-proxy"));
        Assert.Null(_handler.Requests[0].Header("atproto-accept-labelers"));
        Assert.Equal(ServiceProxy.BskyAppViewHeader, _handler.Requests[1].Header("atproto-proxy"));
        Assert.Equal("did:plc:default", _handler.Requests[1].Header("atproto-accept-labelers"));
    }

    [Fact]
    public async Task ConcurrentCallsWithDifferentOptions_DoNotSeeEachOthersHeaders()
    {
        using var client = CreateClient();

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => client.QueryAsync<JsonElement>(
            Nsid.Parse("com.example.ping"),
            new { i },
            new XrpcCallOptions { Proxy = $"did:web:svc{i}.example#svc" })));

        Assert.All(_handler.Requests, r =>
        {
            var i = r.Uri[(r.Uri.LastIndexOf('=') + 1)..];
            Assert.Equal($"did:web:svc{i}.example#svc", r.Header("atproto-proxy"));
        });
    }

    [Fact]
    public async Task SessionCalls_AreNotRoutedThroughTheDefaultProxy()
    {
        // The session calls address the account's own PDS. With an AppView proxy set as the
        // client default, sending the default on them would route sign-in to the AppView.
        using var client = CreateClient();
        client.SetProxy(ServiceProxy.BskyAppViewHeader);
        client.SetLabelers("did:plc:labeler");
        _handler.Respond(_ => Json("""{"did":"did:plc:alice","handle":"alice.test","accessJwt":"a","refreshJwt":"r"}"""));

        await client.LoginAsync("alice.test", "password");                     // createSession
        await client.RefreshSessionAsync();                                    // refreshSession
        await client.ResumeSessionAsync(client.Session!);                      // getSession
        await client.Server.CreateAccountAsync(new() { Handle = Handle.Parse("bob.test") }); // createAccount
        await client.QueryAsync<JsonElement>(Nsid.Parse("app.bsky.feed.getTimeline"));     // an ordinary call
        await client.LogoutAsync();                                            // deleteSession

        var sessionCalls = new[]
        {
            "com.atproto.server.createSession",
            "com.atproto.server.refreshSession",
            "com.atproto.server.getSession",
            "com.atproto.server.createAccount",
            "com.atproto.server.deleteSession",
        };
        var session = _handler.Requests.Where(r => sessionCalls.Any(r.Uri.EndsWith)).ToList();
        Assert.Equal(sessionCalls.Length, session.Count);
        Assert.All(session, r =>
        {
            Assert.Null(r.Header("atproto-proxy"));
            Assert.Null(r.Header("atproto-accept-labelers"));
        });

        // The defaults were in effect all along: the ordinary call carries both.
        var timeline = Assert.Single(_handler.Requests, r => r.Uri.EndsWith("app.bsky.feed.getTimeline"));
        Assert.Equal(ServiceProxy.BskyAppViewHeader, timeline.Header("atproto-proxy"));
        Assert.Equal("did:plc:labeler", timeline.Header("atproto-accept-labelers"));
    }

    [Fact]
    public async Task DirectCall_WithAnExplicitProxy_StillSendsIt()
    {
        // Precedence: a per-call Proxy always wins; IsDirect only suppresses the client default.
        var xrpc = new XrpcClient(_httpClient, new Uri("https://pds.example.com/"));
        xrpc.SetProxy(ServiceProxy.BskyAppViewHeader);

        await xrpc.QueryAsync<JsonElement>("com.example.direct", options: XrpcClient.Direct);
        await xrpc.QueryAsync<JsonElement>(
            "com.example.explicit", options: XrpcClient.Direct with { Proxy = "did:web:svc.example#svc" });

        Assert.Null(_handler.Requests[0].Header("atproto-proxy"));
        Assert.Equal("did:web:svc.example#svc", _handler.Requests[1].Header("atproto-proxy"));
    }

    [Fact]
    public async Task CallOptions_Headers_AreAddedToTheRequest()
    {
        using var client = CreateClient();

        await client.QueryAsync<JsonElement>(
            Nsid.Parse("com.example.ping"),
            options: new XrpcCallOptions
            {
                Headers = new Dictionary<string, string> { ["X-Trace"] = "abc", ["User-Agent"] = "Probe/1" },
            });

        Assert.Equal("abc", _handler.Single().Header("X-Trace"));
        Assert.Equal("Probe/1", _handler.Single().UserAgent);
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("dpop")]
    public async Task CallOptions_Headers_CannotReplaceTheSessionsCredentials(string header)
    {
        using var client = CreateClient();

        await Assert.ThrowsAsync<ArgumentException>(() => client.QueryAsync<JsonElement>(
            Nsid.Parse("com.example.ping"),
            options: new XrpcCallOptions { Headers = new Dictionary<string, string> { [header] = "x" } }));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task CallOptions_Timeout_ThrowsTimeoutException()
    {
        using var client = CreateClient();
        _handler.Delay = TimeSpan.FromSeconds(30);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => client.QueryAsync<JsonElement>(
            Nsid.Parse("com.example.slow"), options: new XrpcCallOptions { Timeout = TimeSpan.FromMilliseconds(50) }));

        Assert.Contains("com.example.slow", ex.Message);
    }

    [Fact]
    public async Task CallerCancellation_StaysAnOperationCanceledException()
    {
        using var client = CreateClient();
        _handler.Delay = TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.QueryAsync<JsonElement>(
            Nsid.Parse("com.example.slow"),
            options: new XrpcCallOptions { Timeout = TimeSpan.FromSeconds(20) },
            cancellationToken: cts.Token));
    }

    // ──────────────────────────────────────────────────────────
    //  Streaming reads
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_DeserializesFromTheStreamWithoutBufferingTheBody()
    {
        // ResponseContentRead would make HttpClient buffer the whole body first — for a
        // timeline-sized response, a large-object-heap array per call.
        var body = new TrackingContent(Encoding.UTF8.GetBytes("""{"feed":[],"cursor":"c"}"""));
        _handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        using var client = CreateClient();

        var result = await client.QueryAsync<JsonElement>(Nsid.Parse("app.bsky.feed.getTimeline"));

        Assert.Equal("c", result.GetProperty("cursor").GetString());
        Assert.True(body.StreamRead);
        Assert.False(body.Buffered);
    }

    [Fact]
    public async Task QueryAsync_WhenTheBodyIsNotTheExpectedType_ThrowsResponseFormatException()
    {
        _handler.Respond(_ => Json("""{"feed": "not-an-array"}"""));
        using var client = CreateClient();

        var ex = await Assert.ThrowsAsync<XrpcResponseFormatException>(
            () => client.QueryAsync<ATProtoNet.Lexicon.App.Bsky.Feed.FeedResponse>(Nsid.Parse("app.bsky.feed.getTimeline")));

        Assert.Equal("app.bsky.feed.getTimeline", ex.Nsid);
        Assert.IsType<JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task QueryAsync_WhenTheBodyIsEmpty_ThrowsResponseFormatException()
    {
        _handler.Respond(_ => Json(""));
        using var client = CreateClient();

        await Assert.ThrowsAsync<XrpcResponseFormatException>(
            () => client.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping")));
    }

    [Fact]
    public async Task QueryAsync_WhenTheServiceFails_ThrowsXrpcExceptionWithTheNsid()
    {
        _handler.Respond(_ => Json("""{"error":"RecordNotFound","message":"gone"}""", HttpStatusCode.BadRequest));
        using var client = CreateClient();

        var ex = await Assert.ThrowsAsync<XrpcException>(
            () => client.QueryAsync<JsonElement>(Nsid.Parse("com.atproto.repo.getRecord")));

        Assert.True(ex.Is(XrpcErrors.RecordNotFound));
        Assert.Equal("com.atproto.repo.getRecord", ex.Nsid);
        Assert.Equal("gone", ex.ErrorMessage);
    }

    [Fact]
    public async Task Download_ReturnsTheStreamWithItsMetadataAndReleasesTheResponse()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var content = new TrackingContent(bytes, "image/png");
        _handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var client = CreateClient();

        await using (var blob = await client.Sync.GetBlobAsync(Did.Parse("did:plc:alice"), Cid.Parse("bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy")))
        {
            Assert.Equal("image/png", blob.ContentType);
            Assert.Equal(4, blob.ContentLength);

            using var copy = new MemoryStream();
            await blob.Content.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }

        Assert.True(content.Disposed);
        Assert.Equal(
            "https://pds.example.com/xrpc/com.atproto.sync.getBlob?did=did%3Aplc%3Aalice&cid=bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy",
            _handler.Single().Uri);
    }

    [Fact]
    public async Task Download_WhenTheServiceFails_ThrowsAndReleasesTheResponse()
    {
        var content = new TrackingContent("""{"error":"BlobNotFound"}"""u8.ToArray());
        _handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = content });
        using var client = CreateClient();

        var ex = await Assert.ThrowsAsync<XrpcException>(() => client.Sync.GetBlobAsync(Did.Parse("did:plc:alice"), Cid.Parse("bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy")));

        Assert.True(ex.Is(XrpcErrors.BlobNotFound));
        Assert.True(content.Disposed);
    }

    // ──────────────────────────────────────────────────────────
    //  Uploads
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_SendsFromTheCallersPositionAndLeavesTheStreamOpen()
    {
        _handler.Respond(_ => Json("""{"blob":{"$type":"blob","ref":{"$link":"bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy"},"mimeType":"image/png","size":3}}"""));
        using var client = CreateClient();
        using var stream = new MemoryStream("HEADERbody"u8.ToArray());
        stream.Position = 6;

        await client.Repo.UploadBlobAsync(stream, "image/png");

        Assert.Equal("body", Encoding.UTF8.GetString(_handler.Single().Body!));
        Assert.Equal(4, _handler.Single().ContentLength);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task Upload_RetryRewindsToTheCallersPositionNotZero()
    {
        var calls = 0;
        _handler.Respond(_ => ++calls == 1
            ? RateLimited()
            : Json("""{"blob":{"$type":"blob","ref":{"$link":"bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy"},"mimeType":"image/png","size":4}}"""));
        using var client = CreateClient();
        using var stream = new MemoryStream("HEADERbody"u8.ToArray());
        stream.Position = 6;

        await client.Repo.UploadBlobAsync(stream, "image/png");

        Assert.Equal(new[] { "body", "body" }, _handler.Requests.Select(r => Encoding.UTF8.GetString(r.Body!)));
    }

    [Fact]
    public async Task Upload_OfANonSeekableStreamThatNeedsARetry_FailsClearly()
    {
        _handler.Respond(_ => RateLimited());
        using var client = CreateClient();
        await using var stream = new NonSeekableStream("body"u8.ToArray());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.Repo.UploadBlobAsync(stream, "image/png"));

        Assert.Contains("seekable", ex.Message);
        Assert.IsType<XrpcRateLimitException>(ex.InnerException);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Upload_OfANonSeekableStream_SucceedsWithoutARetry()
    {
        _handler.Respond(_ => Json("""{"blob":{"$type":"blob","ref":{"$link":"bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy"},"mimeType":"image/png","size":4}}"""));
        using var client = CreateClient();
        await using var stream = new NonSeekableStream("body"u8.ToArray());

        await client.Repo.UploadBlobAsync(stream, "image/png");

        Assert.Equal("body", Encoding.UTF8.GetString(_handler.Single().Body!));
    }

    // ──────────────────────────────────────────────────────────
    //  DPoP nonce
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DPoPNonceChallenge_IsRetriedOnceWithTheNonce()
    {
        using var dpop = new DPoPProofGenerator();
        var xrpc = new XrpcClient(_httpClient, new Uri("https://pds.example.com/"));
        xrpc.SetOAuthTokens("access", "refresh", dpop);

        var calls = 0;
        _handler.Respond(_ =>
        {
            if (++calls > 1)
                return Json("{}");

            var challenge = Json("""{"error":"use_dpop_nonce"}""", HttpStatusCode.Unauthorized);
            challenge.Headers.TryAddWithoutValidation("DPoP-Nonce", "n-1");
            return challenge;
        });

        await xrpc.QueryAsync<JsonElement>("com.example.ping");

        Assert.Equal(2, _handler.Requests.Count);
        Assert.All(_handler.Requests, r => Assert.StartsWith("DPoP ", r.Authorization));
        Assert.True(Jwt.TryDecode(_handler.Requests[1].Header("DPoP")!, out var proof, out var error), error);
        var payload = proof.Payload;
        Assert.Equal("n-1", payload.GetProperty("nonce").GetString());
        Assert.Equal("https://pds.example.com/xrpc/com.example.ping", payload.GetProperty("htu").GetString());
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage RateLimited()
    {
        var response = Json("""{"error":"RateLimitExceeded"}""", HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "0");
        return response;
    }

    private sealed record RecordedRequest(
        string Method, string Uri, string? Authorization, string? UserAgent,
        Dictionary<string, string> Headers, byte[]? Body, long? ContentLength)
    {
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private Func<HttpRequestMessage, HttpResponseMessage> _respond = _ => Json("{}");

        public List<RecordedRequest> Requests { get; } = [];

        public TimeSpan Delay { get; set; }

        public void Respond(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public RecordedRequest Single() => Assert.Single(Requests);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);

            var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var headers = request.Headers.ToDictionary(
                h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);

            lock (_gate)
            {
                Requests.Add(new RecordedRequest(
                    request.Method.Method,
                    request.RequestUri!.AbsoluteUri,
                    request.Headers.Authorization?.ToString(),
                    // Serialized as on the wire: product tokens joined with spaces.
                    request.Headers.UserAgent.Count > 0 ? request.Headers.UserAgent.ToString() : null,
                    headers,
                    body,
                    request.Content?.Headers.ContentLength));
            }

            return _respond(request);
        }
    }

    /// <summary>Records whether HttpClient buffered the body or handed out a stream.</summary>
    private sealed class TrackingContent : HttpContent
    {
        private readonly byte[] _bytes;

        public TrackingContent(byte[] bytes, string mediaType = "application/json")
        {
            _bytes = bytes;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            Headers.ContentLength = bytes.Length;
        }

        public bool Buffered { get; private set; }

        public bool StreamRead { get; private set; }

        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Buffered = true;
            return stream.WriteAsync(_bytes).AsTask();
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            StreamRead = true;
            return Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
