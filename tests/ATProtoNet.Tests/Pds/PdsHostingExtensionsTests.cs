using ATProtoNet.Pds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Tests.Pds;

public class PdsHostingExtensionsTests
{
    [Fact]
    public void AddAtProtoPds_ConfiguredSigningKey_IsUsedBySessionService()
    {
        var key = PdsSessionService.GenerateSigningKey();

        // Two containers stand in for two process lifetimes sharing the same configuration.
        var first = BuildProvider(o => o.SessionSigningKey = key)
            .GetRequiredService<PdsSessionService>();
        var second = BuildProvider(o => o.SessionSigningKey = key)
            .GetRequiredService<PdsSessionService>();

        var token = first.IssueAccessToken("did:plc:test123", "alice.test.local");
        var result = second.ValidateToken(token);

        Assert.False(first.UsesEphemeralSigningKey);
        Assert.NotNull(result);
        Assert.Equal("did:plc:test123", result!.Did);
    }

    [Fact]
    public void AddAtProtoPds_NoSigningKey_FallsBackToEphemeralKeyAndWarns()
    {
        var logger = new CapturingLoggerProvider();
        var sessions = BuildProvider(configure: null, logger).GetRequiredService<PdsSessionService>();

        Assert.True(sessions.UsesEphemeralSigningKey);
        Assert.Contains(logger.Warnings, w => w.Contains("SessionSigningKey"));
    }

    [Fact]
    public void AddAtProtoPds_ConfiguredSigningKey_DoesNotWarn()
    {
        var logger = new CapturingLoggerProvider();
        var provider = BuildProvider(o => o.SessionSigningKey = PdsSessionService.GenerateSigningKey(), logger);

        provider.GetRequiredService<PdsSessionService>();

        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void AddAtProtoPds_ShortSigningKey_Warns()
    {
        var logger = new CapturingLoggerProvider();
        var provider = BuildProvider(o => o.SessionSigningKey = Convert.ToBase64String(new byte[8]), logger);

        var sessions = provider.GetRequiredService<PdsSessionService>();

        Assert.False(sessions.UsesEphemeralSigningKey);
        Assert.Contains(logger.Warnings, w => w.Contains("8 bytes"));
    }

    [Fact]
    public void AddAtProtoPds_WithCustomStores_UsesConfiguredSigningKey()
    {
        var key = PdsSessionService.GenerateSigningKey();

        var services = new ServiceCollection();
        services.AddAtProtoPds<InMemoryAccountStore, InMemoryRepoStore>(o =>
        {
            o.Hostname = "test.local";
            o.SessionSigningKey = key;
        });

        var sessions = services.BuildServiceProvider().GetRequiredService<PdsSessionService>();
        var token = sessions.IssueAccessToken("did:plc:test123", "alice.test.local");

        Assert.False(sessions.UsesEphemeralSigningKey);
        Assert.NotNull(new PdsSessionService(new PdsOptions { Hostname = "test.local", SessionSigningKey = key })
            .ValidateToken(token));
    }

    [Fact]
    public void AddAtProtoPds_InvalidSigningKey_ThrowsOnResolve()
    {
        var provider = BuildProvider(o => o.SessionSigningKey = "not base64!!");

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<PdsSessionService>());
    }

