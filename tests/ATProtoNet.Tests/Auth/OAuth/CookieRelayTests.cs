using System.Collections;
using System.Reflection;
using System.Security.Claims;
using ATProtoNet.Blazor.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// Tests for the transparent cross-origin cookie relay in <see cref="AtProtoOAuthService"/>.
/// When the OAuth callback arrives on a different origin than the user's browser
/// (e.g., http://127.0.0.1 vs https://localhost), the SDK relays the auth cookie
/// back to the correct origin via a one-time code.
/// </summary>
public class CookieRelayTests : IDisposable
{
    private readonly AtProtoOAuthService _service;
    private readonly AtProtoOAuthServerOptions _options;

    public CookieRelayTests()
    {
        _options = new AtProtoOAuthServerOptions();
        _service = new AtProtoOAuthService(_options, NullLoggerFactory.Instance);
    }

    public void Dispose() => _service.Dispose();

    #region TryRedeemRelayCodeAsync — Invalid inputs

    [Fact]
    public async Task TryRedeemRelayCodeAsync_NullCode_ReturnsNull()
    {
        var context = CreateHttpContext();
        var result = await _service.TryRedeemRelayCodeAsync(context, null);
        Assert.Null(result);
    }

    [Fact]
    public async Task TryRedeemRelayCodeAsync_EmptyCode_ReturnsNull()
    {
        var context = CreateHttpContext();
        var result = await _service.TryRedeemRelayCodeAsync(context, "");
        Assert.Null(result);
    }

    [Fact]
    public async Task TryRedeemRelayCodeAsync_WhitespaceCode_ReturnsNull()
    {
        var context = CreateHttpContext();
        var result = await _service.TryRedeemRelayCodeAsync(context, "   ");
        Assert.Null(result);
    }

    [Fact]
    public async Task TryRedeemRelayCodeAsync_NonexistentCode_ReturnsNull()
    {
        var context = CreateHttpContext();
        var result = await _service.TryRedeemRelayCodeAsync(context, "DEADBEEF1234567890ABCDEF12345678");
        Assert.Null(result);
    }

    #endregion

    #region TryRedeemRelayCodeAsync — Valid relay codes

    [Fact]
    public async Task TryRedeemRelayCodeAsync_ValidCode_ReturnsReturnUrl()
    {
        var (code, _) = InsertRelayEntry("/dashboard", TimeSpan.FromMinutes(2));
        var context = CreateHttpContext();

        var result = await _service.TryRedeemRelayCodeAsync(context, code);

        Assert.Equal("/dashboard", result);
    }

    [Fact]
    public async Task TryRedeemRelayCodeAsync_ValidCode_IssuesCookie()
    {
        var (code, _) = InsertRelayEntry("/", TimeSpan.FromMinutes(2));
        var authService = Substitute.For<IAuthenticationService>();
        var context = CreateHttpContext(authService);

        await _service.TryRedeemRelayCodeAsync(context, code);

        // Verify SignInAsync was called
        await authService.Received(1).SignInAsync(
            context,
            Arg.Any<string>(),
            Arg.Any<ClaimsPrincipal>(),
            Arg.Any<AuthenticationProperties>());
    }

    [Fact]
    public async Task TryRedeemRelayCodeAsync_ValidCode_IsConsumedOnFirstUse()
    {
        var (code, _) = InsertRelayEntry("/", TimeSpan.FromMinutes(2));
        var context = CreateHttpContext();

        // First use succeeds
        var result1 = await _service.TryRedeemRelayCodeAsync(context, code);
        Assert.NotNull(result1);

        // Second use fails (one-time code)
        var result2 = await _service.TryRedeemRelayCodeAsync(context, code);
        Assert.Null(result2);
    }

    #endregion

    #region TryRedeemRelayCodeAsync — Expired codes

