using System.Net;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Tests.Auth;
using ATProtoNet.Tests.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// The one path every request to an authorization server takes (pushed authorization, token
/// exchange, refresh, revocation), and the checks on what comes back from a refresh.
/// </summary>
public sealed class OAuthTransportTests : IDisposable
{
    private readonly StubServer _server = new();
    private readonly HttpClient _http;
    private readonly OAuthClient _oauth;

    public OAuthTransportTests()
    {
        _http = new HttpClient(_server);
        _oauth = OAuthClient(_http);
    }

    public void Dispose()
    {
        _oauth.Dispose();
        _http.Dispose();
        _server.Dispose();
    }

    private static HttpResponseMessage NonceChallenge(string nonce)
    {
        var challenge = OAuthError("use_dpop_nonce");
        challenge.Headers.TryAddWithoutValidation("DPoP-Nonce", nonce);
        return challenge;
    }

    private static string? NonceOf(StubRequest request) =>
        request.DPoPClaims.TryGetProperty("nonce", out var nonce) ? nonce.GetString() : null;

    // ──────────────────────────────────────────────────────────
    //  Nonces
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SignIn_TheNonceThePushedRequestLearned_IsUsedByTheTokenExchange()
    {
        _server.Respond = r => NonceOf(r) == "n-1" ? AuthorizationServer(r) : NonceChallenge("n-1");

        await SignInAsync(_oauth);

        var par = _server.To("/oauth/par");
        Assert.Equal(2, par.Count);
        Assert.Null(NonceOf(par[0]));
        Assert.Equal("n-1", NonceOf(par[1]));

        // One request: the exchange does not pay the challenge again.
        Assert.Equal("n-1", NonceOf(Assert.Single(_server.To("/oauth/token"))));
    }

    [Fact]
    public async Task SignIn_TheProofsNameEachEndpoint()
    {
        _server.Respond = AuthorizationServer;

        await SignInAsync(_oauth);

        Assert.Equal($"{Issuer}/oauth/par", _server.To("/oauth/par").Single().DPoPClaims.GetProperty("htu").GetString());
        Assert.Equal($"{Issuer}/oauth/token", _server.To("/oauth/token").Single().DPoPClaims.GetProperty("htu").GetString());
        Assert.All(_server.Requests, r => Assert.Equal("POST", r.DPoPClaims.GetProperty("htm").GetString()));
    }

    [Fact]
    public async Task UseDpopNonceWithoutANonce_IsNotRetried()
    {
        _server.Respond = _ => OAuthError("use_dpop_nonce");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => _oauth.RefreshAsync(OAuthSession(NewDPoPKey())));

