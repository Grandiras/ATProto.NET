using System.Net;
using ATProtoNet.Aspire;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Server;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Services;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Server.TokenStore;
using ATProtoNet.Tests.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Server;

/// <summary>
/// <c>AddAtProto()</c> and every registration that hangs off its builder: each one resolves what
/// it registers, and options are checked when the host starts.
/// </summary>
public class AtProtoBuilderTests
{
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    /// <summary>A host over the registrations, for what happens when it starts.</summary>
    private static IHost Host(Action<IServiceCollection> configure, CapturingLoggerProvider? logs = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        if (logs is not null)
            builder.Logging.AddProvider(logs);

        configure(builder.Services);
        return builder.Build();
    }

    // ── AddAtProto ────────────────────────────────────────────

    [Fact]
    public void AddAtProto_ResolvesOneClientBuiltFromTheOptions()
    {
        // AddHttpClient<AtProtoClient>() used to claim the service as a transient typed client:
        // every resolution was a new client for https://bsky.social, whatever the options said.
        var services = Services();
        services.AddAtProto(o => o.InstanceUrl = "https://pds.example.com");

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<AtProtoClient>();

        Assert.Same(client, provider.GetRequiredService<AtProtoClient>());
        Assert.Equal(new Uri("https://pds.example.com/"), client.ServiceUrl);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == typeof(AtProtoClient)).Lifetime);
    }

    [Fact]
    public void AddAtProto_ReturnsTheBuilderOfItsNamedClient()
    {
        var services = Services();

        var atproto = services.AddAtProto();

        Assert.Same(services, atproto.Services);
        Assert.Equal(AtProtoServiceCollectionExtensions.HttpClientName, atproto.HttpClient.Name);
    }

    [Fact]
    public void AddAtProto_CalledTwice_KeepsOneClientWithBothConfigurations()
    {
        var services = Services();
        services.AddAtProto(o => o.InstanceUrl = "https://pds.example.com");
        services.AddAtProto(o => o.UserAgent = "MyApp/1.0");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AtProtoClientOptions>>().Value;

        Assert.Single(services, d => d.ServiceType == typeof(AtProtoClient));
        Assert.Equal("https://pds.example.com", options.InstanceUrl);
        Assert.Equal("MyApp/1.0", options.UserAgent);
    }

    [Fact]
    public async Task AddAtProto_TheClientSendsThroughTheBuildersHttpClient()
    {
        // The IHttpClientBuilder is how an application adds its own handlers (and resilience).
        var handler = new RecordingHandler();
        var services = Services();
        services.AddAtProto(o => o.InstanceUrl = "https://pds.example.com")
            .HttpClient.AddHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<AtProtoClient>().Server.DescribeServerAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://pds.example.com/xrpc/com.atproto.server.describeServer", request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/xrpc")]
    [InlineData("ftp://pds.example.com")]
    [InlineData("https://pds.example.com/?x=1")]
    public async Task AddAtProto_AnInvalidInstanceUrl_StopsTheHost(string url)
    {
        using var host = Host(s => s.AddAtProto(o => o.InstanceUrl = url));

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(AtProtoClientOptions.InstanceUrl), ex.Message);
    }

    [Fact]
    public void AddAtProto_ThenAddAtProtoClient_CodeWinsOverConfiguration()
    {
        // The configuration used to be bound after the callback when AddAtProto came first.
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AtProto:InstanceUrl"] = "https://from-config.example.com",
            ["ConnectionStrings:pds"] = "http://localhost:2583",
        });
        builder.Services.AddAtProto(o => o.InstanceUrl = "https://from-code.example.com");
        builder.AddAtProtoClient(connectionName: "pds");

        using var host = builder.Build();

        Assert.Equal(new Uri("https://from-code.example.com/"), host.Services.GetRequiredService<AtProtoClient>().ServiceUrl);
    }

    [Fact]
    public void AddAtProto_ThenAConfigurationSection_CodeWins()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AtProto:InstanceUrl"] = "https://from-config.example.com",
                ["AtProto:UserAgent"] = "FromConfig/1.0",
            })
            .Build();
        var services = Services();
        services.AddAtProto(o => o.InstanceUrl = "https://from-code.example.com");
        services.Configure<AtProtoClientOptions>(configuration.GetSection("AtProto"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AtProtoClientOptions>();

        Assert.Equal("https://from-code.example.com", options.InstanceUrl);
        Assert.Equal("FromConfig/1.0", options.UserAgent);
    }

    [Fact]
    public void AddAtProto_BindsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AtProto:InstanceUrl"] = "https://pds.example.com" })
            .Build();
        var services = Services();
        services.AddAtProto();
        services.Configure<AtProtoClientOptions>(configuration.GetSection("AtProto"));

        using var provider = services.BuildServiceProvider();

        Assert.Equal(new Uri("https://pds.example.com/"), provider.GetRequiredService<AtProtoClient>().ServiceUrl);
    }

    [Fact]
    public void AddAtProto_WithTheClientFactory_RefreshesUnderItsCoordinator()
    {
        // A client sharing the store with the factory's clients must take the same lock.
        var services = Services();
        services.AddAtProto().WithClientFactory();

        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<ISessionRefreshCoordinator>(),
            provider.GetRequiredService<IOptions<AtProtoClientOptions>>().Value.RefreshCoordinator);
    }

    // ── WithLifetime ──────────────────────────────────────────

    [Fact]
    public void WithLifetime_Scoped_GivesEachScopeItsOwnClient()
    {
        var services = Services();
        services.AddAtProto().WithLifetime(ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var client = first.ServiceProvider.GetRequiredService<AtProtoClient>();
        Assert.Same(client, first.ServiceProvider.GetRequiredService<AtProtoClient>());
        Assert.NotSame(client, second.ServiceProvider.GetRequiredService<AtProtoClient>());
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(AtProtoClient)).Lifetime);
    }

    [Fact]
    public void WithLifetime_Transient_GivesANewClientEachTime()
    {
        var services = Services();
        services.AddAtProto().WithLifetime(ServiceLifetime.Transient);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotSame(scope.ServiceProvider.GetRequiredService<AtProtoClient>(), scope.ServiceProvider.GetRequiredService<AtProtoClient>());
    }

    [Fact]
    public void WithLifetime_AnUndefinedValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Services().AddAtProto().WithLifetime((ServiceLifetime)42));
    }

    // ── Session stores ────────────────────────────────────────

    [Fact]
    public void WithSessionStore_OfAType_IsTheStoreEitherWayRound()
    {
        foreach (var storeFirst in new[] { true, false })
        {
            var services = Services();
            var atproto = services.AddAtProto();
            if (storeFirst)
                atproto.WithSessionStore<CountingStore>().WithClientFactory();
            else
                atproto.WithClientFactory().WithSessionStore<CountingStore>();

            using var provider = services.BuildServiceProvider();

            Assert.IsType<CountingStore>(provider.GetRequiredService<IAtProtoSessionStore>());
            Assert.Single(services, d => d.ServiceType == typeof(IAtProtoSessionStore));
        }
    }

    [Fact]
    public async Task WithSessionStore_FromAFactory_IsWhereTheClientPersistsItsSession()
    {
        var store = new CountingStore();
        var services = Services();
        services.AddAtProto().WithSessionStore(_ => store);

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<AtProtoClient>().ApplySessionAsync(PasswordSession("access", "refresh"));

        Assert.Same(store, provider.GetRequiredService<IAtProtoSessionStore>());
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public void WithInMemorySessionStore_KeepsSessionsInMemory()
    {
        var services = Services();
        services.AddAtProto().WithClientFactory().WithInMemorySessionStore();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryAtProtoSessionStore>(provider.GetRequiredService<IAtProtoSessionStore>());
    }

    [Fact]
    public async Task WithFileSessionStore_StoresSessionsInTheConfiguredDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("atproto-sessions-");
        try
        {
            var services = Services();
            services.AddAtProto().WithClientFactory().WithFileSessionStore(o => o.Directory = directory.FullName);

            await using var provider = services.BuildServiceProvider();
            var store = Assert.IsType<FileAtProtoSessionStore>(provider.GetRequiredService<IAtProtoSessionStore>());
            await store.SetAsync(PasswordSession("access", "refresh"));

            Assert.Single(directory.GetFiles("*.dat"));
            Assert.Equal("access", Assert.IsType<PasswordSession>(await store.GetAsync(Alice)).AccessJwt);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WithFileSessionStore_ABlankDirectory_StopsTheHost()
    {
        using var host = Host(s => s.AddAtProto().WithFileSessionStore(o => o.Directory = "  "));

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(FileSessionStoreOptions.Directory), ex.Message);
    }

    // ── WithClientFactory ─────────────────────────────────────

    [Fact]
    public void WithClientFactory_RegistersTheFactoryAnInMemoryStoreAndTheCoordinator()
    {
        var services = Services();
        services.AddAtProto().WithClientFactory();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<AtProtoClientFactory>(provider.GetRequiredService<IAtProtoClientFactory>());
        Assert.IsType<InMemoryAtProtoSessionStore>(provider.GetRequiredService<IAtProtoSessionStore>());
        Assert.IsType<InProcessSessionRefreshCoordinator>(provider.GetRequiredService<ISessionRefreshCoordinator>());
    }

    [Fact]
    public async Task WithClientFactory_TheDefaultInMemoryStore_WarnsAtStartup()
    {
        var logs = new CapturingLoggerProvider();
        using var host = Host(s => s.AddAtProto().WithClientFactory(), logs);

        await host.StartAsync();
        await host.StopAsync();

        var warning = Assert.Single(logs.Entries, e => e.Category == typeof(InMemorySessionStoreWarning).FullName);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("WithFileSessionStore()", warning.Message);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("file")]
    [InlineData("custom")]
    public async Task WithClientFactory_AChosenStore_StartsWithoutAWarning(string store)
    {
        var directory = Directory.CreateTempSubdirectory("atproto-sessions-");
        try
        {
            var logs = new CapturingLoggerProvider();
            using var host = Host(s =>
            {
                var atproto = s.AddAtProto().WithClientFactory();
                _ = store switch
                {
                    "memory" => atproto.WithInMemorySessionStore(),
                    "file" => atproto.WithFileSessionStore(o => o.Directory = directory.FullName),
                    _ => atproto.WithSessionStore<CountingStore>(),
                };
            }, logs);

            await host.StartAsync();
            await host.StopAsync();

            Assert.DoesNotContain(logs.Entries, e => e.Category == typeof(InMemorySessionStoreWarning).FullName);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WithClientFactory_PerUserClientsTakeTheTransportOptionsOfAddAtProto()
    {
        // They used to be created with default options, whatever AddAtProto(o => …) said.
        var handler = new RateLimitedHandler();
        var services = Services();
        services.AddAtProto(o =>
            {
                o.UserAgent = "MyApp/1.0";
                o.RateLimit.MaxRetries = 0;
            })
            .WithClientFactory()
            .WithInMemorySessionStore()
            .HttpClient.AddHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IAtProtoSessionStore>().SetAsync(PasswordSession(AccessJwt("a1"), "r1"));
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(AtProtoClaimTypes.Did, Alice.Value)], "ATProto"));

        await using var client = await provider.GetRequiredService<IAtProtoClientFactory>().CreateClientForUserAsync(user);
        await Assert.ThrowsAsync<ATProtoNet.Http.XrpcRateLimitException>(() => client!.Server.DescribeServerAsync());

        var request = Assert.Single(handler.Requests);
        Assert.Equal("MyApp/1.0", request.Headers.UserAgent.ToString());
        Assert.Same(provider.GetRequiredService<ISessionRefreshCoordinator>(), ((AtProtoClientFactory)provider.GetRequiredService<IAtProtoClientFactory>()).RefreshCoordinator);
    }

    [Fact]
    public void WithClientFactory_WithTheDevelopmentLogin_ResolvesBeforeTheServerHasStarted()
    {
        // The loopback client's callback is the server's address, unknown until it has started;
        // the factory resolves the client when a request first needs it, not when it is built.
        var services = Services();
        services.AddAtProto().WithOAuth().WithClientFactory();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<AtProtoClientFactory>(provider.GetRequiredService<IAtProtoClientFactory>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<OAuthClient>());
    }

    // ── WithHealthCheck ───────────────────────────────────────

    [Fact]
    public void WithHealthCheck_RegistersTheCheckOnTheClient()
    {
        var services = Services();
        services.AddAtProto().WithHealthCheck();

        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);

        Assert.Equal("atproto-pds", registration.Name);
        Assert.Equal(HealthStatus.Degraded, registration.FailureStatus);
        Assert.Equal(new[] { "atproto", "ready" }, registration.Tags.Order());
        Assert.IsType<AtProtoPdsHealthCheck>(registration.Factory(provider));
    }

    [Fact]
    public async Task WithHealthCheck_WithAScopedClient_RunsInTheChecksScope()
    {
        var handler = new RecordingHandler();
        var services = Services();
        services.AddAtProto(o => o.InstanceUrl = "https://pds.example.com")
            .WithLifetime(ServiceLifetime.Scoped)
            .WithHealthCheck("pds", HealthStatus.Unhealthy, ["live"])
            .HttpClient.AddHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        var entry = Assert.Single(report.Entries);
        Assert.Equal("pds", entry.Key);
        Assert.Equal(HealthStatus.Healthy, entry.Value.Status);
        Assert.Equal(["live"], entry.Value.Tags);
    }

    [Fact]
    public async Task WithHealthCheck_AfterAddAtProtoClient_ReplacesItsCheck()
    {
        // Two registrations of one name make the health check service throw on the first /health.
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddAtProtoClient()
            .WithHealthCheck(failureStatus: HealthStatus.Unhealthy, tags: ["live"])
            .HttpClient.AddHttpMessageHandler(() => new RecordingHandler());

        using var host = builder.Build();
        var registration = Assert.Single(host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);
        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
        Assert.Equal(["live"], registration.Tags);
        Assert.Equal(HealthStatus.Healthy, Assert.Single(report.Entries).Value.Status);
    }

    [Fact]
    public async Task AddAtProtoClient_CalledTwice_RegistersOneCheck()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddAtProtoClient();
        builder.AddAtProtoClient().HttpClient.AddHttpMessageHandler(() => new RecordingHandler());

        using var host = builder.Build();
        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal("atproto-pds", Assert.Single(report.Entries).Key);
    }

    [Fact]
    public async Task WithHealthCheck_AnUnreachablePds_ReportsTheFailureStatus()
    {
        var services = Services();
        services.AddAtProto(o => o.InstanceUrl = "https://pds.example.com")
            .WithHealthCheck()
            .HttpClient.AddHttpMessageHandler(() => new FailingHandler());

        await using var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Degraded, Assert.Single(report.Entries).Value.Status);
    }

    // ── WithOAuth ─────────────────────────────────────────────

    [Fact]
    public void WithOAuth_RegistersTheLoginAndItsClient()
    {
        var services = Services();
        services.AddAtProto().WithOAuth(o => o.ClientMetadata = new OAuthClientMetadata
        {
            ClientId = "https://app.example.com/oauth-client-metadata.json",
            RedirectUris = ["https://app.example.com/atproto/callback"],
        });

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<AtProtoOAuthService>();

        Assert.Same(service.Client, provider.GetRequiredService<OAuthClient>());
        Assert.Same(provider.GetRequiredService<IOptions<AtProtoOAuthServerOptions>>().Value, provider.GetRequiredService<AtProtoOAuthServerOptions>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithOAuth_AndTheClientFactory_ShareOneRefreshCoordinator(bool loginFirst)
    {
        // The login stores and removes sessions under the lock the factory's clients refresh
        // under; two coordinators would let a sign-out interleave with a refresh.
        var services = Services();
        var atproto = services.AddAtProto();
        Action<AtProtoOAuthServerOptions> login = o => o.BaseUrl = "http://127.0.0.1:5000";
        if (loginFirst)
            atproto.WithOAuth(login).WithClientFactory();
        else
            atproto.WithClientFactory().WithOAuth(login);

        using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<ISessionRefreshCoordinator>();

        Assert.Same(coordinator, provider.GetRequiredService<AtProtoOAuthService>().RefreshCoordinator);
        Assert.Same(coordinator, ((AtProtoClientFactory)provider.GetRequiredService<IAtProtoClientFactory>()).RefreshCoordinator);
        Assert.Same(coordinator, provider.GetRequiredService<AtProtoClientOptions>().RefreshCoordinator);
    }

    [Fact]
    public void WithOAuth_TheLoginSendsThroughItsNamedClient()
    {
        var services = Services();
        services.AddAtProto().WithOAuth(o =>
        {
            o.BaseUrl = "http://127.0.0.1:5000";
            o.HttpClientTimeout = TimeSpan.FromSeconds(7);
        });

        using var provider = services.BuildServiceProvider();
        var oauth = provider.GetRequiredService<OAuthClient>();

        Assert.Equal(TimeSpan.FromSeconds(7), oauth.HttpClient.Timeout);
        Assert.StartsWith("ATProtoNet/", oauth.HttpClient.DefaultRequestHeaders.UserAgent.ToString());

        // The identity fetch policy: its own handler, which follows no redirects, goes through no
        // proxy, and connects only to public addresses.
        var primary = Assert.IsType<SocketsHttpHandler>(PrimaryHandlerOf(provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(AtProtoOAuthExtensions.HttpClientName)));
        Assert.False(primary.AllowAutoRedirect);
        Assert.False(primary.UseProxy);
        Assert.NotNull(primary.ConnectCallback);
    }

    [Fact]
    public void WithOAuth_AllowPrivateNetworks_LiftsTheAddressCheck()
    {
        var services = Services();
        services.AddAtProto().WithOAuth(o => o.AllowPrivateNetworks = true);

        using var provider = services.BuildServiceProvider();
        var primary = Assert.IsType<SocketsHttpHandler>(PrimaryHandlerOf(provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(AtProtoOAuthExtensions.HttpClientName)));

        Assert.Null(primary.ConnectCallback);
    }

    [Fact]
    public void WithOAuth_BindsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AtProto:OAuth:ClientName"] = "My App",
                ["AtProto:OAuth:BaseUrl"] = "https://app.example.com",
                ["AtProto:OAuth:HttpClientTimeout"] = "00:00:12",
                ["AtProto:OAuth:ClientMetadata:ClientId"] = "https://app.example.com/oauth-client-metadata.json",
                ["AtProto:OAuth:ClientMetadata:RedirectUris:0"] = "https://app.example.com/atproto/callback",
            })
            .Build();
        var services = Services();
        services.AddAtProto().WithOAuth();
        services.Configure<AtProtoOAuthServerOptions>(configuration.GetSection("AtProto:OAuth"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AtProtoOAuthServerOptions>();

        Assert.Equal("My App", options.ClientName);
        Assert.Equal("https://app.example.com", options.BaseUrl);
        Assert.Equal(TimeSpan.FromSeconds(12), options.HttpClientTimeout);
        Assert.Equal(["https://app.example.com/atproto/callback"], options.ClientMetadata!.RedirectUris);
        Assert.Equal("https://app.example.com/oauth-client-metadata.json", provider.GetRequiredService<OAuthClient>().ClientId);
    }

    public static TheoryData<string, Action<AtProtoOAuthServerOptions>> InvalidOAuthOptions => new()
    {
        { "Scopes", o => o.Scopes = "transition:generic" },
        { "RoutePrefix", o => o.RoutePrefix = "atproto" },
        { "LoginPath", o => o.LoginPath = "//evil.example.com" },
        { "BaseUrl", o => o.BaseUrl = "app.example.com" },
        { "HttpClientTimeout", o => o.HttpClientTimeout = TimeSpan.Zero },
        { "CookieScheme", o => o.CookieScheme = "" },
        { "ClientKeys", o => o.ClientKeys.Add(OAuthClientKey.Generate("key-1")) },
    };

    [Theory]
    [MemberData(nameof(InvalidOAuthOptions))]
    public async Task WithOAuth_InvalidOptions_StopTheHost(string setting, Action<AtProtoOAuthServerOptions> configure)
    {
        using var host = Host(s => s.AddAtProto().WithOAuth(configure));

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(setting, ex.Message);
    }

    // ── Other registrations' options ──────────────────────────

    [Fact]
    public async Task AddAtProtoIdentity_InvalidOptions_StopTheHost()
    {
        using var host = Host(s => s.AddAtProtoIdentity(o => o.RequestTimeout = TimeSpan.Zero));

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("RequestTimeout", ex.Message);
    }

    [Fact]
    public async Task AddAtProtoSpaces_InvalidOptions_StopTheHost()
    {
        using var host = Host(s => s.AddAtProtoSpaces(o => o.ProofLifetime = TimeSpan.Zero));

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("ProofLifetime", ex.Message);
    }

    [Fact]
    public async Task AddSpaceAuthority_WithoutAServiceDid_StopsTheHostInOptionsValidation()
    {
        using var host = Host(s => s.AddAtProtoSpaces(o => o.WarnOnInMemoryStores = false)
            .AddSpaceAuthority<InMemorySpaceAuthorityStore>(ATProtoNet.Crypto.AtProtoCrypto.GenerateP256Key()));

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(SpaceServerOptions.ServiceDid), ex.Message);
    }

    [Fact]
    public async Task AddAtProtoSpaces_WithoutAnAuthority_StartsWithoutAServiceDid()
    {
        // Verifying tokens, or hosting repos, needs no DID of the service's own.
        using var host = Host(s => s.AddAtProtoSpaces(o => o.WarnOnInMemoryStores = false));

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public void AddAtProtoSpaces_CalledTwice_AppliesBothConfigurations()
    {
        var services = Services();
        services.AddAtProtoSpaces(o => o.PublicBaseUrl = "https://pds.example.com");
        services.AddAtProtoSpaces(o => o.CredentialLifetime = TimeSpan.FromHours(1));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<SpaceServerOptions>();

        Assert.Equal("https://pds.example.com", options.PublicBaseUrl);
        Assert.Equal(TimeSpan.FromHours(1), options.CredentialLifetime);
    }

    [Fact]
    public void AddJetstream_RegistersTheCheck()
    {
        var services = Services();
        services.AddHealthChecks().AddJetstream("wss://jetstream.example.com", tags: ["jetstream"]);

        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);

        Assert.Equal("jetstream", registration.Name);
        Assert.Equal(["jetstream"], registration.Tags);
        Assert.IsType<JetstreamHealthCheck>(registration.Factory(provider));
    }

    private static HttpMessageHandler PrimaryHandlerOf(HttpMessageHandler handler)
    {
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;
        return handler;
    }

    /// <summary>Answers every request with an empty object, and keeps them.</summary>
    private sealed class RecordingHandler : DelegatingHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"did":"did:web:pds.example.com","availableUserDomains":[]}""", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>Answers every request with a 429 that may be retried at once, and keeps them.</summary>
    private sealed class RateLimitedHandler : DelegatingHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":"RateLimitExceeded","message":"slow down"}""", System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }

    /// <summary>Fails every request as an unreachable host would.</summary>
    private sealed class FailingHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused"));
    }

    /// <summary>A store of the application's own, counting what is written to it.</summary>
    private sealed class CountingStore : IAtProtoSessionStore
    {
        private readonly InMemoryAtProtoSessionStore _inner = new();

        public int Writes { get; private set; }

        public ValueTask<AtProtoSession?> GetAsync(Did did, CancellationToken cancellationToken = default) =>
            _inner.GetAsync(did, cancellationToken);

        public ValueTask SetAsync(AtProtoSession session, CancellationToken cancellationToken = default)
        {
            Writes++;
            return _inner.SetAsync(session, cancellationToken);
        }

        public ValueTask RemoveAsync(Did did, CancellationToken cancellationToken = default) =>
            _inner.RemoveAsync(did, cancellationToken);
    }
}
