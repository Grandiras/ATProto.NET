using System.Diagnostics;
using System.Net;
using System.Text;
using ATProtoNet.Identity;
using ATProtoNet.Tests.TestSupport;
using NSubstitute;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// Handle resolution through the handle's own authorities: the two lookups racing under one
/// budget, DNS answers preferred over HTTPS, the configurable DNS-over-HTTPS endpoint, and the
/// hardening of the untrusted <c>/.well-known/atproto-did</c> response.
/// </summary>
public class HandleResolverTests
{
    private static readonly Handle Alice = Handle.Parse("alice.example.com");
    private static readonly Did AliceDid = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
    private const string WellKnownPath = "/.well-known/atproto-did";

    private static HandleResolver Create(
        Func<HttpStub.RecordedRequest, CancellationToken, Task<HttpResponseMessage>> respond,
        out HttpStub handler,
        IdentityResolverOptions? options = null)
    {
        handler = new HttpStub().Fallback(respond);
        return new HandleResolver(new HttpClient(handler), options ?? new IdentityResolverOptions
        {
            HandleResolutionTimeout = TimeSpan.FromSeconds(5),
        });
    }

    private static bool IsWellKnown(HttpStub.RecordedRequest request) =>
        request.Uri.AbsolutePath == WellKnownPath;

    private static bool IsDns(HttpStub.RecordedRequest request) => request.Uri.Host == "dns.google";

    // ── Sources and agreement ────────────────────────────────

    [Fact]
    public async Task ResolveAsync_BothAuthoritiesAgree_ReturnsTheDid()
    {
        using var resolver = Create((request, _) => Task.FromResult(IsWellKnown(request)
            ? HttpStub.Text($"{AliceDid}\n")
            : HttpStub.TxtAnswer($"\"did={AliceDid}\"")), out var handler);

        Assert.Equal(AliceDid, await resolver.ResolveAsync(Alice));
        Assert.Contains(new Uri("https://alice.example.com/.well-known/atproto-did"), handler.Uris);
        Assert.Contains(new Uri("https://dns.google/resolve?name=_atproto.alice.example.com&type=TXT"), handler.Uris);
    }

    [Fact]
    public async Task ResolveAsync_OnlyHttpsAnswers_UsesIt()
    {
        using var resolver = Create((request, _) => Task.FromResult(IsWellKnown(request)
            ? HttpStub.Text(AliceDid.Value)
            : HttpStub.JsonResponse("{\"Status\":3}")), out _);

        Assert.Equal(AliceDid, await resolver.ResolveAsync(Alice));
    }

    [Fact]
    public async Task ResolveAsync_OnlyDnsAnswers_UsesIt()
    {
        using var resolver = Create((request, _) => Task.FromResult(IsWellKnown(request)
            ? HttpStub.Status(HttpStatusCode.NotFound)
            : HttpStub.TxtAnswer($"\"did={AliceDid}\"")), out _);

        Assert.Equal(AliceDid, await resolver.ResolveAsync(Alice));
    }

    [Fact]
    public async Task ResolveAsync_AuthoritiesDisagree_DnsWins()
    {
        using var resolver = Create((request, _) => Task.FromResult(IsWellKnown(request)
            ? HttpStub.Text("did:plc:impostorimpostorimpostor")
            : HttpStub.TxtAnswer($"\"did={AliceDid}\"")), out _);

        Assert.Equal(AliceDid, await resolver.ResolveAsync(Alice));
    }

    [Fact]
    public async Task ResolveAsync_DnsAnswers_DoesNotWaitForHttps()
    {
        using var resolver = Create(
            (request, ct) => IsWellKnown(request)
                ? HttpStub.Never(ct)
                : Task.FromResult(HttpStub.TxtAnswer($"\"did={AliceDid}\"")),
            out _,
            new IdentityResolverOptions { HandleResolutionTimeout = TimeSpan.FromMinutes(1) });

        var stopwatch = Stopwatch.StartNew();

        Assert.Equal(AliceDid, await resolver.ResolveAsync(Alice));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ResolveAsync_DnsPublishesTwoDistinctDids_FailsClosed()
    {
        using var resolver = Create((request, _) => Task.FromResult(IsWellKnown(request)
            ? HttpStub.Status(HttpStatusCode.NotFound)
            : HttpStub.TxtAnswer($"\"did={AliceDid}\"", "\"did=did:plc:bbbbbbbbbbbbbbbbbbbbbbbb\"")), out _);

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => resolver.ResolveAsync(Alice));

        Assert.Equal(DidResolutionErrorKind.HandleConflict, ex.Kind);
    }

