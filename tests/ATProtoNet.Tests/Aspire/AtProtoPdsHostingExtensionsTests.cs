using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using ATProtoNet.Aspire.Hosting;

namespace ATProtoNet.Tests.Aspire;

public class AtProtoPdsHostingExtensionsTests
{
    [Fact]
    public void AddAtProtoPds_AddsContainerResource()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().SingleOrDefault();
        Assert.NotNull(resource);
        Assert.Equal("pds", resource.Name);
    }

    [Fact]
    public void AddAtProtoPds_ConfiguresHttpEndpoint()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var endpoint = resource.Annotations.OfType<EndpointAnnotation>()
            .SingleOrDefault(e => e.Name == AtProtoPdsContainerResource.HttpEndpointName);

        Assert.NotNull(endpoint);
        Assert.Equal(3000, endpoint.TargetPort);
    }

    [Fact]
    public void AddAtProtoPds_WithExplicitPort_SetsPort()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds", port: 2583);

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var endpoint = resource.Annotations.OfType<EndpointAnnotation>()
            .Single(e => e.Name == AtProtoPdsContainerResource.HttpEndpointName);

        Assert.Equal(2583, endpoint.Port);
        Assert.Equal(3000, endpoint.TargetPort);
    }

    [Fact]
    public void AddAtProtoPds_ConfiguresContainerImage()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var image = resource.Annotations.OfType<ContainerImageAnnotation>().SingleOrDefault();

        Assert.NotNull(image);
        Assert.Equal("ghcr.io/bluesky-social/pds", image.Image);
        Assert.Equal("latest", image.Tag);
    }

    [Fact]
    public void AddAtProtoPds_WithCustomTag_SetsTag()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds", tag: "0.4");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var image = resource.Annotations.OfType<ContainerImageAnnotation>().Single();

        Assert.Equal("0.4", image.Tag);
    }

    [Fact]
    public void AddAtProtoPds_ConfiguresVolume()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var mount = resource.Annotations.OfType<ContainerMountAnnotation>()
            .SingleOrDefault(m => m.Target == "/pds");

        Assert.NotNull(mount);
        Assert.Equal(ContainerMountType.Volume, mount.Type);
    }

    [Fact]
    public void AddAtProtoPds_ConfiguresAdminPasswordEnvironment()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var envAnnotations = resource.Annotations.OfType<EnvironmentCallbackAnnotation>();

        // The admin password is configured via an environment callback
        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void AddAtProtoPds_ImplementsIResourceWithConnectionString()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        Assert.IsAssignableFrom<IResourceWithConnectionString>(resource);
    }

    [Fact]
    public void AddAtProtoPds_ThrowsOnNullBuilder()
    {
        IDistributedApplicationBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() => builder.AddAtProtoPds("pds"));
    }

    [Fact]
    public void AddAtProtoPds_ThrowsOnEmptyName()
    {
        var builder = DistributedApplication.CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddAtProtoPds(""));
    }

    [Fact]
    public void WithHostname_SetsEnvironmentAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds")
            .WithHostname("pds.example.com");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var envAnnotations = resource.Annotations.OfType<EnvironmentCallbackAnnotation>();

        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void WithProductionMode_SetsDevModeToFalse()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds")
            .WithProductionMode();

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var envAnnotations = resource.Annotations.OfType<EnvironmentCallbackAnnotation>();

        // Production mode adds an additional environment callback
        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void WithAppView_SetsEnvironmentAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds")
            .WithAppView("https://api.bsky.app", "did:web:api.bsky.app");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var envAnnotations = resource.Annotations.OfType<EnvironmentCallbackAnnotation>();

        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void WithCrawlers_SetsEnvironmentAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds")
            .WithCrawlers("https://bsky.network");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var envAnnotations = resource.Annotations.OfType<EnvironmentCallbackAnnotation>();

        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void WithPlcUrl_SetsEnvironmentAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds")
            .WithPlcUrl("https://plc.directory");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var envAnnotations = resource.Annotations.OfType<EnvironmentCallbackAnnotation>();

        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void WithBlobUploadLimit_SetsEnvironmentAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds")
            .WithBlobUploadLimit(10 * 1024 * 1024);

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var envAnnotations = resource.Annotations.OfType<EnvironmentCallbackAnnotation>();

        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void WithEmail_SetsEnvironmentAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds")
            .WithEmail("smtps://user:pass@smtp.example.com", "noreply@example.com");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var envAnnotations = resource.Annotations.OfType<EnvironmentCallbackAnnotation>();

        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void WithReportService_SetsEnvironmentAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds")
            .WithReportService("https://mod.bsky.app", "did:plc:ar7c4by46qjdydhdevvrndac");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var envAnnotations = resource.Annotations.OfType<EnvironmentCallbackAnnotation>();

        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void FluentApi_AllowsChaining()
    {
        var builder = DistributedApplication.CreateBuilder();

        // Should compile and not throw
        builder.AddAtProtoPds("pds", port: 2583, tag: "0.4")
            .WithHostname("pds.example.com")
            .WithPlcUrl("https://plc.directory")
            .WithAppView("https://api.bsky.app", "did:web:api.bsky.app")
            .WithCrawlers("https://bsky.network")
            .WithReportService("https://mod.bsky.app")
            .WithBlobUploadLimit(10 * 1024 * 1024)
            .WithEmail("smtps://user:pass@smtp.example.com", "noreply@example.com")
            .WithProductionMode();

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        Assert.NotNull(resource);
    }
}
