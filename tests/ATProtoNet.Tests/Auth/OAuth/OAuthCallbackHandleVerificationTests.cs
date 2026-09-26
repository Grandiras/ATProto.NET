using System.Net;
using System.Text;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// The identity checks at the OAuth callback. Whatever the login started from, the account the
/// tokens name is resolved afresh at the callback and its authorization server must be the
/// issuer; handle verification there is best-effort and must never cost a user their sign-in
/// (Issue #42).
/// </summary>
/// <remarks>
/// A probe against a parked or firewalled handle domain — or against any host
/// while the <see cref="HttpClient"/> has a <c>ConnectTimeout</c> — fails with
/// <see cref="TaskCanceledException"/>, not the <see cref="HttpRequestException"/>
/// a refused connection produces. Both mean the same thing here ("could not
/// verify"), so both must land on a session whose handle is <c>handle.invalid</c>.
/// Only the caller's own cancellation aborts the flow.
/// </remarks>
public class OAuthCallbackHandleVerificationTests
{
    private const string Handle = "alice.example.com";
    private const string Did = "did:plc:alice";
    private const string Issuer = "https://auth.example.com";
    private const string PdsUrl = "https://pds.example.com";
    private const string RedirectUri = "https://app.example.com/callback";

    /// <summary>Short enough to keep the silent-authority cases quick.</summary>
    private static readonly TimeSpan ShortBudget = TimeSpan.FromMilliseconds(300);

    /// <summary>How a timed-out connection surfaces from <see cref="HttpClient"/>.</summary>
    private static Exception ConnectTimeout() =>
        new TaskCanceledException(
            "The operation was canceled.",
            new TimeoutException("A connection could not be established within the configured ConnectTimeout."));

    // ── Started from a handle ─────────────────────────────────

    [Fact]
    public async Task Complete_StartedFromAHandle_ResolvesTheAccountAfresh()
    {
        var stub = new FlowStub();
        using var client = Client(stub);
        var state = await StartAsync(client);

        var session = await client.CompleteAuthorizationAsync("code", state, Issuer);

        Assert.Equal(Did, session.Did.Value);
        Assert.Equal(Handle, session.Handle.Value);
        Assert.Equal(new Uri(PdsUrl), session.ServiceEndpoint);
        Assert.NotEqual(0, stub.IdentityRequestsAfterTokens);
    }

    [Fact]
    public async Task Complete_StartedFromAHandle_DidDocumentFetchTimesOut_FailsAndRevokes()
    {
        // What the start resolved is not trusted at the callback: without the document now, the
        // issuer cannot be confirmed, so the login fails closed and the tokens are revoked.
        var stub = new FlowStub();
        using var client = Client(stub);
        var state = await StartAsync(client);
        stub.DidDocumentAtCallback = _ => throw ConnectTimeout();

        await Assert.ThrowsAsync<OAuthException>(() => client.CompleteAuthorizationAsync("code", state, Issuer));

        Assert.Equal(["rt"], stub.RevokedTokens);
    }

    [Fact]
    public async Task Complete_StartedFromAHandle_TokensForAnotherAccount_AreRefused()
    {
        var stub = new FlowStub { TokenSub = "did:plc:mallory" };
        using var client = Client(stub);
        var state = await StartAsync(client);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => client.CompleteAuthorizationAsync("code", state, Issuer));

