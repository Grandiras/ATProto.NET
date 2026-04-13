using Aspire.Hosting.ApplicationModel;

namespace ATProtoNet.Aspire.Hosting;

/// <summary>
/// Represents the official Bluesky PDS (<c>ghcr.io/bluesky-social/pds</c>) container resource
/// in a .NET Aspire application.
/// </summary>
public sealed class AtProtoPdsContainerResource(string name) : ContainerResource(name), IResourceWithConnectionString
{
    /// <summary>
    /// The name of the HTTP endpoint exposed by this PDS container.
    /// </summary>
    public const string HttpEndpointName = "http";

    /// <summary>
    /// Gets the connection string expression for this PDS instance,
    /// formatted as <c>http://{host}:{port}</c>.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"http://{this.GetEndpoint(HttpEndpointName).Property(EndpointProperty.Host)}:{this.GetEndpoint(HttpEndpointName).Property(EndpointProperty.Port)}");
}
