using System.Net;
using ATProtoNet.Auth.OAuth;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// Handle resolution must not let one silent authority hold up the whole flow
/// (Issue #52). A handle whose domain drops packets on :443 never answers and
/// never refuses, so before these guarantees existed the lookup ran until
/// <see cref="HttpClient.Timeout"/> elapsed — 100 s by default — and in the
/// authoritative case surfaced that as a hard failure, which aborted sign-in
/// at the OAuth callback even though DNS had the answer all along.
/// </summary>
/// <remarks>
/// Complements <see cref="HandleResolutionTests"/>, which covers the budget
/// itself and the well-known response hardening. These cases pin the pieces a
/// bounded lookup must not quietly change: that either authority can be the
/// silent one, that both going silent still fails rather than resolving to
/// nothing, that conflict detection survives, and that the appview fallback is
/// still reached.
/// </remarks>
public class HandleResolutionTimeoutTests
{
    private const string Handle = "alice.example.com";
    private const string Did = "did:plc:alice";

    /// <summary>A budget short enough to keep the tests quick.</summary>
    private static readonly TimeSpan ShortBudget = TimeSpan.FromMilliseconds(300);

    private static AuthorizationServerDiscovery Discovery(StubHandler handler) =>
        new(new HttpClient(handler), NullLogger.Instance) { HandleResolutionTimeout = ShortBudget };

    // ── Authoritative resolution ──────────────────────────────

    [Fact]
    public async Task Authoritative_SilentDns_StillResolvesFromHttps()
    {
        var discovery = Discovery(new StubHandler
        {
            HttpsWellKnown = _ => Text(Did),
            DnsTxt = Never,
        });

        var resolved = await discovery.ResolveHandleAuthoritativeAsync(Handle);

        Assert.Equal(Did, resolved);
    }

    [Fact]
    public async Task Authoritative_BothSilent_ThrowsResolutionFailure()
    {
        var discovery = Discovery(new StubHandler { HttpsWellKnown = Never, DnsTxt = Never });

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => discovery.ResolveHandleAuthoritativeAsync(Handle));

        Assert.Equal("handle_resolution_failed", ex.ErrorCode);
    }

    [Fact]
    public async Task Authoritative_ConflictingAnswers_StillFailsClosed()
    {
        var discovery = Discovery(new StubHandler
        {
            HttpsWellKnown = _ => Text("did:plc:impostor"),
            DnsTxt = _ => TxtAnswer(Did),
        });

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => discovery.ResolveHandleAuthoritativeAsync(Handle));

        Assert.Equal("handle_resolution_conflict", ex.ErrorCode);
    }

    [Fact]
    public async Task Authoritative_CallerCancellation_Propagates()
    {
        var discovery = Discovery(new StubHandler { HttpsWellKnown = Never, DnsTxt = Never });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => discovery.ResolveHandleAuthoritativeAsync(Handle, cts.Token));
    }

    // ── Convenience resolution ────────────────────────────────

    [Fact]
    public async Task Convenience_SilentHttpsAndDns_FallsThroughToAppview()
    {
        var discovery = Discovery(new StubHandler
        {
            HttpsWellKnown = Never,
            DnsTxt = Never,
            Appview = _ => Json($"{{\"did\":\"{Did}\"}}"),
        });

        Assert.Equal(Did, await discovery.ResolveHandleToDidAsync(Handle));
    }

    // ── Stub plumbing ─────────────────────────────────────────

    /// <summary>A source that accepts the connection and then says nothing —
    /// the behaviour of a domain that drops packets on :443.</summary>
    private static readonly Func<HttpRequestMessage, HttpResponseMessage>? Never = null;

    private static HttpResponseMessage Text(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TxtAnswer(string did) =>
        Json($"{{\"Answer\":[{{\"data\":\"\\\"did={did}\\\"\"}}]}}");

    /// <summary>
    /// Routes by URL to one of three responders. A null responder never
    /// completes until the request's own token is cancelled, standing in for a
    /// host that swallows the connection.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? HttpsWellKnown { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? DnsTxt { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? Appview { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var responder =
                url.Contains("/.well-known/atproto-did", StringComparison.Ordinal) ? HttpsWellKnown
                : url.Contains("dns.google", StringComparison.Ordinal) ? DnsTxt
                : Appview;

            if (responder is null)
            {
                // Only ever ends by throwing; the throw is for the compiler.
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new OperationCanceledException(cancellationToken);
            }

            return responder(request);
        }
    }
}
