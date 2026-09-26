using System.Net;
using System.Security.Claims;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Services;
using ATProtoNet.Tests.Auth;
using Microsoft.Extensions.Logging;
using NSubstitute;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Server;

public sealed class AtProtoClientFactoryTests : IDisposable
{
    private readonly StubServer _server = new();
    private readonly IAtProtoSessionStore _store = new InMemoryAtProtoSessionStore();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AtProtoClientFactory _factory;

    public AtProtoClientFactoryTests()
    {
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _loggerFactory = Substitute.For<ILoggerFactory>();

        _httpClientFactory.CreateClient("AtProtoClient").Returns(_ => new HttpClient(_server, disposeHandler: false));
        _loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        _factory = new AtProtoClientFactory(_store, _httpClientFactory, _loggerFactory);
    }

    public void Dispose() => _server.Dispose();

    private static ClaimsPrincipal User(string type, string value) =>
        new(new ClaimsIdentity([new Claim(type, value)], "ATProto"));

    [Fact]
    public async Task CreateClientForUserAsync_ReturnsNull_WhenUserHasNoClaims()
    {
        Assert.Null(await _factory.CreateClientForUserAsync(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public async Task CreateClientForUserAsync_ReturnsNull_WhenNoDidClaim()
    {
        Assert.Null(await _factory.CreateClientForUserAsync(User(ClaimTypes.Name, "alice")));
    }

    [Fact]
    public async Task CreateClientForUserAsync_ReturnsNull_WhenTheDidClaimIsNotADid()
    {
        Assert.Null(await _factory.CreateClientForUserAsync(User("did", "alice")));
    }

    [Fact]
    public async Task CreateClientForUserAsync_ReturnsNull_WhenNoSessionIsStored()
    {
        Assert.Null(await _factory.CreateClientForUserAsync(User("did", "did:plc:abc123")));
    }

    [Fact]
    public async Task CreateClientForUserAsync_ReadsTheDidClaim_ThenFallsBackToNameIdentifier()
    {
        var store = Substitute.For<IAtProtoSessionStore>();
        store.GetAsync(Arg.Any<Did>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<AtProtoSession?>(null));
        var factory = new AtProtoClientFactory(store, _httpClientFactory, _loggerFactory);

        await factory.CreateClientForUserAsync(User("did", "did:plc:fromdid"));
        await factory.CreateClientForUserAsync(User(ClaimTypes.NameIdentifier, "did:plc:fallback"));

        await store.Received(1).GetAsync(Did.Parse("did:plc:fromdid"), Arg.Any<CancellationToken>());
        await store.Received(1).GetAsync(Did.Parse("did:plc:fallback"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateClientForUserAsync_ReturnsAClientOnTheStoredSession()
    {
        var session = OAuthSession(NewDPoPKey());
        await _store.SetAsync(session);

        await using var client = await _factory.CreateClientForUserAsync(User("did", "did:plc:alice"));

        Assert.NotNull(client);
        Assert.Same(session, client.Session);
        Assert.Equal(Pds, client.ServiceUrl);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task APerRequestClient_RefreshesOnDemandAndWritesTheRotatedTokensBack()
    {
        var refreshed = AccessJwt("a2");
        _server.Respond = r => r.Nsid switch
        {
            "com.atproto.server.refreshSession" => SessionResponse(refreshed, "r2"),
            "com.example.ping" when r.Authorization == $"Bearer {refreshed}" => JsonResponse("{}"),
            "com.example.ping" => XrpcError(XrpcErrors.ExpiredToken),
            _ => throw new InvalidOperationException(r.Nsid),
        };
        await _store.SetAsync(PasswordSession(AccessJwt("a1"), "r1"));

        await using (var client = await _factory.CreateClientForUserAsync(User("did", "did:plc:alice")))
            await client!.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));

        // The next request's client starts from the rotated refresh token, not the spent one.
        var stored = Assert.IsType<PasswordSession>(await _store.GetAsync(Alice));
        Assert.Equal("r2", stored.RefreshJwt);
        Assert.Equal(refreshed, stored.AccessJwt);
    }

    [Fact]
    public async Task ClientsOfOneAccount_ShareTheImportedDPoPKey()
    {
        _server.Respond = _ => JsonResponse("{}");
        var key = NewDPoPKey();
        await _store.SetAsync(OAuthSession(key));
        var user = User("did", "did:plc:alice");

        var first = await _factory.CreateClientForUserAsync(user);
        await using (var second = await _factory.CreateClientForUserAsync(user))
        {
            // Releasing one request's client leaves the key usable by the others.
            await first!.DisposeAsync();
            await second!.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));
        }

        Assert.Equal(1, _factory.CachedKeyCount);
        using var expected = new ATProtoNet.Auth.OAuth.DPoPProofGenerator(key);
        Assert.Equal(expected.KeyThumbprint, Thumbprint(Assert.Single(_server.Requests).DPoP!));
    }

    [Fact]
    public async Task ASessionWithAnotherKey_ReplacesTheCachedOne()
    {
        _server.Respond = _ => JsonResponse("{}");
        var user = User("did", "did:plc:alice");
        await _store.SetAsync(OAuthSession(NewDPoPKey()));
        await (await _factory.CreateClientForUserAsync(user))!.DisposeAsync();

        var newKey = NewDPoPKey();
        await _store.SetAsync(OAuthSession(newKey));
        await using (var client = await _factory.CreateClientForUserAsync(user))
            await client!.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));

        using var expected = new ATProtoNet.Auth.OAuth.DPoPProofGenerator(newKey);
        Assert.Equal(expected.KeyThumbprint, Thumbprint(Assert.Single(_server.Requests).DPoP!));
        Assert.Equal(1, _factory.CachedKeyCount);
    }

    [Fact]
    public async Task TheKeyCache_IsBounded()
    {
        for (var i = 0; i <= AtProtoClientFactory.MaxCachedKeys; i++)
        {
            var did = Did.Parse($"did:plc:user{i}");
            await _store.SetAsync(OAuthSession(NewDPoPKey()) with { Did = did });
            await (await _factory.CreateClientForUserAsync(User("did", did.Value)))!.DisposeAsync();
        }

        Assert.InRange(_factory.CachedKeyCount, 1, AtProtoClientFactory.MaxCachedKeys);
    }

    [Fact]
    public async Task ConcurrentRequestsOfOneUser_SpendTheRefreshTokenOnce()
    {
        var live = "rt-1";
        var issued = 1;
        _server.Handler = async (r, ct) =>
        {
            if (r.Path != TokenEndpoint.AbsolutePath)
                return JsonResponse("{}");

            // Long enough for every other request to reach its refresh meanwhile.
            await Task.Delay(100, ct);
            lock (_server)
            {
                if (r.Form["refresh_token"] != live)
                    return OAuthError("invalid_grant");
                live = $"rt-{++issued}";
                return TokenResponse($"at-{issued}", live);
            }
        };
        using var http = new HttpClient(_server, disposeHandler: false);
        using var oauth = OAuthClient(http);
        var factory = new AtProtoClientFactory(
            _store, _httpClientFactory, _loggerFactory, oauth, new InProcessSessionRefreshCoordinator());
        await _store.SetAsync(OAuthSession(NewDPoPKey(), expiresAt: DateTimeOffset.UtcNow.AddSeconds(20)));
        var user = User("did", "did:plc:alice");

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var client = await factory.CreateClientForUserAsync(user);
            await client!.QueryAsync<JsonElement>(Nsid.Parse("com.example.ping"));
        })));

        Assert.Single(_server.To(TokenEndpoint.AbsolutePath));
        Assert.Equal("rt-2", Assert.IsType<OAuthSession>(await _store.GetAsync(Alice)).RefreshToken);
    }

    private static string Thumbprint(string proof)
    {
        Assert.True(Jwt.TryDecode(proof, out var jwt, out _));
        var jwk = jwt.Header.GetProperty("jwk");
        return ATProtoNet.Auth.OAuth.DPoP.Thumbprint(new ATProtoNet.Auth.OAuth.JsonWebKey
        {
            Kty = jwk.GetProperty("kty").GetString()!,
            Crv = jwk.GetProperty("crv").GetString(),
            X = jwk.GetProperty("x").GetString(),
            Y = jwk.GetProperty("y").GetString(),
        });
    }

    [Fact]
    public async Task AServiceAuthCaller_DoesNotGetTheAccountsStoredSession()
    {
        // Service auth issues the same did claim for whoever holds a token naming that DID.
        await _store.SetAsync(OAuthSession(NewDPoPKey()));
        var caller = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AtProtoClaimTypes.Did, Alice.Value), new Claim(AtProtoClaimTypes.Audience, "did:web:app.example.com#svc")],
            AtProtoServiceAuthDefaults.AuthenticationScheme,
            AtProtoClaimTypes.Did,
            roleType: null));

        Assert.Null(await _factory.CreateClientForUserAsync(caller));
    }

    [Fact]
    public async Task APrincipalOfBothSchemes_GetsTheSignedInUsersSession()
    {
        await _store.SetAsync(OAuthSession(NewDPoPKey()));
        var principal = new ClaimsPrincipal(
        [
            new ClaimsIdentity([new Claim(AtProtoClaimTypes.Did, "did:plc:bob")], AtProtoServiceAuthDefaults.AuthenticationScheme),
            new ClaimsIdentity([new Claim(AtProtoClaimTypes.Did, Alice.Value)], "ATProto"),
        ]);

        await using var client = await _factory.CreateClientForUserAsync(principal);

        Assert.Equal(Alice, client?.Did);
    }

    [Fact]
    public async Task AnIdentityOfAnotherScheme_CountsWhenItSaysItIsTheOAuthUser()
    {
        await _store.SetAsync(OAuthSession(NewDPoPKey()));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AtProtoClaimTypes.Did, Alice.Value), new Claim(AtProtoClaimTypes.AuthMethod, "oauth")], "MyOwnLogin"));

        await using var client = await _factory.CreateClientForUserAsync(principal);

        Assert.Equal(Alice, client?.Did);
    }

    [Fact]
    public async Task CreateClientForUserAsync_ThrowsOnNullUser()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _factory.CreateClientForUserAsync(null!));
    }

    [Fact]
    public void Constructor_ThrowsOnNullSessionStore()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AtProtoClientFactory(null!, _httpClientFactory, _loggerFactory));
    }

    [Fact]
    public void Constructor_ThrowsOnNullHttpClientFactory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AtProtoClientFactory(_store, null!, _loggerFactory));
    }

    [Fact]
    public void Constructor_ThrowsOnNullLoggerFactory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AtProtoClientFactory(_store, _httpClientFactory, null!));
    }
}
