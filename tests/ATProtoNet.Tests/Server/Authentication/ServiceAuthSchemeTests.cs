using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Tests.Server.Spaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Server.Authentication;

// ── Handlers ──────────────────────────────────────────────────

public sealed class CallerOutput
{
    [JsonPropertyName("did")]
    public string? Did { get; init; }

    [JsonPropertyName("lxm")]
    public string? Lxm { get; init; }

    [JsonPropertyName("aud")]
    public string? Aud { get; init; }

    public static Task<CallerOutput> From(HttpContext context) => Task.FromResult(new CallerOutput
    {
        Did = context.User.Identity?.Name,
        Lxm = context.User.FindFirst(AtProtoServiceAuthDefaults.LexiconMethodClaimType)?.Value,
        Aud = context.User.FindFirst(AtProtoServiceAuthDefaults.AudienceClaimType)?.Value,
    });
}

[RequireServiceAuth]
public sealed class ServiceAuthWhoAmI : IXrpcQuery<CallerOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.serviceAuth.whoAmI");

    public Task<CallerOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken) =>
        CallerOutput.From(context);
}

public sealed class ServiceAuthUnmarked : IXrpcQuery<CallerOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.serviceAuth.unmarked");

    public Task<CallerOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken) =>
        CallerOutput.From(context);
}

[AllowAnonymous]
public sealed class ServiceAuthAnonymous : IXrpcQuery<CallerOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.serviceAuth.anonymous");

    public Task<CallerOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken) =>
        CallerOutput.From(context);
}

[Authorize(Policy = ServiceAuthSchemeTests.OnlyAlice, AuthenticationSchemes = AtProtoServiceAuthDefaults.AuthenticationScheme)]
public sealed class ServiceAuthAliceOnly : IXrpcProcedure<CallerOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.serviceAuth.aliceOnly");

    public Task<CallerOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken) =>
        CallerOutput.From(context);
}

[Authorize]
public sealed class ServiceAuthPlainAuthorize : IXrpcQuery<CallerOutput>
{
    public static Nsid Nsid { get; } = Nsid.Parse("com.example.serviceAuth.plainAuthorize");

    public Task<CallerOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken) =>
        CallerOutput.From(context);
}

// ── Tests ─────────────────────────────────────────────────────

/// <summary>
/// <c>AddAtProtoServiceAuth()</c> end to end, over a test server: the scheme, the attribute and
/// group convention that require it, and the binding of each token to the endpoint's NSID.
/// </summary>
public sealed class ServiceAuthSchemeTests : IDisposable
{
    public const string OnlyAlice = "only-alice";

    private const string Caller = "did:plc:callercallercallercaller";
    private const string Audience = "did:web:feed.example.com#bsky_fg";

    private readonly FakeDidDocumentResolver _resolver = new();
    private readonly InMemoryJtiReplayStore _replay = new();
    private readonly ServiceAuthGenerator _generator;

    public ServiceAuthSchemeTests()
    {
        // The generator owns, and disposes, the key the caller's document publishes.
        var key = AtProtoCrypto.GenerateP256Key();
        _resolver.PublishAccount(Caller, key);
        _generator = new ServiceAuthGenerator(Did.Parse(Caller), key);
    }

    public void Dispose() => _generator.Dispose();