        Assert.Equal("use_dpop_nonce", ex.Error);
        Assert.Single(_server.To(TokenEndpoint.AbsolutePath));
    }

    [Fact]
    public async Task ASecondNonceChallenge_Fails()
    {
        var calls = 0;
        _server.Respond = _ => NonceChallenge($"n-{Interlocked.Increment(ref calls)}");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => _oauth.RefreshAsync(OAuthSession(NewDPoPKey())));

        Assert.Equal("use_dpop_nonce", ex.Error);
        Assert.Equal(2, _server.To(TokenEndpoint.AbsolutePath).Count);
    }

    // ──────────────────────────────────────────────────────────
    //  Errors and responses
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AnErrorBody_IsAnOAuthExceptionCarryingTheError()
    {
        _server.Respond = r => r.Path == "/oauth/par" ? OAuthError("invalid_request") : AuthorizationServer(r);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => SignInAsync(_oauth));

        Assert.Equal("invalid_request", ex.Error);
        Assert.Contains("invalid_request described", ex.Message);
    }

    [Fact]
    public async Task AnErrorStatusWithoutAnOAuthBody_IsServerError()
    {
        _server.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>") };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => _oauth.RefreshAsync(OAuthSession(NewDPoPKey())));

        Assert.Equal("server_error", ex.Error);
    }

    [Fact]
    public async Task ASuccessBodyOverTheCap_IsRejected()
    {
        _server.Respond = _ => JsonResponse("{\"access_token\":\"" + new string('a', 70 * 1024) + "\"}");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => _oauth.RefreshAsync(OAuthSession(NewDPoPKey())));

        Assert.Equal("token_error", ex.Error);
    }

    [Fact]
    public async Task APushedAuthorizationResponseWithoutARequestUri_IsRejected()
    {
        _server.Respond = r => r.Path == "/oauth/par" ? JsonResponse("""{"expires_in":60}""") : AuthorizationServer(r);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => SignInAsync(_oauth));

        Assert.Equal("par_failed", ex.Error);
    }

    [Fact]
    public async Task EveryResponse_IsDisposed()
    {
        var responses = new List<TrackedContent>();
        var calls = 0;
        _server.Respond = r =>
        {
            var response = Interlocked.Increment(ref calls) == 1 ? NonceChallenge("n-1") : AuthorizationServer(r);
            var tracked = new TrackedContent(response.Content);
            lock (responses)
                responses.Add(tracked);
            response.Content = tracked;
            return response;
        };

        var (session, _) = await SignInAsync(_oauth);
        await _oauth.RevokeAsync(session);

        Assert.Equal(4, responses.Count); // PAR twice, token, revoke
        Assert.All(responses, content => Assert.True(content.Disposed));
    }

    // ──────────────────────────────────────────────────────────
    //  Where requests may go
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://auth.example.com/oauth/revoke")]
    [InlineData("https://auth.example.com/oauth/revoke?next=/admin")]
    [InlineData("https://user@auth.example.com/oauth/revoke")]
    public async Task AnEndpointTheUrlRulesRefuse_GetsNoRequest(string endpoint)
    {
        _server.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => _oauth.RevokeAsync(OAuthSession(NewDPoPKey(), revocationEndpoint: new Uri(endpoint))));

        Assert.Equal("invalid_server_url", ex.Error);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task WithoutAClientOfItsOwn_ALoopbackEndpoint_IsRefusedWithoutConnecting()
    {
        // The fetch policy's handler checks the address after DNS, as it does for metadata.
        using var server = new LoopbackServer(_ => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var client = new OAuthClient(
            new OAuthOptions
            {
                ClientMetadata = new OAuthClientMetadata { ClientId = ClientId, RedirectUris = [RedirectUri] },
                IdentityResolver = AliceIdentity(),
            },
            NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => client.RevokeAsync(
            OAuthSession(NewDPoPKey(), revocationEndpoint: new Uri($"https://localhost:{server.Port}/oauth/revoke"))));

        Assert.Equal("invalid_server_url", ex.Error);
        Assert.Equal(0, server.Connections);
    }

    // ──────────────────────────────────────────────────────────
    //  What a refresh accepts
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_TokensForAnotherAccount_AreRejected()
    {
        _server.Respond = _ => TokenResponseFor("did:plc:mallory");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => _oauth.RefreshAsync(OAuthSession(NewDPoPKey())));

        Assert.Equal("did_mismatch", ex.Error);
    }

    [Fact]
    public async Task Refresh_AResponseWithoutSub_IsRejected()
    {
        _server.Respond = _ => TokenResponseFor(sub: null);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => _oauth.RefreshAsync(OAuthSession(NewDPoPKey())));

        Assert.Equal("missing_sub", ex.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("transition:generic")]
    [InlineData("atproto2 transition:generic")]
    public async Task Refresh_AResponseWithoutTheAtprotoScope_IsRejected(string? scope)
    {
        _server.Respond = _ => TokenResponseFor(Alice.Value, scope);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => _oauth.RefreshAsync(OAuthSession(NewDPoPKey())));

        Assert.Equal("invalid_scope", ex.Error);
    }

    [Fact]
    public async Task Refresh_AnAccountNowOnAnotherAuthorizationServer_SpendsNoRefreshToken()
    {
        // The DID document now names a PDS whose authorization server is not the session's issuer.
        _server.ServesOAuthMetadata = false;
        _server.Respond = r => r.Uri.Host switch
        {
            "moved.example.com" when r.Path == "/.well-known/oauth-protected-resource" =>
                JsonResponse("""{"resource":"https://moved.example.com","authorization_servers":["https://other-auth.example.com"]}"""),
            "other-auth.example.com" => JsonResponse(AuthorizationServerMetadataJson("https://other-auth.example.com")),
            _ => TokenResponse("at-2", "rt-2"),
        };
        using var oauth = OAuthClient(_http, AliceIdentity(new Uri("https://moved.example.com")));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => oauth.RefreshAsync(OAuthSession(NewDPoPKey())));

        Assert.Equal("auth_server_mismatch", ex.Error);
        Assert.Empty(_server.To(TokenEndpoint.AbsolutePath));
    }

    [Fact]
    public async Task Refresh_AnAccountNowOnAnotherPdsOfTheSameServer_MovesTheSession()
    {
        _server.Respond = _ => TokenResponse("at-2", "rt-2");
        using var oauth = OAuthClient(_http, AliceIdentity(new Uri("https://pds2.example.com")));

        var refreshed = await oauth.RefreshAsync(OAuthSession(NewDPoPKey()));

        Assert.Equal(new Uri("https://pds2.example.com"), refreshed.ServiceEndpoint);
        Assert.Equal("at-2", refreshed.AccessToken);
    }

    [Fact]
    public async Task Refresh_AnUnresolvableAccount_SpendsNoRefreshToken()
    {
        var identity = Substitute.For<IIdentityResolver>();
        identity.ResolveAsync(Arg.Any<AtIdentifier>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResolvedIdentity>(
                new DidResolutionException("unreachable", DidResolutionErrorKind.NetworkError)));
        _server.Respond = _ => TokenResponse("at-2", "rt-2");
        using var oauth = OAuthClient(_http, identity);

        await Assert.ThrowsAsync<OAuthException>(() => oauth.RefreshAsync(OAuthSession(NewDPoPKey())));

        Assert.Empty(_server.To(TokenEndpoint.AbsolutePath));
    }

    [Fact]
    public async Task Refresh_ThroughAnAtProtoClient_KeepsTheSessionWhenTheResponseIsForAnotherAccount()
    {
        _server.Respond = r => r.Path == TokenEndpoint.AbsolutePath ? TokenResponseFor("did:plc:mallory") : JsonResponse("{}");
        await using var client = Client(_http);
        var original = OAuthSession(NewDPoPKey());
        await client.ApplySessionAsync(original, _oauth);

        await Assert.ThrowsAsync<OAuthException>(() => client.RefreshSessionAsync());

        Assert.Same(original, client.Session);
    }

    [Theory]
    [InlineData("Bearer")]
    [InlineData("")]
    public async Task Refresh_ATokenThatIsNotDPoPBound_IsRejected(string tokenType)
    {
        _server.Respond = _ => JsonResponse(
            $$"""{"access_token":"at-2","token_type":"{{tokenType}}","refresh_token":"rt-2","scope":"atproto","sub":"{{Alice.Value}}"}""");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => _oauth.RefreshAsync(OAuthSession(NewDPoPKey())));

        Assert.Equal("token_error", ex.Error);
    }

    [Fact]
    public async Task SignIn_ATokenThatIsNotDPoPBound_IsRejectedAndRevoked()
    {
        _server.Respond = r => r.Path == "/oauth/token"
            ? JsonResponse($$"""{"access_token":"at-1","token_type":"Bearer","refresh_token":"rt-1","scope":"atproto","sub":"{{Alice.Value}}"}""")
            : AuthorizationServer(r);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => SignInAsync(_oauth));

        Assert.Equal("token_error", ex.Error);
        Assert.Equal("rt-1", Assert.Single(_server.To("/oauth/revoke")).Form["token"]);
    }

    [Fact]
    public async Task Refresh_TokensForAnotherAccount_AreRevoked()
    {
        _server.Respond = r => r.Path == RevocationEndpoint.AbsolutePath
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : TokenResponseFor("did:plc:mallory");

        await Assert.ThrowsAsync<OAuthException>(
            () => _oauth.RefreshAsync(OAuthSession(NewDPoPKey(), revocationEndpoint: RevocationEndpoint)));

        var revoke = Assert.Single(_server.To(RevocationEndpoint.AbsolutePath));
        Assert.Equal("rt-2", revoke.Form["token"]);
        Assert.Equal("refresh_token", revoke.Form["token_type_hint"]);
    }

    // ──────────────────────────────────────────────────────────
    //  Issuer verification at the code exchange
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SignIn_StartedFromAServerUrl_ADidThatResolvesToAnotherAuthorizationServer_IsRejectedAndRevoked()
    {
        // A URL- or entryway-started login trusts the callback's `sub`, not the URL it began at:
        // the account's DID document must lead back to the issuer that answered.
        const string movedPds = "https://moved.example.com";
        const string otherAuth = "https://other-auth.example.com";
        _server.ServesOAuthMetadata = false;
        _server.Respond = r => (r.Uri.Host, r.Path) switch
        {
            ("pds.example.com", "/.well-known/oauth-protected-resource") =>
                JsonResponse($$"""{"resource":"https://pds.example.com","authorization_servers":["{{Issuer}}"]}"""),
            ("auth.example.com", "/.well-known/oauth-authorization-server") =>
                JsonResponse(AuthorizationServerMetadataJson(Issuer)),
            ("moved.example.com", "/.well-known/oauth-protected-resource") =>
                JsonResponse($$"""{"resource":"{{movedPds}}","authorization_servers":["{{otherAuth}}"]}"""),
            ("other-auth.example.com", "/.well-known/oauth-authorization-server") =>
                JsonResponse(AuthorizationServerMetadataJson(otherAuth)),
            _ => AuthorizationServer(r),
        };
        using var oauth = OAuthClient(_http, AliceIdentity(new Uri(movedPds)));

        var authorization = await oauth.StartAuthorizationAsync("https://pds.example.com", RedirectUri);
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => oauth.CompleteAuthorizationAsync("code", authorization.State, Issuer));

        Assert.Equal("auth_server_mismatch", ex.Error);
        Assert.Equal("rt-1", Assert.Single(_server.To("/oauth/revoke")).Form["token"]);
    }

    [Fact]
    public async Task SignIn_TheMetadataCacheFromStartingTheLogin_IsBypassedAtTheCallback()
    {
        // Starting a login caches the PDS's protected-resource metadata for five minutes. If the
        // callback's issuer check reused that cache, an account moved to another authorization
        // server between the redirect and the callback would go undetected.
        var moved = false;
        _server.ServesOAuthMetadata = false;
        _server.Respond = r => (r.Uri.Host, r.Path, moved) switch
        {
            ("pds.example.com", "/.well-known/oauth-protected-resource", false) =>
                JsonResponse($$"""{"resource":"https://pds.example.com","authorization_servers":["{{Issuer}}"]}"""),
            ("pds.example.com", "/.well-known/oauth-protected-resource", true) =>
                JsonResponse("""{"resource":"https://pds.example.com","authorization_servers":["https://other-auth.example.com"]}"""),
            ("auth.example.com", "/.well-known/oauth-authorization-server", _) =>
                JsonResponse(AuthorizationServerMetadataJson(Issuer)),
            ("other-auth.example.com", "/.well-known/oauth-authorization-server", _) =>
                JsonResponse(AuthorizationServerMetadataJson("https://other-auth.example.com")),
            _ => AuthorizationServer(r),
        };

        var authorization = await _oauth.StartAuthorizationAsync(AliceHandle.Value, RedirectUri);
        var cachedRequests = _server.To("/.well-known/oauth-protected-resource").Count;
        moved = true;

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => _oauth.CompleteAuthorizationAsync("code", authorization.State, Issuer));

        Assert.Equal("auth_server_mismatch", ex.Error);
        Assert.True(_server.To("/.well-known/oauth-protected-resource").Count > cachedRequests,
            "the callback's issuer check must fetch the PDS's metadata again rather than trust the cache from starting the login");
    }

    /// <summary>Content that records its disposal, around the real body.</summary>
    private sealed class TrackedContent(HttpContent inner) : HttpContent
    {
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            inner.CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = inner.Headers.ContentLength ?? -1;
            return length >= 0;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
