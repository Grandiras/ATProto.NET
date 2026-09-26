using System.Net;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Server;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace ATProtoNet.Tests.Server.Authentication;

/// <summary>
/// The endpoints <see cref="AtProtoOAuthExtensions.MapAtProtoOAuth"/> maps: what a failed login
/// tells the browser, and the client documents it can serve.
/// </summary>
public sealed class AtProtoOAuthEndpointTests
{
    private const string MetadataUrl = "https://app.example.com/oauth-client-metadata.json";

    private static OAuthClientMetadata Metadata() => new()
    {
        ClientId = MetadataUrl,
        RedirectUris = ["https://app.example.com/atproto/callback"],
    };

    private static async Task<IHost> StartAsync(
        Action<AtProtoOAuthServerOptions> configure, Action<IServiceCollection>? services = null)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddLogging();
                    s.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
                    s.AddAtProtoAuthentication(configure);
                    services?.Invoke(s);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseEndpoints(endpoints => endpoints.MapAtProtoOAuth());
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    private static async Task<string> RedirectOfAsync(IHost host, string url)
    {
        using var response = await host.GetTestClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.OriginalString;
    }

    [Fact]
    public async Task Login_AnOAuthFailure_RedirectsWithItsErrorCode()
    {
        using var host = await StartAsync(o => o.ClientMetadata = Metadata());

        Assert.Equal("/login?error=invalid_handle", await RedirectOfAsync(host, "/atproto/login?handle=not%20a%20handle"));
    }

    [Fact]
    public async Task Login_AnyOtherFailure_RedirectsWithAFixedCodeAndNoMessage()
    {
        var resolver = Substitute.For<IIdentityResolver>();
        resolver.ResolveAsync(Arg.Any<AtIdentifier>(), Arg.Any<CancellationToken>())
            .Returns<ResolvedIdentity>(_ => throw new InvalidOperationException("Server=db.internal;Password=hunter2"));
        using var host = await StartAsync(o => o.ClientMetadata = Metadata(), s => s.AddSingleton(resolver));

        var location = await RedirectOfAsync(host, "/atproto/login?handle=alice.test");

        Assert.Equal("/login?error=login_failed", location);
    }

    [Fact]
    public async Task Callback_AnAuthorizationServerError_PassesItsCodeButNotItsDescription()
    {
        using var host = await StartAsync(o => o.ClientMetadata = Metadata());

        var location = await RedirectOfAsync(
            host, "/atproto/callback?error=access_denied&error_description=Call%20%2B1%20555%200100%20to%20unlock");

        Assert.Equal("/login?error=access_denied", location);
    }

    [Fact]
    public async Task Callback_AnErrorThatIsNotACode_BecomesTheFixedCode()
    {
        using var host = await StartAsync(o => o.ClientMetadata = Metadata());

        var location = await RedirectOfAsync(host, "/atproto/callback?error=%3Cb%3Eyour%20account%20is%20locked%3C%2Fb%3E");

        Assert.Equal("/login?error=login_failed", location);
    }

    [Fact]
    public async Task Callback_AnUnknownState_RedirectsWithItsErrorCode()
    {
        using var host = await StartAsync(o =>
        {
            o.ClientMetadata = Metadata();
            o.LoginPath = "/signin/";
        });

        var location = await RedirectOfAsync(host, "/atproto/callback?code=c&state=unknown&iss=https%3A%2F%2Fauth.example.com");

        Assert.Equal("/signin?error=invalid_state", location);
    }

    [Theory]
    [InlineData("access_denied", "access_denied")]
    [InlineData("use_dpop_nonce", "use_dpop_nonce")]
    [InlineData("a.b-c_d", "a.b-c_d")]
    [InlineData("", "login_failed")]
    [InlineData(null, "login_failed")]
    [InlineData("two words", "login_failed")]
    [InlineData("<script>", "login_failed")]
    [InlineData("ünïcode", "login_failed")]
    public void ErrorCode_KeepsOnlyShortTokens(string? error, string expected)
    {
        Assert.Equal(expected, AtProtoOAuthExtensions.ErrorCode(error));
        Assert.Equal("login_failed", AtProtoOAuthExtensions.ErrorCode(new string('a', 65)));
    }

    [Fact]
    public async Task ServeClientMetadata_ServesTheMetadataAtTheClientIdsPath()
    {
        using var host = await StartAsync(o =>
        {
            o.ClientMetadata = Metadata();
            o.ClientMetadata.ClientName = "My App";
            o.ServeClientMetadata = true;
        });

        using var response = await host.GetTestClient().GetAsync("/oauth-client-metadata.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(MetadataUrl, json.RootElement.GetProperty("client_id").GetString());
        Assert.Equal("My App", json.RootElement.GetProperty("client_name").GetString());
        Assert.False(json.RootElement.TryGetProperty("jwks_uri", out _));
    }

    [Fact]
    public async Task ServeClientMetadata_AConfidentialClient_PublishesOnlyThePublicKeys()
    {
        using var current = OAuthClientKey.Generate("key-2");
        using var retired = OAuthClientKey.Generate("key-1");
        using var host = await StartAsync(o =>
        {
            o.ClientMetadata = Metadata();
            o.ClientMetadata.TokenEndpointAuthMethod = "private_key_jwt";
            o.ClientMetadata.TokenEndpointAuthSigningAlg = "ES256";
            o.ClientMetadata.JwksUri = "https://app.example.com/oauth/jwks.json";
            o.ClientKeys.Add(current);
            o.ClientKeys.Add(retired);
            o.ServeClientMetadata = true;
        });

        using var response = await host.GetTestClient().GetAsync("/oauth/jwks.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());
        var keys = JsonSerializer.Deserialize<JsonWebKeySet>(body)!.Keys;
        Assert.Equal(["key-2", "key-1"], keys.Select(k => k.Kid));
        Assert.All(keys, key => Assert.True(current.Matches(key) || retired.Matches(key)));
        Assert.DoesNotContain("\"d\"", body);

        // The hosted client authenticates with the same keys.
        Assert.Equal(MetadataUrl, host.Services.GetRequiredService<OAuthClient>().ClientId);
    }

    [Fact]
    public async Task ServeClientMetadata_WithoutClientMetadata_FailsAtStartup()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(o => o.ServeClientMetadata = true));
    }

    [Fact]
    public async Task ServeClientMetadata_AClientIdWithoutAPath_FailsAtStartup()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(o =>
        {
            o.ClientMetadata = Metadata();
            o.ClientMetadata.ClientId = "https://app.example.com/";
            o.ServeClientMetadata = true;
        }));
    }

    [Fact]
    public async Task WithoutServeClientMetadata_NothingIsServedAtTheClientId()
    {
        using var host = await StartAsync(o => o.ClientMetadata = Metadata());

        using var response = await host.GetTestClient().GetAsync("/oauth-client-metadata.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddAtProtoAuthentication_RegistersTheServicesClientForTheFactory()
    {
        using var host = await StartAsync(o => o.ClientMetadata = Metadata(), s =>
        {
            s.AddSingleton<IAtProtoSessionStore>(new InMemoryAtProtoSessionStore());
            s.AddAtProtoServer();
        });

        var oauth = host.Services.GetRequiredService<OAuthClient>();

        Assert.Same(host.Services.GetRequiredService<AtProtoOAuthService>().Client, oauth);
        Assert.IsType<AtProtoClientFactory>(host.Services.GetRequiredService<IAtProtoClientFactory>());
        Assert.IsType<InProcessSessionRefreshCoordinator>(host.Services.GetRequiredService<ISessionRefreshCoordinator>());
    }
}
