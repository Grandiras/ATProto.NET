using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace ATProtoNet.Aspire.Hosting;

/// <summary>
/// Extension methods for adding a <see href="https://tangled.org/tranquil.farm/tranquil-pds">Tranquil PDS</see>
/// container to a .NET Aspire application.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="AtProtoPdsHostingExtensions"/>, which hosts the
/// reference Bluesky PDS. Both produce a server that speaks AT Protocol and both feed
/// the same <c>AtProto:Pds</c> configuration section, so an application built against
/// <c>PdsAdminClient</c> works with either.
/// </remarks>
public static class AtProtoTranquilPdsHostingExtensions
{
    private const string DefaultImage = "atcr.io/tranquil.farm/tranquil-pds";
    private const string DefaultTag = "latest";
    private const int PdsContainerPort = 3000;
    private const string BlobTarget = "/var/lib/tranquil-pds/blobs";
    private const string DefaultDatabaseName = "tranquil_pds";

    /// <summary>
    /// Adds a Tranquil PDS container to the application, together with the PostgreSQL
    /// server it stores its repositories in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two resources are added besides the PDS itself: a <c>{name}-postgres</c> server
    /// with a persistent data volume, and a <c>{name}-db</c> database on it. Point the
    /// PDS at a database you already have with <see cref="WithDatabase"/>, or at a
    /// PostgreSQL server outside the application model with
    /// <see cref="WithDatabaseUrl(IResourceBuilder{AtProtoTranquilPdsContainerResource}, string)"/>.
    /// </para>
    /// <para>
    /// The image is <c>atcr.io/tranquil.farm/tranquil-pds</c>, which is not an anonymous
    /// registry — run <c>docker login atcr.io</c> (or <c>podman login atcr.io</c>) once
    /// before the AppHost first starts it, or pass a <paramref name="tag"/>/image you
    /// mirror yourself.
    /// </para>
    /// <para>
    /// The JWT secret, DPoP secret, master key, and administrator password are Aspire
    /// parameters persisted to the AppHost's user secrets, so accounts created on one run
    /// remain usable on the next. Override any of them with <see cref="WithJwtSecret"/>,
    /// <see cref="WithDPoPSecret"/>, <see cref="WithMasterKey"/>, or
    /// <see cref="WithAdminAccount"/>.
    /// </para>
    /// <para>
    /// When running locally the container also gets the relaxations a development
    /// instance needs — see <see cref="WithDevelopmentMode"/> for exactly which, and why
    /// they are not applied when publishing.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var builder = DistributedApplication.CreateBuilder(args);
    ///
    /// var pds = builder.AddAtProtoTranquilPds("pds");
    ///
    /// builder.AddProject&lt;Projects.Web&gt;("web")
    ///        .WithAtProtoTranquilPds(pds);
    ///
    /// builder.Build().Run();
    /// </code>
    /// </example>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name for the PDS container.</param>
    /// <param name="port">Optional host port mapping. If not specified, a random port is assigned.</param>
    /// <param name="tag">Optional container image tag. Defaults to <c>"latest"</c>.</param>
    /// <returns>A resource builder for further configuration.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> AddAtProtoTranquilPds(
        this IDistributedApplicationBuilder builder,
        string name,
        int? port = null,
        string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var isRunMode = builder.ExecutionContext.IsRunMode;

        var adminAccountPassword = ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(
            builder, $"{name}-admin-password", special: false);

        // Tranquil requires at least 32 characters for each of these outside dev mode.
        // Unlike the reference PDS's hex-parsed secrets these are opaque strings, so a
        // manifest 'generate' block describes them exactly and a deployment can produce
        // its own without being handed one the server would reject.
        var jwtSecret = CreateSecretParameter(builder, $"{name}-jwt-secret").Resource;
        var dpopSecret = CreateSecretParameter(builder, $"{name}-dpop-secret").Resource;
        var masterKey = CreateSecretParameter(builder, $"{name}-master-key").Resource;

