using System.Net;
using System.Text;
using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// <c>did:web</c> resolution and the <see cref="DidResolver"/> dispatch in front of it. The URL
/// rules themselves are the table in <see cref="IdentityFetchPolicyTests"/>.
/// </summary>
public class DidWebResolverTests
{
    private static readonly Did ExampleDid = Did.Parse("did:web:example.com");

    private static (DidWebResolver Resolver, ScriptedHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, IdentityResolverOptions? options = null)
    {
        var handler = new ScriptedHandler(respond);
        return (new DidWebResolver(new HttpClient(handler), options), handler);
    }

    [Fact]
    public async Task ResolveAsync_ValidDocument_FetchesTheWellKnownUrlAndParses()
    {
        var (resolver, handler) = Create(_ => ScriptedHandler.Json(DidDocs.Json("did:web:example.com", "example.com")));
        using var _ = resolver;

        var document = await resolver.ResolveAsync(ExampleDid);

        Assert.Equal(new Uri("https://example.com/.well-known/did.json"), Assert.Single(handler.Requests));
        Assert.Equal(ExampleDid, document.Id);
        Assert.Equal(new Uri("https://pds.example.com"), document.GetPdsEndpoint());
        Assert.Equal(Handle.Parse("example.com"), document.GetHandle());
    }

    [Fact]
    public async Task ResolveAsync_IdDiffersOnlyInHostCase_IsRefused()
    {
        // Exactly as @atproto/identity compares: otherwise every casing of a did:web is a
        // separate DID that resolves, and a signer can choose which one it signs as.
        var (resolver, _) = Create(_ => ScriptedHandler.Json(DidDocs.Json("did:web:example.com")));
        using var __ = resolver;

        var ex = await Assert.ThrowsAsync<DidResolutionException>(
            () => resolver.ResolveAsync(Did.Parse("did:web:Example.COM")));

        Assert.Equal(DidResolutionErrorKind.InvalidDocument, ex.Kind);
    }

    [Fact]
    public async Task ResolveAsync_ASpellingThatIsTheDocumentsOwnId_IsAccepted()
    {
        var (resolver, _) = Create(_ => ScriptedHandler.Json(DidDocs.Json("did:web:Example.COM")));
        using var __ = resolver;

        var document = await resolver.ResolveAsync(Did.Parse("did:web:Example.COM"));

        Assert.Equal("did:web:Example.COM", document.Id.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, DidResolutionErrorKind.NotFound)]
    [InlineData(HttpStatusCode.Gone, DidResolutionErrorKind.Deactivated)]
    [InlineData(HttpStatusCode.InternalServerError, DidResolutionErrorKind.HttpError)]
    [InlineData(HttpStatusCode.Found, DidResolutionErrorKind.HttpError)]
    public async Task ResolveAsync_ErrorStatus_IsReportedByKind(HttpStatusCode status, DidResolutionErrorKind expected)
    {
        var (resolver, _) = Create(_ => ScriptedHandler.Status(status));
        using var __ = resolver;

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => resolver.ResolveAsync(ExampleDid));

        Assert.Equal(expected, ex.Kind);
        Assert.Equal(ExampleDid, ex.Did);
    }

    [Theory]
    [InlineData("""{"id":"did:web:attacker.example"}""")]
    [InlineData("not valid json {{{")]
    [InlineData("""{"alsoKnownAs":[]}""")]
    [InlineData("null")]
    public async Task ResolveAsync_UnusableDocument_IsInvalidDocument(string body)
    {
        var (resolver, _) = Create(_ => ScriptedHandler.Json(body));
        using var __ = resolver;

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => resolver.ResolveAsync(ExampleDid));

        Assert.Equal(DidResolutionErrorKind.InvalidDocument, ex.Kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolveAsync_BodyOverTheCap_IsRefused(bool declareLength)
    {
        var padded = DidDocs.Json("did:web:example.com").TrimEnd('}') + ",\"pad\":\"" + new string('x', 2048) + "\"}";
        var (resolver, _) = Create(
            _ =>
            {
                var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(padded)));
                if (!declareLength)
                    content.Headers.ContentLength = null;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            },
            new IdentityResolverOptions { MaxDidDocumentBytes = 1024 });
        using var __ = resolver;

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => resolver.ResolveAsync(ExampleDid));

        Assert.Equal(DidResolutionErrorKind.ResponseTooLarge, ex.Kind);
    }

    [Fact]
    public async Task ResolveAsync_HostNeverAnswers_TimesOutWithinTheRequestTimeout()
    {
        var handler = new ScriptedHandler((_, ct) => ScriptedHandler.Never(ct));
        using var resolver = new DidWebResolver(
            new HttpClient(handler), new IdentityResolverOptions { RequestTimeout = TimeSpan.FromMilliseconds(200) });

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => resolver.ResolveAsync(ExampleDid));

        Assert.Equal(DidResolutionErrorKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task ResolveAsync_CallerCancels_PropagatesCancellation()
    {
        var handler = new ScriptedHandler((_, ct) => ScriptedHandler.Never(ct));
        using var resolver = new DidWebResolver(new HttpClient(handler));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(ExampleDid, cts.Token));
    }

    [Fact]
    public async Task ResolveAsync_NetworkFailure_IsNetworkError()
    {
        var (resolver, _) = Create(_ => throw new HttpRequestException("connection refused"));
        using var __ = resolver;

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => resolver.ResolveAsync(ExampleDid));

        Assert.Equal(DidResolutionErrorKind.NetworkError, ex.Kind);
    }

    [Theory]
    [InlineData("did:web:example.com:user:alice")]
    [InlineData("did:web:internal.corp%3A6379")]
    [InlineData("did:web:localhost%3A2583")]
    public async Task ResolveAsync_RefusedIdentifier_SendsNoRequest(string did)
    {
        var (resolver, handler) = Create(_ => ScriptedHandler.Json("{}"));
        using var _ = resolver;

        await Assert.ThrowsAsync<DidResolutionException>(() => resolver.ResolveAsync(Did.Parse(did)));

        Assert.Equal(0, handler.Count);
    }

    // ── DidResolver ──────────────────────────────────────────

    [Fact]
    public async Task DidResolver_UnsupportedMethod_ThrowsUnsupportedMethod()
    {
        using var resolver = new DidResolver();

        var ex = await Assert.ThrowsAsync<DidResolutionException>(
            () => resolver.ResolveAsync(Did.Parse("did:key:z6Mkfriq1MqLBoPWecGoDLjguo1sB9brj6wT3qZ5BxkKpuP6")));

        Assert.Equal(DidResolutionErrorKind.UnsupportedMethod, ex.Kind);
    }

    [Fact]
    public async Task DidResolver_DispatchesOnTheMethod()
    {
        var handler = new ScriptedHandler(request => request.RequestUri!.Host switch
        {
            "plc.example.com" => ScriptedHandler.Json(DidDocs.AtprotoDotCom),
            _ => ScriptedHandler.Json(DidDocs.Json("did:web:example.com")),
        });
        using var http = new HttpClient(handler);
        using var plc = new PlcClient(http, new Uri("https://plc.example.com"));
        using var web = new DidWebResolver(http);
        using var resolver = new DidResolver(plc, web);

        await resolver.ResolveAsync(Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz"));
        await resolver.ResolveAsync(ExampleDid);

        Assert.Equal(
            [
                new Uri("https://plc.example.com/did:plc:ewvi7nxzyoun6zhxrhs64oiz"),
                new Uri("https://example.com/.well-known/did.json"),
            ],
            handler.Requests);
    }
}