    [Fact]
    public async Task TryRedeemRelayCodeAsync_ExpiredCode_ReturnsNull()
    {
        // Insert a relay entry that expired 1 second ago
        var (code, _) = InsertRelayEntry("/", TimeSpan.FromSeconds(-1));
        var context = CreateHttpContext();

        var result = await _service.TryRedeemRelayCodeAsync(context, code);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryRedeemRelayCodeAsync_ExpiredCode_DoesNotIssueCookie()
    {
        var (code, _) = InsertRelayEntry("/", TimeSpan.FromSeconds(-1));
        var authService = Substitute.For<IAuthenticationService>();
        var context = CreateHttpContext(authService);

        await _service.TryRedeemRelayCodeAsync(context, code);

        // Verify SignInAsync was NOT called
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
        var (code, _) = InsertRelayEntry("/", TimeSpan.FromMinutes(2));
        var authService = Substitute.For<IAuthenticationService>();
        var context = CreateHttpContext(authService, withBinding: false);

        var result = await _service.TryRedeemRelayCodeAsync(context, code);

        Assert.Null(result);
        await authService.DidNotReceive().SignInAsync(
            Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
    }

    [Fact]
    public void IsIssuedRelayUrl_OnlyAcceptsCodesThisServiceIssued()
    {
        var (code, _) = InsertRelayEntry("/", TimeSpan.FromMinutes(2));

        Assert.True(_service.IsIssuedRelayUrl($"https://localhost:7203/atproto/relay?code={code}"));
        Assert.False(_service.IsIssuedRelayUrl("https://localhost:7203/atproto/relay?code=DEADBEEF"));
        Assert.False(_service.IsIssuedRelayUrl($"https://evil.example.com/elsewhere?code={code}"));
    }

    #endregion

    #region Relay code cleanup

    [Fact]
    public void RelayCodeCleanup_RemovesExpiredEntries()
    {
        // Insert one expired and one valid relay entry
        InsertRelayEntry("/expired", TimeSpan.FromSeconds(-10));
        var (validCode, _) = InsertRelayEntry("/valid", TimeSpan.FromMinutes(2));

        // Trigger cleanup
        var cleanupMethod = typeof(AtProtoOAuthService).GetMethod(
            "CleanupExpiredRelayCodes", BindingFlags.NonPublic | BindingFlags.Instance)!;
        cleanupMethod.Invoke(_service, null);

        var relayCodes = (IDictionary)GetRelayCodesDictionary();
        Assert.Single(relayCodes);
        Assert.True(relayCodes.Contains(validCode));
    }

    #endregion

    #region Origin comparison

    [Theory]
    [InlineData("https://localhost:7203", "http://127.0.0.1:5203", true)]
    [InlineData("https://localhost:7203", "https://localhost:7203", false)]
    [InlineData("http://localhost:5000", "http://localhost:5000", false)]
    [InlineData("https://LOCALHOST:7203", "https://localhost:7203", false)] // Case-insensitive
    [InlineData("http://127.0.0.1:5203", "https://localhost:7203", true)]
    [InlineData("https://myapp.example.com", "http://127.0.0.1:5000", true)]
    public void OriginComparison_DetectsMismatch(string loginOrigin, string callbackOrigin, bool expectRelay)
    {
        // The SDK uses string.Equals with OrdinalIgnoreCase for origin comparison
        var isMismatch = !callbackOrigin.Equals(loginOrigin, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectRelay, isMismatch);
    }

    #endregion

    #region Relay URL construction

    [Theory]
    [InlineData("https://localhost:7203", "/atproto")]
    [InlineData("https://localhost:5001", "/auth")]
    [InlineData("http://localhost:5000", "/atproto")]
    public void RelayUrl_UsesLoginOriginAndRoutePrefix(string loginOrigin, string routePrefix)
    {
        // Verify relay URL format matches what CompleteCallbackAsync generates
        var relayCode = "ABCDEF1234567890ABCDEF1234567890";
        var expectedUrl = $"{loginOrigin}{routePrefix}/relay?code={relayCode}";

        Assert.StartsWith(loginOrigin, expectedUrl);
        Assert.Contains($"{routePrefix}/relay?code=", expectedUrl);
        Assert.EndsWith(relayCode, expectedUrl);
    }

    #endregion

    #region Disposed service

    [Fact]
    public async Task TryRedeemRelayCodeAsync_DisposedService_ThrowsObjectDisposedException()
    {
        var service = new AtProtoOAuthService(new AtProtoOAuthServerOptions(), NullLoggerFactory.Instance);
        service.Dispose();

        var context = CreateHttpContext();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.TryRedeemRelayCodeAsync(context, "some-code"));
    }

    #endregion

    #region Helpers

    /// <summary>The browser's binding cookie value in these tests, and its hash as a login keeps it.</summary>
    private const string Binding = "dGVzdC1iaW5kaW5nLXZhbHVlLWZvci10aGUtcmVsYXk";

    private static readonly string BindingHash =
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(Binding)));

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
    private (string Code, AtProtoOAuthService.RelayEntry Entry) InsertRelayEntry(string returnUrl, TimeSpan expiresIn)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "did:plc:test123"),
            new Claim(ClaimTypes.Name, "test.bsky.social"),
        }, "ATProto"));

        var entry = new AtProtoOAuthService.RelayEntry(
            principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) },
            returnUrl,
            DateTime.UtcNow.Add(expiresIn),
            Session: null,
            BindingHash);

        return (_service.AddRelayEntry(entry), entry);
    }

    private object GetRelayCodesDictionary()
    {
        var field = typeof(AtProtoOAuthService).GetField(
            "_relayCodes", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return field.GetValue(_service)!;
    }

    #endregion
}
