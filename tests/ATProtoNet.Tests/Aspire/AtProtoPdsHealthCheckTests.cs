using System.Net;
using System.Text.Json;
using ATProtoNet.Aspire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ATProtoNet.Tests.Aspire;

public class AtProtoPdsHealthCheckTests
{
    private static AtProtoClient CreateClientWithHandler(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = new AtProtoClientOptions
        {
            InstanceUrl = "https://test-pds.example.com",
        };
        return new AtProtoClient(options, httpClient, null, null);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenServerResponds()
    {
        var response = new
        {
            availableUserDomains = new[] { "test.bsky.social" },
            did = "did:web:test-pds.example.com"
        };

        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(response),
                    System.Text.Encoding.UTF8,
                    "application/json")
            });

        var client = CreateClientWithHandler(handler);
        var healthCheck = new AtProtoPdsHealthCheck(client);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("test", healthCheck, null, null)
            });

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("test-pds.example.com", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenServerThrows()
    {
        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server Error")
            });

        var client = CreateClientWithHandler(handler);
        var healthCheck = new AtProtoPdsHealthCheck(client);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration("test", healthCheck, null, null)
            });

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public FakeHttpMessageHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }
}
