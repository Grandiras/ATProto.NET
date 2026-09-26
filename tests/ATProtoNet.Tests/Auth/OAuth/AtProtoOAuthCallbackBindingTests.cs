using System.Net;
using System.Security.Claims;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Blazor.Authentication;
using ATProtoNet.Tests.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// The Blazor callback accepts a login only from the browser that started it, and returns only
/// to a local URL kept with the pending login on the server.
/// </summary>
public sealed class AtProtoOAuthCallbackBindingTests : IDisposable
{
    private readonly StubServer _server = new() { Respond = AuthorizationServer };
    private readonly HttpClient _http;
    private readonly InMemoryOAuthStateStore _store = new();
    private readonly IAuthenticationService _auth = Substitute.For<IAuthenticationService>();
    private readonly IAtProtoSessionStore _sessions = Substitute.For<IAtProtoSessionStore>();

    public AtProtoOAuthCallbackBindingTests() => _http = new HttpClient(_server);

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    private AtProtoOAuthService Service() => new(
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
        AliceIdentity(),
        _store);

    private DefaultHttpContext Request(string host = "app.example.com", string? cookie = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_auth);
        services.AddSingleton(_sessions);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(host);
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        if (cookie is not null)
            context.Request.Headers.Cookie = cookie;
        return context;
    }

    /// <summary>Starts a login and returns its state and the cookie the browser was given.</summary>
    private async Task<(string State, string Cookie)> StartAsync(AtProtoOAuthService service, string? returnUrl = null)
    {
        var context = Request();
        await service.StartLoginAsync(context, AliceHandle.Value, returnUrl);

        var cookie = context.Response.Headers.SetCookie.ToString().Split(';')[0];
        return (_server.To("/oauth/par")[^1].Form["state"], cookie);
    }

    [Fact]
    public async Task Callback_FromTheBrowserThatStarted_SignsInAndReturnsTheLocalUrl()
    {
        using var service = Service();
        var (state, cookie) = await StartAsync(service, "/inbox");
        var callback = Request(cookie: cookie);

        var returnUrl = await service.CompleteCallbackAsync(callback, "code", state, Issuer);

        Assert.Equal("/inbox", returnUrl);
        await _sessions.Received(1).SetAsync(Arg.Any<AtProtoSession>(), Arg.Any<CancellationToken>());
        await _auth.Received(1).SignInAsync(callback, Arg.Any<string>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
        Assert.Contains("expires=Thu, 01 Jan 1970", callback.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Callback_FromAnotherBrowser_IsRefusedStoresNothingAndRevokes()
    {
        using var service = Service();
        var (state, _) = await StartAsync(service);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => service.CompleteCallbackAsync(Request(), "code", state, Issuer));

        Assert.Equal("login_not_bound", ex.Error);
        await _sessions.DidNotReceiveWithAnyArgs().SetAsync(default!, default);
        await _auth.DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
        Assert.Single(_server.To("/oauth/revoke"));
    }

    [Fact]
    public async Task Callback_WithAnotherLoginsCookie_IsRefused()
    {
        using var service = Service();
        var (_, otherCookie) = await StartAsync(service);
        var (state, _) = await StartAsync(service);

        // Each browser gets its own binding; the first one's does not complete the second login.
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => service.CompleteCallbackAsync(Request(cookie: otherCookie), "code", state, Issuer));

        Assert.Equal("login_not_bound", ex.Error);
    }

    [Theory]
    [InlineData("https://evil.example.com/")]
    [InlineData("//evil.example.com/")]
    [InlineData("/\\evil.example.com/")]
    [InlineData("evil")]
    public async Task StartLoginAsync_AReturnUrlThatIsNotLocal_IsIgnored(string returnUrl)
    {
        using var service = Service();
        var (state, cookie) = await StartAsync(service, returnUrl);

        Assert.Equal("/", await service.CompleteCallbackAsync(Request(cookie: cookie), "code", state, Issuer));
    }

    [Fact]
    public async Task Callback_AReturnUrlCookieFromElsewhere_IsIgnored()
    {
        using var service = Service();
        var (state, cookie) = await StartAsync(service);

        var returnUrl = await service.CompleteCallbackAsync(
            Request(cookie: $"{cookie}; atproto_return_url=https://evil.example.com/"), "code", state, Issuer);

        Assert.Equal("/", returnUrl);
    }

    [Fact]
    public async Task Callback_OnAnotherInstance_CompletesWithTheLoginsReturnUrl()
    {
        // The login's context travels with the pending authorization, not in process memory.
        using var first = Service();
        using var second = Service();
        var (state, cookie) = await StartAsync(first, "/inbox");

        Assert.Equal("/inbox", await second.CompleteCallbackAsync(Request(cookie: cookie), "code", state, Issuer));
    }

    [Fact]
    public async Task Callback_OnAnotherPublicHost_IsNotRelayed()
    {
        // Relaying to the login's origin is for loopback development hosts only; elsewhere that
        // origin is whatever Host header the login was started with.
        using var service = Service();
        var (state, _) = await StartAsync(service);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => service.CompleteCallbackAsync(Request(host: "callback.example.com"), "code", state, Issuer));

        Assert.Equal("login_not_bound", ex.Error);
    }
}
