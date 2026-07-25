using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace ATProtoNet.Aspire.Hosting;

/// <summary>
/// Extension methods for adding an AT Protocol PDS container to a .NET Aspire application.
/// </summary>
public static class AtProtoPdsHostingExtensions
{
    private const string DefaultTag = "latest";
    private const int PdsContainerPort = 3000;
    private const string DataTarget = "/pds";

    /// <summary>
    /// The environment variable a referencing project receives the PDS URL under.
    /// Bound by <c>AddAtProtoPdsAdmin()</c> in the <c>ATProtoNet.Server</c> package.
    /// </summary>
    public const string PdsUrlConfigurationKey = "AtProto__Pds__Url";

    /// <summary>
    /// The environment variable a referencing project receives the PDS admin password under.
    /// Bound by <c>AddAtProtoPdsAdmin()</c> in the <c>ATProtoNet.Server</c> package.
    /// </summary>
    public const string AdminPasswordConfigurationKey = "AtProto__Pds__AdminPassword";

    /// <summary>
    /// The environment variable that permits the admin client to talk to the PDS over
    /// plaintext HTTP. Set in run mode only — see <see cref="WithAtProtoPds{T}"/>.
    /// </summary>
    public const string AllowInsecureHttpConfigurationKey = "AtProto__Pds__AllowInsecureHttp";

    /// <summary>
    /// Adds the official Bluesky PDS container (<c>ghcr.io/bluesky-social/pds</c>) to the application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The container starts in dev mode with a persistent data volume and generated
    /// secrets. The admin password, JWT secret, and PLC rotation key are Aspire
    /// parameters persisted to the AppHost's user secrets, so accounts created on one
    /// run remain usable on the next. Override any of them with
    /// <see cref="WithAdminPassword"/>, <see cref="WithJwtSecret"/>, or
    /// <see cref="WithPlcRotationKey"/> — for anything beyond local development you
    /// should supply your own.
    /// </para>
    /// <para>
    /// Wire a project up to the result with <see cref="WithAtProtoPds{T}"/> to get both
    /// the URL and the admin password injected.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var builder = DistributedApplication.CreateBuilder(args);
    ///
    /// var pds = builder.AddAtProtoPds("pds");
    ///
    /// builder.AddProject&lt;Projects.Web&gt;("web")
    ///        .WithAtProtoPds(pds);
    ///
    /// builder.Build().Run();
    /// </code>
    /// </example>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name for the PDS container.</param>
    /// <param name="port">Optional host port mapping. If not specified, a random port is assigned.</param>
    /// <param name="tag">Optional container image tag. Defaults to <c>"latest"</c>.</param>
    /// <returns>A resource builder for further configuration.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> AddAtProtoPds(
        this IDistributedApplicationBuilder builder,
        string name,
        int? port = null,
        string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var adminPassword = ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(
            builder, $"{name}-admin-password", special: false);

        // The PDS parses these two as hex, which Aspire's generator cannot express: a
        // manifest `generate` block only describes alphanumeric output, so a publish-mode
        // deployment would provision a rotation key the server rejects. Generate them only
        // when running locally, and require a supplied value when publishing.
        var jwtSecret = CreateHexSecretParameter(builder, $"{name}-jwt-secret", byteCount: 16);
        var plcRotationKey = CreateHexSecretParameter(builder, $"{name}-plc-rotation-key", byteCount: 32);

        var resource = new AtProtoPdsContainerResource(name, adminPassword, jwtSecret, plcRotationKey);

        // The hostname fixes the server's did:web identity and its handle domain, so
        // "localhost" is only ever right for a local run. A deployment supplies its own.
        if (builder.ExecutionContext.IsPublishMode)
        {
            resource.Hostname = builder.AddParameter($"{name}-hostname").Resource;
        }

