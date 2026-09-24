using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ATProtoNet.Identity;
using ATProtoNet.Server.Xrpc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ATProtoNet.Tests.Server;

// ── Handlers carrying their own endpoint metadata ─────────────

public sealed class WhoAmIOutput
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

[Authorize]
public sealed class AuthorizedQuery : IXrpcQuery<WhoAmIOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.authorized");

    public Task<WhoAmIOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new WhoAmIOutput { Name = context.User.Identity?.Name });
}

[AllowAnonymous]
public sealed class AnonymousQuery : IXrpcQuery<WhoAmIOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.anonymous");

    public Task<WhoAmIOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new WhoAmIOutput { Name = context.User.Identity?.Name });
}

// ── Tests ─────────────────────────────────────────────────────

public class XrpcRouteGroupTests
{
    private const string TestScheme = "Test";

    [Fact]
    public async Task MapXrpcEndpoints_RequireAuthorization_AppliesToEveryEndpoint()
    {
        await using var host = await StartWithAuthAsync(group => group.RequireAuthorization());

        var anonymous = await host.Client.GetAsync("/xrpc/com.example.simpleQuery");
        var authenticated = await host.Client.SendAsync(Authenticated("/xrpc/com.example.simpleQuery"));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    [Fact]
    public async Task MapXrpcEndpoints_AllowAnonymousOnHandler_OverridesGroupAuthorization()
    {
        await using var host = await StartWithAuthAsync(group => group.RequireAuthorization());

        var response = await host.Client.GetAsync("/xrpc/com.example.anonymous");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizeOnHandler_IsHonouredWithoutAGroupConvention()
    {
        await using var host = await StartWithAuthAsync(group => { });

        var anonymous = await host.Client.GetAsync("/xrpc/com.example.authorized");
        var authenticated = await host.Client.SendAsync(Authenticated("/xrpc/com.example.authorized"));
        var otherEndpoint = await host.Client.GetAsync("/xrpc/com.example.simpleQuery");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        Assert.Contains("alice", await authenticated.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, otherEndpoint.StatusCode);
    }

    [Fact]
    public async Task MapXrpcEndpoints_RequireRateLimiting_AppliesToEveryEndpoint()
    {
        await using var host = await XrpcTestHost.StartAsync(
            services =>
            {
                services.AddRateLimiter(options =>
                {
                    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                    options.AddFixedWindowLimiter("xrpc", window =>
                    {
                        window.PermitLimit = 1;
                        window.Window = TimeSpan.FromHours(1);
                        window.QueueLimit = 0;
                        window.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });
                });
                services.AddXrpcEndpoint<SimpleQuery>();
            },
            group => group.RequireRateLimiting("xrpc"),
            app => app.UseRateLimiter());

        var first = await host.Client.GetAsync("/xrpc/com.example.simpleQuery");
        var second = await host.Client.GetAsync("/xrpc/com.example.simpleQuery");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task MapXrpcEndpoints_GroupMetadata_ReachesEveryEndpoint()
    {
        var marker = new object();
        Endpoint? seen = null;

        await using var host = await XrpcTestHost.StartAsync(
            services => services.AddXrpcEndpoint<SimpleQuery>(),
            group => group.WithMetadata(marker),
            app => app.Use((context, next) =>
            {
                seen = context.GetEndpoint();
                return next(context);
            }));

        await host.Client.GetAsync("/xrpc/com.example.simpleQuery");

        Assert.NotNull(seen);
        Assert.Contains(marker, seen.Metadata);
    }

    [Fact]
    public async Task Fallback_DoesNotShadowRoutesTheAppMapsUnderXrpc()
    {
        await using var host = await XrpcTestHost.StartAsync(
            services => services.AddXrpcEndpoint<SimpleQuery>(),
            configureEndpoints: endpoints =>
            {
                endpoints.MapGet("/xrpc/_health", () => "healthy");
                endpoints.MapPost("/xrpc/com.example.appOwned", () => "app");
            });

        var health = await host.Client.GetAsync("/xrpc/_health");
        var appOwned = await host.Client.PostAsync("/xrpc/com.example.appOwned", null);
        var registered = await host.Client.GetAsync("/xrpc/com.example.simpleQuery");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("healthy", await health.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, appOwned.StatusCode);
        Assert.Equal("app", await appOwned.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
    }

    [Fact]
    public async Task Fallback_GroupRequiresAuthorization_ChallengesBeforeAnsweringUnknownNsid()
    {
        await using var host = await StartWithAuthAsync(group => group.RequireAuthorization());

        var anonymous = await host.Client.GetAsync("/xrpc/com.example.doesNotExist");
        var authenticated = await host.Client.SendAsync(Authenticated("/xrpc/com.example.doesNotExist"));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, authenticated.StatusCode);
    }

    private static Task<XrpcTestHost> StartWithAuthAsync(Action<RouteGroupBuilder> configureGroup) =>
        XrpcTestHost.StartAsync(
            services =>
            {
                services.AddAuthentication(TestScheme).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                services.AddAuthorization();
                services.AddXrpcEndpoint<SimpleQuery>();
                services.AddXrpcEndpoint<AuthorizedQuery>();
                services.AddXrpcEndpoint<AnonymousQuery>();
            },
            configureGroup,
            app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
            });

    private static HttpRequestMessage Authenticated(string path) =>
        new(HttpMethod.Get, path) { Headers = { { "Authorization", TestScheme } } };

    /// <summary>Authenticates a request that carries <c>Authorization: Test</c> as "alice".</summary>
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization != TestScheme)
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}
