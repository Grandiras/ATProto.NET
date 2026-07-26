using ATProtoNet.Admin;
using ATProtoNet.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ATProtoNet.Tests.Server;

public class PdsAdminExtensionsTests
{
    [Fact]
    public void AddAtProtoPdsAdmin_BindsConfigurationSection()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AtProto:Pds:Url"] = "https://pds.example.com",
            ["AtProto:Pds:AdminPassword"] = "hunter2",
        });

        builder.AddAtProtoPdsAdmin();

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<PdsAdminClient>();

        Assert.Equal("https://pds.example.com/", client.PdsUrl.ToString());
    }

    [Fact]
    public void AddAtProtoPdsAdmin_RegistersAsTypedHttpClient()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AtProto:Pds:Url"] = "https://pds.example.com",
            ["AtProto:Pds:AdminPassword"] = "hunter2",
        });

        builder.AddAtProtoPdsAdmin();

        using var host = builder.Build();

        // A typed client, so the factory rotates the underlying handler rather than one
        // captured HttpClient living for the whole process.
        var descriptor = Assert.Single(
            builder.Services, s => s.ServiceType == typeof(PdsAdminClient));

        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
        Assert.NotNull(host.Services.GetRequiredService<PdsAdminClient>());
    }

    [Fact]
    public void AddAtProtoPdsAdmin_BindsAllowInsecureHttp()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AtProto:Pds:Url"] = "http://pds:3000",
            ["AtProto:Pds:AdminPassword"] = "hunter2",
            ["AtProto:Pds:AllowInsecureHttp"] = "true",
        });

        builder.AddAtProtoPdsAdmin();

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<PdsAdminClient>();

        Assert.Equal("http://pds:3000/", client.PdsUrl.ToString());
    }

    [Fact]
    public void AddAtProtoPdsAdmin_WithoutAllowInsecureHttp_RejectsPlaintextPds()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AtProto:Pds:Url"] = "http://pds:3000",
            ["AtProto:Pds:AdminPassword"] = "hunter2",
        });

        builder.AddAtProtoPdsAdmin();

        using var host = builder.Build();

        Assert.Throws<ArgumentException>(() => host.Services.GetRequiredService<PdsAdminClient>());
    }

    [Fact]
    public void AddAtProtoPdsAdmin_ConfigureOverridesConfiguration()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AtProto:Pds:Url"] = "https://configured.example.com",
            ["AtProto:Pds:AdminPassword"] = "hunter2",
        });

        builder.AddAtProtoPdsAdmin(configureOptions: o => o.Url = "https://override.example.com");

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<PdsAdminClient>();

        Assert.Equal("https://override.example.com/", client.PdsUrl.ToString());
    }

    [Fact]
    public void AddAtProtoPdsAdmin_WithoutUrl_ThrowsWithActionableMessage()
    {
        var builder = Host.CreateApplicationBuilder();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddAtProtoPdsAdmin());

        Assert.Contains("AtProto:Pds:Url", ex.Message);
    }

    [Fact]
    public void AddAtProtoPdsAdmin_WithoutAdminPassword_ThrowsWithActionableMessage()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AtProto:Pds:Url"] = "https://pds.example.com",
        });

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddAtProtoPdsAdmin());

        Assert.Contains("AtProto:Pds:AdminPassword", ex.Message);
    }

    [Fact]
    public void AddAtProtoPdsAdmin_WithExplicitCredentials_RegistersClient()
    {
        var services = new ServiceCollection();

        services.AddAtProtoPdsAdmin("https://pds.example.com", "hunter2");

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<PdsAdminClient>();

        Assert.Equal("https://pds.example.com/", client.PdsUrl.ToString());
    }

    [Fact]
    public void AddAtProtoPdsAdmin_BindsAccountAuthentication()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AtProto:Pds:Url"] = "https://pds.example.com",
            ["AtProto:Pds:Authentication"] = "AdminAccount",
            ["AtProto:Pds:AdminIdentifier"] = "pdsadmin.pds.example.com",
            ["AtProto:Pds:AdminPassword"] = "hunter2",
        });

        // The shape WithAtProtoTranquilPds() produces: a PDS with no server-wide admin
        // password, administered through an account's session.
        builder.AddAtProtoPdsAdmin();

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<PdsAdminClient>();

        Assert.Equal(PdsAdminAuthentication.AdminAccount, client.Authentication);
    }

    [Fact]
    public void AddAtProtoPdsAdmin_DefaultsToAdminPasswordAuthentication()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AtProto:Pds:Url"] = "https://pds.example.com",
            ["AtProto:Pds:AdminPassword"] = "hunter2",
        });

        builder.AddAtProtoPdsAdmin();

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<PdsAdminClient>();

        Assert.Equal(PdsAdminAuthentication.AdminPassword, client.Authentication);
    }

    [Fact]
    public void AddAtProtoPdsAdmin_AccountAuthenticationWithoutIdentifier_ThrowsWhileTheHostIsBuilt()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AtProto:Pds:Url"] = "https://pds.example.com",
            ["AtProto:Pds:Authentication"] = "AdminAccount",
            ["AtProto:Pds:AdminPassword"] = "hunter2",
        });

        // Naming an account but not saying which one would otherwise surface as an
        // authentication failure on the first admin call, long after startup.
        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddAtProtoPdsAdmin());

        Assert.Contains("AtProto:Pds:AdminIdentifier", ex.Message);
    }
}
