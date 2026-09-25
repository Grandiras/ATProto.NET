using System.Net;
using System.Security.Claims;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
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
        new(new ClaimsIdentity([new Claim(type, value)], "test"));

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