        var pds = builder.AddResource(resource)
            .WithImage("ghcr.io/bluesky-social/pds", tag ?? DefaultTag)
            .WithHttpEndpoint(port: port, targetPort: PdsContainerPort, name: AtProtoPdsContainerResource.HttpEndpointName)
            .WithHttpHealthCheck(AtProtoPdsContainerResource.HealthCheckPath)
            .WithEnvironment("PDS_DATA_DIRECTORY", DataTarget)
            // Required: the PDS refuses to start without a blobstore ("Must configure
            // either S3 or disk blobstore"). Under the data volume so blobs persist with
            // the rest of the server's state.
            .WithEnvironment("PDS_BLOBSTORE_DISK_LOCATION", $"{DataTarget}/blocks")
            // Resolved through the resource so the With* overrides below take effect.
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables["PDS_HOSTNAME"] = resource.Hostname;
                context.EnvironmentVariables["PDS_ADMIN_PASSWORD"] = resource.AdminPasswordParameter;
                context.EnvironmentVariables["PDS_JWT_SECRET"] = resource.JwtSecretParameter;
                context.EnvironmentVariables["PDS_PLC_ROTATION_KEY_K256_PRIVATE_KEY_HEX"] = resource.PlcRotationKeyParameter;
            })
            .WithVolume($"{name}-data", DataTarget);

        // Dev mode relaxes the checks a real deployment needs, so it is a local-run
        // convenience only. Left unset when publishing, the container defaults it off.
        if (builder.ExecutionContext.IsRunMode)
        {
            pds = pds.WithEnvironment("PDS_DEV_MODE", "true");
        }

        return pds;
    }

    /// <summary>
    /// Replaces one of the auto-created parameters with a caller-supplied value, dropping
    /// the original from the application model.
    /// </summary>
    /// <remarks>
    /// The parameters are created up front, before any <c>With*</c> override can run. Left
    /// in the model, a superseded one still appears in a published manifest as an input,
    /// so a deployment would be prompted for a value nothing reads.
    /// </remarks>
    private static void Replace(
        IResourceBuilder<AtProtoPdsContainerResource> builder,
        object current,
        Action<AtProtoPdsContainerResource> assign)
    {
        if (current is ParameterResource superseded)
        {
            builder.ApplicationBuilder.Resources.Remove(superseded);
        }

        assign(builder.Resource);
    }

    /// <summary>
    /// Creates the parameter backing one of the PDS's hex-encoded secrets.
    /// </summary>
    /// <remarks>
    /// In run mode the value is generated as lowercase hex and persisted to the AppHost's
    /// user secrets, so it stays stable across runs alongside the data volume. In publish
    /// mode no default is attached: Aspire's manifest can only ask a deployment to generate
    /// an alphanumeric string, and the PDS would reject one, so the value must be supplied
    /// at deploy time (or through <see cref="WithJwtSecret"/> /
    /// <see cref="WithPlcRotationKey"/>) instead of being generated wrongly.
    /// </remarks>
    private static ParameterResource CreateHexSecretParameter(
        IDistributedApplicationBuilder builder,
        string name,
        int byteCount)
    {
        if (builder.ExecutionContext.IsPublishMode)
        {
            return builder.AddParameter(name, secret: true).Resource;
        }

        return builder
            .AddParameter(name, new HexSecretParameterDefault(byteCount), secret: true, persist: true)
            .Resource;
    }

    /// <summary>
    /// Injects this PDS's URL and admin password into a project (or any resource with
    /// environment variables), and makes it wait for the server to report healthy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project receives <c>AtProto__Pds__Url</c> and <c>AtProto__Pds__AdminPassword</c>,
    /// which <c>AddAtProtoPdsAdmin()</c> in the <c>ATProtoNet.Server</c> package binds into a
    /// <c>PdsAdminClient</c>. The PDS is also referenced as a connection string under its
    /// resource name.
    /// </para>
    /// <para>
    /// The PDS container serves plaintext HTTP, and the URL a consumer resolves is not
    /// always a loopback address — a containerized consumer reaches it over the container
    /// network instead. In <em>run</em> mode this method therefore also sets
    /// <c>AtProto__Pds__AllowInsecureHttp</c>, since both resources are on one local
    /// network. It deliberately does not do so when publishing: sending the admin password
    /// unencrypted across a deployed network should be an explicit decision, so a published
    /// app either fronts the PDS with TLS or opts in by setting that key itself.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The resource type being wired up.</typeparam>
    /// <param name="builder">The resource to inject the PDS configuration into.</param>
    /// <param name="pds">The PDS container resource.</param>
    /// <param name="waitForHealthy">
    /// Whether the resource should wait for the PDS to become healthy before starting.
    /// Default: <c>true</c>.
    /// </param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<T> WithAtProtoPds<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<AtProtoPdsContainerResource> pds,
        bool waitForHealthy = true)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(pds);

        builder = builder
            .WithReference(pds)
            .WithEnvironment(PdsUrlConfigurationKey, pds.GetEndpoint(AtProtoPdsContainerResource.HttpEndpointName))
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables[AdminPasswordConfigurationKey] = pds.Resource.AdminPasswordParameter;
            });

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            builder = builder.WithEnvironment(AllowInsecureHttpConfigurationKey, "true");
        }

        return waitForHealthy ? builder.WaitFor(pds) : builder;
    }

    /// <summary>
    /// Sets the public hostname for the PDS container.
    /// </summary>
    /// <remarks>
    /// The hostname determines the server's <c>did:web</c> identity and, unless
    /// <see cref="WithHandleDomains"/> says otherwise, the domain new handles are
    /// created under. It defaults to <c>localhost</c> when running locally; when
    /// publishing there is no sensible default, so the deployment is asked for one
    /// unless this method supplies it.
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="hostname">The public hostname.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithHostname(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        string hostname)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(hostname);

        Replace(builder, builder.Resource.Hostname, r => r.Hostname = hostname);
        return builder;
    }

    /// <summary>
    /// Sets the public hostname for the PDS container from a parameter.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="hostname">The parameter holding the public hostname.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithHostname(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        IResourceBuilder<ParameterResource> hostname)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(hostname);

        Replace(builder, builder.Resource.Hostname, r => r.Hostname = hostname.Resource);
        return builder;
    }

    /// <summary>
    /// Sets the domains that accounts on this PDS may take handles under
    /// (e.g. <c>.pds.example.com</c>).
    /// </summary>
    /// <remarks>
    /// Call <c>PdsAdminClient.DescribeServerAsync()</c> to confirm which domains the
    /// running server ended up accepting.
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="domains">The handle domains, each starting with a dot.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithHandleDomains(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        params string[] domains)
    {
        ArgumentNullException.ThrowIfNull(domains);
        return builder.WithEnvironment("PDS_SERVICE_HANDLE_DOMAINS", string.Join(",", domains));
    }

    /// <summary>
    /// Uses a specific admin password instead of the generated one.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="adminPassword">The parameter holding the admin password.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithAdminPassword(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        IResourceBuilder<ParameterResource> adminPassword)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(adminPassword);

        Replace(builder, builder.Resource.AdminPasswordParameter, r => r.AdminPasswordParameter = adminPassword.Resource);
        return builder;
    }

    /// <summary>
    /// Uses a specific JWT signing secret instead of the generated one.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="jwtSecret">The parameter holding the JWT secret.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithJwtSecret(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        IResourceBuilder<ParameterResource> jwtSecret)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(jwtSecret);

        Replace(builder, builder.Resource.JwtSecretParameter, r => r.JwtSecretParameter = jwtSecret.Resource);
        return builder;
    }

    /// <summary>
    /// Uses a specific PLC rotation key instead of the generated one.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="plcRotationKey">
    /// The parameter holding a hex-encoded secp256k1 private key.
    /// </param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithPlcRotationKey(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        IResourceBuilder<ParameterResource> plcRotationKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(plcRotationKey);

        Replace(builder, builder.Resource.PlcRotationKeyParameter, r => r.PlcRotationKeyParameter = plcRotationKey.Resource);
        return builder;
    }

    /// <summary>
    /// Stores the PDS data in a host directory instead of the default named volume.
    /// </summary>
    /// <remarks>
    /// On an SELinux host (Fedora, RHEL) the directory needs the container label before
    /// the PDS can write to it, or it exits with <c>SqliteError: unable to open database
    /// file</c>. Aspire mounts without relabelling, so run
    /// <c>chcon -Rt container_file_t &lt;path&gt;</c> first, or keep the default volume,
    /// which the container runtime labels for you.
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="path">The host path to mount at <c>/pds</c>.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithDataBindMount(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        string path)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(path);

        ClearDataMounts(builder);
        return builder.WithBindMount(path, DataTarget);
    }

    /// <summary>
    /// Stores the PDS data in a named volume, replacing the default one.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="name">The volume name. Defaults to <c>{resource name}-data</c>.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithDataVolume(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ClearDataMounts(builder);
        return builder.WithVolume(name ?? $"{builder.Resource.Name}-data", DataTarget);
    }

    /// <summary>
    /// Removes any mount already targeting the data directory.
    /// </summary>
    /// <remarks>
    /// <see cref="AddAtProtoPds"/> always mounts a named volume at <c>/pds</c>, so adding
    /// a second mount on the same destination would leave both annotations in the
    /// container spec. Docker and Podman reject that outright — Podman with
    /// <c>Error: /pds: duplicate mount destination</c> — so the container never starts.
    /// </remarks>
    private static void ClearDataMounts(IResourceBuilder<AtProtoPdsContainerResource> builder)
    {
        var existing = builder.Resource.Annotations
            .OfType<ContainerMountAnnotation>()
            .Where(m => m.Target == DataTarget)
            .ToList();

        foreach (var mount in existing)
        {
            builder.Resource.Annotations.Remove(mount);
        }
    }

    /// <summary>
    /// Configures the PDS to use a specific PLC directory URL.
    /// Default: <c>https://plc.directory</c>.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="plcUrl">The PLC directory URL.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithPlcUrl(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        string plcUrl)
    {
        return builder.WithEnvironment("PDS_DID_PLC_URL", plcUrl);
    }

    /// <summary>
    /// Configures the Bluesky app view URL and optional DID for the PDS.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="appViewUrl">The app view URL.</param>
    /// <param name="appViewDid">The app view DID.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithAppView(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        string appViewUrl,
        string? appViewDid = null)
    {
        builder = builder.WithEnvironment("PDS_BSKY_APP_VIEW_URL", appViewUrl);

        if (appViewDid is not null)
        {
            builder = builder.WithEnvironment("PDS_BSKY_APP_VIEW_DID", appViewDid);
        }

        return builder;
    }

    /// <summary>
    /// Configures the relay crawler URLs for the PDS.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="crawlers">A comma-separated list of relay URLs.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithCrawlers(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        string crawlers)
    {
        return builder.WithEnvironment("PDS_CRAWLERS", crawlers);
    }

    /// <summary>
    /// Disables dev mode, requiring proper PLC directory, app view, and relay configuration.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithProductionMode(
        this IResourceBuilder<AtProtoPdsContainerResource> builder)
    {
        return builder.WithEnvironment("PDS_DEV_MODE", "false");
    }

    /// <summary>
    /// Requires an invite code for signups. <c>PdsAdminClient.CreateAccountAsync()</c>
    /// mints one automatically when this is enabled.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="required">Whether invite codes are required. Default: <c>true</c>.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithInviteCodeRequired(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        bool required = true)
    {
        return builder.WithEnvironment("PDS_INVITE_REQUIRED", required ? "true" : "false");
    }

    /// <summary>
    /// Sets the maximum blob upload size in bytes.
    /// Default: <c>5242880</c> (5 MB).
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="maxBytes">The maximum blob size in bytes.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithBlobUploadLimit(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        long maxBytes)
    {
        return builder.WithEnvironment("PDS_BLOB_UPLOAD_LIMIT", maxBytes.ToString());
    }

    /// <summary>
    /// Configures the moderation / report service URL and DID for the PDS.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="reportServiceUrl">The report service URL.</param>
    /// <param name="reportServiceDid">The report service DID.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithReportService(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        string reportServiceUrl,
        string? reportServiceDid = null)
    {
        builder = builder.WithEnvironment("PDS_REPORT_SERVICE_URL", reportServiceUrl);

        if (reportServiceDid is not null)
        {
            builder = builder.WithEnvironment("PDS_REPORT_SERVICE_DID", reportServiceDid);
        }

        return builder;
    }

    /// <summary>
    /// Configures SMTP email settings for the PDS.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="smtpUrl">The SMTP connection URL.</param>
    /// <param name="fromAddress">The address outgoing mail is sent from.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithEmail(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        string smtpUrl,
        string fromAddress)
    {
        return builder
            .WithEnvironment("PDS_EMAIL_SMTP_URL", smtpUrl)
            .WithEnvironment("PDS_EMAIL_FROM_ADDRESS", fromAddress);
    }
}
