using System.Security.Claims;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Tests.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Server.Authentication;

/// <summary>
/// Tests for the transparent cross-origin cookie relay in <see cref="AtProtoOAuthService"/>.
/// When the OAuth callback arrives on a different origin than the user's browser
/// (e.g., http://127.0.0.1 vs https://localhost), the SDK relays the auth cookie
/// back to the correct origin via a one-time code. A relay nobody redeems holds a live session,
/// which is revoked when it expires or is evicted.
/// </summary>
public sealed class CookieRelayTests : IDisposable
{
    private readonly AtProtoOAuthService _service;
    private readonly StubServer _server = new() { Respond = AuthorizationServer };
    private readonly HttpClient _http;

    public CookieRelayTests()
    {
        _service = new AtProtoOAuthService(new AtProtoOAuthServerOptions(), NullLoggerFactory.Instance);
        _http = new HttpClient(_server);
    }

    public void Dispose()
    {
        _service.Dispose();
        _http.Dispose();
        _server.Dispose();
    }

    /// <summary>A service whose OAuth client talks to the stub, on a clock the test moves.</summary>
    private AtProtoOAuthService RevokingService(ManualTimeProvider time)
    {
        var service = new AtProtoOAuthService(
            new AtProtoOAuthServerOptions
            {
                ClientMetadata = new OAuthClientMetadata
                {
                    ClientId = ClientId,
                    RedirectUris = ["https://app.example.com/atproto/callback"],
                },
                HttpClient = _http,
            },
            NullLoggerFactory.Instance,
            AliceIdentity())
        {
            TimeProvider = time,
        };

        // A relay exists only after a callback, which has built the client.
        _ = service.Client;
        return service;
    }