    [Fact]
    public async Task ResolveAsync_DnsRecordSplitIntoCharacterStrings_IsReassembled()
    {
        using var resolver = Create((request, _) => Task.FromResult(IsWellKnown(request)
            ? HttpStub.Status(HttpStatusCode.NotFound)
            : HttpStub.TxtAnswer("\"v=spf1 -all\"", "\"did=did:plc:aaaa\" \"aaaaaaaaaaaaaaaaaaaa\"")), out _);

        Assert.Equal(AliceDid, await resolver.ResolveAsync(Alice));
    }

    [Fact]
    public async Task ResolveAsync_NeitherAnswers_ReturnsNull()
    {
        using var resolver = Create((_, _) => Task.FromResult(HttpStub.Status(HttpStatusCode.NotFound)), out _);

        Assert.Null(await resolver.ResolveAsync(Alice));
    }

    // ── Cache ────────────────────────────────────────────────

    private static Task<HttpResponseMessage> DnsOnly(HttpStub.RecordedRequest request, CancellationToken _) =>
        Task.FromResult(IsWellKnown(request) ? HttpStub.Status(HttpStatusCode.NotFound) : HttpStub.TxtAnswer($"\"did={AliceDid}\""));

    [Fact]
    public async Task ResolveAsync_AnswerIsCached_SecondResolutionSendsNothing()
    {
        using var resolver = Create(DnsOnly, out var handler);

        await resolver.ResolveAsync(Alice);
        var sent = handler.Requests.Count;

        Assert.Equal(AliceDid, await resolver.ResolveAsync(Alice));
        Assert.Equal(sent, handler.Requests.Count);
    }

    [Fact]
    public async Task ResolveAsync_CacheDisabled_LooksUpEveryTime()
    {
        using var resolver = Create(DnsOnly, out var handler, new IdentityResolverOptions { HandleCacheTtl = TimeSpan.Zero });

        await resolver.ResolveAsync(Alice);
        var sent = handler.Requests.Count;
        await resolver.ResolveAsync(Alice);

        Assert.Equal(2 * sent, handler.Requests.Count);
    }

    [Fact]
    public async Task ResolveAsync_NoAnswer_IsNotCached()
    {
        var answer = false;
        using var resolver = Create((request, ct) => answer ? DnsOnly(request, ct) : Task.FromResult(HttpStub.Status(HttpStatusCode.NotFound)), out _);

        Assert.Null(await resolver.ResolveAsync(Alice));
        answer = true;
        Assert.Equal(AliceDid, await resolver.ResolveAsync(Alice));
    }

    [Fact]
    public async Task IdentityResolver_ResolveUncachedAsync_LooksTheHandleUpAfresh()
    {
        var current = AliceDid;
        using var handles = Create((request, _) => Task.FromResult(IsWellKnown(request)
            ? HttpStub.Status(HttpStatusCode.NotFound)
            : HttpStub.TxtAnswer($"\"did={current}\"")), out _);
        var dids = Substitute.For<IDidResolver>();
        dids.ResolveAsync(AliceDid, Arg.Any<CancellationToken>()).Returns(DidDocs.Parse(AliceDid.Value, Alice.Value, null));
        using var identities = new IdentityResolver(dids, handles);

        Assert.True((await identities.ResolveAsync(AtIdentifier.FromDid(AliceDid))).HandleVerified);
        current = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");

        Assert.True((await identities.ResolveAsync(AtIdentifier.FromDid(AliceDid))).HandleVerified);
        Assert.False((await identities.ResolveUncachedAsync(AliceDid)).HandleVerified);
    }

