using ATProtoNet.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Aspire;

/// <summary>
/// Configuration for the AT Protocol Aspire integration.
/// </summary>
public sealed class AtProtoClientSettings
{
    /// <summary>
    /// The PDS / service instance URL.
    /// Default: "https://bsky.social"
    /// </summary>
    public string InstanceUrl { get; set; } = "https://bsky.social";

    /// <summary>
    /// The WebSocket relay URL for firehose subscriptions.
    /// Default: "wss://bsky.network"
    /// </summary>
    public string? RelayUrl { get; set; } = "wss://bsky.network";

    /// <summary>
    /// Whether to auto-refresh session tokens.
    /// Default: true
    /// </summary>
    public bool AutoRefreshSession { get; set; } = true;

    /// <summary>
    /// Whether to add a health check for PDS connectivity.
    /// Default: true
    /// </summary>
    public bool DisableHealthChecks { get; set; }

    /// <summary>
    /// Whether to add standard resilience (retry, circuit breaker) to the HTTP client.
    /// Default: false (resilience enabled)
    /// </summary>
    public bool DisableResilience { get; set; }
}

/// <summary>
/// Extension methods for integrating ATProto.NET into .NET Aspire service defaults.
/// </summary>
public static class AtProtoAspireExtensions
{
    private const string DefaultConfigSection = "AtProto";
    private const string HealthCheckName = "atproto-pds";

    /// <summary>
    /// Registers <see cref="AtProtoClient"/> as a singleton service with health checks
    /// and resilience policies suitable for distributed Aspire deployments.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configurationSectionName">
    /// The configuration section to bind settings from (default: "AtProto").
    /// </param>
    /// <param name="configureSettings">Optional callback to further configure settings.</param>
    /// <returns>The host application builder for chaining.</returns>
    public static IHostApplicationBuilder AddAtProtoClient(
        this IHostApplicationBuilder builder,
        string configurationSectionName = DefaultConfigSection,
        Action<AtProtoClientSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = new AtProtoClientSettings();
        builder.Configuration.GetSection(configurationSectionName).Bind(settings);
        configureSettings?.Invoke(settings);

        // Register the HTTP client with optional resilience
        var httpClientBuilder = builder.Services.AddHttpClient("ATProtoNet", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "ATProtoNet.Aspire/1.0");
        });

        if (!settings.DisableResilience)
        {
            httpClientBuilder.AddStandardResilienceHandler();
        }

        // Register AtProtoClient as singleton
        builder.Services.AddSingleton(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("ATProtoNet");
            var logger = sp.GetService<ILogger<AtProtoClient>>();

            var options = new AtProtoClientOptions
            {
                InstanceUrl = settings.InstanceUrl,
                AutoRefreshSession = settings.AutoRefreshSession,
                RelayUrl = settings.RelayUrl,
            };

            return new AtProtoClient(options, httpClient, new InMemorySessionStore(), logger);
        });

        // Health check
        if (!settings.DisableHealthChecks)
        {
            builder.Services.AddHealthChecks()
                .Add(new HealthCheckRegistration(
                    HealthCheckName,
                    sp => new AtProtoPdsHealthCheck(sp.GetRequiredService<AtProtoClient>()),
                    failureStatus: HealthStatus.Degraded,
                    tags: ["atproto", "ready"]));
        }

        return builder;
    }
}