        var resource = new AtProtoTranquilPdsContainerResource(
            name, adminAccountPassword, jwtSecret, dpopSecret, masterKey)
        {
            DevelopmentMode = isRunMode,
        };

        // The hostname fixes the domain new handles are created under, so "localhost" is
        // only ever right for a local run. A deployment supplies its own.
        if (builder.ExecutionContext.IsPublishMode)
        {
            resource.Hostname = builder.AddParameter($"{name}-hostname").Resource;
        }

        // Credentials are generated here rather than left to AddPostgres's defaults so
        // that they are known to be URI-safe: Tranquil is handed a postgres:// URL, not
        // an ADO.NET connection string.
        var postgres = builder
            .AddPostgres(
                $"{name}-postgres",
                userName: builder.AddParameter($"{name}-postgres-user", DefaultDatabaseName),
                password: CreateSecretParameter(builder, $"{name}-postgres-password"))
            .WithDataVolume($"{name}-postgres-data");

        var database = postgres.AddDatabase($"{name}-db", DefaultDatabaseName);

        // UriExpression, not ConnectionStringExpression: the latter is the ADO.NET
        // key/value form Tranquil cannot parse. This one resolves to
        // postgresql://{user}:{password}@{host}:{port}/{database}.
        resource.DatabaseUrl = database.Resource.UriExpression;

        var pds = builder.AddResource(resource)
            .WithImage(DefaultImage, tag ?? DefaultTag)
            .WithHttpEndpoint(port: port, targetPort: PdsContainerPort, name: AtProtoTranquilPdsContainerResource.HttpEndpointName)
            .WithHttpHealthCheck(AtProtoTranquilPdsContainerResource.HealthCheckPath)
            // Tranquil binds 127.0.0.1 by default, which inside a container accepts
            // nothing from outside it — the published port would connect to a closed
            // socket. Its own compose file sets the same value.
            .WithEnvironment("SERVER_HOST", "[::]")
            .WithEnvironment("SERVER_PORT", PdsContainerPort.ToString())
            .WithEnvironment("BLOB_STORAGE_PATH", BlobTarget)
            // Resolved through the resource so the With* overrides below take effect.
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables["PDS_HOSTNAME"] = resource.Hostname;
                context.EnvironmentVariables["DATABASE_URL"] = resource.DatabaseUrl;
                context.EnvironmentVariables["JWT_SECRET"] = resource.JwtSecretParameter;
                context.EnvironmentVariables["DPOP_SECRET"] = resource.DPoPSecretParameter;
                context.EnvironmentVariables["MASTER_KEY"] = resource.MasterKeyParameter;

