using Aspire.Hosting.ApplicationModel;

namespace ATProtoNet.Aspire.Hosting;

/// <summary>
/// Represents a <see href="https://tangled.org/tranquil.farm/tranquil-pds">Tranquil PDS</see>
/// container resource in a .NET Aspire application.
/// </summary>
/// <remarks>
/// <para>
/// Tranquil is a community AT Protocol PDS implementation — a single Rust binary rather
/// than the reference server's Node.js runtime — and a superset of the reference PDS:
/// passkeys and 2FA, SSO, granular OAuth scopes, account delegation, and a built-in web
/// UI. It stores its repositories in PostgreSQL, so the resource always has a database
/// alongside it.
/// </para>
/// <para>
/// The most important difference for a consumer is authentication:
/// <see cref="AtProtoPdsContainerResource"/> (the reference PDS) has a single shared
/// admin password used with HTTP Basic, while Tranquil marks individual <em>accounts</em>
/// as administrators and expects an ordinary session token. A
/// <c>PdsAdminClient</c> pointed at this resource therefore runs in
/// <c>PdsAdminAuthentication.AdminAccount</c> mode — see
/// <see cref="AtProtoTranquilPdsHostingExtensions.WithAtProtoTranquilPds{T}"/>.
/// </para>
/// </remarks>
/// <param name="name">The resource name.</param>
/// <param name="adminAccountPassword">The parameter holding the administrator account's password.</param>
/// <param name="jwtSecret">The parameter holding the server's JWT signing secret.</param>
/// <param name="dpopSecret">The parameter holding the server's DPoP proof validation secret.</param>
/// <param name="masterKey">The parameter holding the server's key-encryption master key.</param>
public sealed class AtProtoTranquilPdsContainerResource(
    string name,
    ParameterResource adminAccountPassword,
    ParameterResource jwtSecret,
    ParameterResource dpopSecret,
    ParameterResource masterKey)
    : ContainerResource(name), IResourceWithConnectionString
{
    /// <summary>
    /// The name of the HTTP endpoint exposed by this Tranquil PDS container.
    /// </summary>
    public const string HttpEndpointName = "http";

    /// <summary>
    /// The path the PDS serves its health endpoint from. Tranquil answers the same
    /// <c>com.atproto</c> health route as the reference server.
    /// </summary>
    public const string HealthCheckPath = "/xrpc/_health";

    /// <summary>
    /// The local part prepended to the hostname when no administrator handle is set.
    /// </summary>
    /// <remarks>
    /// Not <c>admin</c>: Tranquil rejects a signup whose first handle label is a reserved
    /// subdomain, and <c>admin</c> is on that list.
    /// </remarks>
    public const string DefaultAdminHandlePrefix = "pdsadmin";

    /// <summary>
    /// The password of the account this PDS is administered through.
    /// </summary>
    /// <remarks>
    /// Tranquil has no server-wide admin password. The account named by
    /// <see cref="AtProtoTranquilPdsHostingExtensions.WithAdminAccount"/> is an ordinary
    /// account that happens to be flagged as an administrator, and this is its password;
    /// a <c>PdsAdminClient</c> signs in with it and uses the resulting session for
    /// <c>com.atproto.admin.*</c>.
    /// </remarks>
    public ParameterResource AdminAccountPasswordParameter { get; internal set; } = adminAccountPassword;

    /// <summary>
    /// The secret this PDS signs its session JWTs with (<c>JWT_SECRET</c>).
    /// </summary>
    public ParameterResource JwtSecretParameter { get; internal set; } = jwtSecret;

    /// <summary>
    /// The secret this PDS validates OAuth DPoP proofs with (<c>DPOP_SECRET</c>).
    /// </summary>
    public ParameterResource DPoPSecretParameter { get; internal set; } = dpopSecret;

    /// <summary>
    /// The master key this PDS derives its key-encryption keys from (<c>MASTER_KEY</c>).
    /// </summary>
    /// <remarks>
    /// Every account's signing key is encrypted with a key derived from this value, so
    /// changing it strands the identities already created on the server. It is persisted
    /// to the AppHost's user secrets rather than regenerated on every run.
    /// </remarks>
    public ParameterResource MasterKeyParameter { get; internal set; } = masterKey;

    /// <summary>
    /// The public hostname the PDS advertises — a literal string, or a
    /// <see cref="ParameterResource"/> the deployment supplies.
    /// </summary>
    /// <remarks>
    /// The hostname is required by Tranquil and, unless
    /// <see cref="AtProtoTranquilPdsHostingExtensions.WithHandleDomains"/> says otherwise,
    /// is the domain new handles are created under.
    /// </remarks>
    internal object Hostname { get; set; } = "localhost";

    /// <summary>
    /// The handle of the administrator account, or <c>null</c> to derive it from
    /// <see cref="Hostname"/>.
    /// </summary>
    internal object? AdminHandle { get; set; }

    /// <summary>
    /// The <c>DATABASE_URL</c> the container is given — a <c>postgres://</c> URI.
    /// </summary>
    internal object DatabaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Whether the container starts with the local-development relaxations applied.
    /// </summary>
    internal bool DevelopmentMode { get; set; }

    /// <summary>
    /// Resolves the administrator handle, deriving <c>pdsadmin.{hostname}</c> when none
    /// was set explicitly.
    /// </summary>
    /// <remarks>
    /// Derived rather than defaulted to a literal so that
    /// <see cref="AtProtoTranquilPdsHostingExtensions.WithHostname(IResourceBuilder{AtProtoTranquilPdsContainerResource}, string)"/>
    /// alone leaves a usable handle: one under a domain the server actually issues
    /// handles for.
    /// </remarks>
    internal object ResolveAdminHandle() => (AdminHandle, Hostname) switch
    {
        ({ } handle, _) => handle,
        (null, string hostname) => $"{DefaultAdminHandlePrefix}.{hostname}",
        (null, ParameterResource hostname) =>
            ReferenceExpression.Create($"{DefaultAdminHandlePrefix}.{hostname}"),
        _ => throw new InvalidOperationException(
            $"Cannot derive an administrator handle from a hostname of type {Hostname.GetType()}."),
    };

    /// <summary>
    /// Gets the connection string expression for this PDS instance,
    /// formatted as <c>http://{host}:{port}</c>.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"http://{this.GetEndpoint(HttpEndpointName).Property(EndpointProperty.Host)}:{this.GetEndpoint(HttpEndpointName).Property(EndpointProperty.Port)}");
}
