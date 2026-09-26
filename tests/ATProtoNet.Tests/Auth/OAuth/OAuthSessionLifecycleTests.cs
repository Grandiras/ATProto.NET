using System.Net;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Tests.Auth;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// The lifecycle of an OAuth session against a scripted PDS and authorization server:
/// DPoP-bound requests, token refresh (and its failures), reactive refresh on
/// <c>invalid_token</c>, and revocation on sign-out.
/// </summary>
public sealed class OAuthSessionLifecycleTests : IDisposable
{
    private static readonly Nsid Ping = Nsid.Parse("com.example.ping");

    private readonly StubServer _server = new();
    private readonly HttpClient _http;
    private readonly OAuthClient _oauth;
    private readonly byte[] _key = NewDPoPKey();
    private readonly List<AtProtoSessionChange> _changes = [];

    public OAuthSessionLifecycleTests()
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

    private AtProtoClient NewClient(IAtProtoSessionStore? store = null)
    {
        var client = Client(_http, store, instanceUrl: "https://entryway.example.com");
        client.SessionChanged += (_, change) =>
        {
            lock (_changes)
                _changes.Add(change.Change);
        };
        return client;
    }

    private static HttpResponseMessage InvalidTokenChallenge()
    {
        var response = JsonResponse("""{"error":"InvalidToken","message":"\"exp\" claim timestamp check failed"}""", HttpStatusCode.Unauthorized);
        response.Headers.TryAddWithoutValidation("WWW-Authenticate", """DPoP error="invalid_token", error_description="token expired" """);
        return response;
    }

    [Fact]
    public async Task ApplySessionAsync_SendsDPoPBoundRequestsToTheSessionsPds()
    {
        _server.Respond = _ => JsonResponse("{}");
        await using var client = NewClient();

        await client.ApplySessionAsync(OAuthSession(_key), _oauth);
        await client.QueryAsync<JsonElement>(Ping);

        var request = Assert.Single(_server.Requests);
        Assert.Equal("pds.example.com", request.Uri.Host);
        Assert.Equal("DPoP at-1", request.Authorization);
        Assert.Equal("https://pds.example.com/xrpc/com.example.ping", request.DPoPClaims.GetProperty("htu").GetString());
        Assert.True(request.DPoPClaims.TryGetProperty("ath", out _));
    }

    [Fact]
    public async Task RefreshSessionAsync_ExchangesTheRefreshTokenWithADPoPProof()
    {
        _server.Respond = r => r.Path == TokenEndpoint.AbsolutePath ? TokenResponse("at-2", "rt-2") : JsonResponse("{}");
        var store = new InMemoryAtProtoSessionStore();
        await using var client = NewClient(store);
        await client.ApplySessionAsync(OAuthSession(_key), _oauth);

        await client.RefreshSessionAsync();
        await client.QueryAsync<JsonElement>(Ping);

        var refresh = _server.To(TokenEndpoint.AbsolutePath).Single();
        Assert.Equal("refresh_token", refresh.Form["grant_type"]);
        Assert.Equal("rt-1", refresh.Form["refresh_token"]);
        Assert.Equal(ClientId, refresh.Form["client_id"]);
        Assert.Equal(TokenEndpoint.ToString(), refresh.DPoPClaims.GetProperty("htu").GetString());
        Assert.False(refresh.DPoPClaims.TryGetProperty("ath", out _));

        var session = Assert.IsType<OAuthSession>(client.Session);
        Assert.Equal("at-2", session.AccessToken);
        Assert.Equal("rt-2", session.RefreshToken);
        Assert.InRange(session.ExpiresAt!.Value, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
        Assert.Same(session, await store.GetAsync(Alice));
        Assert.Equal("DPoP at-2", _server.To("com.example.ping").Single().Authorization);
    }

    [Fact]
    public async Task RefreshSessionAsync_InvalidGrant_ExpiresTheSessionAndStoresNoToken()
    {
        _server.Respond = _ => OAuthError("invalid_grant");
        var store = new InMemoryAtProtoSessionStore();
        await using var client = NewClient(store);
        await client.ApplySessionAsync(OAuthSession(_key), _oauth);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => client.RefreshSessionAsync());

        Assert.Equal("invalid_grant", ex.Error);
        Assert.False(client.IsAuthenticated);
        Assert.Null(await store.GetAsync(Alice));
        Assert.Equal([AtProtoSessionChange.Created, AtProtoSessionChange.Expired], _changes);
    }

