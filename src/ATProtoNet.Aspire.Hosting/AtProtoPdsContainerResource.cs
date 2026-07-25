using Aspire.Hosting.ApplicationModel;

namespace ATProtoNet.Aspire.Hosting;

/// <summary>
/// Represents the official Bluesky PDS (<c>ghcr.io/bluesky-social/pds</c>) container resource
/// in a .NET Aspire application.
/// </summary>
/// <param name="name">The resource name.</param>
/// <param name="adminPassword">The parameter holding the server's admin password.</param>
/// <param name="jwtSecret">The parameter holding the server's JWT signing secret.</param>
/// <param name="plcRotationKey">The parameter holding the server's PLC rotation key.</param>
public sealed class AtProtoPdsContainerResource(
    string name,
    ParameterResource adminPassword,
    ParameterResource jwtSecret,
    ParameterResource plcRotationKey)
    : ContainerResource(name), IResourceWithConnectionString
{
    /// <summary>
    /// The name of the HTTP endpoint exposed by this PDS container.
    /// </summary>
    public const string HttpEndpointName = "http";

    /// <summary>
    /// The path the PDS serves its health endpoint from.
    /// </summary>
    public const string HealthCheckPath = "/xrpc/_health";

    /// <summary>
    /// The admin password for this PDS, used for HTTP Basic authentication against
    /// <c>com.atproto.admin.*</c>. Pass it to <c>PdsAdminClient</c> (core <c>ATProtoNet</c>
    /// package) to administer the server programmatically.
    /// </summary>
    public ParameterResource AdminPasswordParameter { get; internal set; } = adminPassword;

    /// <summary>
    /// The secret this PDS signs its session JWTs with.
    /// </summary>
    public ParameterResource JwtSecretParameter { get; internal set; } = jwtSecret;

    /// <summary>
    /// The hex-encoded secp256k1 private key this PDS uses as its PLC rotation key.
    /// </summary>
    /// <remarks>
    /// Changing this key strands the <c>did:plc</c> identities already created on the
    /// server, which is why it is persisted to the AppHost's user secrets rather than
    /// regenerated on every run.
    /// </remarks>
    public ParameterResource PlcRotationKeyParameter { get; internal set; } = plcRotationKey;

    /// <summary>
    /// The public hostname the PDS advertises — a literal string, or a
    /// <see cref="ParameterResource"/> the deployment supplies.
    /// </summary>
    /// <remarks>
    /// The hostname determines the server's <c>did:web</c> identity and the domain new
    /// handles are created under, so a deployed PDS cannot inherit the local default.
    /// </remarks>
    internal object Hostname { get; set; } = "localhost";

    /// <summary>
    /// Gets the connection string expression for this PDS instance,
    /// formatted as <c>http://{host}:{port}</c>.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"http://{this.GetEndpoint(HttpEndpointName).Property(EndpointProperty.Host)}:{this.GetEndpoint(HttpEndpointName).Property(EndpointProperty.Port)}");
}
