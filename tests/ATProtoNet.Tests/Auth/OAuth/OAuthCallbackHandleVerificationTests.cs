using System.Net;
using System.Text;
using ATProtoNet.Auth.OAuth;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// Handle verification at the OAuth callback is best-effort and must never cost a
/// user their sign-in (Issue #42). By the time <see cref="OAuthClient.CompleteAuthorizationAsync"/>
/// reaches it, the authoritative DID is already established by the token response's
/// <c>sub</c>; the only thing left to learn is whether the handle the PDS advertises
/// resolves back to that DID.
/// </summary>
/// <remarks>
/// A probe against a parked or firewalled handle domain — or against any host
/// while the <see cref="HttpClient"/> has a <c>ConnectTimeout</c> — fails with
/// <see cref="TaskCanceledException"/>, not the <see cref="HttpRequestException"/>
/// a refused connection produces. Both mean the same thing here ("could not
/// verify"), so both must land on <see cref="OAuthSessionResult.IsHandleVerified"/>
/// <c>false</c>. Only the caller's own cancellation aborts the flow.
/// </remarks>
public class OAuthCallbackHandleVerificationTests
{
    private const string Handle = "alice.example.com";
    private const string Did = "did:plc:alice";
    private const string Issuer = "https://auth.example.com";
    private const string RedirectUri = "https://app.example.com/callback";

    /// <summary>Short enough to keep the silent-authority cases quick.</summary>
    private static readonly TimeSpan ShortBudget = TimeSpan.FromMilliseconds(300);

    /// <summary>How a timed-out connection surfaces from <see cref="HttpClient"/>.</summary>
    private static Exception ConnectTimeout() =>
        new TaskCanceledException(
            "The operation was canceled.",
            new TimeoutException("A connection could not be established within the configured ConnectTimeout."));

    [Fact]
    public async Task Complete_DidDocumentFetchTimesOut_CompletesUnverifiedInsteadOfThrowing()
    {
        var stub = new FlowStub();
        using var client = Client(stub);
        var state = await StartAsync(client);

        // The verification round-trip starts by re-reading the DID document; a
        // timeout there used to escape CompleteAuthorizationAsync entirely.
        stub.DidDocumentAtCallback = _ => throw ConnectTimeout();

        using var session = await client.CompleteAuthorizationAsync("code", state, Issuer);

        Assert.False(session.IsHandleVerified);
        Assert.Equal(Did, session.Did);
        Assert.Equal(Did, session.Handle);
    }

    [Fact]
    public async Task Complete_HandleAuthoritiesTimeOut_CompletesUnverifiedInsteadOfThrowing()
    {
        var stub = new FlowStub
        {
            // A domain that refuses fast and one that never answers are both
            // "unverifiable"; neither may abort a login.
            WellKnownAtCallback = _ => throw ConnectTimeout(),
            DnsAtCallback = _ => throw ConnectTimeout(),
        };
        using var client = Client(stub);
        var state = await StartAsync(client);

        using var session = await client.CompleteAuthorizationAsync("code", state, Issuer);

        Assert.False(session.IsHandleVerified);
        Assert.Equal(Did, session.Handle);
    }

    [Fact]
    public async Task Complete_SilentWellKnownButDnsAnswers_VerifiesFromDns()
    {
        // The reported case: DNS-only handle whose apex black-holes port 443.
        var stub = new FlowStub { WellKnownAtCallback = HangForever };
        using var client = Client(stub);
        var state = await StartAsync(client);

        using var session = await client.CompleteAuthorizationAsync("code", state, Issuer);

        Assert.True(session.IsHandleVerified);
        Assert.Equal(Handle, session.Handle);
    }

    [Fact]
    public async Task Complete_HandleResolvesToAnotherDid_CompletesUnverified()
    {
        var stub = new FlowStub { DnsAtCallback = _ => Task.FromResult(TxtAnswer("did:plc:impostor")) };
        using var client = Client(stub);
        var state = await StartAsync(client);

        using var session = await client.CompleteAuthorizationAsync("code", state, Issuer);

        Assert.False(session.IsHandleVerified);
        Assert.Equal(Did, session.Handle);
    }

    [Fact]
    public async Task Complete_CallerCancelsDuringVerification_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var stub = new FlowStub();
        using var client = Client(stub);
        var state = await StartAsync(client);

