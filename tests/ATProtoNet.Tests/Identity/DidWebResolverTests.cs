using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

public class DidWebResolverTests
{
    // ─── URL Building ────────────────────────────────────────

    [Theory]
    [InlineData("did:web:example.com", "https://example.com/.well-known/did.json")]
    [InlineData("did:web:api.bsky.app", "https://api.bsky.app/.well-known/did.json")]
    [InlineData("did:web:labeler.example.com", "https://labeler.example.com/.well-known/did.json")]
    public void BuildResolutionUrl_ValidDid_ReturnsCorrectUrl(string did, string expectedUrl)
    {
        Assert.Equal(expectedUrl, DidWebResolver.BuildResolutionUrl(did));
    }

    [Fact]
    public void BuildResolutionUrl_LocalhostWithPort_UsesHttp()
    {
        var url = DidWebResolver.BuildResolutionUrl("did:web:localhost%3A3000");
        Assert.Equal("http://localhost:3000/.well-known/did.json", url);
    }

    [Fact]
    public void BuildResolutionUrl_PathBased_Throws()
    {
        var ex = Assert.Throws<DidWebException>(() =>
            DidWebResolver.BuildResolutionUrl("did:web:example.com:path:to:resource"));
        Assert.Equal(DidWebErrorKind.InvalidDid, ex.Kind);
    }

    [Fact]
    public void BuildResolutionUrl_IpAddress_Throws()
    {
        var ex = Assert.Throws<DidWebException>(() =>
            DidWebResolver.BuildResolutionUrl("did:web:192.168.1.1"));
        Assert.Equal(DidWebErrorKind.InvalidDid, ex.Kind);
    }

    [Fact]
    public void BuildResolutionUrl_IPv6Bracketed_Throws()
    {
        var ex = Assert.Throws<DidWebException>(() =>
            DidWebResolver.BuildResolutionUrl("did:web:[::1]"));
        Assert.Equal(DidWebErrorKind.InvalidDid, ex.Kind);
    }

    [Fact]
    public void BuildResolutionUrl_EmptyDomain_Throws()
    {
        Assert.Throws<DidWebException>(() =>
            DidWebResolver.BuildResolutionUrl("did:web:"));
    }

    [Fact]
    public void BuildResolutionUrl_WrongMethod_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DidWebResolver.BuildResolutionUrl("did:plc:12345"));
    }

    [Fact]
    public void BuildResolutionUrl_NullInput_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DidWebResolver.BuildResolutionUrl(null!));
    }

    // ─── Resolution ──────────────────────────────────────────

    [Fact]
    public async Task ResolveDidAsync_ValidResponse_ReturnsDocument()
    {
        var didJson = """
        {
            "id": "did:web:example.com",
            "alsoKnownAs": ["at://example.com"],
            "verificationMethod": [{
                "id": "#atproto",
                "type": "Multikey",
                "controller": "did:web:example.com",
                "publicKeyMultibase": "zQ3shZc2QzFh7MC..."
            }],
            "service": [{
                "id": "#atproto_pds",
                "type": "AtprotoPersonalDataServer",
                "serviceEndpoint": "https://pds.example.com"
            }]
        }
        """;

        var handler = new MockHandler(didJson);
        using var httpClient = new HttpClient(handler);
        using var resolver = new DidWebResolver(httpClient);

        var doc = await resolver.ResolveDidAsync("did:web:example.com");

        Assert.Equal("did:web:example.com", doc.Id);
        Assert.Equal("https://pds.example.com", doc.GetPdsEndpoint());
        Assert.Equal("example.com", doc.GetHandle());
    }

    [Fact]
    public async Task ResolveDidAsync_IdMismatch_Throws()
    {
        var didJson = """{"id": "did:web:wrong.com"}""";

        var handler = new MockHandler(didJson);
        using var httpClient = new HttpClient(handler);
        using var resolver = new DidWebResolver(httpClient);

        var ex = await Assert.ThrowsAsync<DidWebException>(
            () => resolver.ResolveDidAsync("did:web:example.com"));
        Assert.Equal(DidWebErrorKind.ValidationError, ex.Kind);
    }

    [Fact]
    public async Task ResolveDidAsync_NotFound_Throws()
    {
        var handler = new MockHandler(System.Net.HttpStatusCode.NotFound);
        using var httpClient = new HttpClient(handler);
        using var resolver = new DidWebResolver(httpClient);

        var ex = await Assert.ThrowsAsync<DidWebException>(
            () => resolver.ResolveDidAsync("did:web:example.com"));
        Assert.Equal(DidWebErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task ResolveDidAsync_InvalidJson_Throws()
    {
        var handler = new MockHandler("not valid json {{{");
        using var httpClient = new HttpClient(handler);
        using var resolver = new DidWebResolver(httpClient);

        var ex = await Assert.ThrowsAsync<DidWebException>(
            () => resolver.ResolveDidAsync("did:web:example.com"));
        Assert.Equal(DidWebErrorKind.ParseError, ex.Kind);
    }

    // ─── DidResolver (unified) ───────────────────────────────

    [Fact]
    public async Task DidResolver_UnsupportedMethod_Throws()
    {
        using var resolver = new DidResolver();
        await Assert.ThrowsAsync<ArgumentException>(
            () => resolver.ResolveDidAsync("did:key:z6Mkfriq1MqLBoPWecGoDLjguo1sB9brj6wT3qZ5BxkKpuP6"));
    }

    [Fact]
    public async Task DidResolver_EmptyDid_Throws()
    {
        using var resolver = new DidResolver();
        await Assert.ThrowsAsync<ArgumentException>(() => resolver.ResolveDidAsync(""));
    }

    // ─── Test helpers ────────────────────────────────────────

    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly string? _content;
        private readonly System.Net.HttpStatusCode _statusCode;

        public MockHandler(string content, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
        {
            _content = content;
            _statusCode = statusCode;
        }

        public MockHandler(System.Net.HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode);
            if (_content is not null)
                response.Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        }
    }
}
