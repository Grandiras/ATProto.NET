using System.Net;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Http;
using ATProtoNet.Tests.Auth;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// The process-wide DPoP nonce cache: nonces are the server's, kept by origin, and shared by
/// every transport and OAuth client, so a client created for one request does not pay a
/// <c>use_dpop_nonce</c> round trip on its first call.
/// </summary>
public sealed class DPoPNonceCacheTests : IDisposable
{
    private readonly StubServer _server = new();
    private readonly HttpClient _http;

    public DPoPNonceCacheTests() => _http = new HttpClient(_server);

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    [Fact]
    public void Get_IsKeyedByOrigin()
    {
        var cache = new DPoPNonceCache();

        cache.Set(new Uri("https://pds.example.com/xrpc/com.example.ping?x=1"), "n-1");

        Assert.Equal("n-1", cache.Get(new Uri("https://PDS.example.com:443/oauth/token")));
        Assert.Null(cache.Get(new Uri("https://pds.example.com:8443/xrpc/com.example.ping")));
        Assert.Null(cache.Get(new Uri("http://pds.example.com/xrpc/com.example.ping")));
        Assert.Null(cache.Get(new Uri("https://other.example.com/xrpc/com.example.ping")));
    }

    [Fact]
    public void Observe_KeepsTheResponsesNonce()
    {
        var cache = new DPoPNonceCache();
        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.TryAddWithoutValidation("DPoP-Nonce", "n-2");

        Assert.Equal("n-2", cache.Observe(new Uri("https://pds.example.com/"), response));
        Assert.Equal("n-2", cache.Get(new Uri("https://pds.example.com/xrpc/x")));
    }

    [Fact]
    public void Observe_AnOverlongNonce_IsNotKept()
    {
        var cache = new DPoPNonceCache();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("DPoP-Nonce", new string('n', DPoPNonceCache.MaxNonceLength + 1));

        Assert.Null(cache.Observe(new Uri("https://pds.example.com/"), response));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Set_PastCapacity_StaysBounded()
    {
        var cache = new DPoPNonceCache();

        for (var i = 0; i < DPoPNonceCache.Capacity + 100; i++)
            cache.Set(new Uri($"https://host{i}.example.com/"), "n");

        Assert.Equal(DPoPNonceCache.Capacity, cache.Count);
    }

    [Fact]
    public async Task ANewTransport_SendsTheNonceAnotherOneReceived()
    {
        var cache = new DPoPNonceCache();
        using var dpop = new DPoPProofGenerator();
        _server.Respond = r =>
        {
            var nonce = r.DPoPClaims.TryGetProperty("nonce", out var sent) ? sent.GetString() : null;
            if (nonce == "n-1")
                return JsonResponse("{}");

            var challenge = JsonResponse("""{"error":"use_dpop_nonce"}""", HttpStatusCode.Unauthorized);
            challenge.Headers.TryAddWithoutValidation("DPoP-Nonce", "n-1");
            return challenge;
        };

        // The first transport pays the challenge once...
        var first = new XrpcClient(_http, Pds) { NonceCache = cache };
        first.SetOAuthTokens("at-1", null, dpop);
        await first.QueryAsync<JsonElement>("com.example.ping");
        Assert.Equal(2, _server.Requests.Count);

        // ...and a transport created afterwards, as a server creates one per request, does not.
        var second = new XrpcClient(_http, Pds) { NonceCache = cache };
        second.SetOAuthTokens("at-1", null, dpop);
        await second.QueryAsync<JsonElement>("com.example.ping");

        Assert.Equal(3, _server.Requests.Count);
        Assert.Equal("n-1", _server.Requests[2].DPoPClaims.GetProperty("nonce").GetString());
    }

    [Fact]
    public async Task ANonceIsSentOnlyToItsOrigin()
    {
        var cache = new DPoPNonceCache();
        cache.Set(Pds, "pds-nonce");
        using var dpop = new DPoPProofGenerator();
        _server.Respond = _ => JsonResponse("{}");

        var other = new XrpcClient(_http, new Uri("https://other-pds.example.com/")) { NonceCache = cache };
        other.SetOAuthTokens("at-1", null, dpop);
        await other.QueryAsync<JsonElement>("com.example.ping");

        Assert.False(Assert.Single(_server.Requests).DPoPClaims.TryGetProperty("nonce", out _));
    }

    [Fact]
    public async Task TheAuthorizationServersNonce_IsSharedWithANewOAuthClient()
    {
        var cache = new DPoPNonceCache();
        _server.Respond = r =>
        {
            if (r.DPoPClaims.TryGetProperty("nonce", out var sent) && sent.GetString() == "as-nonce")
                return TokenResponse("at-2", "rt-2");

            var challenge = OAuthError("use_dpop_nonce");
            challenge.Headers.TryAddWithoutValidation("DPoP-Nonce", "as-nonce");
            return challenge;
        };

        using (var first = new OAuthClient(Options()) { NonceCache = cache })
            await first.RefreshAsync(OAuthSession(NewDPoPKey()));
        Assert.Equal(2, _server.To(TokenEndpoint.AbsolutePath).Count);

        using (var second = new OAuthClient(Options()) { NonceCache = cache })
            await second.RefreshAsync(OAuthSession(NewDPoPKey()));

        var attempts = _server.To(TokenEndpoint.AbsolutePath);
        Assert.Equal(3, attempts.Count);
        Assert.Equal("as-nonce", attempts[2].DPoPClaims.GetProperty("nonce").GetString());
    }

    private OAuthOptions Options() => new()
    {
        ClientMetadata = new OAuthClientMetadata { ClientId = ClientId, RedirectUris = [RedirectUri] },
        HttpClient = _http,
        IdentityResolver = AliceIdentity(),
    };
}
