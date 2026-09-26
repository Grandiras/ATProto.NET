using System.Collections.Concurrent;
using System.Text.Json;
using ATProtoNet.Server.Xrpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Tests.TestSupport;

/// <summary>A test server hosting whatever ASP.NET Core endpoints the test registers.</summary>
/// <remarks>
/// Replaces the hand-rolled <c>HostBuilder().ConfigureWebHost(web =&gt; web.UseTestServer() ...)</c>
/// boilerplate that used to be repeated per test file. By default it maps
/// <see cref="XrpcEndpointExtensions.MapXrpcEndpoints"/> before <paramref name="configureEndpoints"/>
/// runs; pass <c>mapXrpcEndpoints: false</c> for a host that maps only its own endpoints (a Tap
/// webhook, OAuth endpoints, a space endpoint group) and never registers an
/// <see cref="IServiceCollection"/> XRPC handler, which <c>MapXrpcEndpoints</c> requires.
/// </remarks>
internal sealed class XrpcTestHost : IAsyncDisposable
{
    private readonly IHost _host;

    private XrpcTestHost(IHost host)
    {
        _host = host;
        Client = host.GetTestClient();
    }

    public HttpClient Client { get; }

    public static async Task<XrpcTestHost> StartAsync(
        Action<IServiceCollection> configureServices,
        Action<RouteGroupBuilder>? configureGroup = null,
        Action<IApplicationBuilder>? configureMiddleware = null,
        Action<IEndpointRouteBuilder>? configureEndpoints = null,
        bool mapXrpcEndpoints = true)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    configureServices(services);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    configureMiddleware?.Invoke(app);
                    app.UseEndpoints(endpoints =>
                    {
                        if (mapXrpcEndpoints)
                        {
                            var group = endpoints.MapXrpcEndpoints();
                            configureGroup?.Invoke(group);
                        }

                        configureEndpoints?.Invoke(endpoints);
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return new XrpcTestHost(host);
    }

    /// <summary>Reads an XRPC error envelope, asserting that the response is one.</summary>
    public static async Task<(string Error, string Message)> ReadErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (
            document.RootElement.GetProperty("error").GetString()!,
            document.RootElement.GetProperty("message").GetString()!);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}

/// <summary>Captures every log entry written through it, for asserting on what a host logged.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            provider.Entries.Enqueue((category, logLevel, formatter(state, exception), exception));
    }
}