    private Task<XrpcTestHost> StartAsync(
        Action<AtProtoServiceAuthOptions>? configure = null,
        Action<RouteGroupBuilder>? configureGroup = null,
        Action<IEndpointRouteBuilder>? configureEndpoints = null) =>
        XrpcTestHost.StartAsync(
            services =>
            {
                // Registered first, so AddAtProtoServiceAuth's own registrations yield.
                services.AddSingleton<IDidResolver>(_resolver);
                services.AddSingleton<IJtiReplayStore>(_replay);
                services.AddAuthentication()
                    .AddAtProtoServiceAuth(configure ?? (o => o.Audiences.Add(Audience)));
                services.AddAuthorization(o =>
                    o.AddPolicy(OnlyAlice, p => p.RequireClaim(AtProtoServiceAuthDefaults.DidClaimType, "did:plc:alice")));
                services.AddXrpcEndpoint<ServiceAuthWhoAmI>();
                services.AddXrpcEndpoint<ServiceAuthUnmarked>();
                services.AddXrpcEndpoint<ServiceAuthAnonymous>();
                services.AddXrpcEndpoint<ServiceAuthAliceOnly>();
                services.AddXrpcEndpoint<ServiceAuthPlainAuthorize>();
            },
            configureGroup,
            app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
            },
            configureEndpoints);

    private string Token(Nsid method, string audience = Audience) => _generator.CreateToken(audience, method);

    private static HttpRequestMessage Get(string path, string? token) => new(HttpMethod.Get, path)
    {
        Headers = { Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token) },
    };

    private static async Task<CallerOutput> ReadCallerAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<CallerOutput>(await response.Content.ReadAsStringAsync())!;
    }

    [Fact]
    public async Task RequireServiceAuthOnHandler_ValidToken_AuthenticatesAsTheIssuer()
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(
            Get("/xrpc/com.example.serviceAuth.whoAmI", Token(ServiceAuthWhoAmI.Nsid)));

        var caller = await ReadCallerAsync(response);
        Assert.Equal(Caller, caller.Did);
        Assert.Equal("com.example.serviceAuth.whoAmI", caller.Lxm);
        Assert.Equal(Audience, caller.Aud);
    }

    [Fact]
    public async Task RequireServiceAuthOnHandler_TokenForAnotherMethod_IsRefusedWithTheXrpcEnvelope()
    {
        // The lxm is bound to the NSID of the endpoint reached, with no code in the handler.
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(
            Get("/xrpc/com.example.serviceAuth.whoAmI", Token(ServiceAuthUnmarked.Nsid)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        var (error, message) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal(ServiceAuthErrors.BadJwtLexiconMethod, error);
        Assert.Contains("com.example.serviceAuth.unmarked", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequireServiceAuthOnHandler_NoToken_IsAskedToAuthenticate()
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(Get("/xrpc/com.example.serviceAuth.whoAmI", token: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var (error, _) = await XrpcTestHost.ReadErrorAsync(response);
        Assert.Equal("AuthenticationRequired", error);
    }

    [Fact]
    public async Task RequireServiceAuthOnHandler_TokenForAnotherAudience_IsRefused()
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(Get(
            "/xrpc/com.example.serviceAuth.whoAmI",
            Token(ServiceAuthWhoAmI.Nsid, audience: "did:web:feed.example.com")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ServiceAuthErrors.BadJwtAudience, (await XrpcTestHost.ReadErrorAsync(response)).Error);
    }

    [Fact]
    public async Task Audiences_WithTheBareDidAdded_AcceptsBothForms()
    {
        await using var host = await StartAsync(o =>
        {
            o.Audiences.Add(Audience);
            o.Audiences.Add("did:web:feed.example.com");
        });

        var qualified = await host.Client.SendAsync(
            Get("/xrpc/com.example.serviceAuth.whoAmI", Token(ServiceAuthWhoAmI.Nsid)));
        var bare = await host.Client.SendAsync(Get(
            "/xrpc/com.example.serviceAuth.whoAmI",
            Token(ServiceAuthWhoAmI.Nsid, audience: "did:web:feed.example.com")));

        Assert.Equal(Audience, (await ReadCallerAsync(qualified)).Aud);
        Assert.Equal("did:web:feed.example.com", (await ReadCallerAsync(bare)).Aud);
    }

    [Fact]
    public async Task RequireServiceAuthOnHandler_SameTokenTwice_IsRefusedTheSecondTime()
    {
        await using var host = await StartAsync();
        var token = Token(ServiceAuthWhoAmI.Nsid);

        var first = await host.Client.SendAsync(Get("/xrpc/com.example.serviceAuth.whoAmI", token));
        var second = await host.Client.SendAsync(Get("/xrpc/com.example.serviceAuth.whoAmI", token));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(ServiceAuthErrors.BadJwt, (await XrpcTestHost.ReadErrorAsync(second)).Error);
    }

    [Fact]
    public async Task RequireServiceAuthOnGroup_AppliesToEveryHandlerButAllowAnonymous()
    {
        await using var host = await StartAsync(configureGroup: group => group.RequireServiceAuth());

        var unauthenticated = await host.Client.SendAsync(Get("/xrpc/com.example.serviceAuth.unmarked", token: null));
        var authenticated = await host.Client.SendAsync(
            Get("/xrpc/com.example.serviceAuth.unmarked", Token(ServiceAuthUnmarked.Nsid)));
        var anonymous = await host.Client.SendAsync(Get("/xrpc/com.example.serviceAuth.anonymous", token: null));

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(Caller, (await ReadCallerAsync(authenticated)).Did);
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
    }

    [Fact]
    public async Task RequireServiceAuthOnGroup_UnmatchedNsid_GetsTheChallengeNotAMethodList()
    {
        await using var host = await StartAsync(configureGroup: group => group.RequireServiceAuth());

        var response = await host.Client.SendAsync(
            Get("/xrpc/com.example.serviceAuth.doesNotExist", Token(ServiceAuthWhoAmI.Nsid)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonXrpcEndpoint_RefusesServiceAuthWithoutSpendingTheToken()
    {
        // No NSID means nothing to hold the token's lxm to. The token is refused before it is
        // verified, so it is still good for the method it was minted for.
        await using var host = await StartAsync(configureEndpoints: endpoints =>
            endpoints.MapGet("/plain", (HttpContext context) => context.User.Identity?.Name).RequireServiceAuth());
        var token = Token(ServiceAuthWhoAmI.Nsid);

        var plain = await host.Client.SendAsync(Get("/plain", token));
        var xrpc = await host.Client.SendAsync(Get("/xrpc/com.example.serviceAuth.whoAmI", token));

        Assert.Equal(HttpStatusCode.Unauthorized, plain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, xrpc.StatusCode);
    }

    [Fact]
    public async Task NonXrpcEndpointWithMethodMetadata_BindsTheTokenToIt()
    {
        var method = Nsid.Parse("com.example.serviceAuth.handMapped");
        await using var host = await StartAsync(configureEndpoints: endpoints =>
            endpoints.MapGet("/xrpc/com.example.serviceAuth.handMapped", (HttpContext context) => context.User.Identity?.Name)
                .WithMetadata(new XrpcMethodMetadata(method))
                .RequireServiceAuth());

        var response = await host.Client.SendAsync(Get("/xrpc/com.example.serviceAuth.handMapped", Token(method)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Caller, await response.Content.ReadAsStringAsync());
    }

    // ── Only endpoints that ask for service auth spend a token ──

    [Fact]
    public async Task OnlyScheme_AnonymousEndpoint_LeavesTheTokenUnspent()
    {
        // The scheme is the only one registered, so ASP.NET Core makes it the default and the
        // authentication middleware runs it on every request. It must not verify, and so spend,
        // a token an endpoint never asked for.
        await using var host = await StartAsync();
        var token = Token(ServiceAuthAnonymous.Nsid);

        var response = await host.Client.SendAsync(Get("/xrpc/com.example.serviceAuth.anonymous", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await ReadCallerAsync(response)).Did);
        Assert.Equal(0, _replay.Count);
    }

    [Fact]
    public async Task OnlyScheme_EndpointWithoutAuthorization_LeavesTheTokenUnspent()
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(
            Get("/xrpc/com.example.serviceAuth.unmarked", Token(ServiceAuthUnmarked.Nsid)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, _replay.Count);
    }

    [Fact]
    public async Task RequireServiceAuthOnGroup_AllowAnonymousHandler_LeavesTheTokenUnspent()
    {
        // Authorization still authenticates an anonymous endpoint's policy schemes, to populate
        // the user; the scheme declines rather than spend the token there.
        await using var host = await StartAsync(configureGroup: group => group.RequireServiceAuth());

        var response = await host.Client.SendAsync(
            Get("/xrpc/com.example.serviceAuth.anonymous", Token(ServiceAuthAnonymous.Nsid)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, _replay.Count);
    }

    [Fact]
    public async Task OnlyScheme_PlainAuthorize_AuthenticatesWithServiceAuth()
    {
        // A policy naming no scheme authenticates with the default one, which this is.
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(
            Get("/xrpc/com.example.serviceAuth.plainAuthorize", Token(ServiceAuthPlainAuthorize.Nsid)));

        Assert.Equal(Caller, (await ReadCallerAsync(response)).Did);
        Assert.Equal(1, _replay.Count);
    }

    [Fact]
    public async Task RequireServiceAuthOnHandler_SpendsTheTokenOnce()
    {
        // The middleware (default scheme) and authorization (the policy's scheme) both ask; the
        // handler's result is shared within the request, so the token is spent once.
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(
            Get("/xrpc/com.example.serviceAuth.whoAmI", Token(ServiceAuthWhoAmI.Nsid)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _replay.Count);
    }

    [Fact]
    public async Task AuthorizationPolicyNotMet_IsForbiddenWithTheXrpcEnvelope()
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/xrpc/com.example.serviceAuth.aliceOnly")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", Token(ServiceAuthAliceOnly.Nsid)) },
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Forbidden", (await XrpcTestHost.ReadErrorAsync(response)).Error);
    }

    [Fact]
    public async Task AddAtProtoServiceAuth_NoAudiences_FailsTheHostStart()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => StartAsync(_ => { }));

        Assert.Contains("audiences", ex.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://feed.example.com")]
    [InlineData("did:web:feed.example.com#")]
    public async Task AddAtProtoServiceAuth_MalformedAudience_FailsTheHostStart(string audience)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => StartAsync(o => o.Audiences.Add(audience)));

        Assert.Contains(audience, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddAtProtoServiceAuth_RegistersTheResolverAndAnInProcessReplayStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication().AddAtProtoServiceAuth(o => o.Audiences.Add(Audience));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IDidResolver>());
        Assert.IsType<InMemoryJtiReplayStore>(provider.GetRequiredService<IJtiReplayStore>());
    }

    [Fact]
    public void AddAtProtoServiceAuth_KeepsAReplayStoreAlreadyRegistered()
    {
        var shared = new InMemoryJtiReplayStore();
        var services = new ServiceCollection();
        services.AddSingleton<IJtiReplayStore>(shared);
        services.AddAuthentication().AddAtProtoServiceAuth(o => o.Audiences.Add(Audience));

        using var provider = services.BuildServiceProvider();

        Assert.Same(shared, provider.GetRequiredService<IJtiReplayStore>());
    }

    [Fact]
    public void MapXrpcEndpoints_AttachesTheMethodToEveryEndpoint()
    {
        var registration = XrpcEndpointRegistration.Create<ServiceAuthWhoAmI>();

        var metadata = Assert.Single(registration.Metadata.OfType<XrpcMethodMetadata>());
        Assert.Equal(ServiceAuthWhoAmI.Nsid, metadata.Nsid);
        Assert.Single(registration.Metadata.OfType<RequireServiceAuthAttribute>());
    }

    [Fact]
    public void RequireServiceAuthAttribute_NamesTheScheme()
    {
        Assert.Equal(AtProtoServiceAuthDefaults.AuthenticationScheme, new RequireServiceAuthAttribute().AuthenticationSchemes);
        Assert.Equal("Other", new RequireServiceAuthAttribute("Other").AuthenticationSchemes);
    }
}
