using System.Net;
using System.Text;
using ATProtoNet.Identity;
using ATProtoNet.Net;
using ATProtoNet.TestKit;
using ATProtoNet.Tests.Net;
using Microsoft.Extensions.Time.Testing;
using static ATProtoNet.Tests.Identity.IdentityFixtures;

namespace ATProtoNet.Tests.Identity;

/// <summary><c>did:web</c> resolution and the <see cref="DidResolver"/> dispatch in front of it. The URL rules are the table in <see cref="IdentityFetchPolicyTests"/>.</summary>
public sealed class DidWebResolverTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Did ExampleDid = Did.Parse("did:web:example.com");

    private readonly FakeIdentityNetwork _network = new();
    private readonly FakeTimeProvider _time = new(TestIds.Now);
    private readonly UntrustedHttp _http;

    public DidWebResolverTests() => _http = new(time: _time, handler: _network.Server);

    public void Dispose() => _http.Dispose();

    private DidWebResolver Resolver(DidWebOptions? options = null) => new(_http, options);

    private void Serve(Func<RecordedRequest, HttpResponseMessage> respond) => _network.Server.Fallback((request, _) => Task.FromResult(respond(request)));

    private async Task<IdentityResolutionException> FailsAsync(Did did, IdentityResolutionError expected, DidWebOptions? options = null)
    {
        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => Resolver(options).ResolveAsync(did, Ct));
        Assert.Equal(expected, ex.Error);
        Assert.Equal(did.Value, ex.Identifier?.ToString());
        return ex;
    }

    private static UntrustedFetchException Fetch(IdentityResolutionException ex) => Assert.IsType<UntrustedFetchException>(ex.InnerException);

    [Fact]
    [Trait("Spec", "IDN-22")]
    [Trait("Spec", "IDN-24")]
    public async Task ResolveAsync_ValidDocument_FetchesTheWellKnownUrlAndParses()
    {
        _network.AddWeb(ExampleDid.Value, Document(ExampleDid.Value));

        var document = await Resolver().ResolveAsync(ExampleDid, Ct);

        var request = Assert.Single(_network.Server.Requests);
        Assert.Equal(new Uri("https://example.com/.well-known/did.json"), request.Uri);
        Assert.Equal(["application/did+ld+json", "application/json"], request.Headers.Accept.Select(a => a.MediaType));
        Assert.Equal(ExampleDid, document.Id);
        Assert.Equal(new Uri(TestIds.Pds), document.GetPdsEndpoint());
        Assert.Equal(Handle.Parse("example.com"), document.GetHandle());
    }

    [Fact]
    [Trait("Spec", "IDN-19")]
    public async Task ResolveAsync_IdDiffersOnlyInHostCase_IsRefused()
    {
        // Exactly as @atproto/identity compares: otherwise every casing of a did:web is a separate DID that resolves, and a
        // signer can choose which one it signs as.
        _network.AddWeb(ExampleDid.Value, Document(ExampleDid.Value));

        await FailsAsync(Did.Parse("did:web:Example.COM"), IdentityResolutionError.InvalidDocument);
    }

    [Fact]
    [Trait("Spec", "IDN-19")]
    public async Task ResolveAsync_ASpellingThatIsTheDocumentsOwnId_IsAccepted()
    {
        _network.AddWeb(ExampleDid.Value, Document("did:web:Example.COM"));

        var document = await Resolver().ResolveAsync(Did.Parse("did:web:Example.COM"), Ct);

        Assert.Equal("did:web:Example.COM", document.Id.Value);
    }

    [Theory]
    [Trait("Spec", "IDN-10")]
    [InlineData(HttpStatusCode.NotFound, IdentityResolutionError.NotFound)]
    [InlineData(HttpStatusCode.Gone, IdentityResolutionError.Deactivated)]
    [InlineData(HttpStatusCode.InternalServerError, IdentityResolutionError.Unavailable)]
    [InlineData(HttpStatusCode.Forbidden, IdentityResolutionError.Unavailable)]
    public async Task ResolveAsync_ErrorStatus_IsReportedByKind(HttpStatusCode status, IdentityResolutionError expected)
    {
        Serve(_ => Responses.Status(status));

        var ex = await FailsAsync(ExampleDid, expected);

        if (expected == IdentityResolutionError.Unavailable)
        {
            Assert.Equal(UntrustedFetchError.HttpStatus, Fetch(ex).Error);
            Assert.Equal(status, Fetch(ex).Status);
        }
    }

    [Theory]
    [Trait("Spec", "IDN-12")]
    [Trait("Spec", "IDN-19")]
    [Trait("Spec", "IDN-24")]
    [InlineData("""{"id":"did:web:attacker.example"}""")]
    [InlineData("not valid json {{{")]
    [InlineData("""{"alsoKnownAs":[]}""")]
    [InlineData("""{"id":"did:web:example.com","service":[null]}""")]
    [InlineData("null")]
    [InlineData("")]
    public async Task ResolveAsync_UnusableDocument_IsInvalidDocument(string body)
    {
        Serve(_ => Responses.Json(body));

        var ex = await FailsAsync(ExampleDid, IdentityResolutionError.InvalidDocument);

        Assert.IsNotType<UntrustedFetchException>(ex.InnerException);
    }

    [Theory]
    [Trait("Spec", "IDN-5")]
    [Trait("Spec", "IDN-24")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolveAsync_BodyOverTheCap_IsRefused(bool declareLength)
    {
        var padded = Document(ExampleDid.Value).TrimEnd('}') + ",\"pad\":\"" + new string('x', 2048) + "\"}";
        Serve(_ => Bodies.Of(Encoding.UTF8.GetBytes(padded), declareLength));

        var ex = await FailsAsync(ExampleDid, IdentityResolutionError.Unavailable, new() { MaxDidDocumentBytes = 1024 });

        Assert.Equal(UntrustedFetchError.ResponseTooLarge, Fetch(ex).Error);
    }

    [Fact]
    [Trait("Spec", "IDN-24")]
    public async Task ResolveAsync_DocumentUpToTheDefaultCap_IsAccepted()
    {
        var json = Document(ExampleDid.Value);
        var padded = json.TrimEnd('}') + ",\"pad\":\"" + new string('x', 64 * 1024 - json.Length - 9) + "\"}";
        Assert.Equal(64 * 1024, Encoding.UTF8.GetByteCount(padded));
        Serve(_ => Bodies.Of(Encoding.UTF8.GetBytes(padded), declareLength: false));

        Assert.Equal(ExampleDid, (await Resolver().ResolveAsync(ExampleDid, Ct)).Id);
    }

    [Fact]
    [Trait("Spec", "IDN-24")]
    public void Constructor_CapBelowOneKibibyte_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolver(new() { MaxDidDocumentBytes = 1023 }));

    [Fact]
    [Trait("Spec", "IDN-4")]
    public async Task ResolveAsync_HostNeverAnswers_TimesOutWithinTheRequestTimeout()
    {
        _network.Server.Fallback(Responses.Never);
        var resolving = Resolver().ResolveAsync(ExampleDid, Ct);

        _time.Advance(_http.Policy.RequestTimeout);

        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => resolving);
        Assert.Equal(IdentityResolutionError.Unavailable, ex.Error);
        Assert.Equal(UntrustedFetchError.Timeout, Fetch(ex).Error);
    }

    [Fact]
    [Trait("Spec", "IDN-4")]
    public async Task ResolveAsync_CallerCancels_PropagatesCancellation()
    {
        _network.Server.Fallback(Responses.Never);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var resolving = Resolver().ResolveAsync(ExampleDid, cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolving);
    }

    [Fact]
    [Trait("Spec", "IDN-10")]
    public async Task ResolveAsync_NetworkFailure_IsUnavailableWithTheFetchErrorInside()
    {
        _network.Server.Fallback((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")));

        var ex = await FailsAsync(ExampleDid, IdentityResolutionError.Unavailable);

        Assert.Equal(UntrustedFetchError.Network, Fetch(ex).Error);
        Assert.Equal(new Uri("https://example.com/.well-known/did.json"), Fetch(ex).Url);
    }

    [Fact]
    [Trait("Spec", "IDN-5")]
    public async Task ResolveAsync_BodyCutShort_IsUnavailableWithANetworkError()
    {
        Serve(_ => Bodies.Streamed(new BrokenStream(), 1000));

        var ex = await FailsAsync(ExampleDid, IdentityResolutionError.Unavailable);

        Assert.Equal(UntrustedFetchError.Network, Fetch(ex).Error);
    }

    [Theory]
    [Trait("Spec", "IDN-6")]
    [InlineData("https://elsewhere.example.net/.well-known/did.json")]
    [InlineData("https://example.com/other/did.json")]
    public async Task ResolveAsync_Redirect_IsRefusedWithoutFollowing(string location)
    {
        Serve(_ => Bodies.Redirect(location));

        var ex = await FailsAsync(ExampleDid, IdentityResolutionError.Unavailable);

        Assert.Equal(UntrustedFetchError.RedirectRefused, Fetch(ex).Error);
        Assert.Single(_network.Server.Requests);
    }

    [Fact]
    [Trait("Spec", "IDN-6")]
    public async Task ResolveAsync_CallersHandlerThatFollowedARedirect_IsRefused()
    {
        Serve(_ => Bodies.FollowedTo("https://elsewhere.example.net/did.json", Document(ExampleDid.Value)));

        var ex = await FailsAsync(ExampleDid, IdentityResolutionError.Unavailable);

        Assert.Equal(UntrustedFetchError.RedirectRefused, Fetch(ex).Error);
    }

    [Theory]
    [Trait("Spec", "IDN-20")]
    [Trait("Spec", "IDN-22")]
    [InlineData("did:web:example.com:user:alice")]
    [InlineData("did:web:internal.corp%3A6379")]
    [InlineData("did:web:127.0.0.1")]
    public async Task ResolveAsync_RefusedIdentifier_IsInvalidDidAndSendsNoRequest(string did)
    {
        await FailsAsync(Did.Parse(did), IdentityResolutionError.InvalidDid);

        Assert.Empty(_network.Server.Requests);
    }

    [Fact]
    [Trait("Spec", "IDN-20")]
    public async Task ResolveAsync_AnotherMethod_IsUnsupportedMethodAndSendsNoRequest()
    {
        await FailsAsync(Did.Parse(TestIds.Did), IdentityResolutionError.UnsupportedMethod);

        Assert.Empty(_network.Server.Requests);
    }

    // ── DidResolver ──────────────────────────────────────────

    [Fact]
    [Trait("Spec", "IDN-20")]
    public async Task DidResolver_UnsupportedMethod_ThrowsUnsupportedMethodAndSendsNoRequest()
    {
        var resolver = new DidResolver(new Dictionary<string, IDidResolver> { ["web"] = Resolver() });

        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => resolver.ResolveAsync(Did.Parse("did:key:z6Mkfriq1MqLBoPWecGoDLjguo1sB9brj6wT3qZ5BxkKpuP6"), Ct));

        Assert.Equal(IdentityResolutionError.UnsupportedMethod, ex.Error);
        Assert.Empty(_network.Server.Requests);
    }

    [Fact]
    [Trait("Spec", "IDN-20")]
    public async Task DidResolver_DispatchesOnTheMethod()
    {
        _network.AddWeb(ExampleDid.Value, Document(ExampleDid.Value));
        var resolver = new DidResolver(new Dictionary<string, IDidResolver> { ["web"] = Resolver() });

        Assert.Equal(ExampleDid, (await resolver.ResolveAsync(ExampleDid, Ct)).Id);
        Assert.Equal(ExampleDid, (await resolver.RefreshAsync(ExampleDid, Ct)).Id);
        Assert.Equal(ExampleDid, (await resolver.FetchFreshAsync(ExampleDid, Ct)).Id);
        Assert.Equal(3, _network.Server.Requests.Count);
    }
}