    [Fact]
    public async Task AddAtProtoPds_NoSigningKey_WarnsAtHostStartup()
    {
        var logger = new CapturingLoggerProvider();
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(builder => builder.AddProvider(logger));
                services.AddAtProtoPds(options => options.Hostname = "test.local");
            })
            .Build();

        // The warning must land before the first request, not on the first login.
        await host.StartAsync();
        try
        {
            Assert.Contains(logger.Warnings, w => w.Contains("SessionSigningKey"));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public void AddAtProtoPds_CalledTwice_RegistersOneStartupCheck()
    {
        var services = new ServiceCollection();
        services.AddAtProtoPds(o => o.Hostname = "test.local");
        services.AddAtProtoPds(o => o.Hostname = "test.local");

        var hostedServices = services.BuildServiceProvider().GetServices<IHostedService>();

        Assert.Single(hostedServices);
    }

    [Fact]
    public void AddAtProtoPds_ResolvesAFederatingPdsService()
    {
        // PdsService has a federating and a non-federating constructor. The registration picks
        // one explicitly rather than leaving it to the container's greediest-constructor rule,
        // so this asserts the documented outcome instead of the heuristic's.
        var pds = BuildProvider(configure: null).GetRequiredService<PdsService>();

        Assert.NotNull(pds.RepoManager);
        Assert.NotNull(pds.Identity);
    }

    [Fact]
    public void AddAtProtoPds_CustomRepoStore_StillResolvesAFederatingPdsService()
    {
        var services = new ServiceCollection();
        services.AddAtProtoPds<InMemoryAccountStore, InMemoryRepoStore>(o => o.Hostname = "test.local");

        var pds = services.BuildServiceProvider().GetRequiredService<PdsService>();

        Assert.NotNull(pds.RepoManager);
        Assert.False(pds.RepoManager!.IsRepositoryEnumerationUnsupported);
    }

    [Fact]
    public void AddAtProtoPds_RegistersTheInMemoryInviteCodeStoreByDefault()
    {
        var store = BuildProvider(configure: null).GetRequiredService<IInviteCodeStore>();

        Assert.IsType<InMemoryInviteCodeStore>(store);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddAtProtoPdsInviteCodeStore_WinsOverTheDefaultInEitherCallOrder(bool storeFirst)
    {
        var services = new ServiceCollection();
        if (storeFirst) services.AddAtProtoPdsInviteCodeStore<TrackingInviteCodeStore>();
        services.AddAtProtoPds(o => o.Hostname = "test.local");
        if (!storeFirst) services.AddAtProtoPdsInviteCodeStore<TrackingInviteCodeStore>();

        var provider = services.BuildServiceProvider();
        var store = Assert.IsType<TrackingInviteCodeStore>(provider.GetRequiredService<IInviteCodeStore>());

        // Resolved, not just registered: the PdsService has to issue codes into this store.
        var code = await provider.GetRequiredService<PdsService>().CreateInviteCodeAsync();

        Assert.Equal([code], store.Created);
    }

    /// <summary>Records what was written, delegating everything else to the in-memory store.</summary>
    private sealed class TrackingInviteCodeStore : IInviteCodeStore
    {
        private readonly InMemoryInviteCodeStore _inner = new();

        public List<string> Created { get; } = [];

        public Task CreateAsync(PdsInviteCode code, CancellationToken cancellationToken = default)
        {
            Created.Add(code.Code);
            return _inner.CreateAsync(code, cancellationToken);
        }

        public Task<PdsInviteCode?> GetAsync(string code, CancellationToken cancellationToken = default)
            => _inner.GetAsync(code, cancellationToken);

        public Task<bool> TryClaimAsync(string code, CancellationToken cancellationToken = default)
            => _inner.TryClaimAsync(code, cancellationToken);

        public Task ConfirmClaimAsync(string code, string usedByDid, CancellationToken cancellationToken = default)
            => _inner.ConfirmClaimAsync(code, usedByDid, cancellationToken);

        public Task ReleaseClaimAsync(string code, CancellationToken cancellationToken = default)
            => _inner.ReleaseClaimAsync(code, cancellationToken);

        public Task<InviteCodePage> ListAsync(InviteCodeQuery query, CancellationToken cancellationToken = default)
            => _inner.ListAsync(query, cancellationToken);

        public Task<int> DisableAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
            => _inner.DisableAsync(codes, cancellationToken);

        public Task<int> DisableForAccountsAsync(IEnumerable<string> accountDids, CancellationToken cancellationToken = default)
            => _inner.DisableForAccountsAsync(accountDids, cancellationToken);
    }

    private static ServiceProvider BuildProvider(
        Action<PdsOptions>? configure,
        ILoggerProvider? loggerProvider = null)
    {
        var services = new ServiceCollection();
        if (loggerProvider is not null)
            services.AddLogging(builder => builder.AddProvider(loggerProvider));

        services.AddAtProtoPds(options =>
        {
            options.Hostname = "test.local";
            configure?.Invoke(options);
        });

        return services.BuildServiceProvider();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Warnings);

        public void Dispose() { }

        private sealed class CapturingLogger(List<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning)
                    warnings.Add(formatter(state, exception));
            }
        }
    }
}
