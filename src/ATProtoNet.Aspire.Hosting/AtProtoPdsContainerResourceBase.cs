using Aspire.Hosting.ApplicationModel;

namespace ATProtoNet.Aspire.Hosting;

/// <summary>
/// The members shared by the PDS container resources:
/// <see cref="AtProtoPdsContainerResource"/> (the reference Bluesky PDS) and
/// <see cref="AtProtoTranquilPdsContainerResource"/>.
/// </summary>
/// <remarks>
/// The <c>With*</c> methods that apply to either server, such as
/// <see cref="AtProtoPdsHostingExtensions.WithHostname{T}(IResourceBuilder{T}, string)"/>
/// and <see cref="AtProtoPdsHostingExtensions.WithJwtSecret{T}"/>, are generic over this
/// type, so they return the concrete resource builder and chain into the server-specific
/// methods.
/// </remarks>
public abstract class AtProtoPdsContainerResourceBase : ContainerResource, IResourceWithConnectionString
{
    /// <summary>
    /// The name of the HTTP endpoint exposed by the PDS container.
    /// </summary>
    public const string HttpEndpointName = "http";

    /// <summary>
    /// The path the PDS serves its health endpoint from. Both servers answer the same
    /// <c>com.atproto</c> health route.
    /// </summary>
    public const string HealthCheckPath = "/xrpc/_health";

    private protected AtProtoPdsContainerResourceBase(string name, ParameterResource jwtSecret)
        : base(name)
    {
        JwtSecretParameter = jwtSecret;
    }

    /// <summary>
    /// The secret this PDS signs its session JWTs with (<c>PDS_JWT_SECRET</c> on the
    /// reference PDS, <c>JWT_SECRET</c> on Tranquil).
    /// </summary>
    public ParameterResource JwtSecretParameter { get; internal set; }

    /// <summary>
    /// The public hostname the PDS advertises: a literal string, or a
    /// <see cref="ParameterResource"/> the deployment supplies.
    /// </summary>
    /// <remarks>
    /// The hostname is the domain new handles are created under unless the server's handle
    /// domains say otherwise, and on the reference PDS it also fixes the server's
    /// <c>did:web</c> identity. A deployed PDS therefore cannot inherit the local default.
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
