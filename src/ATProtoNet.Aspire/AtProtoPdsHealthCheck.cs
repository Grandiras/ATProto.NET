using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ATProtoNet.Aspire;

/// <summary>
/// Health check that verifies connectivity to the configured PDS instance
/// by calling the <c>com.atproto.server.describeServer</c> endpoint.
/// </summary>
public sealed class AtProtoPdsHealthCheck : IHealthCheck
{
    private readonly AtProtoClient _client;

    public AtProtoPdsHealthCheck(AtProtoClient client)
    {
        _client = client;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var description = await _client.Server.DescribeServerAsync(cancellationToken);

            if (description is not null)
            {
                return HealthCheckResult.Healthy($"PDS reachable at {_client.PdsUrl}");
            }

            return HealthCheckResult.Degraded("PDS returned empty response");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"PDS unreachable at {_client.PdsUrl}",
                exception: ex);
        }
    }
}
