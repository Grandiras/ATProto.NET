using ATProtoNet.Aspire;
using ATProtoNet.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ATProtoNet.Tests.Aspire;

public class AtProtoAspireExtensionsTests
{
    private static HostApplicationBuilder CreateBuilder(Dictionary<string, string?>? config = null)
    {
        var builder = Host.CreateApplicationBuilder();
        if (config is not null)
        {
            builder.Configuration.AddInMemoryCollection(config);
        }
        return builder;
    }

    [Fact]
    public void AddAtProtoClient_RegistersAtProtoClientAsSingleton()
    {
        var builder = CreateBuilder();
        builder.AddAtProtoClient();

        using var host = builder.Build();

        Assert.Same(host.Services.GetRequiredService<AtProtoClient>(), host.Services.GetRequiredService<AtProtoClient>());
    }

    [Fact]
    public void AddAtProtoClient_ReturnsTheBuilderOfTheRegistration()
    {
        var builder = CreateBuilder();

        var atproto = builder.AddAtProtoClient();

        Assert.Same(builder.Services, atproto.Services);
        Assert.Equal(AtProtoServiceCollectionExtensions.HttpClientName, atproto.HttpClient.Name);
    }

    [Fact]
    public void AddAtProtoClient_BindsConfigurationSection()
    {
        var builder = CreateBuilder(new()
        {
            ["AtProto:InstanceUrl"] = "https://my-pds.example.com",
            ["AtProto:AutoRefreshSession"] = "false",
            ["AtProto:UserAgent"] = "MyApp/1.0",
            ["AtProto:RateLimit:MaxRetries"] = "0",
        });
        builder.AddAtProtoClient();

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<AtProtoClient>();
        var options = host.Services.GetRequiredService<IOptions<AtProtoClientOptions>>().Value;

        Assert.Equal(new Uri("https://my-pds.example.com/"), client.ServiceUrl);
        Assert.False(options.AutoRefreshSession);
        Assert.Equal("MyApp/1.0", options.UserAgent);
        Assert.Equal(0, options.RateLimit.MaxRetries);
    }

    [Fact]
    public void AddAtProtoClient_UsesCustomConfigurationSection()
    {
        var builder = CreateBuilder(new()
        {
            ["MyAtProto:InstanceUrl"] = "https://custom-pds.example.com",
        });
        builder.AddAtProtoClient(configurationSectionName: "MyAtProto");

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<AtProtoClient>();

        Assert.Equal(new Uri("https://custom-pds.example.com/"), client.ServiceUrl);
    }

    [Fact]
    public void AddAtProtoClient_ConfigureCallbackOverridesConfig()
    {
        var builder = CreateBuilder(new()
        {
            ["AtProto:InstanceUrl"] = "https://from-config.example.com",
        });
        builder.AddAtProtoClient(configure: options => options.InstanceUrl = "https://from-callback.example.com");

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<AtProtoClient>();

        Assert.Equal(new Uri("https://from-callback.example.com/"), client.ServiceUrl);
    }

    [Fact]
    public void AddAtProtoClient_WithAConnectionName_TakesTheInstanceUrlFromTheConnectionString()
    {
        // What an AppHost's WithReference(pds) supplies: the PDS resource's connection string.
        var builder = CreateBuilder(new()
        {
            ["ConnectionStrings:pds"] = "http://localhost:2583",
            ["AtProto:InstanceUrl"] = "https://from-config.example.com",
        });
        builder.AddAtProtoClient(connectionName: "pds");

        using var host = builder.Build();

        Assert.Equal(new Uri("http://localhost:2583/"), host.Services.GetRequiredService<AtProtoClient>().ServiceUrl);
    }

    [Fact]
    public void AddAtProtoClient_WithAConnectionNameNotConfigured_KeepsTheSection()
    {
        var builder = CreateBuilder(new() { ["AtProto:InstanceUrl"] = "https://from-config.example.com" });
        builder.AddAtProtoClient(connectionName: "pds");

        using var host = builder.Build();

        Assert.Equal(new Uri("https://from-config.example.com/"), host.Services.GetRequiredService<AtProtoClient>().ServiceUrl);
    }

    [Fact]
    public async Task AddAtProtoClient_AnInvalidInstanceUrl_StopsTheHostAtStartup()
    {
        var builder = CreateBuilder(new() { ["AtProto:InstanceUrl"] = "not a url" });
        builder.AddAtProtoClient();

        using var host = builder.Build();

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains("InstanceUrl", ex.Message);
    }

    [Fact]
    public void AddAtProtoClient_RegistersTheHealthCheckByDefault()
    {
        var builder = CreateBuilder();
        builder.AddAtProtoClient();

        using var host = builder.Build();
        var registration = Assert.Single(
            host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations,
            r => r.Name == "atproto-pds");

        Assert.Equal(HealthStatus.Degraded, registration.FailureStatus);
        Assert.Equal(new[] { "atproto", "ready" }, registration.Tags.Order());
        Assert.IsType<AtProtoPdsHealthCheck>(registration.Factory(host.Services));
    }

    [Fact]
    public void AddAtProtoClient_DisableHealthChecks_DoesNotRegisterHealthCheck()
    {
        var builder = CreateBuilder(new()
        {
            ["AtProto:DisableHealthChecks"] = "true",
        });
        builder.AddAtProtoClient();

        using var host = builder.Build();
        var options = host.Services.GetService<IOptions<HealthCheckServiceOptions>>();

        Assert.DoesNotContain(options?.Value.Registrations ?? [], r => r.Name == "atproto-pds");
    }

    [Fact]
    public void AddAtProtoClient_DefaultSettings_UsesDefaultInstanceUrl()
    {
        var builder = CreateBuilder();
        builder.AddAtProtoClient();

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<AtProtoClient>();

        Assert.Equal(new Uri("https://bsky.social/"), client.ServiceUrl);
    }

    [Fact]
    public void AddAtProtoClient_AddsNoResilienceHandler()
    {
        // Retrying below the SDK would resend createRecord and DPoP proofs; the application adds
        // its own resilience, knowingly, through the builder's HttpClient.
        var builder = CreateBuilder();
        builder.AddAtProtoClient();

        using var host = builder.Build();
        var handler = host.Services.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(AtProtoServiceCollectionExtensions.HttpClientName);

        var chain = new List<Type>();
        for (HttpMessageHandler? current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
            chain.Add(current.GetType());

        Assert.DoesNotContain(chain, type => type.FullName!.Contains("Resilience", StringComparison.Ordinal));
    }

    [Fact]
    public void AddAtProtoClient_ThrowsOnNullBuilder()
    {
        IHostApplicationBuilder builder = null!;
        Assert.Throws<ArgumentNullException>(() => builder.AddAtProtoClient());
    }
}
