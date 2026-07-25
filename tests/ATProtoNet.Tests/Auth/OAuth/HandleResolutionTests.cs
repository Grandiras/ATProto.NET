using System.Diagnostics;
using System.Net;
using System.Text;
using ATProtoNet.Auth.OAuth;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// Tests for handle resolution in <see cref="AuthorizationServerDiscovery"/>: the
/// per-round timeout budget, racing the HTTPS well-known lookup against DNS, and the
/// response hardening on the untrusted <c>/.well-known/atproto-did</c> endpoint.
/// </summary>
public class HandleResolutionTests
{
    private const string Handle = "example.com";
    private const string WellKnownPath = "/.well-known/atproto-did";

    // ──────────────────────────────────────────────────────────
    //  Timeout budget & racing
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveHandleToDid_HttpsHangs_ResolvesViaDnsWithoutWaitingForHttps()
    {
        using var handler = new ScriptedHandler(async (request, ct) =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains(WellKnownPath, StringComparison.Ordinal))
            {
                // Simulates a parked domain that drops packets on :443.
                await Task.Delay(Timeout.Infinite, ct);
            }

            if (url.Contains("dns.google", StringComparison.Ordinal))
                return DnsAnswer("did:plc:fromdns");

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var discovery = CreateDiscovery(handler, TimeSpan.FromSeconds(5));

        var stopwatch = Stopwatch.StartNew();
        var did = await discovery.ResolveHandleToDidAsync(Handle);
        stopwatch.Stop();

        Assert.Equal("did:plc:fromdns", did);

        // Sequential resolution would have blocked on the dead HTTPS host for the
        // whole 5 s budget before even trying DNS.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            $"Expected the DNS answer to win the race quickly, took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ResolveHandleToDid_AllMethodsHang_FailsWithinBudgetInsteadOfHttpClientTimeout()
    {
        using var handler = new ScriptedHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        // HttpClient keeps its 100 s default here: the budget is what must bound the call.
        var discovery = CreateDiscovery(handler, TimeSpan.FromMilliseconds(500));

        var stopwatch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => discovery.ResolveHandleToDidAsync(Handle));
        stopwatch.Stop();

        Assert.Equal("handle_resolution_failed", ex.ErrorCode);

        // Two rounds of 500 ms (raced HTTPS + DNS, then the appview fallback).
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Expected failure within the resolution budget, took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ResolveHandleAuthoritative_HttpsHangs_ReturnsDnsAnswerWithinBudget()
    {
        using var handler = new ScriptedHandler(async (request, ct) =>
        {
            if (request.RequestUri!.ToString().Contains(WellKnownPath, StringComparison.Ordinal))
                await Task.Delay(Timeout.Infinite, ct);

            return DnsAnswer("did:plc:fromdns");
        });

        var discovery = CreateDiscovery(handler, TimeSpan.FromMilliseconds(500));

        var stopwatch = Stopwatch.StartNew();
        var did = await discovery.ResolveHandleAuthoritativeAsync(Handle);
        stopwatch.Stop();

        Assert.Equal("did:plc:fromdns", did);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Expected the dead HTTPS authority to be bounded by the budget, took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ResolveHandleToDid_CallerCancellation_PropagatesInsteadOfFallingThrough()
    {
        using var handler = new ScriptedHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var discovery = CreateDiscovery(handler, TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => discovery.ResolveHandleToDidAsync(Handle, cts.Token));
    }

    [Fact]
    public void HandleResolutionTimeout_RejectsZeroAndNegative()
    {
        using var handler = new ScriptedHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var discovery = CreateDiscovery(handler, AuthorizationServerDiscovery.DefaultHandleResolutionTimeout);

        Assert.Throws<ArgumentOutOfRangeException>(() => discovery.HandleResolutionTimeout = TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => discovery.HandleResolutionTimeout = TimeSpan.FromSeconds(-1));

        // Infinite is the documented opt-out.
        discovery.HandleResolutionTimeout = Timeout.InfiniteTimeSpan;
        Assert.Equal(Timeout.InfiniteTimeSpan, discovery.HandleResolutionTimeout);
    }

    // ──────────────────────────────────────────────────────────
    //  well-known endpoint hardening
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveHandleToDid_WellKnownReturnsDid_ResolvesViaHttps()
    {
        using var handler = new ScriptedHandler((request, _) =>
        {
            if (request.RequestUri!.ToString().Contains(WellKnownPath, StringComparison.Ordinal))
                return Task.FromResult(WellKnownResponse(request, "did:plc:fromhttps\n"));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var discovery = CreateDiscovery(handler, TimeSpan.FromSeconds(5));

        Assert.Equal("did:plc:fromhttps", await discovery.ResolveHandleToDidAsync(Handle));
    }

    [Fact]
    public async Task ResolveHandleToDid_WellKnownRedirectedToOtherHost_IgnoresTheAnswer()
    {
        using var handler = new ScriptedHandler((request, _) =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains(WellKnownPath, StringComparison.Ordinal))
            {
                // What a followed cross-host redirect looks like: a 200 whose final
                // request URI is no longer the handle's domain.
                var response = WellKnownResponse(request, "did:plc:attacker");
                response.RequestMessage = new HttpRequestMessage(
                    HttpMethod.Get, $"https://attacker.example{WellKnownPath}");
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var discovery = CreateDiscovery(handler, TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => discovery.ResolveHandleToDidAsync(Handle));
        Assert.Equal("handle_resolution_failed", ex.ErrorCode);
    }

    [Fact]
    public async Task ResolveHandleToDid_WellKnownBodyOverCap_IgnoresTheAnswer()
    {
        using var handler = new ScriptedHandler((request, _) =>
        {
            if (request.RequestUri!.ToString().Contains(WellKnownPath, StringComparison.Ordinal))
                return Task.FromResult(WellKnownResponse(
                    request, "did:plc:padded" + new string('x', 4096)));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var discovery = CreateDiscovery(handler, TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => discovery.ResolveHandleToDidAsync(Handle));
        Assert.Equal("handle_resolution_failed", ex.ErrorCode);
    }

    [Fact]
    public async Task ResolveHandleToDid_WellKnownBodyOverCapWithoutContentLength_IgnoresTheAnswer()
    {
        using var handler = new ScriptedHandler((request, _) =>
        {
            if (request.RequestUri!.ToString().Contains(WellKnownPath, StringComparison.Ordinal))
            {
                // Chunked/unknown-length body: the read itself must enforce the cap.
                var bytes = Encoding.UTF8.GetBytes("did:plc:padded" + new string('x', 4096));
                var content = new StreamContent(new MemoryStream(bytes));
                content.Headers.ContentLength = null;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                    RequestMessage = request,
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var discovery = CreateDiscovery(handler, TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => discovery.ResolveHandleToDidAsync(Handle));
        Assert.Equal("handle_resolution_failed", ex.ErrorCode);
    }

    // ──────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────

    private static AuthorizationServerDiscovery CreateDiscovery(
        ScriptedHandler handler, TimeSpan handleResolutionTimeout) =>
        new(new HttpClient(handler, disposeHandler: false), NullLogger.Instance)
        {
            HandleResolutionTimeout = handleResolutionTimeout,
        };

    private static HttpResponseMessage WellKnownResponse(HttpRequestMessage request, string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
            RequestMessage = request,
        };

    private static HttpResponseMessage DnsAnswer(string did) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"Status\":0,\"Answer\":[{{\"data\":\"\\\"did={did}\\\"\"}}]}}",
                Encoding.UTF8,
                "application/json"),
        };

    private sealed class ScriptedHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
