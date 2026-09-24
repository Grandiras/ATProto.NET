using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using ATProtoNet.Aspire.Hosting;
using static ATProtoNet.Tests.Aspire.AspireTestHost;

namespace ATProtoNet.Tests.Aspire;

/// <summary>
/// Behaviour both PDS resources share through <see cref="AtProtoPdsContainerResourceBase"/>,
/// run against each. What only one server does is tested in
/// <see cref="AtProtoPdsHostingExtensionsTests"/> and
/// <see cref="AtProtoTranquilPdsHostingExtensionsTests"/>.
/// </summary>
public class PdsContainerResourceTests
{
    // ──────────────────────────────────────────────────────────
    //  The container
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PdsFlavour.Reference, typeof(AtProtoPdsContainerResource), "ghcr.io/bluesky-social/pds")]
    [InlineData(PdsFlavour.Tranquil, typeof(AtProtoTranquilPdsContainerResource), "atcr.io/tranquil.farm/tranquil-pds")]
    public void Add_AddsContainerResource(PdsFlavour flavour, Type expectedType, string expectedImage)
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddPds(flavour);

        var resource = builder.SingleResource<AtProtoPdsContainerResourceBase>();
        Assert.IsType(expectedType, resource);
        Assert.Equal("pds", resource.Name);

        var image = Assert.Single(resource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal(expectedImage, image.Image);
        Assert.Equal("latest", image.Tag);
    }

    [Theory]
    [InlineData(PdsFlavour.Reference)]
    [InlineData(PdsFlavour.Tranquil)]
    public void Add_WithCustomTag_SetsTag(PdsFlavour flavour)
    {
        var builder = DistributedApplication.CreateBuilder();

        var pds = builder.AddPds(flavour, tag: "0.4");

        var image = Assert.Single(pds.Resource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("0.4", image.Tag);
    }

    [Theory]
    [InlineData(PdsFlavour.Reference, null)]
    [InlineData(PdsFlavour.Reference, 2583)]
    [InlineData(PdsFlavour.Tranquil, null)]
    [InlineData(PdsFlavour.Tranquil, 2583)]
    public void Add_MapsTheHostPortToContainerPort3000(PdsFlavour flavour, int? port)
    {
        var builder = DistributedApplication.CreateBuilder();

        var pds = builder.AddPds(flavour, port: port);

        var endpoint = Assert.Single(
            pds.Resource.Annotations.OfType<EndpointAnnotation>(),
            e => e.Name == AtProtoPdsContainerResourceBase.HttpEndpointName);

        // Without an explicit port the host side is left for Aspire to assign.
        Assert.Equal(port, endpoint.Port);
        Assert.Equal(3000, endpoint.TargetPort);
        Assert.NotEmpty(pds.Resource.Annotations.OfType<HealthCheckAnnotation>());
    }

    [Theory]
    [InlineData(PdsFlavour.Reference)]
    [InlineData(PdsFlavour.Tranquil)]
    public void ConnectionStringExpression_IsTheHttpEndpointUrl(PdsFlavour flavour)
    {
        var builder = DistributedApplication.CreateBuilder();

        var pds = builder.AddPds(flavour);

        // What a project referencing the PDS receives as ConnectionStrings__{name}.
        Assert.Equal(
            "http://{pds.bindings.http.host}:{pds.bindings.http.port}",
            pds.Resource.ConnectionStringExpression.ValueExpression);
    }

    [Theory]
    [InlineData(PdsFlavour.Reference)]
    [InlineData(PdsFlavour.Tranquil)]
    public void Add_ThrowsOnNullBuilderOrEmptyName(PdsFlavour flavour)
    {
        IDistributedApplicationBuilder nullBuilder = null!;

        Assert.Throws<ArgumentNullException>(() => nullBuilder.AddPds(flavour));
        Assert.Throws<ArgumentException>(() => DistributedApplication.CreateBuilder().AddPds(flavour, name: ""));
    }

    // ──────────────────────────────────────────────────────────
    //  Hostname
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PdsFlavour.Reference)]
    [InlineData(PdsFlavour.Tranquil)]
    public async Task Add_InRunMode_DefaultsHostnameToLocalhost(PdsFlavour flavour)
    {
        var builder = DistributedApplication.CreateBuilder();

        var pds = builder.AddPds(flavour);

        var env = await GetEnvironmentAsync(pds.Resource);
        Assert.Equal("localhost", env["PDS_HOSTNAME"]);
    }

    [Theory]
    [InlineData(PdsFlavour.Reference)]
    [InlineData(PdsFlavour.Tranquil)]
    public async Task Add_InPublishMode_AsksForTheHostname(PdsFlavour flavour)
    {
        var builder = PublishModeBuilder();

        var pds = builder.AddPds(flavour);

        var env = await GetEnvironmentAsync(pds.Resource);

        // "localhost" would give a deployed PDS a handle domain (and, on the reference
        // PDS, a did:web identity) nothing can resolve, so the deployment supplies one.
        var hostname = Assert.IsType<ParameterResource>(env["PDS_HOSTNAME"]);
        Assert.Equal("pds-hostname", hostname.Name);
    }

    [Theory]
    [InlineData(PdsFlavour.Reference)]
    [InlineData(PdsFlavour.Tranquil)]
    public async Task WithHostname_OverridesTheDefault(PdsFlavour flavour)
    {
        var builder = DistributedApplication.CreateBuilder();

        var pds = builder.AddPds(flavour).WithHostname("pds.example.com");

        var env = await GetEnvironmentAsync(pds.Resource);
        Assert.Equal("pds.example.com", env["PDS_HOSTNAME"]);
    }

    [Theory]
    [InlineData(PdsFlavour.Reference)]
    [InlineData(PdsFlavour.Tranquil)]
    public async Task WithHostname_AcceptsAParameter(PdsFlavour flavour)
    {
        var builder = DistributedApplication.CreateBuilder();
        var hostname = builder.AddParameter("pds-host", "pds.example.com");

        var pds = builder.AddPds(flavour).WithHostname(hostname);

        var env = await GetEnvironmentAsync(pds.Resource);
        Assert.Same(hostname.Resource, env["PDS_HOSTNAME"]);
    }

    [Theory]
    [InlineData(PdsFlavour.Reference)]
    [InlineData(PdsFlavour.Tranquil)]
    public void WithHostname_ThrowsOnMissingHostname(PdsFlavour flavour)
    {
        var pds = DistributedApplication.CreateBuilder().AddPds(flavour);

        Assert.Throws<ArgumentException>(() => pds.WithHostname(""));
        Assert.Throws<ArgumentNullException>(() => pds.WithHostname((IResourceBuilder<ParameterResource>)null!));
    }

    // ──────────────────────────────────────────────────────────
    //  Overriding generated parameters
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PdsFlavour.Reference, "PDS_JWT_SECRET")]
    [InlineData(PdsFlavour.Tranquil, "JWT_SECRET")]
    public async Task WithJwtSecret_OverridesGeneratedParameter(PdsFlavour flavour, string variable)
    {
        var builder = DistributedApplication.CreateBuilder();
        var custom = builder.AddParameter("custom-jwt", new string('a', 48), secret: true);

        var pds = builder.AddPds(flavour).WithJwtSecret(custom);

        var env = await GetEnvironmentAsync(pds.Resource);
        Assert.Same(custom.Resource, pds.Resource.JwtSecretParameter);
        Assert.Same(custom.Resource, env[variable]);
    }

    [Theory]
    [InlineData(PdsFlavour.Reference, "pds-hostname")]
    [InlineData(PdsFlavour.Reference, "pds-jwt-secret")]
    [InlineData(PdsFlavour.Tranquil, "pds-hostname")]
    [InlineData(PdsFlavour.Tranquil, "pds-jwt-secret")]
    public void PublishMode_OverriddenParameterIsDroppedFromTheModel(PdsFlavour flavour, string parameterName)
    {
        var builder = PublishModeBuilder();
        var custom = builder.AddParameter("custom-value", new string('a', 64), secret: true);

        var pds = builder.AddPds(flavour);

        Assert.Contains(builder.Resources.OfType<ParameterResource>(), p => p.Name == parameterName);

        _ = parameterName == "pds-hostname" ? pds.WithHostname(custom) : pds.WithJwtSecret(custom);

        // Left in the model it would still appear as a manifest input, so a deployment
        // would be prompted for a value nothing reads.
        Assert.DoesNotContain(builder.Resources.OfType<ParameterResource>(), p => p.Name == parameterName);
    }

    // ──────────────────────────────────────────────────────────
    //  Storage
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PdsFlavour.Reference, "/pds", "pds-data")]
    [InlineData(PdsFlavour.Tranquil, "/var/lib/tranquil-pds/blobs", "pds-blobs")]
    public void Add_MountsANamedVolumeAtTheStorageDirectory(PdsFlavour flavour, string target, string volume)
    {
        var builder = DistributedApplication.CreateBuilder();

        var pds = builder.AddPds(flavour);

        var mount = Assert.Single(
            pds.Resource.Annotations.OfType<ContainerMountAnnotation>(), m => m.Target == target);

        Assert.Equal(ContainerMountType.Volume, mount.Type);
        Assert.Equal(volume, mount.Source);
    }

    [Theory]
    [InlineData(PdsFlavour.Reference, "/pds")]
    [InlineData(PdsFlavour.Tranquil, "/var/lib/tranquil-pds/blobs")]
    public void StorageBindMount_ReplacesTheDefaultVolume(PdsFlavour flavour, string target)
    {
        var builder = DistributedApplication.CreateBuilder();

        var pds = builder.AddPds(flavour);
        pds.ReplaceStorage(bindMount: true, "./pds-storage");

        // Two mounts on one destination is rejected by the container runtime
        // ("duplicate mount destination"), so the container would never start.
        var mount = Assert.Single(
            pds.Resource.Annotations.OfType<ContainerMountAnnotation>(), m => m.Target == target);

        Assert.Equal(ContainerMountType.BindMount, mount.Type);
        Assert.EndsWith("pds-storage", mount.Source);
    }

    [Theory]
    [InlineData(PdsFlavour.Reference, "/pds", "custom-storage", "custom-storage")]
    [InlineData(PdsFlavour.Reference, "/pds", null, "pds-data")]
    [InlineData(PdsFlavour.Tranquil, "/var/lib/tranquil-pds/blobs", "custom-storage", "custom-storage")]
    [InlineData(PdsFlavour.Tranquil, "/var/lib/tranquil-pds/blobs", null, "pds-blobs")]
    public void StorageVolume_ReplacesTheCurrentMount(PdsFlavour flavour, string target, string? name, string expectedVolume)
    {
        var builder = DistributedApplication.CreateBuilder();

        var pds = builder.AddPds(flavour);
        pds.ReplaceStorage(bindMount: true, "./pds-storage");
        pds.ReplaceStorage(bindMount: false, name);

        var mount = Assert.Single(
            pds.Resource.Annotations.OfType<ContainerMountAnnotation>(), m => m.Target == target);

        Assert.Equal(ContainerMountType.Volume, mount.Type);
        Assert.Equal(expectedVolume, mount.Source);
    }

    // ──────────────────────────────────────────────────────────
    //  Consumer wiring
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PdsFlavour.Reference)]
    [InlineData(PdsFlavour.Tranquil)]
    public async Task WithPds_InjectsUrlAndAdminPassword(PdsFlavour flavour)
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddPds(flavour);

        var consumer = builder.AddContainer("web", "nginx").WithPds(pds);

        var env = await GetEnvironmentAsync(consumer.Resource);

        var url = Assert.IsType<EndpointReference>(env[AtProtoPdsHostingExtensions.PdsUrlConfigurationKey]);
        Assert.Same(pds.Resource, url.Resource);
        Assert.Equal(AtProtoPdsContainerResourceBase.HttpEndpointName, url.EndpointName);

        // The server's admin password, or the administrator account's on Tranquil.
        var adminPassword = pds.Resource switch
        {
            AtProtoPdsContainerResource reference => reference.AdminPasswordParameter,
            AtProtoTranquilPdsContainerResource tranquil => tranquil.AdminAccountPasswordParameter,
            _ => throw new ArgumentOutOfRangeException(nameof(flavour)),
        };
        Assert.Same(adminPassword, env[AtProtoPdsHostingExtensions.AdminPasswordConfigurationKey]);

        // The PDS is also referenced as a connection string under its resource name.
        Assert.True(env.ContainsKey("ConnectionStrings__pds"));
    }

    [Theory]
    [InlineData(PdsFlavour.Reference)]
    [InlineData(PdsFlavour.Tranquil)]
    public async Task WithPds_InRunMode_AllowsPlaintextHttp(PdsFlavour flavour)
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddPds(flavour);

        var consumer = builder.AddContainer("web", "nginx").WithPds(pds);

        var env = await GetEnvironmentAsync(consumer.Resource);

        // A containerized consumer resolves the PDS over the container network, not as a
        // loopback address, so the admin client's HTTPS guard would otherwise reject the
        // URL this very method supplies.
        Assert.Equal("true", env[AtProtoPdsHostingExtensions.AllowInsecureHttpConfigurationKey]);
    }

    [Theory]
    [InlineData(PdsFlavour.Reference)]
    [InlineData(PdsFlavour.Tranquil)]
    public async Task WithPds_InPublishMode_DoesNotAllowPlaintextHttp(PdsFlavour flavour)
    {
        var builder = PublishModeBuilder();
        var pds = builder.AddPds(flavour);

        var consumer = builder.AddContainer("web", "nginx").WithPds(pds);

        var env = await GetEnvironmentAsync(consumer.Resource);

        // Sending administrator credentials unencrypted across a deployed network should
        // be the operator's explicit decision, not something the AppHost turns on for them.
        Assert.False(env.ContainsKey(AtProtoPdsHostingExtensions.AllowInsecureHttpConfigurationKey));
    }

    [Theory]
    [InlineData(PdsFlavour.Reference, true)]
    [InlineData(PdsFlavour.Reference, false)]
    [InlineData(PdsFlavour.Tranquil, true)]
    [InlineData(PdsFlavour.Tranquil, false)]
    public void WithPds_WaitsForThePdsUnlessDisabled(PdsFlavour flavour, bool waitForHealthy)
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddPds(flavour);

        var consumer = builder.AddContainer("web", "nginx").WithPds(pds, waitForHealthy);

        IResource[] expected = waitForHealthy ? [pds.Resource] : [];
        Assert.Equal(expected, consumer.Resource.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource));
    }

    [Fact]
    public void WithPds_ThrowsOnNullPds()
    {
        var consumer = DistributedApplication.CreateBuilder().AddContainer("web", "nginx");

        Assert.Throws<ArgumentNullException>(() => consumer.WithAtProtoPds(null!));
        Assert.Throws<ArgumentNullException>(() => consumer.WithAtProtoTranquilPds(null!));
    }
}
