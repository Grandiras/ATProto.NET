using System.Net;
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

        var result = await service.CompleteCallbackAsync(callback, "code", state, Issuer);

        Assert.Equal("/inbox", result.RedirectUrl);
        Assert.False(result.IsRelay);
        Assert.Equal(Alice, result.Did);
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

        Assert.Equal("/", (await service.CompleteCallbackAsync(Request(cookie: cookie), "code", state, Issuer)).RedirectUrl);
    }

    [Fact]
    public async Task Callback_AReturnUrlCookieFromElsewhere_IsIgnored()
    {
        using var service = Service();
        var (state, cookie) = await StartAsync(service);

        var result = await service.CompleteCallbackAsync(
            Request(cookie: $"{cookie}; atproto_return_url=https://evil.example.com/"), "code", state, Issuer);

        Assert.Equal("/", result.RedirectUrl);
    }

    [Fact]
    public async Task Callback_OnAnotherLoopbackOrigin_ReturnsTheRelayOnTheLoginsOrigin()
    {
        using var service = Service();
        var context = Request(host: "localhost:7203");
        await service.StartLoginAsync(context, AliceHandle.Value, "/inbox");
        var state = _server.To("/oauth/par")[^1].Form["state"];

        var callback = Request(host: "127.0.0.1:5203");
        callback.Request.Scheme = "http";
        var result = await service.CompleteCallbackAsync(callback, "code", state, Issuer);

        Assert.True(result.IsRelay);
        Assert.StartsWith("https://localhost:7203/atproto/relay?code=", result.RedirectUrl);
        Assert.Equal(1, service.PendingRelayCount);
        await _auth.DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task Callback_OnAnotherInstance_CompletesWithTheLoginsReturnUrl()
    {
        // The login's context travels with the pending authorization, not in process memory.
        using var first = Service();
        using var second = Service();
        var (state, cookie) = await StartAsync(first, "/inbox");

        Assert.Equal("/inbox", (await second.CompleteCallbackAsync(Request(cookie: cookie), "code", state, Issuer)).RedirectUrl);
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

    [Fact]
    public async Task Callback_OnALoopbackOrigin_OfALoginStartedOnAPublicHost_IsNotRelayed()
    {
        // The check that keeps a login started with `Host: evil.example.com` from having the
        // victim's loopback callback relayed to that host.
        using var service = Service();
        await service.StartLoginAsync(Request(host: "evil.example.com"), AliceHandle.Value);
        var state = _server.To("/oauth/par")[^1].Form["state"];
        var callback = Request(host: "127.0.0.1:5203");
        callback.Request.Scheme = "http";

        var ex = await Assert.ThrowsAsync<OAuthException>(() => service.CompleteCallbackAsync(callback, "code", state, Issuer));

        Assert.Equal("login_not_bound", ex.Error);
        Assert.Equal(0, service.PendingRelayCount);
        Assert.Single(_server.To("/oauth/revoke"));
    }

    [Fact]
    public async Task Callback_AStoredReturnUrlThatIsNotLocal_IsIgnored()
    {
        // The return URL comes back from the state store, which may be shared or tampered with.
        using var service = Service();
        var (state, cookie) = await StartAsync(service, "/inbox");
        await RewriteAppStateAsync(state, app => app.Replace("\"/inbox\"", "\"https://evil.example.com/\""));

        var result = await service.CompleteCallbackAsync(Request(cookie: cookie), "code", state, Issuer);

        Assert.Equal("/", result.RedirectUrl);
    }

    [Theory]
    [InlineData("https://localhost:7203/elsewhere")]
    [InlineData("https://user@localhost:7203")]
    [InlineData("javascript:alert(1)")]
    public async Task Callback_AStoredOriginThatIsNotAnOrigin_IsRefused(string origin)
    {
        using var service = Service();
        await service.StartLoginAsync(Request(host: "localhost:7203"), AliceHandle.Value);
        var state = _server.To("/oauth/par")[^1].Form["state"];
        await RewriteAppStateAsync(state, app => app.Replace("\"https://localhost:7203\"", $"\"{origin}\""));
        var callback = Request(host: "127.0.0.1:5203");
        callback.Request.Scheme = "http";

        var ex = await Assert.ThrowsAsync<OAuthException>(() => service.CompleteCallbackAsync(callback, "code", state, Issuer));

        Assert.Equal("login_not_bound", ex.Error);
        Assert.Equal(0, service.PendingRelayCount);
    }

    private async Task RewriteAppStateAsync(string state, Func<string, string> rewrite)
    {
        var pending = await _store.TakeAsync(state);
        Assert.NotNull(pending?.AppState);
        var rewritten = rewrite(pending.AppState);
        Assert.NotEqual(pending.AppState, rewritten);
        await _store.SetAsync(pending with { AppState = rewritten });
    }

    [Fact]
    public async Task LogoutAsync_DropsTheClientFactorysCachedKey()
    {
        var store = new InMemoryAtProtoSessionStore();
        await store.SetAsync(OAuthSession(NewDPoPKey(), revocationEndpoint: RevocationEndpoint));
        var httpClients = Substitute.For<IHttpClientFactory>();
        httpClients.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_server, disposeHandler: false));
        var factory = new ATProtoNet.Server.Services.AtProtoClientFactory(store, httpClients, NullLoggerFactory.Instance);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AtProtoClaimTypes.Did, Alice.Value)], "ATProto"));
        await (await factory.CreateClientForUserAsync(user))!.DisposeAsync();
        Assert.Equal(1, factory.CachedKeyCount);

        using var service = Service();
        var services = new ServiceCollection();
        services.AddSingleton(_auth);
        services.AddSingleton<IAtProtoSessionStore>(store);
        services.AddSingleton<ATProtoNet.Server.Services.IAtProtoClientFactory>(factory);
        await service.LogoutAsync(new DefaultHttpContext { RequestServices = services.BuildServiceProvider(), User = user });

        Assert.Equal(0, factory.CachedKeyCount);
    }

    [Fact]
    public async Task LogoutAsync_ForAServiceAuthCaller_LeavesTheAccountsSessionAlone()
    {
        var store = new InMemoryAtProtoSessionStore();
        await store.SetAsync(OAuthSession(NewDPoPKey(), revocationEndpoint: RevocationEndpoint));
        using var service = Service();
        var services = new ServiceCollection();
        services.AddSingleton(_auth);
        services.AddSingleton<IAtProtoSessionStore>(store);
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(AtProtoClaimTypes.Did, Alice.Value)], AtProtoServiceAuthDefaults.AuthenticationScheme)),
        };

        await service.LogoutAsync(context);

        Assert.NotNull(await store.GetAsync(Alice));
        Assert.Empty(_server.To("/oauth/revoke"));
    }

    [Fact]
    public async Task LogoutAsync_WaitsForARefreshUnderWay_ThenRemovesAndRevokesTheSession()
    {
        // A refresh holding the account's lock finishes before the session is removed, and one
        // waiting behind the sign-out finds nothing to bring back.
        var coordinator = new InProcessSessionRefreshCoordinator();
        var store = new InMemoryAtProtoSessionStore();
        await store.SetAsync(OAuthSession(NewDPoPKey(), refreshToken: "rt-live", revocationEndpoint: RevocationEndpoint));
        using var service = new AtProtoOAuthService(
            new AtProtoOAuthServerOptions
            {
                ClientMetadata = new OAuthClientMetadata { ClientId = ClientId, RedirectUris = ["https://app.example.com/atproto/callback"] },
                HttpClient = _http,
            },
            NullLoggerFactory.Instance,
            AliceIdentity(),
            refreshCoordinator: coordinator);

        var services = new ServiceCollection();
        services.AddSingleton(_auth);
        services.AddSingleton<IAtProtoSessionStore>(store);
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AtProtoClaimTypes.Did, Alice.Value)], "ATProto")),
        };

        var refreshing = await coordinator.AcquireAsync(Alice);
        var logout = service.LogoutAsync(context);
        await Task.Delay(50);
        Assert.False(logout.IsCompleted);
        Assert.NotNull(await store.GetAsync(Alice));

        await refreshing.DisposeAsync();
        Assert.Equal("/", await logout);

        Assert.Null(await store.GetAsync(Alice));
        Assert.Equal("rt-live", Assert.Single(_server.To("/oauth/revoke")).Form["token"]);
        await _auth.Received(1).SignOutAsync(context, Arg.Any<string>(), Arg.Any<AuthenticationProperties>());
    }
}