        // Caller cancellation is the one case that still aborts: the DID may be
        // in hand, but nobody is waiting for the session any more.
        stub.DidDocumentAtCallback = ct =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("unreachable");
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CompleteAuthorizationAsync("code", state, Issuer, cts.Token));
    }

    // ── Flow plumbing ─────────────────────────────────────────

    private static OAuthClient Client(FlowStub stub) => new(
        new OAuthOptions
        {
            ClientMetadata = new OAuthClientMetadata
            {
                ClientId = "https://app.example.com/client-metadata.json",
                RedirectUris = [RedirectUri],
            },
            HandleResolutionTimeout = ShortBudget,
        },
        new HttpClient(stub),
        NullLogger.Instance);

    /// <summary>
    /// Runs the pre-redirect half of the flow (discovery + PAR) and returns the
    /// state parameter the callback is completed with.
    /// </summary>
    private static async Task<string> StartAsync(OAuthClient client)
    {
        var (_, state) = await client.StartAuthorizationAsync(Handle, RedirectUri);
        return state;
    }

    private static Task<HttpResponseMessage> HangForever(CancellationToken cancellationToken) =>
        Task.Delay(Timeout.Infinite, cancellationToken)
            .ContinueWith<HttpResponseMessage>(_ => throw new OperationCanceledException(cancellationToken),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TxtAnswer(string did) =>
        Json($$"""{"Answer":[{"data":"\"did={{did}}\""}]}""");

    private static HttpResponseMessage DidDocument() =>
        Json($$"""
        {
          "id": "{{Did}}",
          "alsoKnownAs": ["at://{{Handle}}"],
          "service": [{
            "id": "#atproto_pds",
            "type": "AtprotoPersonalDataServer",
            "serviceEndpoint": "https://pds.example.com"
          }]
        }
        """);

    /// <summary>
    /// A whole OAuth deployment in one handler: handle authorities, the PLC
    /// directory, PDS/authorization-server metadata, PAR, and the token endpoint.
    /// The <c>*AtCallback</c> hooks replace a responder from the moment the token
    /// exchange has happened, which is where the identity checks under test run.
    /// </summary>
    private sealed class FlowStub : HttpMessageHandler
    {
        private bool _tokensIssued;

        public Func<CancellationToken, Task<HttpResponseMessage>>? WellKnownAtCallback { get; set; }
        public Func<CancellationToken, Task<HttpResponseMessage>>? DnsAtCallback { get; set; }
        public Func<CancellationToken, Task<HttpResponseMessage>>? DidDocumentAtCallback { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("/.well-known/atproto-did", StringComparison.Ordinal))
                return await Respond(WellKnownAtCallback, cancellationToken, () => HangForever(cancellationToken));

            if (url.Contains("dns.google", StringComparison.Ordinal))
                return await Respond(DnsAtCallback, cancellationToken, () => Task.FromResult(TxtAnswer(Did)));

            if (url.StartsWith("https://plc.directory/", StringComparison.Ordinal))
                return await Respond(DidDocumentAtCallback, cancellationToken, () => Task.FromResult(DidDocument()));

            if (url.EndsWith("/.well-known/oauth-protected-resource", StringComparison.Ordinal))
                return Json($$"""{"resource":"https://pds.example.com","authorization_servers":["{{Issuer}}"]}""");

            if (url.EndsWith("/.well-known/oauth-authorization-server", StringComparison.Ordinal))
                return Json($$"""
                {
                  "issuer": "{{Issuer}}",
                  "authorization_endpoint": "{{Issuer}}/oauth/authorize",
                  "token_endpoint": "{{Issuer}}/oauth/token",
                  "pushed_authorization_request_endpoint": "{{Issuer}}/oauth/par",
                  "scopes_supported": ["atproto", "transition:generic"],
                  "dpop_signing_alg_values_supported": ["ES256"]
                }
                """);

            if (url.EndsWith("/oauth/par", StringComparison.Ordinal))
                return Json("""{"request_uri":"urn:ietf:params:oauth:request_uri:stub","expires_in":60}""");

            if (url.EndsWith("/oauth/token", StringComparison.Ordinal))
            {
                _tokensIssued = true;
                return Json($$"""
                {
                  "access_token": "at",
                  "token_type": "DPoP",
                  "refresh_token": "rt",
                  "expires_in": 3600,
                  "scope": "atproto transition:generic",
                  "sub": "{{Did}}"
                }
                """);
            }

            throw new InvalidOperationException($"Unexpected request to {url}");
        }

        private Task<HttpResponseMessage> Respond(
            Func<CancellationToken, Task<HttpResponseMessage>>? callbackStage,
            CancellationToken cancellationToken,
            Func<Task<HttpResponseMessage>> healthy) =>
            _tokensIssued && callbackStage is not null ? callbackStage(cancellationToken) : healthy();
    }
}
