using System.Net;
using ATProtoNet.Aspire;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ATProtoNet.Tests.Aspire;

public class AtProtoPdsHealthCheckTests
{
    private const string DescribeServer = "com.atproto.server.describeServer";

    private static async Task<HealthCheckResult> CheckAsync(HttpStub pds, HealthStatus? failureStatus = null)
    {
        var healthCheck = new AtProtoPdsHealthCheck(pds.CreateClient("https://test-pds.example.com"));

        return await healthCheck.CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, failureStatus, null),
        });
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenServerResponds()
    {
        using var pds = new HttpStub().On(
            DescribeServer, """{"availableUserDomains":["test.bsky.social"],"did":"did:web:test-pds.example.com"}""");

        var result = await CheckAsync(pds);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("test-pds.example.com", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenServerThrows()
    {
        using var pds = new HttpStub().On(DescribeServer, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await CheckAsync(pds);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenServerThrows_ReportsTheRegistrationsFailureStatus()
    {
        // WithHealthCheck() registers the check as Degraded; it always answered Unhealthy.
        using var pds = new HttpStub().On(DescribeServer, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await CheckAsync(pds, HealthStatus.Degraded);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.NotNull(result.Exception);
    }
}
