using System.Security.Cryptography;
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

    /// <summary>
    /// Adds the official Bluesky PDS container (<c>ghcr.io/bluesky-social/pds</c>) to the application.
    /// The container is configured in dev mode with auto-generated secrets.
    /// </summary>
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

        var jwtSecret = GenerateHexSecret(16);
        var rotationKey = GenerateHexSecret(32);

        var resource = new AtProtoPdsContainerResource(name);

        return builder.AddResource(resource)
            .WithImage("ghcr.io/bluesky-social/pds", tag ?? DefaultTag)
            .WithHttpEndpoint(port: port, targetPort: PdsContainerPort, name: AtProtoPdsContainerResource.HttpEndpointName)
            .WithEnvironment("PDS_DATA_DIRECTORY", "/pds")
            .WithEnvironment("PDS_JWT_SECRET", jwtSecret)
            .WithEnvironment("PDS_PLC_ROTATION_KEY_K256_PRIVATE_KEY_HEX", rotationKey)
            .WithEnvironment("PDS_DEV_MODE", "true")
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables["PDS_ADMIN_PASSWORD"] = adminPassword;
            })
            .WithVolume($"{name}-data", "/pds");
    }

    /// <summary>
    /// Sets the public hostname for the PDS container.
    /// </summary>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithHostname(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        string hostname)
    {
        return builder.WithEnvironment("PDS_HOSTNAME", hostname);
    }

    /// <summary>
    /// Configures the PDS to use a specific PLC directory URL.
    /// Default: <c>https://plc.directory</c>.
    /// </summary>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithPlcUrl(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        string plcUrl)
    {
        return builder.WithEnvironment("PDS_DID_PLC_URL", plcUrl);
    }

    /// <summary>
    /// Configures the Bluesky app view URL and optional DID for the PDS.
    /// </summary>
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
    public static IResourceBuilder<AtProtoPdsContainerResource> WithCrawlers(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        string crawlers)
    {
        return builder.WithEnvironment("PDS_CRAWLERS", crawlers);
    }

    /// <summary>
    /// Disables dev mode, requiring proper PLC directory, app view, and relay configuration.
    /// </summary>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithProductionMode(
        this IResourceBuilder<AtProtoPdsContainerResource> builder)
    {
        return builder.WithEnvironment("PDS_DEV_MODE", "false");
    }

    /// <summary>
    /// Sets the maximum blob upload size in bytes.
    /// Default: <c>5242880</c> (5 MB).
    /// </summary>
    public static IResourceBuilder<AtProtoPdsContainerResource> WithBlobUploadLimit(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        long maxBytes)
    {
        return builder.WithEnvironment("PDS_BLOB_UPLOAD_LIMIT", maxBytes.ToString());
    }

    /// <summary>
    /// Configures the moderation / report service URL and DID for the PDS.
    /// </summary>
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
    public static IResourceBuilder<AtProtoPdsContainerResource> WithEmail(
        this IResourceBuilder<AtProtoPdsContainerResource> builder,
        string smtpUrl,
        string fromAddress)
    {
        return builder
            .WithEnvironment("PDS_EMAIL_SMTP_URL", smtpUrl)
            .WithEnvironment("PDS_EMAIL_FROM_ADDRESS", fromAddress);
    }

    private static string GenerateHexSecret(int byteCount) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(byteCount)).ToLowerInvariant();
}