    // ── Budget ───────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_HttpsHangs_ReturnsTheDnsAnswerWithinTheBudget()
    {
        using var resolver = Create(
            (request, ct) => IsWellKnown(request)
                ? HttpStub.Never(ct)
                : Task.FromResult(HttpStub.TxtAnswer($"\"did={AliceDid}\"")),
            out _,
            new IdentityResolverOptions { HandleResolutionTimeout = TimeSpan.FromMilliseconds(300) });

        var stopwatch = Stopwatch.StartNew();
        var did = await resolver.ResolveAsync(Alice);

        Assert.Equal(AliceDid, did);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ResolveAsync_BothHang_ReturnsNullWithinTheBudget()
    {
        using var resolver = Create(
            (_, ct) => HttpStub.Never(ct),
            out _,
            new IdentityResolverOptions { HandleResolutionTimeout = TimeSpan.FromMilliseconds(300) });

        var stopwatch = Stopwatch.StartNew();
        Assert.Null(await resolver.ResolveAsync(Alice));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ResolveAsync_CallerCancels_Propagates()
    {
        using var resolver = Create((_, ct) => HttpStub.Never(ct), out _);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(Alice, cts.Token));
    }

    // ── DNS-over-HTTPS endpoint ──────────────────────────────

    [Fact]
    public async Task ResolveAsync_ConfiguredDnsEndpoint_IsQueriedInsteadOfTheDefault()
    {
        using var resolver = Create(
            (request, _) => Task.FromResult(IsWellKnown(request)
                ? HttpStub.Status(HttpStatusCode.NotFound)
                : HttpStub.TxtAnswer($"\"did={AliceDid}\"")),
            out var handler,
            new IdentityResolverOptions { DnsOverHttpsUrl = new Uri("https://cloudflare-dns.com/dns-query") });

        Assert.Equal(AliceDid, await resolver.ResolveAsync(Alice));
        Assert.Contains(new Uri("https://cloudflare-dns.com/dns-query?name=_atproto.alice.example.com&type=TXT"), handler.Uris);
        Assert.DoesNotContain(handler.Uris, r => r.Host == "dns.google");
    }

    [Fact]
    public async Task ResolveAsync_DnsDisabled_ResolvesOverHttpsOnly()
    {
        using var resolver = Create(
            (_, _) => Task.FromResult(HttpStub.Text(AliceDid.Value)),
            out var handler,
            new IdentityResolverOptions { DnsOverHttpsUrl = null });

        Assert.Equal(AliceDid, await resolver.ResolveAsync(Alice));
        Assert.Equal(new Uri("https://alice.example.com/.well-known/atproto-did"), Assert.Single(handler.Uris));
    }

    [Fact]
    public void Constructor_PlainHttpDnsEndpoint_IsRefused() =>
        Assert.Throws<ArgumentException>(() => new HandleResolver(new IdentityResolverOptions
        {
            DnsOverHttpsUrl = new Uri("http://dns.internal/resolve"),
        }));

    [Theory]
    [InlineData("alice.local")]
    [InlineData("alice.internal")]
    [InlineData("alice.test")]
    public async Task ResolveAsync_ReservedTld_SendsNoRequest(string handle)
    {
        using var resolver = Create((_, _) => Task.FromResult(HttpStub.Text(AliceDid.Value)), out var handler);

        Assert.Null(await resolver.ResolveAsync(Handle.Parse(handle)));
        Assert.Empty(handler.Requests);
    }

    // ── well-known hardening ─────────────────────────────────

    [Fact]
    public async Task ResolveAsync_WellKnownRedirectedToAnotherHost_IsIgnored()
    {
        using var resolver = Create((request, _) =>
        {
            if (!IsWellKnown(request))
                return Task.FromResult(HttpStub.Status(HttpStatusCode.NotFound));

            // What a followed cross-host redirect looks like: a 200 whose final request URI is no
            // longer the handle's domain.
            var response = HttpStub.Text("did:plc:attackerattackerattacker");
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, $"https://attacker.example{WellKnownPath}");
            return Task.FromResult(response);
        }, out _);

        Assert.Null(await resolver.ResolveAsync(Alice));
    }

    [Theory]
    [InlineData("<html>not a did</html>")]
    [InlineData("did:plc:has spaces in it")]
    [InlineData("")]
    public async Task ResolveAsync_WellKnownBodyIsNotADid_IsIgnored(string body)
    {
        using var resolver = Create((request, _) => Task.FromResult(IsWellKnown(request)
            ? HttpStub.Text(body)
            : HttpStub.Status(HttpStatusCode.NotFound)), out _);

        Assert.Null(await resolver.ResolveAsync(Alice));
    }

    [Theory]
    [InlineData(2048, true, true)]
    [InlineData(2049, true, false)]
    [InlineData(2048, false, true)]
    [InlineData(2049, false, false)]
    public async Task ResolveAsync_WellKnownBody_IsReadUpToExactlyTheCap(int bodyBytes, bool declareLength, bool resolves)
    {
        using var resolver = Create((request, _) =>
        {
            if (!IsWellKnown(request))
                return Task.FromResult(HttpStub.Status(HttpStatusCode.NotFound));

            // Trailing whitespace is trimmed from the answer, so only the length differs.
            var bytes = Encoding.UTF8.GetBytes(AliceDid.Value + new string(' ', bodyBytes - AliceDid.Value.Length));
            var content = new StreamContent(new MemoryStream(bytes));
            content.Headers.ContentLength = declareLength ? bytes.Length : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }, out _);

        Assert.Equal(resolves ? AliceDid : null, await resolver.ResolveAsync(Alice));
    }
}
