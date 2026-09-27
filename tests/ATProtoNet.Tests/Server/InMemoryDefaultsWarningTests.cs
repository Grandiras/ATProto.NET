using ATProtoNet.Identity;
using ATProtoNet.Server;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Tests.Server;

/// <summary>
/// The startup notice about the in-process default replay store. Nothing else in a deployment
/// says that single-use tokens are only being tracked per process, and by the time it matters the
/// symptom is an accepted replay rather than an error. (The session-store notice is in
/// <c>AtProtoBuilderTests</c>.)
/// </summary>
public class InMemoryDefaultsWarningTests
{
    public static TheoryData<string> Registrations => ["spaces", "service auth"];

    [Theory]
    [MemberData(nameof(Registrations))]
    public async Task StartAsync_WithTheDefaultReplayStore_Warns(string registration)
    {
        // Service auth used to fall back to it without a word.
        var logs = new CapturingLoggerProvider();
        var services = BuildServices(logs, registration);

        await StartAsync(services);

        var warning = Assert.Single(logs.Entries, entry => entry.Message.Contains("Single-use tokens"));
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("AddAtProtoEfCoreJtiReplayStore", warning.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartAsync_WithAReplayStoreTheApplicationRegistered_SaysNothing(bool inMemory)
    {
        // Registering the in-memory store yourself is the choice that silences it.
        var logs = new CapturingLoggerProvider();
        var services = BuildServices(logs, "spaces", s =>
            s.AddSingleton<IJtiReplayStore>(inMemory ? new InMemoryJtiReplayStore() : new SharedReplayStore()));

        await StartAsync(services);

        Assert.Empty(logs.Entries);
    }

    [Fact]
    public async Task StartAsync_WithTheInMemorySpaceStores_SaysNothingAboutThem()
    {
        // Only the replay store is a silent correctness gap across instances; the space stores
        // are the documented development defaults and say so in their own docs.
        var logs = new CapturingLoggerProvider();
        var services = BuildServices(logs, "spaces", s =>
        {
            s.AddSingleton<IJtiReplayStore>(new SharedReplayStore());
            s.AddSingleton<ISimpleSpaceStore>(new InMemorySimpleSpaceStore());
            s.AddSingleton<ISpaceAuthorityStore>(new InMemorySpaceAuthorityStore());
        });

        await StartAsync(services);

        Assert.Empty(logs.Entries);
    }

    [Theory]
    [MemberData(nameof(Registrations))]
    public async Task HostStart_WithAScopedReplayStore_UnderScopeValidation_StartsWithoutAWarning(string registration)
    {
        // Regression: the warning resolved the store from the root provider to look at it, which
        // scope validation (on in Development) refuses for a scoped store, stopping the host.
        var logs = new CapturingLoggerProvider();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateScopes = true }));
        builder.Services.AddLogging(logging => logging.AddProvider(logs));
        Register(builder.Services, registration);
        builder.Services.AddScoped<IJtiReplayStore, SharedReplayStore>();
        using var host = builder.Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        // Only this warning's own category: AddAuthentication() brings Data Protection, which on a
        // machine with no key ring yet (a fresh CI container) creates one at startup and warns
        // that no XML encryptor is configured.
        Assert.DoesNotContain(logs.Entries, entry => entry.Category == typeof(InMemoryDefaultsWarning).FullName);
    }

    [Fact]
    public async Task StartAsync_ServiceAuthOnly_DoesNotResolveASessionStore()
    {
        // The warning reads the registrations; a store the application registered is never built.
        var logs = new CapturingLoggerProvider();
        var services = BuildServices(logs, "service auth", s =>
            s.AddSingleton<ATProtoNet.Auth.IAtProtoSessionStore>(_ => throw new InvalidOperationException("resolved")));

        await StartAsync(services);

        Assert.Single(logs.Entries, entry => entry.Message.Contains("Single-use tokens"));
    }

    [Fact]
    public async Task StartAsync_AReplayStoreRegisteredBeforeTheFallback_SaysNothing()
    {
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddSingleton<IJtiReplayStore, InMemoryJtiReplayStore>();
        Register(services, "spaces");
        Register(services, "service auth");

        using var provider = services.BuildServiceProvider();

        Assert.DoesNotContain(provider.GetServices<IHostedService>(), service => service is InMemoryDefaultsWarning);
    }

    private static void Register(IServiceCollection services, string registration)
    {
        if (registration == "spaces")
            services.AddAtProtoSpaces(o => o.ServiceDid = Did.Parse("did:web:pds.example.com"));
        else
            services.AddAuthentication().AddAtProtoServiceAuth(o => o.Audiences.Add("did:web:pds.example.com#svc"));
    }

    private static ServiceProvider BuildServices(
        CapturingLoggerProvider logs, string registration, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        Register(services, registration);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static async Task StartAsync(ServiceProvider services)
    {
        var warning = services.GetServices<IHostedService>().OfType<InMemoryDefaultsWarning>().Single();
        await warning.StartAsync(TestContext.Current.CancellationToken);
    }

    private sealed class SharedReplayStore : IJtiReplayStore
    {
        public ValueTask<bool> TryConsumeAsync(
            string issuer, string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }
}
