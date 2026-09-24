using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;
using ATProtoNet.Aspire.Hosting;
using static ATProtoNet.Tests.Aspire.AspireTestHost;

namespace ATProtoNet.Tests.Aspire;

/// <summary>
/// What only the reference PDS does. Behaviour it shares with Tranquil is in
/// <see cref="PdsContainerResourceTests"/>.
/// </summary>
public class AtProtoPdsHostingExtensionsTests
{
    [Fact]
    public async Task AddAtProtoPds_SetsBaselineEnvironment()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var env = await GetEnvironmentAsync(builder.SingleResource<AtProtoPdsContainerResource>());

        Assert.Equal("/pds", env["PDS_DATA_DIRECTORY"]);
        Assert.Equal("true", env["PDS_DEV_MODE"]);

        // Without a blobstore the container exits on startup with
        // "Must configure either S3 or disk blobstore".
        Assert.Equal("/pds/blocks", env["PDS_BLOBSTORE_DISK_LOCATION"]);
    }

    [Fact]
    public async Task AddAtProtoPds_InPublishMode_LeavesDevModeUnset()
    {
        var builder = PublishModeBuilder();

        builder.AddAtProtoPds("pds");

        var env = await GetEnvironmentAsync(builder.SingleResource<AtProtoPdsContainerResource>());

        // Dev mode relaxes checks a real deployment needs; unset, the container defaults it off.
        Assert.False(env.ContainsKey("PDS_DEV_MODE"));
    }

    [Fact]
    public async Task FluentApi_AllowsChaining()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds", port: 2583, tag: "0.4")
            .WithHostname("pds.example.com")
            .WithHandleDomains(".pds.example.com", ".example.com")
            .WithPlcUrl("https://plc.directory")
            .WithAppView("https://api.bsky.app", "did:web:api.bsky.app")
            .WithCrawlers("https://bsky.network")
            .WithReportService("https://mod.bsky.app", "did:plc:ar7c4by46qjdydhdevvrndac")
            .WithInviteCodeRequired()
            .WithBlobUploadLimit(10 * 1024 * 1024)
            .WithEmail("smtps://user:pass@smtp.example.com", "noreply@example.com")
            .WithProductionMode();

        var env = await GetEnvironmentAsync(builder.SingleResource<AtProtoPdsContainerResource>());

        Assert.Equal(".pds.example.com,.example.com", env["PDS_SERVICE_HANDLE_DOMAINS"]);
        Assert.Equal("https://plc.directory", env["PDS_DID_PLC_URL"]);
        Assert.Equal("https://api.bsky.app", env["PDS_BSKY_APP_VIEW_URL"]);
        Assert.Equal("did:web:api.bsky.app", env["PDS_BSKY_APP_VIEW_DID"]);
        Assert.Equal("https://bsky.network", env["PDS_CRAWLERS"]);
        Assert.Equal("https://mod.bsky.app", env["PDS_REPORT_SERVICE_URL"]);
        Assert.Equal("did:plc:ar7c4by46qjdydhdevvrndac", env["PDS_REPORT_SERVICE_DID"]);
        Assert.Equal("true", env["PDS_INVITE_REQUIRED"]);
        Assert.Equal("10485760", env["PDS_BLOB_UPLOAD_LIMIT"]);
        Assert.Equal("smtps://user:pass@smtp.example.com", env["PDS_EMAIL_SMTP_URL"]);
        Assert.Equal("noreply@example.com", env["PDS_EMAIL_FROM_ADDRESS"]);

        // Environment callbacks run in the order they were added, so the later
        // WithProductionMode wins over the run-mode default AddAtProtoPds applied.
        Assert.Equal("false", env["PDS_DEV_MODE"]);
    }

    // ──────────────────────────────────────────────────────────
    //  Secrets and persistence
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAtProtoPds_BindsSecretsToPersistedParameters()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.SingleResource<AtProtoPdsContainerResource>();
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

        var resource = builder.SingleResource<AtProtoPdsContainerResource>();

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
        var builder = PublishModeBuilder();

        builder.AddAtProtoPds("pds");

        var resource = builder.SingleResource<AtProtoPdsContainerResource>();

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

        var resource = builder.SingleResource<AtProtoPdsContainerResource>();
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

        var resource = builder.SingleResource<AtProtoPdsContainerResource>();

        Assert.True(resource.AdminPasswordParameter.Secret);
        Assert.True(resource.JwtSecretParameter.Secret);
        Assert.True(resource.PlcRotationKeyParameter.Secret);
    }

    [Fact]
    public async Task WithAdminPassword_OverridesGeneratedParameter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var custom = builder.AddParameter("custom-admin-password", "s3cret", secret: true);

        builder.AddAtProtoPds("pds").WithAdminPassword(custom);

        var resource = builder.SingleResource<AtProtoPdsContainerResource>();
        var env = await GetEnvironmentAsync(resource);

        Assert.Same(custom.Resource, resource.AdminPasswordParameter);
        Assert.Same(custom.Resource, env["PDS_ADMIN_PASSWORD"]);
    }

    [Fact]
    public async Task WithPlcRotationKey_OverridesGeneratedParameter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var custom = builder.AddParameter("custom-rotation-key", new string('a', 64), secret: true);

        builder.AddAtProtoPds("pds").WithPlcRotationKey(custom);

        var resource = builder.SingleResource<AtProtoPdsContainerResource>();
        var env = await GetEnvironmentAsync(resource);

        Assert.Same(custom.Resource, resource.PlcRotationKeyParameter);
        Assert.Same(custom.Resource, env["PDS_PLC_ROTATION_KEY_K256_PRIVATE_KEY_HEX"]);
    }

    [Fact]
    public void WithPlcRotationKey_InPublishMode_DropsTheGeneratedParameter()
    {
        var builder = PublishModeBuilder();
        var custom = builder.AddParameter("custom-value", new string('a', 64), secret: true);

        var pds = builder.AddAtProtoPds("pds");

        Assert.Contains(builder.Resources.OfType<ParameterResource>(), p => p.Name == "pds-plc-rotation-key");

        pds.WithPlcRotationKey(custom);

        // Left in the model it would still appear as a manifest input, so a deployment
        // would be prompted for a value nothing reads.
        Assert.DoesNotContain(builder.Resources.OfType<ParameterResource>(), p => p.Name == "pds-plc-rotation-key");
    }

    // ──────────────────────────────────────────────────────────
    //  Consumer wiring
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task WithAtProtoPds_LeavesTheDefaultAuthenticationScheme()
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddAtProtoPds("pds");

        var consumer = builder.AddContainer("web", "nginx").WithAtProtoPds(pds);
        var env = await GetEnvironmentAsync(consumer.Resource);

        // HTTP Basic with the server's admin password is PdsAdminClient's default and
        // what the reference PDS expects, so there is no scheme or account to name.
        Assert.False(env.ContainsKey(AtProtoPdsHostingExtensions.AuthenticationConfigurationKey));
        Assert.False(env.ContainsKey(AtProtoPdsHostingExtensions.AdminIdentifierConfigurationKey));
    }
}
