using ATProtoNet.Aspire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

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
        var client = host.Services.GetService<AtProtoClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddAtProtoClient_RegistersSameInstanceAsSingleton()
    {
        var builder = CreateBuilder();
        builder.AddAtProtoClient();

        using var host = builder.Build();
        var client1 = host.Services.GetRequiredService<AtProtoClient>();
        var client2 = host.Services.GetRequiredService<AtProtoClient>();

        Assert.Same(client1, client2);
    }

    [Fact]
    public void AddAtProtoClient_BindsConfigurationSection()
    {
        var config = new Dictionary<string, string?>
        {
            ["AtProto:InstanceUrl"] = "https://my-pds.example.com",
            ["AtProto:RelayUrl"] = "wss://my-relay.example.com",
            ["AtProto:AutoRefreshSession"] = "false",
        };

        var builder = CreateBuilder(config);
        builder.AddAtProtoClient();

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<AtProtoClient>();

        Assert.Equal("https://my-pds.example.com", client.PdsUrl);
    }

    [Fact]
    public void AddAtProtoClient_UsesCustomConfigurationSection()
    {
        var config = new Dictionary<string, string?>
        {
            ["MyAtProto:InstanceUrl"] = "https://custom-pds.example.com",
        };

        var builder = CreateBuilder(config);
        builder.AddAtProtoClient(configurationSectionName: "MyAtProto");

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<AtProtoClient>();

        Assert.Equal("https://custom-pds.example.com", client.PdsUrl);
    }

    [Fact]
    public void AddAtProtoClient_ConfigureSettingsCallbackOverridesConfig()
    {
        var config = new Dictionary<string, string?>
        {
            ["AtProto:InstanceUrl"] = "https://from-config.example.com",
        };

        var builder = CreateBuilder(config);
        builder.AddAtProtoClient(configureSettings: settings =>
        {
            settings.InstanceUrl = "https://from-callback.example.com";
        });

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<AtProtoClient>();

        Assert.Equal("https://from-callback.example.com", client.PdsUrl);
    }

    [Fact]
    public void AddAtProtoClient_RegistersHealthCheckByDefault()
    {
        var builder = CreateBuilder();
        builder.AddAtProtoClient();

        using var host = builder.Build();
        var healthCheckService = host.Services.GetService<HealthCheckService>();

        Assert.NotNull(healthCheckService);
    }

    [Fact]
    public void AddAtProtoClient_DisableHealthChecks_DoesNotRegisterHealthCheck()
    {
        var config = new Dictionary<string, string?>
        {
            ["AtProto:DisableHealthChecks"] = "true",
        };

        var builder = CreateBuilder(config);
        builder.AddAtProtoClient();

        using var host = builder.Build();

        // The HealthCheckService is still registered (by the framework),
        // but we can verify no "atproto-pds" registration exists
        // by checking the options
        var options = host.Services.GetService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        Assert.NotNull(options);

        var registrations = options.Value.Registrations;
        Assert.DoesNotContain(registrations, r => r.Name == "atproto-pds");
    }

    [Fact]
    public void AddAtProtoClient_DefaultSettings_UsesDefaultInstanceUrl()
    {
        var builder = CreateBuilder();
        builder.AddAtProtoClient();

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<AtProtoClient>();

        Assert.Equal("https://bsky.social", client.PdsUrl);
    }

    [Fact]
    public void AddAtProtoClient_RegistersHttpClientFactory()
    {
        var builder = CreateBuilder();
        builder.AddAtProtoClient();

        using var host = builder.Build();
        var factory = host.Services.GetService<IHttpClientFactory>();

        Assert.NotNull(factory);
    }

    [Fact]
    public void AddAtProtoClient_ThrowsOnNullBuilder()
    {
        IHostApplicationBuilder builder = null!;
        Assert.Throws<ArgumentNullException>(() => builder.AddAtProtoClient());
    }
}