    #region TryRedeemRelayCodeAsync — Invalid inputs

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("DEADBEEF1234567890ABCDEF12345678")]
    public async Task TryRedeemRelayCodeAsync_NoSuchCode_ReturnsNull(string? code)
    {
        Assert.Null(await _service.TryRedeemRelayCodeAsync(CreateHttpContext(), code));
    }

    #endregion

    #region TryRedeemRelayCodeAsync — Valid relay codes

    [Fact]
    public async Task TryRedeemRelayCodeAsync_ValidCode_ReturnsReturnUrl()
    {
        var code = InsertRelayEntry(_service, "/dashboard", TimeSpan.FromMinutes(2));

        var result = await _service.TryRedeemRelayCodeAsync(CreateHttpContext(), code);

        Assert.Equal("/dashboard", result);
    }

    [Fact]
    public async Task TryRedeemRelayCodeAsync_ValidCode_IssuesCookie()
    {
        var code = InsertRelayEntry(_service, "/", TimeSpan.FromMinutes(2));
        var authService = Substitute.For<IAuthenticationService>();
        var context = CreateHttpContext(authService);

        await _service.TryRedeemRelayCodeAsync(context, code);

        await authService.Received(1).SignInAsync(
            context,
            Arg.Any<string>(),
            Arg.Any<ClaimsPrincipal>(),
            Arg.Any<AuthenticationProperties>());
    }

    [Fact]
    public async Task TryRedeemRelayCodeAsync_ValidCode_IsConsumedOnFirstUse()
    {
        var code = InsertRelayEntry(_service, "/", TimeSpan.FromMinutes(2));
        var context = CreateHttpContext();

        Assert.NotNull(await _service.TryRedeemRelayCodeAsync(context, code));
        Assert.Null(await _service.TryRedeemRelayCodeAsync(context, code));
    }

    #endregion

    #region TryRedeemRelayCodeAsync — Expired codes

    [Fact]
    public async Task TryRedeemRelayCodeAsync_ExpiredCode_ReturnsNullAndIssuesNoCookie()
    {
        var code = InsertRelayEntry(_service, "/", TimeSpan.FromSeconds(-1));
        var authService = Substitute.For<IAuthenticationService>();

        var result = await _service.TryRedeemRelayCodeAsync(CreateHttpContext(authService), code);

        Assert.Null(result);
        await authService.DidNotReceive().SignInAsync(
            Arg.Any<HttpContext>(),
            Arg.Any<string>(),
            Arg.Any<ClaimsPrincipal>(),
            Arg.Any<AuthenticationProperties>());
    }

    #endregion

    #region Browser binding

    [Fact]
    public async Task TryRedeemRelayCodeAsync_WithoutTheBindingCookie_IssuesNoCookie()
    {
        // Someone else's relay link: the browser redeeming it did not start the login.
        var code = InsertRelayEntry(_service, "/", TimeSpan.FromMinutes(2));
        var authService = Substitute.For<IAuthenticationService>();
        var context = CreateHttpContext(authService, withBinding: false);

        var result = await _service.TryRedeemRelayCodeAsync(context, code);

        Assert.Null(result);
        await authService.DidNotReceive().SignInAsync(
            Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
    }

    #endregion

    #region Relays nobody redeems

    [Fact]
    public void RelayCodeCleanup_RemovesExpiredEntries()
    {
        var time = new ManualTimeProvider();
        using var service = RevokingService(time);
        InsertRelayEntry(service, "/expired", TimeSpan.FromSeconds(10), time.Now);
        InsertRelayEntry(service, "/valid", TimeSpan.FromMinutes(2), time.Now);

        time.Now += TimeSpan.FromSeconds(30);
        service.CleanupExpiredRelayCodes();

        Assert.Equal(1, service.PendingRelayCount);
    }

    [Fact]
    public async Task ARelayNobodyRedeems_IsRevokedWhenItExpires()
    {
        var time = new ManualTimeProvider();
        using var service = RevokingService(time);
        InsertRelayEntry(service, "/", AtProtoOAuthService.RelayLifetime, time.Now, Session("rt-unredeemed"));

        time.Now += AtProtoOAuthService.RelayLifetime;
        Assert.Single(time.ActiveTimers).Fire();

        await Eventually(() => _server.To("/oauth/revoke").Count == 1);
        Assert.Equal("rt-unredeemed", _server.To("/oauth/revoke")[0].Form["token"]);
        Assert.Equal(0, service.PendingRelayCount);
    }

    [Fact]
    public async Task ARedeemedRelay_IsNotRevokedLater()
    {
        var time = new ManualTimeProvider();
        using var service = RevokingService(time);
        var code = InsertRelayEntry(service, "/", AtProtoOAuthService.RelayLifetime, time.Now, Session("rt-redeemed"));
        var timer = Assert.Single(time.ActiveTimers);

        Assert.Equal("/", await service.TryRedeemRelayCodeAsync(CreateHttpContext(), code));
        time.Now += AtProtoOAuthService.RelayLifetime;
        timer.Fire();

        Assert.True(timer.Disposed);
        Assert.Empty(_server.To("/oauth/revoke"));
    }

    [Fact]
    public async Task TooManyWaitingRelays_EvictAndRevokeTheOldest()
    {
        var time = new ManualTimeProvider();
        using var service = RevokingService(time);

        InsertRelayEntry(service, "/", AtProtoOAuthService.RelayLifetime, time.Now, Session("rt-oldest"));
        for (var i = 0; i < AtProtoOAuthService.MaxRelayEntries; i++)
        {
            time.Now += TimeSpan.FromMilliseconds(1);
            InsertRelayEntry(service, "/", AtProtoOAuthService.RelayLifetime, time.Now, Session($"rt-{i}"));
        }

        await Eventually(() => _server.To("/oauth/revoke").Count == 1);
        Assert.Equal("rt-oldest", _server.To("/oauth/revoke")[0].Form["token"]);
        Assert.Equal(AtProtoOAuthService.MaxRelayEntries, service.PendingRelayCount);
    }

    #endregion

    #region Disposed service

    [Fact]
    public async Task TryRedeemRelayCodeAsync_DisposedService_ThrowsObjectDisposedException()
    {
        var service = new AtProtoOAuthService(new AtProtoOAuthServerOptions(), NullLoggerFactory.Instance);
        service.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.TryRedeemRelayCodeAsync(CreateHttpContext(), "some-code"));
    }

    #endregion

    #region Helpers

    /// <summary>The browser's binding cookie value in these tests, and its hash as a login keeps it.</summary>
    private const string Binding = "dGVzdC1iaW5kaW5nLXZhbHVlLWZvci10aGUtcmVsYXk";

    private static readonly string BindingHash =
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(Binding)));

    private static OAuthSession Session(string refreshToken) =>
        OAuthSession(NewDPoPKey(), refreshToken: refreshToken, revocationEndpoint: RevocationEndpoint);

    /// <summary>
    /// Creates a <see cref="DefaultHttpContext"/> with a mock <see cref="IAuthenticationService"/>,
    /// presenting the binding cookie unless <paramref name="withBinding"/> is false.
    /// </summary>
    private static DefaultHttpContext CreateHttpContext(IAuthenticationService? authService = null, bool withBinding = true)
    {
        authService ??= Substitute.For<IAuthenticationService>();
        var services = new ServiceCollection();
        services.AddSingleton(authService);
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost", 7203);
        if (withBinding)
            context.Request.Headers.Cookie = $"__Host-atproto-oauth-binding={Binding}";
        return context;
    }

    /// <summary>Adds a relay entry, as a callback on another loopback origin does, and returns its code.</summary>
    private static string InsertRelayEntry(
        AtProtoOAuthService service, string returnUrl, TimeSpan expiresIn, DateTimeOffset? now = null, OAuthSession? session = null)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "did:plc:test123"),
            new Claim(ClaimTypes.Name, "test.bsky.social"),
        }, "ATProto"));

        return service.AddRelayEntry(new AtProtoOAuthService.RelayEntry(
            principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) },
            returnUrl,
            (now ?? DateTimeOffset.UtcNow) + expiresIn,
            session,
            BindingHash));
    }

    #endregion
}
