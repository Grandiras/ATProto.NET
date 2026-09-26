using ATProtoNet.Identity;

namespace ATProtoNet.Server.Xrpc;

/// <summary>
/// Endpoint metadata naming the XRPC method an endpoint serves.
/// </summary>
/// <remarks>
/// <see cref="XrpcEndpointExtensions.MapXrpcEndpoints"/> attaches it to every endpoint it maps,
/// which is how <c>AddAtProtoServiceAuth()</c> binds a token's <c>lxm</c> to the method called.
/// Attach it yourself — <c>.WithMetadata(new XrpcMethodMetadata(nsid))</c> — to an XRPC route you
/// map by hand.
/// </remarks>
public sealed class XrpcMethodMetadata
{
    /// <summary>Creates the metadata.</summary>
    /// <param name="nsid">The method the endpoint serves.</param>
    public XrpcMethodMetadata(Nsid nsid)
    {
        ArgumentNullException.ThrowIfNull(nsid);
        Nsid = nsid;
    }

    /// <summary>The method the endpoint serves.</summary>
    public Nsid Nsid { get; }
}