        Assert.Equal("did_mismatch", ex.Error);
        Assert.Equal(["rt"], stub.RevokedTokens);
    }

    [Fact]
    public async Task Complete_ReturnsASessionWithEverythingARefreshNeeds()
    {
        var stub = new FlowStub();
        using var client = Client(stub);
        var state = await StartAsync(client);

        var session = await client.CompleteAuthorizationAsync("code", state, Issuer);

        Assert.Equal(new Uri(PdsUrl), session.ServiceEndpoint);
        Assert.Equal("at", session.AccessToken);
        Assert.Equal("rt", session.RefreshToken);
        Assert.Equal(Issuer, session.Issuer);
        Assert.Equal(new Uri($"{Issuer}/oauth/token"), session.TokenEndpoint);
        Assert.Equal(new Uri($"{Issuer}/oauth/revoke"), session.RevocationEndpoint);
        Assert.Null(session.ClientKeyId);
        Assert.InRange(session.ExpiresAt!.Value, DateTimeOffset.UtcNow.AddMinutes(59), DateTimeOffset.UtcNow.AddMinutes(61));

        using var key = new DPoPProofGenerator(session.DPoPKey.ToArray());
        Assert.False(string.IsNullOrEmpty(key.KeyThumbprint));
    }

    // ── Started from a server URL ─────────────────────────────

    [Fact]
    public async Task Complete_StartedFromAServer_ResolvesTheAccountAndItsPds()
    {
        var stub = new FlowStub();
        using var client = Client(stub);
        var state = await StartAsync(client, fromServer: true);

        var session = await client.CompleteAuthorizationAsync("code", state, Issuer);

        Assert.Equal(Did, session.Did.Value);
        Assert.Equal(Handle, session.Handle.Value);
        Assert.Equal(new Uri(PdsUrl), session.ServiceEndpoint);
        Assert.NotEqual(0, stub.IdentityRequestsAfterTokens);
    }

    [Fact]
    public async Task Complete_StartedFromAServer_DidDocumentFetchTimesOut_FailsTheIssuerCheck()
    {
        // The account's authorization server cannot be confirmed without its DID document, so
        // the login fails closed.
        var stub = new FlowStub();
        using var client = Client(stub);
        var state = await StartAsync(client, fromServer: true);
        stub.DidDocumentAtCallback = _ => throw ConnectTimeout();

        await Assert.ThrowsAsync<OAuthException>(() => client.CompleteAuthorizationAsync("code", state, Issuer));
    }

    [Fact]
    public async Task Complete_StartedFromAServer_HandleAuthoritiesTimeOut_CompletesUnverifiedInsteadOfThrowing()
    {
        var stub = new FlowStub
        {
            // A domain that refuses fast and one that never answers are both
            // "unverifiable"; neither may abort a login.
            WellKnownAtCallback = _ => throw ConnectTimeout(),
            DnsAtCallback = _ => throw ConnectTimeout(),
        };
        using var client = Client(stub);
        var state = await StartAsync(client, fromServer: true);

        var session = await client.CompleteAuthorizationAsync("code", state, Issuer);

        Assert.Equal("handle.invalid", session.Handle.Value);
    }

    [Fact]
    public async Task Complete_StartedFromAServer_SilentWellKnownButDnsAnswers_VerifiesFromDns()
    {
        // The reported case: DNS-only handle whose apex black-holes port 443.
        var stub = new FlowStub { WellKnownAtCallback = HangForever };
        using var client = Client(stub);
        var state = await StartAsync(client, fromServer: true);

        var session = await client.CompleteAuthorizationAsync("code", state, Issuer);

        Assert.Equal(Handle, session.Handle.Value);
    }

    [Fact]
    public async Task Complete_StartedFromAServer_HandleResolvesToAnotherDid_CompletesUnverified()
    {
        var stub = new FlowStub { DnsAtCallback = _ => Task.FromResult(TxtAnswer("did:plc:impostor")) };
        using var client = Client(stub);
        var state = await StartAsync(client, fromServer: true);

        var session = await client.CompleteAuthorizationAsync("code", state, Issuer);

        Assert.Equal("handle.invalid", session.Handle.Value);
    }

    [Fact]
    public async Task Complete_StartedFromAServer_CallerCancelsDuringVerification_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var stub = new FlowStub();
        using var client = Client(stub);
        var state = await StartAsync(client, fromServer: true);

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

    private static OAuthClient Client(FlowStub stub)
    {
        var http = new HttpClient(stub);

        // The stub stands in for every host, identity ones included. No DID document cache, so
        // every identity request the client makes reaches the stub.
        var identityOptions = new IdentityResolverOptions { HandleResolutionTimeout = ShortBudget };
        var identity = new IdentityResolver(
            new DidResolver(
                new PlcClient(http, new Uri("https://plc.directory/"), identityOptions),
                new DidWebResolver(http, identityOptions)),
            new HandleResolver(http, identityOptions));

        return new OAuthClient(
            new OAuthOptions
            {
                ClientMetadata = new OAuthClientMetadata
                {
                    ClientId = "https://app.example.com/oauth-client-metadata.json",
                    RedirectUris = [RedirectUri],
                },
                IdentityResolver = identity,
                HttpClient = http,
            },
            NullLogger.Instance)
        {
            NonceCache = new DPoPNonceCache(),
        };
    }

    /// <summary>
    /// Runs the pre-redirect half of the flow (discovery + PAR), from the handle or from the PDS
    /// URL, and returns the state parameter the callback is completed with.
    /// </summary>
    private static async Task<string> StartAsync(OAuthClient client, bool fromServer = false)
    {
        var authorization = await client.StartAuthorizationAsync(fromServer ? PdsUrl : Handle, RedirectUri);
        return authorization.State;
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
            "serviceEndpoint": "{{PdsUrl}}"
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
        private volatile bool _tokensIssued;
        private int _identityRequestsAfterTokens;

        public Func<CancellationToken, Task<HttpResponseMessage>>? WellKnownAtCallback { get; set; }
        public Func<CancellationToken, Task<HttpResponseMessage>>? DnsAtCallback { get; set; }
        public Func<CancellationToken, Task<HttpResponseMessage>>? DidDocumentAtCallback { get; set; }

        /// <summary>The account the token endpoint issues tokens for.</summary>
        public string TokenSub { get; init; } = Did;

        /// <summary>Requests to a handle authority or the PLC directory after the token exchange.</summary>
        public int IdentityRequestsAfterTokens => Volatile.Read(ref _identityRequestsAfterTokens);

        private readonly List<string> _revoked = [];

        /// <summary>The tokens revoked at the revocation endpoint, in order.</summary>
        public IReadOnlyList<string> RevokedTokens
        {
            get
            {
                lock (_revoked)
                    return [.. _revoked];
            }
        }

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
                return Json($$"""{"resource":"{{PdsUrl}}","authorization_servers":["{{Issuer}}"]}""");

            if (url.EndsWith("/.well-known/oauth-authorization-server", StringComparison.Ordinal))
                return Json(SessionKit.AuthorizationServerMetadataJson(Issuer));

            if (url.EndsWith("/oauth/revoke", StringComparison.Ordinal))
            {
                var form = await request.Content!.ReadAsStringAsync(cancellationToken);
                lock (_revoked)
                    _revoked.Add(System.Web.HttpUtility.ParseQueryString(form)["token"]!);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

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
                  "sub": "{{TokenSub}}"
                }
                """);
            }

            throw new InvalidOperationException($"Unexpected request to {url}");
        }

        private Task<HttpResponseMessage> Respond(
            Func<CancellationToken, Task<HttpResponseMessage>>? callbackStage,
            CancellationToken cancellationToken,
            Func<Task<HttpResponseMessage>> healthy)
        {
            if (!_tokensIssued)
                return healthy();

            Interlocked.Increment(ref _identityRequestsAfterTokens);
            return callbackStage is not null ? callbackStage(cancellationToken) : healthy();
        }
    }
}
