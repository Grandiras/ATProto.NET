using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Server.Xrpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ATProtoNet.Tests.Server;

// ── Test Endpoint Handlers ────────────────────────────────────

public sealed class HealthQueryParams
{
    [JsonPropertyName("verbose")]
    public string? Verbose { get; init; }
}

public sealed class HealthQueryOutput
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

[XrpcEndpoint(Nsid = "com.example.healthCheck")]
public sealed class HealthCheckQuery : IXrpcQuery<HealthQueryParams, HealthQueryOutput>
{
    public string Nsid => "com.example.healthCheck";

    public Task<HealthQueryOutput> HandleAsync(HealthQueryParams parameters, HttpContext context, CancellationToken cancellationToken)
    {
        var output = new HealthQueryOutput
        {
            Status = "ok",
            Version = parameters.Verbose == "true" ? "1.0.0" : null,
        };
        return Task.FromResult(output);
    }
}

public sealed class SimpleQueryOutput
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

[XrpcEndpoint(Nsid = "com.example.simpleQuery")]
public sealed class SimpleQuery : IXrpcQuery<SimpleQueryOutput>
{
    public string Nsid => "com.example.simpleQuery";

    public Task<SimpleQueryOutput> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SimpleQueryOutput { Message = "hello" });
    }
}

public sealed class EchoInput
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

public sealed class EchoOutput
{
    [JsonPropertyName("echo")]
    public required string Echo { get; init; }
}

[XrpcEndpoint(Nsid = "com.example.echo")]
public sealed class EchoProcedure : IXrpcProcedure<EchoInput, EchoOutput>
{
    public string Nsid => "com.example.echo";

    public Task<EchoOutput> HandleAsync(EchoInput input, HttpContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new EchoOutput { Echo = input.Text });
    }
}

public sealed class PingInput
{
    [JsonPropertyName("target")]
    public required string Target { get; init; }
}

[XrpcEndpoint(Nsid = "com.example.ping")]
public sealed class PingProcedure : IXrpcProcedureVoid<PingInput>
{
    public string Nsid => "com.example.ping";
    public static string LastTarget { get; private set; } = "";

    public Task HandleAsync(PingInput input, HttpContext context, CancellationToken cancellationToken)
    {
        LastTarget = input.Target;
        return Task.CompletedTask;
    }
}

// ── Tests ─────────────────────────────────────────────────────

public class XrpcEndpointTests : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddXrpcEndpoint<HealthCheckQuery>();
                    services.AddXrpcEndpoint<SimpleQuery>();
                    services.AddXrpcEndpoint<EchoProcedure>();
                    services.AddXrpcEndpoint<PingProcedure>();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapXrpcEndpoints());
                });
            })
            .Build();

        await _host.StartAsync();
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
            await _host.StopAsync();
        _host?.Dispose();
    }

    [Fact]
    public async Task QueryWithParams_ReturnsCorrectResponse()
    {
        var response = await _client!.GetAsync("/xrpc/com.example.healthCheck?verbose=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<HealthQueryOutput>();
        Assert.NotNull(result);
        Assert.Equal("ok", result.Status);
        Assert.Equal("1.0.0", result.Version);
    }

    [Fact]
    public async Task QueryWithParams_NoParams_StillWorks()
    {
        var response = await _client!.GetAsync("/xrpc/com.example.healthCheck");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<HealthQueryOutput>();
        Assert.NotNull(result);
        Assert.Equal("ok", result.Status);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task SimpleQuery_ReturnsOutput()
    {
        var response = await _client!.GetAsync("/xrpc/com.example.simpleQuery");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SimpleQueryOutput>();
        Assert.NotNull(result);
        Assert.Equal("hello", result.Message);
    }

    [Fact]
    public async Task Procedure_EchoesInput()
    {
        var response = await _client!.PostAsJsonAsync("/xrpc/com.example.echo", new EchoInput { Text = "test123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<EchoOutput>();
        Assert.NotNull(result);
        Assert.Equal("test123", result.Echo);
    }

    [Fact]
    public async Task ProcedureVoid_Returns200()
    {
        var response = await _client!.PostAsJsonAsync("/xrpc/com.example.ping", new PingInput { Target = "bsky.social" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownEndpoint_Returns404()
    {
        var response = await _client!.GetAsync("/xrpc/com.example.doesNotExist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Procedure_PostWithNoBody_Returns400()
    {
        var response = await _client!.PostAsync("/xrpc/com.example.echo", null);
        // Empty body should result in BadRequest
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task QueryEndpoint_PostMethod_Returns405()
    {
        var response = await _client!.PostAsync("/xrpc/com.example.simpleQuery", null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}

public class XrpcAssemblyScanTests : IAsyncLifetime
{
    private IHost? _host;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddXrpcEndpointsFromAssembly(typeof(HealthCheckQuery).Assembly);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapXrpcEndpoints());
                });
            })
            .Build();

        await _host.StartAsync();
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
            await _host.StopAsync();
        _host?.Dispose();
    }

    [Fact]
    public async Task AssemblyScannedEndpoints_AreReachable()
    {
        // HealthCheckQuery should have been scanned and registered
        var response = await _client!.GetAsync("/xrpc/com.example.healthCheck");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AssemblyScannedProcedure_Works()
    {
        var response = await _client!.PostAsJsonAsync("/xrpc/com.example.echo", new EchoInput { Text = "scanned" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<EchoOutput>();
        Assert.NotNull(result);
        Assert.Equal("scanned", result.Echo);
    }
}