    [Fact]
    public async Task RefreshSessionAsync_ErrorStatus_IsNeverTakenForTokens()
    {
        // An error body is JSON too; read as a token response it would be an empty access token.
        _server.Respond = _ => OAuthError("server_error", HttpStatusCode.InternalServerError);
        var store = new InMemoryAtProtoSessionStore();
        await using var client = NewClient(store);
        var original = OAuthSession(_key);
        await client.ApplySessionAsync(original, _oauth);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => client.RefreshSessionAsync());

        Assert.Equal("server_error", ex.Error);
        Assert.Same(original, client.Session);
        Assert.Same(original, await store.GetAsync(Alice));
    }

    [Fact]
    public async Task RefreshSessionAsync_SuccessWithoutAnAccessToken_IsRejected()
    {
        _server.Respond = _ => JsonResponse("""{"token_type":"DPoP","expires_in":900}""");
        await using var client = NewClient();
        var original = OAuthSession(_key);
        await client.ApplySessionAsync(original, _oauth);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => client.RefreshSessionAsync());

        Assert.Equal("token_error", ex.Error);
        Assert.Same(original, client.Session);
    }

    [Fact]
    public async Task ADPoPNonceChallengeOnRefresh_IsAnsweredAndRemembered()
    {
        var calls = 0;
        _server.Respond = _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                var challenge = OAuthError("use_dpop_nonce");
                challenge.Headers.TryAddWithoutValidation("DPoP-Nonce", "nonce-1");
                return challenge;
            }

            return TokenResponse($"at-{calls}", $"rt-{calls}");
        };
        await using var client = NewClient();
        await client.ApplySessionAsync(OAuthSession(_key), _oauth);

        await client.RefreshSessionAsync();
        await client.RefreshSessionAsync();

        var attempts = _server.To(TokenEndpoint.AbsolutePath);
        Assert.Equal(3, attempts.Count);
        Assert.False(attempts[0].DPoPClaims.TryGetProperty("nonce", out _));
        Assert.Equal("nonce-1", attempts[1].DPoPClaims.GetProperty("nonce").GetString());
        Assert.Equal("nonce-1", attempts[2].DPoPClaims.GetProperty("nonce").GetString());
    }

    [Fact]
    public async Task AnInvalidTokenChallenge_RefreshesAndRetriesOnce()
    {
        _server.Respond = r =>
            r.Path == TokenEndpoint.AbsolutePath ? TokenResponse("at-2", "rt-2")
            : r.Authorization == "DPoP at-2" ? JsonResponse("""{"ok":true}""")
            : InvalidTokenChallenge();
        await using var client = NewClient();
        await client.ApplySessionAsync(OAuthSession(_key), _oauth);

        var result = await client.QueryAsync<JsonElement>(Ping);

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(["DPoP at-1", "DPoP at-2"], _server.To("com.example.ping").Select(r => r.Authorization));
        Assert.Single(_server.To(TokenEndpoint.AbsolutePath));
    }

    [Fact]
    public async Task ConcurrentInvalidTokenChallenges_SpendTheRefreshTokenOnce()
    {
        const int callers = 12;
        var rejections = 0;
        var allRejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Handler = async (r, ct) =>
        {
            if (r.Path == TokenEndpoint.AbsolutePath)
            {
                await allRejected.Task.WaitAsync(ct);
                return TokenResponse("at-2", "rt-2");
            }

            if (r.Authorization == "DPoP at-2")
                return JsonResponse("{}");

            if (Interlocked.Increment(ref rejections) == callers)
                allRejected.SetResult();
            return InvalidTokenChallenge();
        };
        await using var client = NewClient();
        await client.ApplySessionAsync(OAuthSession(_key), _oauth);

        await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => client.QueryAsync<JsonElement>(Ping)));

        Assert.Single(_server.To(TokenEndpoint.AbsolutePath));
    }

    [Fact]
    public async Task AnAccessTokenAboutToExpire_IsRefreshedBeforeTheRequest()
    {
        _server.Respond = r => r.Path == TokenEndpoint.AbsolutePath ? TokenResponse("at-2", "rt-2") : JsonResponse("{}");
        await using var client = NewClient();
        await client.ApplySessionAsync(OAuthSession(_key, expiresAt: DateTimeOffset.UtcNow.AddSeconds(20)), _oauth);

        await client.QueryAsync<JsonElement>(Ping);

        Assert.Equal([TokenEndpoint.AbsolutePath, "/xrpc/com.example.ping"], _server.Requests.Select(r => r.Path));
        Assert.Equal("DPoP at-2", _server.To("com.example.ping").Single().Authorization);
    }

    [Fact]
    public async Task RefreshSessionAsync_WithoutAnOAuthClient_Throws()
    {
        await using var client = NewClient();
        await client.ApplySessionAsync(OAuthSession(_key));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RefreshSessionAsync());

        Assert.True(client.IsAuthenticated);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task ReApplyingWithoutAnOAuthClient_KeepsTheOneGivenBefore()
    {
        _server.Respond = _ => TokenResponse("at-2", "rt-2");
        await using var client = NewClient();
        await client.ApplySessionAsync(OAuthSession(_key), _oauth);

        await client.ApplySessionAsync(OAuthSession(NewDPoPKey(), accessToken: "at-other"));
        await client.RefreshSessionAsync();

        Assert.Equal("at-2", Assert.IsType<OAuthSession>(client.Session).AccessToken);
    }

    [Fact]
    public async Task APasswordLoginAfterAnOAuthSession_RefreshesThroughRefreshSession()
    {
        _server.Respond = r => r.Nsid switch
        {
            "com.atproto.server.createSession" => SessionResponse(AccessJwt("a1"), "r1"),
            "com.atproto.server.refreshSession" => SessionResponse(AccessJwt("a2"), "r2"),
            _ => throw new InvalidOperationException($"Unexpected request to {r.Uri}"),
        };
        await using var client = NewClient();
        await client.ApplySessionAsync(OAuthSession(_key), _oauth);

        await client.LoginAsync("alice.test", "password");
        await client.RefreshSessionAsync();

        Assert.Single(_server.To("com.atproto.server.refreshSession"));
        Assert.Empty(_server.To(TokenEndpoint.AbsolutePath));
        Assert.IsType<PasswordSession>(client.Session);
    }

    [Fact]
    public async Task ApplySessionAsync_AMalformedDPoPKey_LeavesTheClientUntouched()
    {
        await using var client = NewClient();
        await client.ApplySessionAsync(OAuthSession(_key), _oauth);
        var installed = client.Session;

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ApplySessionAsync(OAuthSession([1, 2, 3], accessToken: "at-broken"), _oauth));

        Assert.Same(installed, client.Session);
    }

    // ──────────────────────────────────────────────────────────
    //  Sign-out
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LogoutAsync_RevokesTheRefreshTokenWithADPoPProof()
    {
        _server.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
        var store = new InMemoryAtProtoSessionStore();
        await using var client = NewClient(store);
        await client.ApplySessionAsync(OAuthSession(_key, revocationEndpoint: RevocationEndpoint), _oauth);

        await client.LogoutAsync();

        var revoke = Assert.Single(_server.Requests);
        Assert.Equal(RevocationEndpoint, revoke.Uri);
        Assert.Equal("rt-1", revoke.Form["token"]);
        Assert.Equal("refresh_token", revoke.Form["token_type_hint"]);
        Assert.Equal(ClientId, revoke.Form["client_id"]);
        Assert.Equal(RevocationEndpoint.ToString(), revoke.DPoPClaims.GetProperty("htu").GetString());
        Assert.False(client.IsAuthenticated);
        Assert.Null(await store.GetAsync(Alice));
        Assert.Equal([AtProtoSessionChange.Created, AtProtoSessionChange.Removed], _changes);
    }

    [Fact]
    public async Task LogoutAsync_RevocationEndpointNotKnown_LooksItUp()
    {
        _server.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
        await using var client = NewClient();
        await client.ApplySessionAsync(OAuthSession(_key, revocationEndpoint: null), _oauth);

        await client.LogoutAsync();

        Assert.Equal(new Uri($"{Issuer}/.well-known/oauth-authorization-server"), Assert.Single(_server.MetadataRequests));
        Assert.Single(_server.To(RevocationEndpoint.AbsolutePath));
    }

    [Fact]
    public async Task LogoutAsync_WithoutARefreshToken_RevokesTheAccessToken()
    {
        _server.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
        await using var client = NewClient();
        await client.ApplySessionAsync(OAuthSession(_key, refreshToken: null, revocationEndpoint: RevocationEndpoint), _oauth);

        await client.LogoutAsync();

        var revoke = Assert.Single(_server.Requests);
        Assert.Equal("at-1", revoke.Form["token"]);
        Assert.Equal("access_token", revoke.Form["token_type_hint"]);
    }

    [Fact]
    public async Task LogoutAsync_RevocationFails_TearsDownLocallyThenThrows()
    {
        _server.Respond = _ => OAuthError("temporarily_unavailable", HttpStatusCode.ServiceUnavailable);
        var store = new InMemoryAtProtoSessionStore();
        await using var client = NewClient(store);
        await client.ApplySessionAsync(OAuthSession(_key, revocationEndpoint: RevocationEndpoint), _oauth);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => client.LogoutAsync());

        Assert.Equal("temporarily_unavailable", ex.Error);
        Assert.False(client.IsAuthenticated);
        Assert.Null(await store.GetAsync(Alice));
        Assert.Equal(AtProtoSessionChange.Removed, _changes[^1]);
    }

    [Fact]
    public async Task LogoutAsync_NeverCallsDeleteSessionForAnOAuthSession()
    {
        _server.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
        await using var client = NewClient();
        await client.ApplySessionAsync(OAuthSession(_key, revocationEndpoint: RevocationEndpoint), _oauth);

        await client.LogoutAsync();

        Assert.Empty(_server.To("com.atproto.server.deleteSession"));
    }

    // ──────────────────────────────────────────────────────────
    //  Ownership
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_LeavesTheOAuthClientAndTheSessionUsable()
    {
        _server.Respond = _ => TokenResponse("at-2", "rt-2");
        var session = OAuthSession(_key);
        var client = NewClient();
        await client.ApplySessionAsync(session, _oauth);

        await client.DisposeAsync();

        // The session is a value with its key as bytes; the client disposed only its own key object.
        var refreshed = await _oauth.RefreshAsync(session);
        Assert.Equal("at-2", refreshed.AccessToken);
    }

    [Fact]
    public async Task OAuthClientRefreshAsync_ReturnsANewSessionAndLeavesTheOriginal()
    {
        _server.Respond = _ => TokenResponse("at-2", refreshToken: null);
        var session = OAuthSession(_key);

        var refreshed = await _oauth.RefreshAsync(session);

        Assert.Equal("at-1", session.AccessToken);
        Assert.Equal("at-2", refreshed.AccessToken);
        Assert.Equal("rt-1", refreshed.RefreshToken); // a response without one keeps the current refresh token
        Assert.Equal(session.DPoPKey, refreshed.DPoPKey);
    }
}
