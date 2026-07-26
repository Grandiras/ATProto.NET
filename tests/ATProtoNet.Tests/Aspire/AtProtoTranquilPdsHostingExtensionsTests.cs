using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using ATProtoNet.Aspire.Hosting;

namespace ATProtoNet.Tests.Aspire;

public class AtProtoTranquilPdsHostingExtensionsTests
{
    // ──────────────────────────────────────────────────────────
    //  The container and its database
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void AddAtProtoTranquilPds_AddsContainerResource()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var resource = Resource(builder);
        Assert.Equal("pds", resource.Name);
    }

    [Fact]
    public void AddAtProtoTranquilPds_ConfiguresContainerImage()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var image = Assert.Single(Resource(builder).Annotations.OfType<ContainerImageAnnotation>());

        Assert.Equal("atcr.io/tranquil.farm/tranquil-pds", image.Image);
        Assert.Equal("latest", image.Tag);
    }

    [Fact]
    public void AddAtProtoTranquilPds_WithCustomTag_SetsTag()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds", tag: "0.1.0");

        var image = Assert.Single(Resource(builder).Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("0.1.0", image.Tag);
    }

    [Fact]
    public void AddAtProtoTranquilPds_ConfiguresHttpEndpoint()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds", port: 3001);

        var endpoint = Assert.Single(
            Resource(builder).Annotations.OfType<EndpointAnnotation>(),
            e => e.Name == AtProtoTranquilPdsContainerResource.HttpEndpointName);

        Assert.Equal(3001, endpoint.Port);
        Assert.Equal(3000, endpoint.TargetPort);
    }

    [Fact]
    public void AddAtProtoTranquilPds_AddsHealthCheck()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        Assert.NotEmpty(Resource(builder).Annotations.OfType<HealthCheckAnnotation>());
    }

    [Fact]
    public void AddAtProtoTranquilPds_AddsPostgresServerAndDatabase()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        // Unlike the reference PDS, Tranquil keeps its repositories in PostgreSQL and
        // will not start without one.
        Assert.Contains(builder.Resources, r => r.Name == "pds-postgres");

        var database = Assert.Single(builder.Resources.OfType<PostgresDatabaseResource>());
        Assert.Equal("pds-db", database.Name);
        Assert.Equal("tranquil_pds", database.DatabaseName);
    }

    [Fact]
    public void AddAtProtoTranquilPds_WaitsForItsDatabase()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var waits = Resource(builder).Annotations.OfType<WaitAnnotation>();
        Assert.Contains(waits, w => w.Resource.Name == "pds-db");
    }

    [Fact]
    public async Task AddAtProtoTranquilPds_PassesAPostgresUriAsDatabaseUrl()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var env = await GetEnvironmentAsync(Resource(builder));

        // Tranquil parses DATABASE_URL as a URI, not as an ADO.NET connection string.
        // PostgresDatabaseResource exposes both shapes: ConnectionStringExpression is the
        // ADO.NET one ("{pds-postgres.connectionString};Database=tranquil_pds"), which
        // would not parse at all, and UriExpression is the one used here. Pinning the
        // whole template rather than just its ends catches a swap between the two, and
        // catches credentials or host landing in the wrong place.
        var databaseUrl = Assert.IsType<ReferenceExpression>(env["DATABASE_URL"]);
        Assert.Equal(
            "postgresql://{pds-postgres-user.value}:{pds-postgres-password.value}" +
            "@{pds-postgres.bindings.tcp.host}:{pds-postgres.bindings.tcp.port}/tranquil_pds",
            databaseUrl.ValueExpression);
    }

    [Fact]
    public async Task AddAtProtoTranquilPds_BindsToAllInterfaces()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var env = await GetEnvironmentAsync(Resource(builder));

        // Tranquil's own default is 127.0.0.1, which inside a container accepts nothing
        // from outside it: the published port would connect to a closed socket.
        Assert.Equal("[::]", env["SERVER_HOST"]);
        Assert.Equal("3000", env["SERVER_PORT"]);
    }

    [Fact]
    public void AddAtProtoTranquilPds_ConfiguresBlobVolume()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var mount = Assert.Single(
            Resource(builder).Annotations.OfType<ContainerMountAnnotation>(),
            m => m.Target == "/var/lib/tranquil-pds/blobs");

        Assert.Equal(ContainerMountType.Volume, mount.Type);
    }

    [Fact]
    public void AddAtProtoTranquilPds_ImplementsIResourceWithConnectionString()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        Assert.IsAssignableFrom<IResourceWithConnectionString>(Resource(builder));
    }

    [Fact]
    public void AddAtProtoTranquilPds_ThrowsOnNullBuilder()
    {
        IDistributedApplicationBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() => builder.AddAtProtoTranquilPds("pds"));
    }

    [Fact]
    public void AddAtProtoTranquilPds_ThrowsOnEmptyName()
    {
        var builder = DistributedApplication.CreateBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddAtProtoTranquilPds(""));
    }

    // ──────────────────────────────────────────────────────────
    //  Secrets
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAtProtoTranquilPds_BindsSecretsToParameters()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var resource = Resource(builder);
        var env = await GetEnvironmentAsync(resource);

        // Literals baked in at AppHost build time would change on every run while the
        // data volume persisted — a new master key alone strands every account, whose
        // signing key was encrypted with the old one.
        Assert.Same(resource.JwtSecretParameter, env["JWT_SECRET"]);
        Assert.Same(resource.DPoPSecretParameter, env["DPOP_SECRET"]);
        Assert.Same(resource.MasterKeyParameter, env["MASTER_KEY"]);
    }

    [Theory]
    [InlineData("pds-jwt-secret")]
    [InlineData("pds-dpop-secret")]
    [InlineData("pds-master-key")]
    [InlineData("pds-postgres-password")]
    public async Task AddAtProtoTranquilPds_GeneratesSecretsTranquilAccepts(string parameterName)
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var parameter = Assert.Single(
            builder.Resources.OfType<ParameterResource>(), p => p.Name == parameterName);

        var value = await parameter.GetValueAsync(CancellationToken.None) ?? "";

        // Tranquil refuses to start with a secret under 32 characters outside dev mode.
        Assert.True(value.Length >= 32, $"{parameterName} is only {value.Length} characters");

        // The postgres credentials end up inside a URI, so nothing may need escaping.
        Assert.True(
            value.All(char.IsAsciiLetterOrDigit),
            $"{parameterName} is not URI-safe: {value}");

        Assert.True(parameter.Secret);
    }

    [Fact]
    public void AddAtProtoTranquilPds_InPublishMode_StillDescribesItsSecrets()
    {
        var builder = PublishModeBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var resource = Resource(builder);

        // Unlike the reference PDS's hex-parsed secrets, these are opaque strings a
        // manifest 'generate' block describes exactly, so a deployment can produce its
        // own rather than being handed one the server rejects.
        Assert.NotNull(resource.JwtSecretParameter.Default);
        Assert.NotNull(resource.DPoPSecretParameter.Default);
        Assert.NotNull(resource.MasterKeyParameter.Default);
    }

    [Fact]
    public void WithMasterKey_OverridesGeneratedParameterAndDropsIt()
    {
        var builder = PublishModeBuilder();
        var custom = builder.AddParameter("custom-master-key", new string('a', 48), secret: true);

        var pds = builder.AddAtProtoTranquilPds("pds");
        Assert.Contains(builder.Resources.OfType<ParameterResource>(), p => p.Name == "pds-master-key");

        pds.WithMasterKey(custom);

        Assert.Same(custom.Resource, pds.Resource.MasterKeyParameter);

        // Left in the model it would still appear as a manifest input, so a deployment
        // would be prompted for a value nothing reads.
        Assert.DoesNotContain(builder.Resources.OfType<ParameterResource>(), p => p.Name == "pds-master-key");
    }

    [Fact]
    public void WithJwtSecretAndDPoPSecret_OverrideGeneratedParameters()
    {
        var builder = DistributedApplication.CreateBuilder();
        var jwt = builder.AddParameter("custom-jwt", new string('a', 48), secret: true);
        var dpop = builder.AddParameter("custom-dpop", new string('b', 48), secret: true);

        var pds = builder.AddAtProtoTranquilPds("pds")
            .WithJwtSecret(jwt)
            .WithDPoPSecret(dpop);

        Assert.Same(jwt.Resource, pds.Resource.JwtSecretParameter);
        Assert.Same(dpop.Resource, pds.Resource.DPoPSecretParameter);
    }

    // ──────────────────────────────────────────────────────────
    //  Hostname and the administrator account
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAtProtoTranquilPds_InRunMode_DefaultsHostnameToLocalhost()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var env = await GetEnvironmentAsync(Resource(builder));
        Assert.Equal("localhost", env["PDS_HOSTNAME"]);
    }

    [Fact]
    public async Task AddAtProtoTranquilPds_InPublishMode_AsksForTheHostname()
    {
        var builder = PublishModeBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var env = await GetEnvironmentAsync(Resource(builder));

        // "localhost" would give a deployed PDS a handle domain nothing can resolve.
        Assert.IsType<ParameterResource>(env["PDS_HOSTNAME"]);
    }

    [Fact]
    public async Task WithHostname_OverridesTheDefault()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds").WithHostname("pds.example.com");

        var env = await GetEnvironmentAsync(Resource(builder));
        Assert.Equal("pds.example.com", env["PDS_HOSTNAME"]);
    }

    [Fact]
    public async Task WithHostname_MovesTheDerivedAdminHandleWithIt()
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddAtProtoTranquilPds("pds").WithHostname("pds.example.com");

        var consumer = builder.AddContainer("web", "nginx").WithAtProtoTranquilPds(pds);
        var env = await GetEnvironmentAsync(consumer.Resource);

        // A handle left on the old domain would not be one the server issues, so the
        // account could never be registered and admin calls would never authenticate.
        Assert.Equal(
            "pdsadmin.pds.example.com",
            env[AtProtoPdsHostingExtensions.AdminIdentifierConfigurationKey]);
    }

    [Fact]
    public async Task InPublishMode_TheDerivedAdminHandleFollowsTheHostnameParameter()
    {
        var builder = PublishModeBuilder();
        var pds = builder.AddAtProtoTranquilPds("pds");

        var consumer = builder.AddContainer("web", "nginx").WithAtProtoTranquilPds(pds);
        var env = await GetEnvironmentAsync(consumer.Resource);

        // Publishing, the hostname is a deployment input rather than a literal, so the
        // handle has to be an expression over it — a literal resolved at AppHost build
        // time would name an account on the wrong domain.
        var identifier = Assert.IsType<ReferenceExpression>(
            env[AtProtoPdsHostingExtensions.AdminIdentifierConfigurationKey]);

        Assert.Equal("pdsadmin.{pds-hostname.value}", identifier.ValueExpression);
    }

    [Fact]
    public async Task WithAdminAccount_NamesTheAdministrator()
    {
        var builder = DistributedApplication.CreateBuilder();
        var password = builder.AddParameter("custom-admin-password", "s3cret", secret: true);

        var pds = builder.AddAtProtoTranquilPds("pds")
            .WithAdminAccount("root.localhost", password);

        var consumer = builder.AddContainer("web", "nginx").WithAtProtoTranquilPds(pds);
        var env = await GetEnvironmentAsync(consumer.Resource);

        Assert.Equal("root.localhost", env[AtProtoPdsHostingExtensions.AdminIdentifierConfigurationKey]);
        Assert.Same(password.Resource, env[AtProtoPdsHostingExtensions.AdminPasswordConfigurationKey]);
        Assert.Same(password.Resource, pds.Resource.AdminAccountPasswordParameter);
    }

    [Fact]
    public void DefaultAdminHandle_IsNotAReservedSubdomain()
    {
        // Tranquil rejects a signup whose first handle label is reserved, and "admin" is
        // on that list — the obvious default would be one no server would accept.
        Assert.Equal("pdsadmin", AtProtoTranquilPdsContainerResource.DefaultAdminHandlePrefix);
    }

    // ──────────────────────────────────────────────────────────
    //  Development-mode relaxations
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAtProtoTranquilPds_InRunMode_MakesTheServerUsableWithoutMail()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var env = await GetEnvironmentAsync(Resource(builder));

        // Tranquil mints a bootstrap invite code on an empty instance and only writes it
        // to its log, so with invites required no program could register the first
        // account; and login is blocked until the account has a verified communication
        // channel, which a container with no mail server can never get.
        Assert.Equal("false", env["INVITE_CODE_REQUIRED"]);
        Assert.Equal("true", env["DISABLE_ACCOUNT_VERIFICATION_GATE"]);
        Assert.Equal("true", env["PDS_AGE_ASSURANCE_OVERRIDE"]);
        Assert.Equal("true", env["ALLOW_HTTP_PROXY"]);
        Assert.Equal("true", env["DISABLE_RATE_LIMITING"]);
    }

    [Fact]
    public async Task AddAtProtoTranquilPds_InPublishMode_AppliesNoRelaxations()
    {
        var builder = PublishModeBuilder();

        builder.AddAtProtoTranquilPds("pds");

        var env = await GetEnvironmentAsync(Resource(builder));

        Assert.False(env.ContainsKey("INVITE_CODE_REQUIRED"));
        Assert.False(env.ContainsKey("DISABLE_ACCOUNT_VERIFICATION_GATE"));
        Assert.False(env.ContainsKey("DISABLE_RATE_LIMITING"));
        Assert.False(env.ContainsKey("ALLOW_HTTP_PROXY"));
        Assert.False(env.ContainsKey("PDS_AGE_ASSURANCE_OVERRIDE"));
    }

    [Fact]
    public async Task WithDevelopmentMode_Disabled_LeavesTheServerDefaults()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds").WithDevelopmentMode(false);

        var env = await GetEnvironmentAsync(Resource(builder));

        Assert.False(env.ContainsKey("INVITE_CODE_REQUIRED"));
        Assert.False(env.ContainsKey("DISABLE_ACCOUNT_VERIFICATION_GATE"));
    }

    [Fact]
    public async Task WithInviteCodeRequired_WinsOverTheDevelopmentDefault()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds").WithInviteCodeRequired();

        var env = await GetEnvironmentAsync(Resource(builder));

        // Environment callbacks run in the order they were added, so a later override has
        // to be the one that survives — otherwise the development default is unopposable.
        Assert.Equal("true", env["INVITE_CODE_REQUIRED"]);
    }

    // ──────────────────────────────────────────────────────────
    //  Bringing your own database
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task WithDatabase_UsesTheGivenDatabaseAndDropsTheGeneratedOne()
    {
        var builder = DistributedApplication.CreateBuilder();
        var shared = builder.AddPostgres("shared").AddDatabase("shared-pds", "pds");

        var pds = builder.AddAtProtoTranquilPds("pds").WithDatabase(shared);

        var env = await GetEnvironmentAsync(pds.Resource);
        // Same URI shape as the generated database gets, pointed at the given server.
        var databaseUrl = Assert.IsType<ReferenceExpression>(env["DATABASE_URL"]);
        Assert.StartsWith("postgresql://", databaseUrl.ValueExpression);
        Assert.Contains("@{shared.bindings.tcp.host}:{shared.bindings.tcp.port}", databaseUrl.ValueExpression);
        Assert.EndsWith("/pds", databaseUrl.ValueExpression);

        // Nothing should start a PostgreSQL container the PDS will never connect to.
        Assert.DoesNotContain(builder.Resources, r => r.Name == "pds-postgres");
        Assert.DoesNotContain(builder.Resources, r => r.Name == "pds-db");

        // And a wait on a resource no longer in the model would never complete.
        Assert.DoesNotContain(
            pds.Resource.Annotations.OfType<WaitAnnotation>(), w => w.Resource.Name == "pds-db");
        Assert.Contains(
            pds.Resource.Annotations.OfType<WaitAnnotation>(), w => w.Resource.Name == "shared-pds");
    }

    [Fact]
    public async Task WithDatabaseUrl_PassesTheUrlThroughVerbatim()
    {
        var builder = DistributedApplication.CreateBuilder();

        var pds = builder.AddAtProtoTranquilPds("pds")
            .WithDatabaseUrl("postgres://user:pass@db.example.com:5432/pds");

        var env = await GetEnvironmentAsync(pds.Resource);

        Assert.Equal("postgres://user:pass@db.example.com:5432/pds", env["DATABASE_URL"]);
        Assert.DoesNotContain(builder.Resources, r => r.Name == "pds-postgres");
    }

    [Fact]
    public async Task WithDatabaseUrl_AcceptsAParameter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var url = builder.AddParameter("db-url", "postgres://user:pass@db:5432/pds", secret: true);

        var pds = builder.AddAtProtoTranquilPds("pds").WithDatabaseUrl(url);

        var env = await GetEnvironmentAsync(pds.Resource);
        Assert.Same(url.Resource, env["DATABASE_URL"]);
    }

    // ──────────────────────────────────────────────────────────
    //  Storage overrides
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void WithBlobBindMount_ReplacesTheDefaultVolume()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds").WithBlobBindMount("./pds-blobs");

        // Two mounts on one destination is rejected by the container runtime
        // ("duplicate mount destination"), so the container would never start.
        var mount = Assert.Single(
            Resource(builder).Annotations.OfType<ContainerMountAnnotation>(),
            m => m.Target == "/var/lib/tranquil-pds/blobs");

        Assert.Equal(ContainerMountType.BindMount, mount.Type);
    }

    [Fact]
    public void WithBlobVolume_ReplacesTheDefaultVolume()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds").WithBlobVolume("custom-blobs");

        var mount = Assert.Single(
            Resource(builder).Annotations.OfType<ContainerMountAnnotation>(),
            m => m.Target == "/var/lib/tranquil-pds/blobs");

        Assert.Equal("custom-blobs", mount.Source);
    }

    [Fact]
    public async Task WithS3BlobStorage_SwitchesTheBackend()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds")
            .WithS3BlobStorage("pds-blobs", "https://s3.example.com");

        var env = await GetEnvironmentAsync(Resource(builder));

        Assert.Equal("s3", env["BLOB_STORAGE_BACKEND"]);
        Assert.Equal("pds-blobs", env["S3_BUCKET"]);
        Assert.Equal("https://s3.example.com", env["S3_ENDPOINT"]);
    }

    // ──────────────────────────────────────────────────────────
    //  Consumer wiring
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task WithAtProtoTranquilPds_SelectsAccountAuthentication()
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddAtProtoTranquilPds("pds");

        var consumer = builder.AddContainer("web", "nginx").WithAtProtoTranquilPds(pds);
        var env = await GetEnvironmentAsync(consumer.Resource);

        // Tranquil has no server-wide admin password, so a client left on the default
        // HTTP Basic scheme would be rejected by every admin endpoint.
        Assert.Equal("AdminAccount", env[AtProtoPdsHostingExtensions.AuthenticationConfigurationKey]);
        Assert.Equal("pdsadmin.localhost", env[AtProtoPdsHostingExtensions.AdminIdentifierConfigurationKey]);
        Assert.Same(
            pds.Resource.AdminAccountPasswordParameter,
            env[AtProtoPdsHostingExtensions.AdminPasswordConfigurationKey]);
        Assert.True(env.ContainsKey(AtProtoPdsHostingExtensions.PdsUrlConfigurationKey));
    }

    [Fact]
    public async Task WithAtProtoTranquilPds_InRunMode_AllowsPlaintextHttp()
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddAtProtoTranquilPds("pds");

        var consumer = builder.AddContainer("web", "nginx").WithAtProtoTranquilPds(pds);
        var env = await GetEnvironmentAsync(consumer.Resource);

        // A containerized consumer resolves the PDS over the container network, not as a
        // loopback address, so the admin client's HTTPS guard would otherwise reject the
        // URL this very method supplies.
        Assert.Equal("true", env[AtProtoPdsHostingExtensions.AllowInsecureHttpConfigurationKey]);
    }

    [Fact]
    public async Task WithAtProtoTranquilPds_InPublishMode_DoesNotAllowPlaintextHttp()
    {
        var builder = PublishModeBuilder();
        var pds = builder.AddAtProtoTranquilPds("pds");

        var consumer = builder.AddContainer("web", "nginx").WithAtProtoTranquilPds(pds);
        var env = await GetEnvironmentAsync(consumer.Resource);

        // Sending administrator credentials unencrypted across a deployed network should
        // be the operator's explicit decision.
        Assert.False(env.ContainsKey(AtProtoPdsHostingExtensions.AllowInsecureHttpConfigurationKey));
    }

    [Fact]
    public void WithAtProtoTranquilPds_AddsWaitAnnotationByDefault()
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddAtProtoTranquilPds("pds");

        var consumer = builder.AddContainer("web", "nginx").WithAtProtoTranquilPds(pds);

        Assert.NotEmpty(consumer.Resource.Annotations.OfType<WaitAnnotation>());
    }

    [Fact]
    public void WithAtProtoTranquilPds_WithWaitDisabled_AddsNoWaitAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var pds = builder.AddAtProtoTranquilPds("pds");

        var consumer = builder.AddContainer("web", "nginx")
            .WithAtProtoTranquilPds(pds, waitForHealthy: false);

        Assert.Empty(consumer.Resource.Annotations.OfType<WaitAnnotation>());
    }

    [Fact]
    public void WithAtProtoTranquilPds_ThrowsOnNullPds()
    {
        var builder = DistributedApplication.CreateBuilder();
        var consumer = builder.AddContainer("web", "nginx");

        Assert.Throws<ArgumentNullException>(() => consumer.WithAtProtoTranquilPds(null!));
    }

    [Fact]
    public async Task FluentApi_AllowsChaining()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddAtProtoTranquilPds("pds", port: 3001, tag: "latest")
            .WithHostname("pds.example.com")
            .WithHandleDomains("pds.example.com", "example.com")
            .WithAdminAccount("root.pds.example.com")
            .WithPlcUrl("https://plc.directory")
            .WithPlcRecoveryKey("did:key:zQ3shokFTS3brHcDQrn82RUDfCZESWL1ZdCEJwekUDPQiYBme")
            .WithCrawlers("https://bsky.network")
            .WithReportService("https://mod.bsky.app", "did:plc:ar7c4by46qjdydhdevvrndac")
            .WithBlobUploadLimit(10 * 1024 * 1024)
            .WithEmail("noreply@example.com", "smtp.example.com")
            .WithInviteCodeRequired()
            .WithDevelopmentMode(false);

        var env = await GetEnvironmentAsync(Resource(builder));

        Assert.Equal("pds.example.com,example.com", env["PDS_USER_HANDLE_DOMAINS"]);
        Assert.Equal("https://plc.directory", env["PLC_DIRECTORY_URL"]);
        Assert.Equal("https://bsky.network", env["CRAWLERS"]);
        Assert.Equal("10485760", env["MAX_BLOB_SIZE"]);
        Assert.Equal("noreply@example.com", env["MAIL_FROM_ADDRESS"]);
        Assert.Equal("smtp.example.com", env["MAIL_SMARTHOST_HOST"]);
        Assert.Equal("587", env["MAIL_SMARTHOST_PORT"]);
        Assert.Equal("https://mod.bsky.app", env["REPORT_SERVICE_URL"]);
    }

    private static AtProtoTranquilPdsContainerResource Resource(IDistributedApplicationBuilder builder) =>
        builder.Resources.OfType<AtProtoTranquilPdsContainerResource>().Single();

    private static IDistributedApplicationBuilder PublishModeBuilder()
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = ["--publisher", "manifest", "--output-path", "manifest.json"] });

        Assert.True(builder.ExecutionContext.IsPublishMode, "expected a publish-mode builder");
        return builder;
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
