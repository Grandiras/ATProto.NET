using System.Net;
using System.Text;
using ATProtoNet.Net;
using ATProtoNet.TestKit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Net;

/// <summary>The rules every untrusted fetch runs under, over a caller's handler: URLs, budget, bodies, redirects and the concurrency gate.</summary>
public sealed class UntrustedHttpTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Uri Url = new("https://host.example.com/doc");
    private static readonly UntrustedRequest Small = new() { MaxBodyBytes = 1024 };

    private readonly FakeXrpcServer _server = new();
    private readonly FakeTimeProvider _time = new(TestIds.Now);
    private readonly List<UntrustedHttp> _created = [];

    public void Dispose() => _created.ForEach(h => h.Dispose());

    private UntrustedHttp Http(UntrustedFetchPolicy? policy = null, ILogger? logger = null)
    {
        var http = new UntrustedHttp(policy, _time, logger, _server);
        _created.Add(http);
        return http;
    }

    private void Serve(Func<RecordedRequest, HttpResponseMessage> respond) => _server.Fallback((request, _) => Task.FromResult(respond(request)));

    // ── URLs ─────────────────────────────────────────────────

    [Theory]
    [Trait("Spec", "IDN-8")]
    [InlineData("http://host.example.com/doc", false)]
    [InlineData("https://host.example.com:8443/doc", false)]
    [InlineData("https://host.example.com:443/doc", true)]
    [InlineData("ftp://host.example.com/doc", false)]
    [InlineData("wss://host.example.com/doc", false)]
    public async Task GetAsync_WithoutTheOptOut_FetchesOnlyHttpsOnTheDefaultPort(string url, bool fetched)
    {
        Serve(_ => Responses.Text("ok"));
        var http = Http();

        if (fetched)
            Assert.True((await http.GetAsync(new Uri(url), Small, Ct)).IsSuccess);
        else
        {
            var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => http.GetAsync(new Uri(url), Small, Ct));
            Assert.Equal(UntrustedFetchError.Blocked, ex.Error);
            Assert.Equal(new Uri(url), ex.Url);
        }
        Assert.Equal(fetched ? 1 : 0, _server.Requests.Count);
    }

    [Theory]
    [Trait("Spec", "IDN-8")]
    [InlineData("http://localhost:2583/doc", true)]
    [InlineData("https://10.0.0.5:8443/doc", true)]
    [InlineData("ftp://host.example.com/doc", false)]
    public async Task GetAsync_AllowPrivateNetworks_AlsoFetchesHttpAndExplicitPorts(string url, bool fetched)
    {
        Serve(_ => Responses.Text("ok"));
        var http = Http(new() { AllowPrivateNetworks = true });

        if (fetched)
            Assert.Equal(new Uri(url), (await http.GetAsync(new Uri(url), Small, Ct)).FinalUrl);
        else
            Assert.Equal(UntrustedFetchError.Blocked, (await Assert.ThrowsAsync<UntrustedFetchException>(() => http.GetAsync(new Uri(url), Small, Ct))).Error);
        Assert.Equal(fetched ? 1 : 0, _server.Requests.Count);
    }

    [Fact]
    public async Task GetAsync_RelativeUrl_ThrowsArgumentException() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Http().GetAsync(new Uri("/doc", UriKind.Relative), Small, Ct));

    [Fact]
    public async Task GetAsync_Accept_IsSent()
    {
        Serve(_ => Responses.Text("ok"));

        await Http().GetAsync(Url, Small with { Accept = "application/did+ld+json, application/json" }, Ct);

        Assert.Equal("application/did+ld+json, application/json", string.Join(", ", _server.Last.Headers.Accept));
    }

    // ── Opt-out and enforcement ──────────────────────────────

    [Fact]
    [Trait("Spec", "IDN-8")]
    public async Task Constructor_AllowPrivateNetworks_LogsOneWarning()
    {
        Serve(_ => Responses.Text("ok"));
        var log = new ListLogger();
        var http = Http(new() { AllowPrivateNetworks = true }, log);

        await http.GetAsync(Url, Small, Ct);
        await http.GetAsync(Url, Small, Ct);

        Assert.Equal(LogLevel.Warning, Assert.Single(log.Entries).Level);
    }

    [Fact]
    [Trait("Spec", "IDN-9")]
    public void IsEnforced_OwnHandlerWithoutTheOptOut_IsTrueAndLogsNothing()
    {
        var log = new ListLogger();
        using var http = new UntrustedHttp(logger: log);

        Assert.True(http.IsEnforced);
        Assert.Empty(log.Entries);
    }

    [Fact]
    [Trait("Spec", "IDN-9")]
    public void IsEnforced_CallersHandlerOrOptOut_IsFalse()
    {
        using var development = new UntrustedHttp(new() { AllowPrivateNetworks = true });

        Assert.False(Http().IsEnforced);
        Assert.False(development.IsEnforced);
    }

    [Fact]
    public void Constructor_InvalidPolicy_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UntrustedHttp(new() { MaxConcurrentFetches = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UntrustedHttp(new() { RequestTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UntrustedHttp(new() { MaxResponseHeadersBytes = 1023 }));
    }

    // ── Bodies ───────────────────────────────────────────────

    [Theory]
    [Trait("Spec", "IDN-5")]
    [InlineData(1024, true, true)]
    [InlineData(1025, true, false)]
    [InlineData(1024, false, true)]
    [InlineData(1025, false, false)]
    public async Task GetAsync_SuccessBody_IsReadUpToExactlyTheCap(int length, bool declareLength, bool accepted)
    {
        var body = Encoding.ASCII.GetBytes(new string('x', length));
        Serve(_ => Bodies.Of(body, declareLength));
        var http = Http();

        if (accepted)
            Assert.Equal(body, (await http.GetAsync(Url, Small, Ct)).Body.ToArray());
        else
            Assert.Equal(UntrustedFetchError.ResponseTooLarge, (await Assert.ThrowsAsync<UntrustedFetchException>(() => http.GetAsync(Url, Small, Ct))).Error);
    }

    [Fact]
    [Trait("Spec", "IDN-5")]
    public async Task GetAsync_EndlessUndeclaredBody_IsNotReadPastTheCap()
    {
        var stream = new EndlessStream();
        Serve(_ => Bodies.Streamed(stream, null));

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => Http().GetAsync(Url, Small, Ct));

        Assert.Equal(UntrustedFetchError.ResponseTooLarge, ex.Error);
        Assert.Equal(Small.MaxBodyBytes + 1, stream.BytesRead);
    }

    [Fact]
    [Trait("Spec", "IDN-5")]
    public async Task GetAsync_DeclaredLengthOverTheCap_IsRefusedBeforeReading()
    {
        var stream = new EndlessStream();
        Serve(_ => Bodies.Streamed(stream, 10_000_000));

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => Http().GetAsync(Url, Small, Ct));

        Assert.Equal(UntrustedFetchError.ResponseTooLarge, ex.Error);
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    [Trait("Spec", "IDN-5")]
    public async Task GetAsync_BodyCutShort_IsNetwork()
    {
        Serve(_ => Bodies.Streamed(new BrokenStream(), null));

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => Http().GetAsync(Url, Small, Ct));

        Assert.Equal(UntrustedFetchError.Network, ex.Error);
        Assert.Equal(Url, ex.Url);
    }

    [Theory]
    [Trait("Spec", "IDN-5")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAsync_ErrorBodyOverTheErrorCap_IsTruncated(bool declareLength)
    {
        var body = Encoding.ASCII.GetBytes(new string('e', UntrustedHttp.MaxErrorBodyBytes + 100));
        Serve(_ => Bodies.Of(body, declareLength, HttpStatusCode.InternalServerError));

        var response = await Http().GetAsync(Url, new() { MaxBodyBytes = 1024 * 1024 }, Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.False(response.IsSuccess);
        Assert.Equal(body[..UntrustedHttp.MaxErrorBodyBytes], response.Body.ToArray());
    }

    [Fact]
    [Trait("Spec", "IDN-5")]
    public async Task GetAsync_EndlessErrorBody_IsTruncatedAtTheSmallerCap()
    {
        var stream = new EndlessStream();
        Serve(_ => Bodies.Streamed(stream, null, HttpStatusCode.NotFound));

        var response = await Http().GetAsync(Url, Small, Ct);

        Assert.Equal(Small.MaxBodyBytes, response.Body.Length);
        Assert.InRange(stream.BytesRead, 0, Small.MaxBodyBytes + 1);
    }

    [Fact]
    [Trait("Spec", "IDN-10")]
    public async Task EnsureSuccess_ErrorStatus_ThrowsHttpStatusWithUrlAndStatus()
    {
        Serve(_ => Responses.Status(HttpStatusCode.BadGateway));
        var response = await Http().GetAsync(Url, Small, Ct);

        var ex = Assert.Throws<UntrustedFetchException>(() => response.EnsureSuccess());

        Assert.Equal(UntrustedFetchError.HttpStatus, ex.Error);
        Assert.Equal(HttpStatusCode.BadGateway, ex.Status);
        Assert.Equal(Url, ex.Url);
    }

    // ── Redirects ────────────────────────────────────────────

    [Fact]
    [Trait("Spec", "IDN-6")]
    public async Task GetAsync_RedirectWithoutAllowance_IsRefused()
    {
        Serve(_ => Bodies.Redirect("https://host.example.com/moved"));

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => Http().GetAsync(Url, Small, Ct));

        Assert.Equal(UntrustedFetchError.RedirectRefused, ex.Error);
        Assert.Equal(HttpStatusCode.Found, ex.Status);
        Assert.Single(_server.Requests);
    }

    [Theory]
    [Trait("Spec", "IDN-6")]
    [InlineData(HttpStatusCode.MovedPermanently, "https://host.example.com/moved")]
    [InlineData(HttpStatusCode.Found, "https://HOST.example.com/moved")]
    [InlineData(HttpStatusCode.SeeOther, "/moved")]
    [InlineData(HttpStatusCode.TemporaryRedirect, "moved")]
    [InlineData(HttpStatusCode.PermanentRedirect, "https://host.example.com/moved")]
    public async Task GetAsync_SameHostHttpsRedirect_IsFollowed(HttpStatusCode status, string location)
    {
        _server.On("host.example.com/doc", _ => Bodies.Redirect(location, status)).On("host.example.com/moved", _ => Responses.Text("moved"));

        var response = await Http().GetAsync(Url, Small with { MaxSameHostRedirects = 1 }, Ct);

        Assert.Equal("moved", Encoding.UTF8.GetString(response.Body.Span));
        Assert.Equal(new Uri("https://host.example.com/moved"), response.FinalUrl);
        Assert.Equal(2, _server.Requests.Count);
    }

    [Theory]
    [Trait("Spec", "IDN-6")]
    [Trait("Spec", "IDN-8")]
    [InlineData("https://other.example.com/doc", UntrustedFetchError.RedirectRefused)]
    [InlineData("http://host.example.com/doc", UntrustedFetchError.RedirectRefused)]
    [InlineData(null, UntrustedFetchError.RedirectRefused)]
    [InlineData("https://host.example.com:8443/doc", UntrustedFetchError.Blocked)]
    public async Task GetAsync_RedirectElsewhere_IsRefusedWithoutFollowing(string? location, UntrustedFetchError expected)
    {
        Serve(_ => Bodies.Redirect(location));

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => Http().GetAsync(Url, Small with { MaxSameHostRedirects = 3 }, Ct));

        Assert.Equal(expected, ex.Error);
        Assert.Single(_server.Requests);
    }

    [Theory]
    [Trait("Spec", "IDN-6")]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task GetAsync_RedirectChain_IsFollowedUpToTheAllowance(int hops, bool followed)
    {
        foreach (var i in Enumerable.Range(0, hops)) _server.On($"host.example.com/{(i == 0 ? "doc" : $"r{i}")}", _ => Bodies.Redirect($"/r{i + 1}"));
        _server.On($"host.example.com/r{hops}", _ => Responses.Text("end"));
        var http = Http();

        if (followed)
            Assert.Equal(new Uri($"https://host.example.com/r{hops}"), (await http.GetAsync(Url, Small with { MaxSameHostRedirects = 3 }, Ct)).FinalUrl);
        else
            Assert.Equal(UntrustedFetchError.RedirectRefused, (await Assert.ThrowsAsync<UntrustedFetchException>(() => http.GetAsync(Url, Small with { MaxSameHostRedirects = 3 }, Ct))).Error);
        Assert.Equal(followed ? hops + 1 : 4, _server.Requests.Count);
    }

    [Fact]
    [Trait("Spec", "IDN-6")]
    public async Task GetAsync_CallersHandlerFollowedARedirect_IsRefused()
    {
        Serve(_ => Bodies.FollowedTo("https://elsewhere.example.net/doc", "{}"));

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => Http().GetAsync(Url, Small with { MaxSameHostRedirects = 3 }, Ct));

        Assert.Equal(UntrustedFetchError.RedirectRefused, ex.Error);
    }

    [Fact]
    [Trait("Spec", "IDN-6")]
    public async Task PostAsync_Redirect_IsRefusedWhateverTheRequestAllows()
    {
        Serve(_ => Bodies.Redirect("https://host.example.com/moved", HttpStatusCode.TemporaryRedirect));
        using var content = new StringContent("{}");

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => Http().PostAsync(Url, content, Small with { MaxSameHostRedirects = 3 }, Ct));

        Assert.Equal(UntrustedFetchError.RedirectRefused, ex.Error);
        Assert.Equal(HttpMethod.Post, Assert.Single(_server.Requests).Method);
    }

    // ── Budget and cancellation ──────────────────────────────

    [Fact]
    [Trait("Spec", "IDN-4")]
    public async Task GetAsync_HostNeverAnswers_TimesOutAfterTheRequestTimeout()
    {
        _server.Fallback(Responses.Never);
        var fetch = Http().GetAsync(Url, Small, Ct);

        _time.Advance(TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<UntrustedFetchException>(() => fetch);
        Assert.Equal(UntrustedFetchError.Timeout, ex.Error);
        Assert.Equal(Url, ex.Url);
    }

    [Fact]
    [Trait("Spec", "IDN-4")]
    public async Task GetAsync_BodyNeverArrives_TimesOutAfterTheRequestTimeout()
    {
        Serve(_ => Bodies.Streamed(new EndlessStream(hang: true), null));
        var fetch = Http(new() { RequestTimeout = TimeSpan.FromSeconds(2) }).GetAsync(Url, Small, Ct);

        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(UntrustedFetchError.Timeout, (await Assert.ThrowsAsync<UntrustedFetchException>(() => fetch)).Error);
    }

    [Fact]
    [Trait("Spec", "IDN-4")]
    [Trait("Spec", "IDN-7")]
    public async Task GetAsync_WaitingForASlot_CountsAgainstTheBudget()
    {
        // A host that ignores cancellation holds the only slot for good.
        _server.Fallback((_, _) => new TaskCompletionSource<HttpResponseMessage>().Task);
        var http = Http(new() { MaxConcurrentFetches = 1 });
        var first = http.GetAsync(Url, Small, Ct);
        var second = http.GetAsync(new Uri("https://other.example.com/"), Small, Ct);

        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(UntrustedFetchError.Timeout, (await Assert.ThrowsAsync<UntrustedFetchException>(() => second)).Error);
        Assert.False(first.IsCompleted);
        Assert.Single(_server.Requests);
    }

    [Fact]
    [Trait("Spec", "IDN-4")]
    public async Task GetAsync_CallerCancels_ThrowsOperationCanceledNotTimeout()
    {
        _server.Fallback(Responses.Never);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var fetch = Http().GetAsync(Url, Small, cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
    }

    [Fact]
    [Trait("Spec", "IDN-4")]
    public async Task GetAsync_AlreadyCancelled_SendsNothing()
    {
        Serve(_ => Responses.Text("ok"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Http().GetAsync(Url, Small, new CancellationToken(canceled: true)));

        Assert.Empty(_server.Requests);
    }

    // ── Concurrency ──────────────────────────────────────────

    [Fact]
    [Trait("Spec", "IDN-7")]
    public async Task GetAsync_MoreFetchesThanSlots_RunsAtMostTheCapAtOnceAndPerInstance()
    {
        var gate = new Lock();
        int inFlight = 0, peak = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var full = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Fallback(async (_, ct) =>
        {
            lock (gate)
            {
                peak = Math.Max(peak, ++inFlight);
                if (inFlight == 2) full.TrySetResult();
            }
            await release.Task.WaitAsync(ct);
            lock (gate) inFlight--;
            return Responses.Text("ok");
        });
        var http = Http(new() { MaxConcurrentFetches = 2 });

        var fetches = Enumerable.Range(0, 6).Select(i => http.GetAsync(new Uri($"https://host{i}.example.com/"), Small, Ct)).ToArray();
        await full.Task.WaitAsync(Ct);
        Assert.Equal(2, _server.Requests.Count);

        // The gate is the instance's: another one is not held up by this one's full slots.
        using var other = new UntrustedHttp(handler: new FakeXrpcServer().Fallback((_, _) => Task.FromResult(Responses.Text("ok"))));
        Assert.True((await other.GetAsync(Url, Small, Ct)).IsSuccess);

        release.SetResult();
        await Task.WhenAll(fetches);
        Assert.Equal(2, peak);
        Assert.Equal(6, _server.Requests.Count);
    }
}