                if (resource.DevelopmentMode)
                {
                    ApplyDevelopmentDefaults(context.EnvironmentVariables);
                }
            })
            .WithVolume($"{name}-blobs", BlobTarget)
            .WaitFor(database);

        return pds;
    }

    /// <summary>
    /// Applies the settings a local Tranquil instance needs in order to be usable
    /// without any of the infrastructure a real deployment has.
    /// </summary>
    /// <remarks>
    /// These are set as defaults rather than fixed values: every one of them can be
    /// overridden by a later <c>With*</c> call, because environment callbacks run in the
    /// order they were added and the last write to a key wins.
    /// </remarks>
    private static void ApplyDevelopmentDefaults(IDictionary<string, object> environment)
    {
        // Tranquil generates a bootstrap invite code on an empty instance and only
        // writes it to its log, so a program has no way to read it. Off, the first
        // signup — which Tranquil makes an administrator — needs no code.
        environment["INVITE_CODE_REQUIRED"] = "false";

        // Tranquil blocks login until the account has a verified communication channel.
        // A local container has no mail server, so nothing could ever verify one and no
        // account could sign in.
        environment["DISABLE_ACCOUNT_VERIFICATION_GATE"] = "true";

        // Skips the age-assurance birthday prompt on every account.
        environment["PDS_AGE_ASSURANCE_OVERRIDE"] = "true";

        // Requests between containers on the AppHost network are plaintext.
        environment["ALLOW_HTTP_PROXY"] = "true";

        // Login is rate limited per IP, and everything on the AppHost network shares one.
        environment["DISABLE_RATE_LIMITING"] = "true";
    }

    /// <summary>
    /// Creates the parameter backing one of Tranquil's secrets.
    /// </summary>
    /// <remarks>
    /// 48 characters, comfortably over the 32 Tranquil requires in production. Persisted
    /// to the AppHost's user secrets when running locally so the value stays stable
    /// across runs alongside the data volume; in publish mode the manifest carries a
    /// <c>generate</c> block the deployment satisfies instead.
    /// </remarks>
    private static IResourceBuilder<ParameterResource> CreateSecretParameter(
        IDistributedApplicationBuilder builder,
        string name)
    {
        var generated = new GenerateParameterDefault
        {
            MinLength = 48,
            Lower = true,
            Upper = true,
            Numeric = true,
            // Excluded so the value is safe to drop into the postgres:// URI Tranquil
            // is handed without percent-encoding.
            Special = false,
        };

        return builder.AddParameter(
            name, generated, secret: true, persist: builder.ExecutionContext.IsRunMode);
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
        IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        object? current,
        Action<AtProtoTranquilPdsContainerResource> assign)
    {
        if (current is ParameterResource superseded)
        {
            builder.ApplicationBuilder.Resources.Remove(superseded);
        }

        assign(builder.Resource);
    }

    /// <summary>
    /// Injects this PDS's URL and administrator credentials into a project (or any
    /// resource with environment variables), and makes it wait for the server to report
    /// healthy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project receives <c>AtProto__Pds__Url</c>, <c>AtProto__Pds__Authentication</c>
    /// (<c>AdminAccount</c>), <c>AtProto__Pds__AdminIdentifier</c>, and
    /// <c>AtProto__Pds__AdminPassword</c>, which <c>AddAtProtoPdsAdmin()</c> in the
    /// <c>ATProtoNet.Server</c> package binds into a <c>PdsAdminClient</c>. The PDS is
    /// also referenced as a connection string under its resource name.
    /// </para>
    /// <para>
    /// <strong>The administrator account is not created for you.</strong> Tranquil flags
    /// the first account registered on an empty instance as an administrator, so the
    /// application creates it once — <c>PdsAdminClient.CreateAccountAsync</c> with the
    /// configured handle and password — and every later admin call authenticates as it.
    /// Until that account exists, admin endpoints answer with an authentication error;
    /// the client signs in lazily, so it is safe to resolve before then.
    /// </para>
    /// <para>
    /// The container serves plaintext HTTP, and the URL a consumer resolves is not always
    /// a loopback address — a containerized consumer reaches it over the container
    /// network instead. In <em>run</em> mode this method therefore also sets
    /// <c>AtProto__Pds__AllowInsecureHttp</c>, since both resources are on one local
    /// network. It deliberately does not do so when publishing: sending administrator
    /// credentials unencrypted across a deployed network should be an explicit decision.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The resource type being wired up.</typeparam>
    /// <param name="builder">The resource to inject the PDS configuration into.</param>
    /// <param name="pds">The Tranquil PDS container resource.</param>
    /// <param name="waitForHealthy">
    /// Whether the resource should wait for the PDS to become healthy before starting.
    /// Default: <c>true</c>.
    /// </param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<T> WithAtProtoTranquilPds<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<AtProtoTranquilPdsContainerResource> pds,
        bool waitForHealthy = true)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(pds);

        builder = builder
            .WithReference(pds)
            .WithEnvironment(
                AtProtoPdsHostingExtensions.PdsUrlConfigurationKey,
                pds.GetEndpoint(AtProtoTranquilPdsContainerResource.HttpEndpointName))
            .WithEnvironment(context =>
            {
                // Tranquil has no shared admin password: administration goes through a
                // session belonging to an account flagged as an administrator.
                context.EnvironmentVariables[AtProtoPdsHostingExtensions.AuthenticationConfigurationKey] =
                    AtProtoPdsHostingExtensions.AdminAccountAuthentication;
                context.EnvironmentVariables[AtProtoPdsHostingExtensions.AdminIdentifierConfigurationKey] =
                    pds.Resource.ResolveAdminHandle();
                context.EnvironmentVariables[AtProtoPdsHostingExtensions.AdminPasswordConfigurationKey] =
                    pds.Resource.AdminAccountPasswordParameter;
            });

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            builder = builder.WithEnvironment(
                AtProtoPdsHostingExtensions.AllowInsecureHttpConfigurationKey, "true");
        }

        return waitForHealthy ? builder.WaitFor(pds) : builder;
    }

    /// <summary>
    /// Sets the public hostname for the PDS container.
    /// </summary>
    /// <remarks>
    /// Unless <see cref="WithHandleDomains"/> says otherwise, the hostname is the domain
    /// new handles are created under — including the administrator's, which is derived
    /// from it when <see cref="WithAdminAccount"/> does not name one.
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="hostname">The public hostname.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithHostname(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
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
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithHostname(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        IResourceBuilder<ParameterResource> hostname)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(hostname);

        Replace(builder, builder.Resource.Hostname, r => r.Hostname = hostname.Resource);
        return builder;
    }

    /// <summary>
    /// Names the account this PDS is administered through, and optionally sets its password.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <c>pdsadmin.{hostname}</c> with a generated password. The handle must
    /// fall under one of the server's handle domains, and its first label must not be a
    /// reserved subdomain — Tranquil rejects <c>admin</c>, <c>api</c>, <c>owner</c>, and
    /// a long list of others at signup.
    /// </para>
    /// <para>
    /// This only decides <em>which</em> account is the administrator. Creating it is the
    /// application's job — see
    /// <see cref="WithAtProtoTranquilPds{T}"/>.
    /// </para>
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="handle">The administrator account's handle.</param>
    /// <param name="password">
    /// The parameter holding its password. When <c>null</c>, the generated one is kept.
    /// </param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithAdminAccount(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        string handle,
        IResourceBuilder<ParameterResource>? password = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(handle);

        builder.Resource.AdminHandle = handle;

        if (password is not null)
        {
            Replace(
                builder,
                builder.Resource.AdminAccountPasswordParameter,
                r => r.AdminAccountPasswordParameter = password.Resource);
        }

        return builder;
    }

    /// <summary>
    /// Stores this PDS's repositories in an existing Aspire PostgreSQL database instead
    /// of the one <see cref="AddAtProtoTranquilPds"/> created.
    /// </summary>
    /// <remarks>
    /// The generated <c>{name}-postgres</c> server and <c>{name}-db</c> database are
    /// removed from the application model, so nothing starts a container the PDS will not
    /// use.
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="database">The database to use.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithDatabase(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        IResourceBuilder<PostgresDatabaseResource> database)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        RemoveGeneratedDatabase(builder);

        // See AddAtProtoTranquilPds: UriExpression is the postgresql:// form, whereas
        // ConnectionStringExpression is the ADO.NET one Tranquil cannot parse.
        builder.Resource.DatabaseUrl = database.Resource.UriExpression;
        return builder.WaitFor(database);
    }

    /// <summary>
    /// Points this PDS at a PostgreSQL server outside the application model.
    /// </summary>
    /// <remarks>
    /// The URL is passed to Tranquil verbatim as <c>DATABASE_URL</c>, so it must be a
    /// <c>postgres://user:password@host:port/database</c> URI with any reserved
    /// characters in the credentials already percent-encoded. Prefer the
    /// <see cref="ParameterResource"/> overload for anything carrying a password, so the
    /// value is not baked into the AppHost.
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="databaseUrl">The PostgreSQL connection URI.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithDatabaseUrl(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        string databaseUrl)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(databaseUrl);

        RemoveGeneratedDatabase(builder);

        builder.Resource.DatabaseUrl = databaseUrl;
        return builder;
    }

    /// <summary>
    /// Points this PDS at a PostgreSQL server outside the application model, taking the
    /// connection URI from a parameter.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="databaseUrl">The parameter holding the PostgreSQL connection URI.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithDatabaseUrl(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        IResourceBuilder<ParameterResource> databaseUrl)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(databaseUrl);

        RemoveGeneratedDatabase(builder);

        builder.Resource.DatabaseUrl = databaseUrl.Resource;
        return builder;
    }

    /// <summary>
    /// Drops the PostgreSQL resources <see cref="AddAtProtoTranquilPds"/> created, along
    /// with the wait on them, once the PDS has been pointed somewhere else.
    /// </summary>
    private static void RemoveGeneratedDatabase(
        IResourceBuilder<AtProtoTranquilPdsContainerResource> builder)
    {
        var name = builder.Resource.Name;
        var application = builder.ApplicationBuilder;

        var generated = application.Resources
            .Where(r => r.Name is not null
                && (r.Name == $"{name}-db"
                    || r.Name == $"{name}-postgres"
                    || r.Name == $"{name}-postgres-user"
                    || r.Name == $"{name}-postgres-password"))
            .ToList();

        // The container waits on the database it was given, and a wait on a resource that
        // is no longer in the model never completes.
        var waits = builder.Resource.Annotations
            .OfType<WaitAnnotation>()
            .Where(w => generated.Contains(w.Resource))
            .ToList();

        foreach (var wait in waits)
        {
            builder.Resource.Annotations.Remove(wait);
        }

        foreach (var resource in generated)
        {
            application.Resources.Remove(resource);
        }
    }

    /// <summary>
    /// Sets the domains that accounts on this PDS may take handles under
    /// (e.g. <c>pds.example.com</c>). Defaults to the PDS hostname.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="domains">The handle domains.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithHandleDomains(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        params string[] domains)
    {
        ArgumentNullException.ThrowIfNull(domains);
        return builder.WithEnvironment("PDS_USER_HANDLE_DOMAINS", string.Join(",", domains));
    }

    /// <summary>
    /// Uses a specific JWT signing secret instead of the generated one.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="jwtSecret">The parameter holding the JWT secret (at least 32 characters).</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithJwtSecret(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        IResourceBuilder<ParameterResource> jwtSecret)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(jwtSecret);

        Replace(builder, builder.Resource.JwtSecretParameter, r => r.JwtSecretParameter = jwtSecret.Resource);
        return builder;
    }

    /// <summary>
    /// Uses a specific DPoP proof validation secret instead of the generated one.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="dpopSecret">The parameter holding the DPoP secret (at least 32 characters).</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithDPoPSecret(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        IResourceBuilder<ParameterResource> dpopSecret)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(dpopSecret);

        Replace(builder, builder.Resource.DPoPSecretParameter, r => r.DPoPSecretParameter = dpopSecret.Resource);
        return builder;
    }

    /// <summary>
    /// Uses a specific key-encryption master key instead of the generated one.
    /// </summary>
    /// <remarks>
    /// Changing this value on a server that already has accounts makes their signing keys
    /// undecryptable.
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="masterKey">The parameter holding the master key (at least 32 characters).</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithMasterKey(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        IResourceBuilder<ParameterResource> masterKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(masterKey);

        Replace(builder, builder.Resource.MasterKeyParameter, r => r.MasterKeyParameter = masterKey.Resource);
        return builder;
    }

    /// <summary>
    /// Registers an operator-held PLC recovery key, as a public <c>did:key</c>.
    /// </summary>
    /// <remarks>
    /// Unlike the reference PDS, Tranquil keeps signing PLC operations with each account's
    /// own key; this key is added to the rotation keys so the operator can recover an
    /// identity. It is a <em>public</em> <c>did:key</c>, not a private key.
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="recoveryKey">The recovery key as a <c>did:key</c>.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithPlcRecoveryKey(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        string recoveryKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(recoveryKey);

        return builder.WithEnvironment("PLC_ROTATION_KEY", recoveryKey);
    }

    /// <summary>
    /// Stores the PDS's blobs in a host directory instead of the default named volume.
    /// </summary>
    /// <remarks>
    /// On an SELinux host (Fedora, RHEL) the directory needs the container label before
    /// the PDS can write to it. Aspire mounts without relabelling, so run
    /// <c>chcon -Rt container_file_t &lt;path&gt;</c> first, or keep the default volume,
    /// which the container runtime labels for you.
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="path">The host path to mount at <c>/var/lib/tranquil-pds/blobs</c>.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithBlobBindMount(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        string path)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(path);

        ClearBlobMounts(builder);
        return builder.WithBindMount(path, BlobTarget);
    }

    /// <summary>
    /// Stores the PDS's blobs in a named volume, replacing the default one.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="name">The volume name. Defaults to <c>{resource name}-blobs</c>.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithBlobVolume(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ClearBlobMounts(builder);
        return builder.WithVolume(name ?? $"{builder.Resource.Name}-blobs", BlobTarget);
    }

    /// <summary>
    /// Removes any mount already targeting the blob directory.
    /// </summary>
    /// <remarks>
    /// <see cref="AddAtProtoTranquilPds"/> always mounts a named volume there, so adding
    /// a second mount on the same destination would leave both annotations in the
    /// container spec. Docker and Podman reject that outright — Podman with
    /// <c>duplicate mount destination</c> — so the container never starts.
    /// </remarks>
    private static void ClearBlobMounts(
        IResourceBuilder<AtProtoTranquilPdsContainerResource> builder)
    {
        var existing = builder.Resource.Annotations
            .OfType<ContainerMountAnnotation>()
            .Where(m => m.Target == BlobTarget)
            .ToList();

        foreach (var mount in existing)
        {
            builder.Resource.Annotations.Remove(mount);
        }
    }

    /// <summary>
    /// Stores blobs in an S3 bucket instead of on disk.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="bucket">The bucket name.</param>
    /// <param name="endpoint">An optional custom S3 endpoint URL.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithS3BlobStorage(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        string bucket,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(bucket);

        builder = builder
            .WithEnvironment("BLOB_STORAGE_BACKEND", "s3")
            .WithEnvironment("S3_BUCKET", bucket);

        if (endpoint is not null)
        {
            builder = builder.WithEnvironment("S3_ENDPOINT", endpoint);
        }

        return builder;
    }

    /// <summary>
    /// Configures the PDS to use a specific PLC directory URL.
    /// Default: <c>https://plc.directory</c>.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="plcUrl">The PLC directory URL.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithPlcUrl(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        string plcUrl)
    {
        return builder.WithEnvironment("PLC_DIRECTORY_URL", plcUrl);
    }

    /// <summary>
    /// Configures the relay / crawler notification URLs for the PDS.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="crawlers">A comma-separated list of relay URLs.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithCrawlers(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        string crawlers)
    {
        return builder.WithEnvironment("CRAWLERS", crawlers);
    }

    /// <summary>
    /// Requires an invite code for signups.
    /// </summary>
    /// <remarks>
    /// Tranquil's own default is <c>true</c>; running locally,
    /// <see cref="AddAtProtoTranquilPds"/> turns it off so the first account can be
    /// created without one. Turning it back on means the first signup needs the bootstrap
    /// code Tranquil writes to its log on an empty instance, which no program can read —
    /// so create the administrator account first, then enable this.
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="required">Whether invite codes are required. Default: <c>true</c>.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithInviteCodeRequired(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        bool required = true)
    {
        return builder.WithEnvironment("INVITE_CODE_REQUIRED", required ? "true" : "false");
    }

    /// <summary>
    /// Sets the maximum blob upload size in bytes.
    /// Tranquil's default is <c>10737418240</c> (10 GiB).
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="maxBytes">The maximum blob size in bytes.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithBlobUploadLimit(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        long maxBytes)
    {
        return builder.WithEnvironment("MAX_BLOB_SIZE", maxBytes.ToString());
    }

    /// <summary>
    /// Configures the moderation / report service URL and DID for the PDS.
    /// </summary>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="reportServiceUrl">The report service URL.</param>
    /// <param name="reportServiceDid">The report service DID.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithReportService(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        string reportServiceUrl,
        string? reportServiceDid = null)
    {
        builder = builder.WithEnvironment("REPORT_SERVICE_URL", reportServiceUrl);

        if (reportServiceDid is not null)
        {
            builder = builder.WithEnvironment("REPORT_SERVICE_DID", reportServiceDid);
        }

        return builder;
    }

    /// <summary>
    /// Configures outgoing mail for the PDS, relayed through an SMTP smarthost.
    /// </summary>
    /// <remarks>
    /// With mail configured, accounts can verify an email address, which is what
    /// Tranquil's login gate looks for — so this is what lets
    /// <see cref="WithDevelopmentMode"/> be turned off.
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="fromAddress">The address outgoing mail is sent from.</param>
    /// <param name="host">The SMTP relay host.</param>
    /// <param name="port">The SMTP relay port. Default: <c>587</c>.</param>
    /// <param name="userName">An optional SMTP user name.</param>
    /// <param name="password">The parameter holding the SMTP password, if any.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithEmail(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        string fromAddress,
        string host,
        int port = 587,
        string? userName = null,
        IResourceBuilder<ParameterResource>? password = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(fromAddress);
        ArgumentException.ThrowIfNullOrEmpty(host);

        builder = builder
            .WithEnvironment("MAIL_FROM_ADDRESS", fromAddress)
            .WithEnvironment("MAIL_SMARTHOST_HOST", host)
            .WithEnvironment("MAIL_SMARTHOST_PORT", port.ToString());

        if (userName is not null)
        {
            builder = builder.WithEnvironment("MAIL_SMARTHOST_USERNAME", userName);
        }

        if (password is not null)
        {
            builder = builder.WithEnvironment("MAIL_SMARTHOST_PASSWORD", password);
        }

        return builder;
    }

    /// <summary>
    /// Turns the local-development relaxations on or off explicitly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On, the container gets <c>INVITE_CODE_REQUIRED=false</c>,
    /// <c>DISABLE_ACCOUNT_VERIFICATION_GATE=true</c>,
    /// <c>PDS_AGE_ASSURANCE_OVERRIDE=true</c>, <c>ALLOW_HTTP_PROXY=true</c>, and
    /// <c>DISABLE_RATE_LIMITING=true</c>. The two that matter are the first pair: an
    /// empty Tranquil instance mints a bootstrap invite code and only writes it to its
    /// log, and login is blocked until an account has a verified communication channel —
    /// which nothing can give it without a mail server. Left at their defaults, no
    /// account could be created and none could sign in.
    /// </para>
    /// <para>
    /// <see cref="AddAtProtoTranquilPds"/> enables them when running locally and leaves
    /// them off when publishing, where they would each weaken a real deployment. Turn
    /// them off locally once the AppHost has a mail server the PDS can verify addresses
    /// through; turn them on when publishing only if you have decided the deployment
    /// should behave like a development instance.
    /// </para>
    /// <para>
    /// Individual settings can also be overridden on their own — a later
    /// <see cref="WithInviteCodeRequired"/> wins over the value this applies.
    /// </para>
    /// </remarks>
    /// <param name="builder">The PDS resource builder.</param>
    /// <param name="enabled">Whether to apply the development defaults. Default: <c>true</c>.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<AtProtoTranquilPdsContainerResource> WithDevelopmentMode(
        this IResourceBuilder<AtProtoTranquilPdsContainerResource> builder,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Resource.DevelopmentMode = enabled;
        return builder;
    }
}
