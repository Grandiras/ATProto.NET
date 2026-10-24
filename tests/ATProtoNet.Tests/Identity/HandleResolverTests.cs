using System.Net;
using System.Text;
using ATProtoNet.Identity;
using ATProtoNet.Net;
using ATProtoNet.TestKit;
using ATProtoNet.Tests.Net;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// Handle resolution through the handle's own authorities: the two lookups racing under one budget, DNS preferred over HTTPS,
/// the positive-answer cache, and the hardening of the untrusted <c>/.well-known/atproto-did</c> response.
/// </summary>
public sealed class HandleResolverTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string WellKnown = "alice.example.com/.well-known/atproto-did";
    private static readonly Handle Alice = Handle.Parse("alice.example.com");
    private static readonly Did AliceDid = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Did OtherDid = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");

    private readonly FakeIdentityNetwork _network = new();
    private readonly FakeTimeProvider _time = new(TestIds.Now);
    private readonly UntrustedHttp _http;
    private readonly TxtResolver _dns;

    public HandleResolverTests()
    {
        // Anything not scripted is a 404, so "no answer" never hides behind an unexpected request.
        _network.Server.Fallback((_, _) => Task.FromResult(Responses.Status(HttpStatusCode.NotFound)));
        _http = new(time: _time, handler: _network.Server);
        _dns = TxtResolver.Over(_network);
    }

    public void Dispose() => _http.Dispose();

    private HandleResolver Resolver(HandleResolverOptions? options = null, IDnsTxtResolver? dns = null) => new(_http, dns ?? _dns, options, _time);

    private HandleResolver HttpsOnly(HandleResolverOptions? options = null) => new(_http, null, options, _time);

    private int WellKnownRequests => _network.Server.To(WellKnown).Count();

    // ── Sources and agreement ────────────────────────────────

    [Fact]
    [Trait("Spec", "IDN-25")]
    [Trait("Spec", "IDN-26")]
    public async Task ResolveAsync_BothAuthoritiesAgree_ReturnsTheDid()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value);

        Assert.Equal(AliceDid, await Resolver().ResolveAsync(Alice, Ct));
        Assert.Equal(["_atproto.alice.example.com"], _dns.Names);
        Assert.Equal(new Uri("https://alice.example.com/.well-known/atproto-did"), Assert.Single(_network.Server.Requests).Uri);
    }

    [Fact]
    [Trait("Spec", "IDN-27")]
    public async Task ResolveAsync_OnlyHttpsAnswers_UsesIt()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value, dns: false);

        Assert.Equal(AliceDid, await Resolver().ResolveAsync(Alice, Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-25")]
    public async Task ResolveAsync_OnlyDnsAnswers_UsesIt()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value, https: false);

        Assert.Equal(AliceDid, await Resolver().ResolveAsync(Alice, Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-28")]
    public async Task ResolveAsync_AuthoritiesDisagree_DnsWins()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value, https: false).AddHandle(Alice.Value, OtherDid.Value, dns: false);

        Assert.Equal(AliceDid, await Resolver().ResolveAsync(Alice, Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-25")]
    public async Task ResolveAsync_DnsPublishesTwoDistinctDids_FailsClosed()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value).AddTxt("_atproto.alice.example.com", $"did={OtherDid}");

        var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => Resolver().ResolveAsync(Alice, Ct));

        Assert.Equal(IdentityResolutionError.HandleConflict, ex.Error);
        Assert.Equal(Alice.Value, ex.Identifier?.ToString());
    }

    [Fact]
    [Trait("Spec", "IDN-25")]
    public async Task ResolveAsync_DnsPublishesTheSameDidTwice_ReturnsIt()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value, https: false).AddTxt("_atproto.alice.example.com", $"did={AliceDid}");

        Assert.Equal(AliceDid, await Resolver().ResolveAsync(Alice, Ct));
    }

    [Theory]
    [Trait("Spec", "IDN-25")]
    [InlineData("v=spf1 -all")]
    [InlineData("did=not a did")]
    [InlineData("DID=did:plc:bbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData(" did=did:plc:bbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb")]
    public async Task ResolveAsync_DnsRecordThatIsNotADidEntry_IsIgnored(string record)
    {
        _network.AddTxt("_atproto.alice.example.com", record).AddTxt("_atproto.alice.example.com", $"did={AliceDid}");

        Assert.Equal(AliceDid, await Resolver().ResolveAsync(Alice, Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-25")]
    [Trait("Spec", "IDN-27")]
    public async Task ResolveAsync_DnsHasOnlyOtherRecords_FallsBackToHttps()
    {
        _network.AddTxt("_atproto.alice.example.com", "did=not a did").AddHandle(Alice.Value, AliceDid.Value, dns: false);

        Assert.Equal(AliceDid, await Resolver().ResolveAsync(Alice, Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-11")]
    [Trait("Spec", "IDN-25")]
    public async Task ResolveAsync_OverDnsOverHttps_ReassemblesASplitRecord()
    {
        _network.Server.On("dns.google/resolve",
            """{"Status":0,"Answer":[{"name":"_atproto.alice.example.com.","type":16,"data":"\"did=did:plc:aaaa\" \"aaaaaaaaaaaaaaaaaaaa\""}]}""");

        Assert.Equal(AliceDid, await Resolver(dns: new DnsOverHttpsTxtResolver(_http)).ResolveAsync(Alice, Ct));
        Assert.Contains(_network.Server.Requests, r => r.Uri == new Uri("https://dns.google/resolve?name=_atproto.alice.example.com&type=TXT"));
    }

    [Fact]
    [Trait("Spec", "IDN-27")]
    public async Task ResolveAsync_NeitherAnswers_ReturnsNull() => Assert.Null(await Resolver().ResolveAsync(Alice, Ct));

    // ── Budget and failures ──────────────────────────────────

    [Fact]
    [Trait("Spec", "IDN-27")]
    public async Task ResolveAsync_DnsAnswersWhileHttpsHangs_ReturnsWithoutWaitingAndStopsHttps()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _network.AddHandle(Alice.Value, AliceDid.Value, https: false);
        _network.Server.On(WellKnown, (_, ct) =>
        {
            ct.Register(() => stopped.TrySetResult());
            return Responses.Never(null!, ct);
        });

        // The clock never moves: only the DNS answer can end the resolution.
        Assert.Equal(AliceDid, await Resolver().ResolveAsync(Alice, Ct).WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.True(stopped.Task.IsCompleted);
    }

    [Fact]
    [Trait("Spec", "IDN-27")]
    public async Task ResolveAsync_DnsHangsAndHttpsAnswers_UsesHttpsWhenTheBudgetEnds()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value, dns: false);
        var resolving = Resolver(new() { Budget = TimeSpan.FromSeconds(2) }, TxtResolver.Hanging()).ResolveAsync(Alice, Ct);

        Assert.False(resolving.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(AliceDid, await resolving);
    }

    [Fact]
    [Trait("Spec", "IDN-27")]
    public async Task ResolveAsync_BothHang_ReturnsNullWhenTheBudgetEnds()
    {
        _network.Server.On(WellKnown, Responses.Never);
        var resolving = Resolver(new() { Budget = TimeSpan.FromSeconds(2) }, TxtResolver.Hanging()).ResolveAsync(Alice, Ct);

        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Null(await resolving);
    }

    [Fact]
    [Trait("Spec", "IDN-32")]
    public async Task ResolveAsync_CallerCancels_Propagates()
    {
        _network.Server.On(WellKnown, Responses.Never);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var resolving = Resolver(dns: TxtResolver.Hanging()).ResolveAsync(Alice, cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolving);
    }

    [Fact]
    [Trait("Spec", "IDN-32")]
    public async Task ResolveAsync_DnsResolverThrows_IsNoAnswerFromDns()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value, dns: false);
        var dns = new TxtResolver((_, _) => Task.FromException<IReadOnlyList<string>?>(
            new UntrustedFetchException(UntrustedFetchError.Malformed, new Uri("https://dns.google/resolve"), "malformed")));

        Assert.Equal(AliceDid, await Resolver(dns: dns).ResolveAsync(Alice, Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-32")]
    public async Task ResolveAsync_WellKnownBodyCutShort_IsNoAnswerFromHttps()
    {
        _network.Server.On(WellKnown, _ => Bodies.Streamed(new BrokenStream(), null));

        Assert.Null(await HttpsOnly().ResolveAsync(Alice, Ct));
        _network.AddHandle(Alice.Value, AliceDid.Value, https: false);
        Assert.Equal(AliceDid, await Resolver().ResolveAsync(Alice, Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-29")]
    public async Task ResolveAsync_DnsDisabled_ResolvesOverHttpsOnly()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value);

        Assert.Equal(AliceDid, await HttpsOnly().ResolveAsync(Alice, Ct));
        Assert.Empty(_dns.Names);
        Assert.Equal(new Uri("https://alice.example.com/.well-known/atproto-did"), Assert.Single(_network.Server.Requests).Uri);
    }

    [Fact]
    [Trait("Spec", "IDN-29")]
    public async Task ResolveAsync_DnsNameOverTheLimit_SkipsDns()
    {
        // 245 characters: "_atproto." makes 254, one over the DNS name limit.
        var handle = Handle.Parse($"{new string('a', 63)}.{new string('b', 63)}.{new string('c', 63)}.{new string('d', 49)}.com");
        Assert.Equal(245, handle.Value.Length);
        _network.AddHandle(handle.Value, AliceDid.Value);

        Assert.Equal(AliceDid, await Resolver().ResolveAsync(handle, Ct));
        Assert.Empty(_dns.Names);
    }

    [Fact]
    [Trait("Spec", "IDN-29")]
    public async Task ResolveAsync_DnsNameAtTheLimit_QueriesDns()
    {
        var handle = Handle.Parse($"{new string('a', 63)}.{new string('b', 63)}.{new string('c', 63)}.{new string('d', 48)}.com");
        _network.AddHandle(handle.Value, AliceDid.Value, https: false);

        Assert.Equal(AliceDid, await Resolver().ResolveAsync(handle, Ct));
        Assert.Equal(253, Assert.Single(_dns.Names).Length);
    }

    // ── Cache ────────────────────────────────────────────────

    [Fact]
    [Trait("Spec", "IDN-31")]
    public async Task ResolveAsync_AnswerIsCached_SecondResolutionSendsNothing()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value);
        var resolver = Resolver();

        await resolver.ResolveAsync(Alice, Ct);
        Assert.Equal(AliceDid, await resolver.ResolveAsync(Alice, Ct));

        Assert.Single(_dns.Names);
        Assert.Equal(1, WellKnownRequests);
    }

    [Fact]
    [Trait("Spec", "IDN-31")]
    public async Task ResolveAsync_CachedAnswer_ExpiresAfterTheTtl()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value);
        var resolver = Resolver(new() { CacheTtl = TimeSpan.FromMinutes(1) });

        await resolver.ResolveAsync(Alice, Ct);
        _time.Advance(TimeSpan.FromSeconds(59));
        await resolver.ResolveAsync(Alice, Ct);
        Assert.Single(_dns.Names);

        _time.Advance(TimeSpan.FromSeconds(1));
        await resolver.ResolveAsync(Alice, Ct);
        Assert.Equal(2, _dns.Names.Count);
    }

    [Fact]
    [Trait("Spec", "IDN-31")]
    public async Task ResolveAsync_CacheDisabled_LooksUpEveryTime()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value);
        var resolver = Resolver(new() { CacheTtl = TimeSpan.Zero });

        await resolver.ResolveAsync(Alice, Ct);
        await resolver.ResolveAsync(Alice, Ct);

        Assert.Equal(2, _dns.Names.Count);
        Assert.Equal(2, WellKnownRequests);
    }

    [Fact]
    [Trait("Spec", "IDN-31")]
    public async Task ResolveAsync_NoAnswer_IsNotCached()
    {
        var resolver = Resolver();

        Assert.Null(await resolver.ResolveAsync(Alice, Ct));
        _network.AddHandle(Alice.Value, AliceDid.Value);

        Assert.Equal(AliceDid, await resolver.ResolveAsync(Alice, Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-31")]
    public async Task ResolveAsync_CacheOverCapacity_EvictsTheLeastRecentlyUsed()
    {
        var bob = Handle.Parse("bob.example.com");
        _network.AddHandle(Alice.Value, AliceDid.Value, https: false).AddHandle(bob.Value, OtherDid.Value, https: false);
        var resolver = Resolver(new() { CacheCapacity = 1 });

        await resolver.ResolveAsync(Alice, Ct);
        await resolver.ResolveAsync(bob, Ct);
        await resolver.ResolveAsync(Alice, Ct);

        Assert.Equal(["_atproto.alice.example.com", "_atproto.bob.example.com", "_atproto.alice.example.com"], _dns.Names);
    }

    [Fact]
    [Trait("Spec", "IDN-36")]
    public async Task InvalidateAsync_CachedAnswer_IsLookedUpAfresh()
    {
        _network.AddHandle(Alice.Value, AliceDid.Value, https: false);
        var resolver = Resolver();

        await resolver.ResolveAsync(Alice, Ct);
        await resolver.InvalidateAsync(Alice, Ct);
        await resolver.ResolveAsync(Alice, Ct);

        Assert.Equal(2, _dns.Names.Count);
    }

    [Fact]
    public void Constructor_InvalidOptions_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolver(new() { Budget = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolver(new() { CacheTtl = TimeSpan.FromSeconds(-1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolver(new() { CacheCapacity = 0 }));
    }

    // ── The well-known answer ────────────────────────────────

    [Theory]
    [Trait("Spec", "IDN-26")]
    [InlineData("<html>not a did</html>")]
    [InlineData("did:plc:has spaces in it")]
    [InlineData("did=did:plc:aaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("")]
    public async Task ResolveAsync_WellKnownBodyIsNotADid_IsIgnored(string body)
    {
        _network.Server.On(WellKnown, _ => Responses.Text(body));

        Assert.Null(await HttpsOnly().ResolveAsync(Alice, Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-26")]
    public async Task ResolveAsync_WellKnownBodyWithSurroundingWhitespace_IsTrimmed()
    {
        _network.Server.On(WellKnown, _ => Responses.Text($" \r\n{AliceDid}\n\t"));

        Assert.Equal(AliceDid, await HttpsOnly().ResolveAsync(Alice, Ct));
    }

    [Fact]
    [Trait("Spec", "IDN-26")]
    public async Task ResolveAsync_WellKnownErrorStatus_IsIgnoredWhateverItsBody()
    {
        _network.Server.On(WellKnown, _ => Responses.Text(AliceDid.Value, HttpStatusCode.InternalServerError));

        Assert.Null(await HttpsOnly().ResolveAsync(Alice, Ct));
    }

    [Theory]
    [Trait("Spec", "IDN-26")]
    [InlineData(2048, true, true)]
    [InlineData(2049, true, false)]
    [InlineData(2048, false, true)]
    [InlineData(2049, false, false)]
    public async Task ResolveAsync_WellKnownBody_IsReadUpToExactlyTheCap(int bodyBytes, bool declareLength, bool resolves)
    {
        // Trailing whitespace is trimmed from the answer, so only the length differs.
        var bytes = Encoding.UTF8.GetBytes(AliceDid.Value + new string(' ', bodyBytes - AliceDid.Value.Length));
        _network.Server.On(WellKnown, _ => Bodies.Of(bytes, declareLength));

        Assert.Equal(resolves ? AliceDid : null, await HttpsOnly().ResolveAsync(Alice, Ct));
        Assert.Equal(2048, HandleResolver.MaxWellKnownBytes);
    }

    [Theory]
    [Trait("Spec", "IDN-6")]
    [Trait("Spec", "IDN-26")]
    [InlineData("https://alice.example.com/.well-known/atproto-did/", true)]
    [InlineData("https://ALICE.example.com/other", true)]
    [InlineData("/relative", true)]
    [InlineData("https://attacker.example.net/.well-known/atproto-did", false)]
    [InlineData("http://alice.example.com/.well-known/atproto-did", false)]
    public async Task ResolveAsync_WellKnownRedirect_IsFollowedOnlyToTheSameHostOverHttps(string location, bool followed)
    {
        _network.Server.On(WellKnown, _ => Bodies.Redirect(location))
            .Fallback((_, _) => Task.FromResult(Responses.Text(AliceDid.Value)));

        Assert.Equal(followed ? AliceDid : null, await HttpsOnly().ResolveAsync(Alice, Ct));
        Assert.Equal(followed ? 2 : 1, _network.Server.Requests.Count);
    }

    [Theory]
    [Trait("Spec", "IDN-26")]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task ResolveAsync_WellKnownRedirectChain_IsFollowedAtMostThreeTimes(int hops, bool resolves)
    {
        foreach (var i in Enumerable.Range(0, hops)) _network.Server.On(i == 0 ? WellKnown : $"alice.example.com/r{i}", _ => Bodies.Redirect($"/r{i + 1}"));
        _network.Server.On($"alice.example.com/r{hops}", _ => Responses.Text(AliceDid.Value));

        Assert.Equal(resolves ? AliceDid : null, await HttpsOnly().ResolveAsync(Alice, Ct));
        Assert.Equal(3, HandleResolver.MaxWellKnownRedirects);
    }

    [Fact]
    [Trait("Spec", "IDN-6")]
    [Trait("Spec", "IDN-26")]
    public async Task ResolveAsync_CallersHandlerFollowedTheWellKnownElsewhere_IsIgnored()
    {
        _network.Server.On(WellKnown, _ => Bodies.FollowedTo("https://attacker.example/.well-known/atproto-did", "did:plc:attackerattackerattacker"));

        Assert.Null(await HttpsOnly().ResolveAsync(Alice, Ct));
    }
}
