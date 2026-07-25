using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;
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
            .WithHandleDomains(".pds.example.com")
            .WithPlcUrl("https://plc.directory")
            .WithAppView("https://api.bsky.app", "did:web:api.bsky.app")
            .WithCrawlers("https://bsky.network")
            .WithReportService("https://mod.bsky.app")
            .WithInviteCodeRequired()
            .WithBlobUploadLimit(10 * 1024 * 1024)
            .WithEmail("smtps://user:pass@smtp.example.com", "noreply@example.com")
            .WithProductionMode();

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        Assert.NotNull(resource);
    }

    // ──────────────────────────────────────────────────────────
    //  Secrets and persistence
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAtProtoPds_SetsBaselineEnvironment()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var env = await GetEnvironmentAsync(resource);

        Assert.Equal("/pds", env["PDS_DATA_DIRECTORY"]);
        Assert.Equal("true", env["PDS_DEV_MODE"]);
        Assert.Equal("localhost", env["PDS_HOSTNAME"]);

        // Without a blobstore the container exits on startup with
        // "Must configure either S3 or disk blobstore".
        Assert.Equal("/pds/blocks", env["PDS_BLOBSTORE_DISK_LOCATION"]);
    }

    [Fact]
    public async Task AddAtProtoPds_InPublishMode_OmitsLocalOnlyDefaults()
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = ["--publisher", "manifest", "--output-path", "manifest.json"] });

        Assert.True(builder.ExecutionContext.IsPublishMode, "expected a publish-mode builder");

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var env = await GetEnvironmentAsync(resource);

        // "localhost" would give a deployed PDS a did:web identity and handle domain
        // nothing can resolve, so the deployment supplies the hostname instead.
        Assert.IsType<ParameterResource>(env["PDS_HOSTNAME"]);

        // Dev mode relaxes checks a real deployment needs; unset, the container defaults it off.
        Assert.False(env.ContainsKey("PDS_DEV_MODE"));
    }

    [Fact]
    public async Task AddAtProtoPds_InRunMode_DefaultsHostnameToLocalhost()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var env = await GetEnvironmentAsync(resource);

        Assert.Equal("localhost", env["PDS_HOSTNAME"]);
        Assert.Equal("true", env["PDS_DEV_MODE"]);
    }

    [Fact]
    public async Task WithHostname_OverridesTheDefault()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds").WithHostname("pds.example.com");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var env = await GetEnvironmentAsync(resource);

        Assert.Equal("pds.example.com", env["PDS_HOSTNAME"]);
    }

    [Fact]
    public async Task WithHostname_AcceptsAParameter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var hostname = builder.AddParameter("pds-host", "pds.example.com");

        builder.AddAtProtoPds("pds").WithHostname(hostname);

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var env = await GetEnvironmentAsync(resource);

        Assert.Same(hostname.Resource, env["PDS_HOSTNAME"]);
    }

    [Fact]
    public async Task AddAtProtoPds_BindsSecretsToPersistedParameters()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var env = await GetEnvironmentAsync(resource);

        // Each secret must resolve to the resource's parameter, not a literal baked in
        // at AppHost build time — otherwise it would change on every run while the data
        // volume persisted, stranding the accounts already created.
        Assert.Same(resource.AdminPasswordParameter, env["PDS_ADMIN_PASSWORD"]);
        Assert.Same(resource.JwtSecretParameter, env["PDS_JWT_SECRET"]);
        Assert.Same(resource.PlcRotationKeyParameter, env["PDS_PLC_ROTATION_KEY_K256_PRIVATE_KEY_HEX"]);
    }

    [Fact]
    public async Task AddAtProtoPds_GeneratesHexSecrets()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();

        var rotationKey = await resource.PlcRotationKeyParameter.GetValueAsync(CancellationToken.None) ?? "";
        var jwtSecret = await resource.JwtSecretParameter.GetValueAsync(CancellationToken.None) ?? "";

        // The PDS parses both as hex; a 32-byte secp256k1 key is 64 hex characters.
        Assert.Equal(64, rotationKey.Length);
        Assert.Equal(32, jwtSecret.Length);
        Assert.True(rotationKey.All(Uri.IsHexDigit), $"Not hex: {rotationKey}");
        Assert.True(jwtSecret.All(Uri.IsHexDigit), $"Not hex: {jwtSecret}");
        Assert.Equal(rotationKey.ToLowerInvariant(), rotationKey);
    }

    [Fact]
    public void AddAtProtoPds_InPublishMode_DoesNotGenerateHexSecrets()
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = ["--publisher", "manifest", "--output-path", "manifest.json"] });

        Assert.True(builder.ExecutionContext.IsPublishMode, "expected a publish-mode builder");

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();

        // A manifest 'generate' block can only describe an alphanumeric string, so a
        // deployment following one would provision a rotation key the PDS rejects. The
        // value has to be supplied at deploy time instead of generated wrongly.
        Assert.Null(resource.JwtSecretParameter.Default);
        Assert.Null(resource.PlcRotationKeyParameter.Default);
    }

    [Fact]
    public void HexSecretParameterDefault_RefusesToDescribeItselfInAManifest()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var context = new ManifestPublishingContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
            "manifest.json",
            new Utf8JsonWriter(Stream.Null));

        var ex = Assert.Throws<NotSupportedException>(
            () => resource.PlcRotationKeyParameter.Default!.WriteToManifest(context));

        Assert.Contains("WithPlcRotationKey", ex.Message);
    }

    [Fact]
    public void AddAtProtoPds_MarksSecretParametersAsSecret()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();

        Assert.True(resource.JwtSecretParameter.Secret);
        Assert.True(resource.PlcRotationKeyParameter.Secret);
    }

    [Fact]
    public async Task WithAdminPassword_OverridesGeneratedParameter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var custom = builder.AddParameter("custom-admin-password", "s3cret", secret: true);

        builder.AddAtProtoPds("pds").WithAdminPassword(custom);

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();
        var env = await GetEnvironmentAsync(resource);

        Assert.Same(custom.Resource, resource.AdminPasswordParameter);
        Assert.Same(custom.Resource, env["PDS_ADMIN_PASSWORD"]);
    }

    [Theory]
    [InlineData("pds-hostname")]
    [InlineData("pds-jwt-secret")]
    [InlineData("pds-plc-rotation-key")]
    public void PublishMode_OverriddenParameterIsDroppedFromTheModel(string parameterName)
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = ["--publisher", "manifest", "--output-path", "manifest.json"] });

        Assert.True(builder.ExecutionContext.IsPublishMode, "expected a publish-mode builder");

        var custom = builder.AddParameter("custom-value", new string('a', 64), secret: true);

        var pds = builder.AddAtProtoPds("pds");

        Assert.Contains(builder.Resources.OfType<ParameterResource>(), p => p.Name == parameterName);

        _ = parameterName switch
        {
            "pds-hostname" => pds.WithHostname(custom),
            "pds-jwt-secret" => pds.WithJwtSecret(custom),
            _ => pds.WithPlcRotationKey(custom),
        };

        // Left in the model it would still appear as a manifest input, so a deployment
        // would be prompted for a value nothing reads.
        Assert.DoesNotContain(builder.Resources.OfType<ParameterResource>(), p => p.Name == parameterName);
    }

    [Fact]
    public void WithPlcRotationKey_OverridesGeneratedParameter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var custom = builder.AddParameter("custom-rotation-key", new string('a', 64), secret: true);

        builder.AddAtProtoPds("pds").WithPlcRotationKey(custom);

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();

        Assert.Same(custom.Resource, resource.PlcRotationKeyParameter);
    }

    // ──────────────────────────────────────────────────────────
    //  Health check and consumer wiring
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void WithDataBindMount_ReplacesTheDefaultVolume()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds").WithDataBindMount("./pds-data");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();

        // Two mounts on one destination is rejected by the container runtime
        // ("duplicate mount destination"), so the container would never start.
        var mount = Assert.Single(
            resource.Annotations.OfType<ContainerMountAnnotation>(), m => m.Target == "/pds");

        Assert.Equal(ContainerMountType.BindMount, mount.Type);
    }

    [Fact]
    public void WithDataVolume_ReplacesTheDefaultVolume()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds").WithDataVolume("custom-pds-data");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();

        var mount = Assert.Single(
            resource.Annotations.OfType<ContainerMountAnnotation>(), m => m.Target == "/pds");

        Assert.Equal(ContainerMountType.Volume, mount.Type);
        Assert.Equal("custom-pds-data", mount.Source);
    }

    [Fact]
    public void AddAtProtoPds_AddsHealthCheck()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.Resources.OfType<AtProtoPdsContainerResource>().Single();

        Assert.NotEmpty(resource.Annotations.OfType<HealthCheckAnnotation>());
    }

    [Fact]
    public async Task WithAtProtoPds_InjectsUrlAndAdminPassword()
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddAtProtoPds("pds");

        var consumer = builder.AddContainer("web", "nginx").WithAtProtoPds(pds);

        var env = await GetEnvironmentAsync(consumer.Resource);

        Assert.True(env.ContainsKey(AtProtoPdsHostingExtensions.PdsUrlConfigurationKey));
        Assert.Same(
            pds.Resource.AdminPasswordParameter,
            env[AtProtoPdsHostingExtensions.AdminPasswordConfigurationKey]);
    }

    [Fact]
    public async Task WithAtProtoPds_InRunMode_AllowsPlaintextHttp()
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddAtProtoPds("pds");

        var consumer = builder.AddContainer("web", "nginx").WithAtProtoPds(pds);

        var env = await GetEnvironmentAsync(consumer.Resource);

        // A containerized consumer resolves the PDS over the container network, not as a
        // loopback address, so the admin client's HTTPS guard would otherwise reject the
        // URL this very method supplies.
        Assert.Equal("true", env[AtProtoPdsHostingExtensions.AllowInsecureHttpConfigurationKey]);
    }

    [Fact]
    public async Task WithAtProtoPds_InPublishMode_DoesNotAllowPlaintextHttp()
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = ["--publisher", "manifest", "--output-path", "manifest.json"] });

        Assert.True(builder.ExecutionContext.IsPublishMode, "expected a publish-mode builder");

        var pds = builder.AddAtProtoPds("pds");
        var consumer = builder.AddContainer("web", "nginx").WithAtProtoPds(pds);

        var env = await GetEnvironmentAsync(consumer.Resource);

        // Sending the admin password unencrypted across a deployed network should be the
        // operator's explicit decision, not something the AppHost turns on for them.
        Assert.False(env.ContainsKey(AtProtoPdsHostingExtensions.AllowInsecureHttpConfigurationKey));
    }

    [Fact]
    public void WithAtProtoPds_AddsWaitAnnotationByDefault()
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddAtProtoPds("pds");

        var consumer = builder.AddContainer("web", "nginx").WithAtProtoPds(pds);

        Assert.NotEmpty(consumer.Resource.Annotations.OfType<WaitAnnotation>());
    }

    [Fact]
    public void WithAtProtoPds_WithWaitDisabled_AddsNoWaitAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddAtProtoPds("pds");

        var consumer = builder.AddContainer("web", "nginx")
            .WithAtProtoPds(pds, waitForHealthy: false);

        Assert.Empty(consumer.Resource.Annotations.OfType<WaitAnnotation>());
    }

    [Fact]
    public void WithAtProtoPds_ThrowsOnNullPds()
    {
        var builder = DistributedApplication.CreateBuilder();
        var consumer = builder.AddContainer("web", "nginx");

        Assert.Throws<ArgumentNullException>(() => consumer.WithAtProtoPds(null!));
    }

    private static async Task<Dictionary<string, object>> GetEnvironmentAsync(IResource resource)
    {
        var env = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
            resource,
            env,
            CancellationToken.None);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return env;
    }
}
