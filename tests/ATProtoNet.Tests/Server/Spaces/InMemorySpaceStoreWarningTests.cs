using ATProtoNet.Server.Spaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// The startup notice about the in-process default stores. Nothing else in a deployment says
/// that single-use tokens are only being tracked per process, and by the time it matters the
/// symptom is an accepted replay rather than an error.
/// </summary>
public class InMemorySpaceStoreWarningTests
{
    [Fact]
    public async Task StartAsync_WithTheDefaultReplayStore_Warns()
    {
        var logs = new CapturingLoggerProvider();
        var services = BuildServices(logs);

        await StartAsync(services);

        var warning = Assert.Single(logs.Records, record => record.Message.Contains("single-use tokens"));
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("AddAtProtoRedisSpaceReplayStore", warning.Message);
    }

    [Fact]
    public async Task StartAsync_WithAnotherReplayStore_SaysNothing()
    {
        var logs = new CapturingLoggerProvider();
        var services = BuildServices(logs, configure: s =>
            s.AddSingleton<ISpaceReplayStore>(new SharedReplayStore()));

        await StartAsync(services);

        Assert.DoesNotContain(logs.Records, record => record.Message.Contains("single-use tokens"));
    }

    [Fact]
    public async Task StartAsync_WithTheDefaultSimpleSpaceStore_WarnsThatMemberListsAreLost()
    {
        var logs = new CapturingLoggerProvider();
        var services = BuildServices(logs, configure: s =>
            s.AddSingleton<ISimpleSpaceStore>(new InMemorySimpleSpaceStore()));

        await StartAsync(services);

        var warning = Assert.Single(logs.Records, record => record.Message.Contains("member lists"));
        Assert.Equal(LogLevel.Warning, warning.Level);
    }

    [Fact]
    public async Task StartAsync_WithTheDefaultAuthorityStore_OnlyInforms()
    {
        // The writer set is only what the authority claims, and the next notifyWrite from any
        // repo host restores an entry — worth saying, not worth warning about.
        var logs = new CapturingLoggerProvider();
        var services = BuildServices(logs, configure: s =>
            s.AddSingleton<ISpaceAuthorityStore>(new InMemorySpaceAuthorityStore()));

        await StartAsync(services);

        var record = Assert.Single(logs.Records, r => r.Message.Contains("writer sets"));
        Assert.Equal(LogLevel.Information, record.Level);
    }

    [Fact]
    public async Task StartAsync_WhenSuppressed_SaysNothing()
    {
        var logs = new CapturingLoggerProvider();
        var services = BuildServices(
            logs,
            options => options.WarnOnInMemoryStores = false,
            s => s.AddSingleton<ISimpleSpaceStore>(new InMemorySimpleSpaceStore()));

        await StartAsync(services);

        Assert.Empty(logs.Records);
    }

    private static ServiceProvider BuildServices(
        CapturingLoggerProvider logs,
        Action<SpaceServerOptions>? options = null,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddAtProtoSpaces(o =>
        {
            o.ServiceDid = "did:web:pds.example.com";
            options?.Invoke(o);
        });
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static async Task StartAsync(ServiceProvider services)
    {
        var warning = services.GetServices<IHostedService>().OfType<InMemorySpaceStoreWarning>().Single();
        await warning.StartAsync(TestContext.Current.CancellationToken);
    }

    private sealed class SharedReplayStore : ISpaceReplayStore
    {
        public ValueTask<bool> TryConsumeAsync(
            string issuer, string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Records { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capturing(this);

        public void Dispose()
        {
        }

        private sealed class Capturing(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => provider.Records.Add((logLevel, formatter(state, exception)));
        }
    }
}
