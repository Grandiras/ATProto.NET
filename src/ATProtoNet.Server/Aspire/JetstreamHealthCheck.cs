using ATProtoNet.Streaming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ATProtoNet.Aspire;

/// <summary>
/// Health check that asks a Jetstream instance whether it is up, through its public
/// <c>/xrpc/_health</c> endpoint.
/// </summary>
public sealed class JetstreamHealthCheck : IHealthCheck
{
    private readonly JetstreamArchiveClient _client;
    private readonly string _serviceUrl;

    /// <summary>
    /// Creates a health check for the Jetstream instance <paramref name="client"/> talks to.
    /// </summary>
    /// <param name="client">The client to call <see cref="JetstreamArchiveClient.GetHealthAsync"/> on.</param>
    /// <param name="serviceUrl">The instance's URL, for the result description.</param>
    public JetstreamHealthCheck(JetstreamArchiveClient client, string serviceUrl)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUrl);
        _client = client;
        _serviceUrl = serviceUrl;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await _client.GetHealthAsync(cancellationToken);
            return HealthCheckResult.Healthy(
                $"Jetstream reachable at {_serviceUrl}" + (health.Version is { } version ? $" (version {version})" : string.Empty));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy($"Jetstream unreachable at {_serviceUrl}", exception: ex);
        }
    }
}

/// <summary>Registers <see cref="JetstreamHealthCheck"/>.</summary>
public static class JetstreamHealthCheckExtensions
{
    /// <summary>
    /// Adds a health check for the Jetstream instance at <paramref name="serviceUrl"/>, calling its
    /// public <c>/xrpc/_health</c> endpoint.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="serviceUrl">The Jetstream host, e.g. <see cref="JetstreamEndpoints.UsEast"/>.
    /// <c>ws(s)</c> schemes are converted to <c>http(s)</c>.</param>
    /// <param name="name">The health check's name. Default: <c>jetstream</c>.</param>
    /// <param name="failureStatus">The status to report when the instance is unreachable, or null
    /// for the default (<see cref="HealthStatus.Unhealthy"/>).</param>
    /// <param name="tags">Tags to filter health checks by.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IHealthChecksBuilder AddJetstream(
        this IHealthChecksBuilder builder,
        string serviceUrl,
        string name = "jetstream",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUrl);

        // One client for the check's lifetime: its HttpClient is pooled, and the endpoint is public.
        var client = new Lazy<JetstreamArchiveClient>(() => new JetstreamArchiveClient(serviceUrl));
        return builder.Add(new HealthCheckRegistration(
            name,
            _ => new JetstreamHealthCheck(client.Value, serviceUrl),
            failureStatus,
            tags));
    }
}
