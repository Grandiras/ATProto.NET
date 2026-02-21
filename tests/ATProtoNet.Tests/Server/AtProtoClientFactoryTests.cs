using System.Security.Claims;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Server.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ATProtoNet.Tests.Server;

public class AtProtoClientFactoryTests
{
    private readonly IAtProtoTokenStore _tokenStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AtProtoClientFactory _factory;

    public AtProtoClientFactoryTests()
    {
        _tokenStore = Substitute.For<IAtProtoTokenStore>();
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _loggerFactory = Substitute.For<ILoggerFactory>();

        _httpClientFactory.CreateClient("AtProtoClient").Returns(new HttpClient());
        _loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        _factory = new AtProtoClientFactory(_tokenStore, _httpClientFactory, _loggerFactory);
    }

    [Fact]
    public async Task CreateClientForUserAsync_ReturnsNull_WhenUserHasNoClaims()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        var client = await _factory.CreateClientForUserAsync(user);

        Assert.Null(client);
    }

    [Fact]
    public async Task CreateClientForUserAsync_ReturnsNull_WhenNoDidClaim()
    {
        var claims = new[] { new Claim(ClaimTypes.Name, "alice") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var client = await _factory.CreateClientForUserAsync(user);

        Assert.Null(client);
    }

    [Fact]
    public async Task CreateClientForUserAsync_ReturnsNull_WhenNoTokensStored()
    {
        var claims = new[] { new Claim("did", "did:plc:abc123") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        _tokenStore.GetAsync("did:plc:abc123", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AtProtoTokenData?>(null));

        var client = await _factory.CreateClientForUserAsync(user);

        Assert.Null(client);
    }

    [Fact]
    public async Task CreateClientForUserAsync_UsesDidClaim()
    {
        var claims = new[] { new Claim("did", "did:plc:abc123") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        _tokenStore.GetAsync("did:plc:abc123", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AtProtoTokenData?>(null));

        await _factory.CreateClientForUserAsync(user);

        await _tokenStore.Received(1).GetAsync("did:plc:abc123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateClientForUserAsync_FallsBackToNameIdentifierClaim()
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "did:plc:fallback") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        _tokenStore.GetAsync("did:plc:fallback", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AtProtoTokenData?>(null));

        await _factory.CreateClientForUserAsync(user);

        await _tokenStore.Received(1).GetAsync("did:plc:fallback", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateClientForUserAsync_ReturnsClient_WhenTokensExist()
    {
        // Generate a real DPoP key for testing
        using var dpop = new DPoPProofGenerator();
        var privateKey = dpop.ExportPrivateKey();

        var tokenData = new AtProtoTokenData
        {
            Did = "did:plc:abc123",
            Handle = "alice.bsky.social",
            AccessToken = "access-token",
            RefreshToken = "refresh-token",
            PdsUrl = "https://bsky.social",
            Issuer = "https://bsky.social",
            TokenEndpoint = "https://bsky.social/oauth/token",
            DPoPPrivateKey = privateKey,
            ExpiresIn = 3600,
            Scope = "atproto",
        };

        var claims = new[] { new Claim("did", "did:plc:abc123") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        _tokenStore.GetAsync("did:plc:abc123", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AtProtoTokenData?>(tokenData));

        await using var client = await _factory.CreateClientForUserAsync(user);

        Assert.NotNull(client);
        Assert.True(client.IsAuthenticated);
        Assert.Equal("did:plc:abc123", client.Did);
        Assert.Equal("alice.bsky.social", client.Handle);
    }

    [Fact]
    public async Task CreateClientForUserAsync_ThrowsOnNullUser()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _factory.CreateClientForUserAsync(null!));
    }

    [Fact]
    public void Constructor_ThrowsOnNullTokenStore()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AtProtoClientFactory(null!, _httpClientFactory, _loggerFactory));
    }

    [Fact]
    public void Constructor_ThrowsOnNullHttpClientFactory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AtProtoClientFactory(_tokenStore, null!, _loggerFactory));
    }

    [Fact]
    public void Constructor_ThrowsOnNullLoggerFactory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AtProtoClientFactory(_tokenStore, _httpClientFactory, null!));
    }
}
