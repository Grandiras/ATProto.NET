namespace ATProtoNet.Server.Xrpc;

/// <summary>
/// Marks a class as an XRPC endpoint handler. Used for assembly scanning with
/// <see cref="XrpcEndpointExtensions.AddXrpcEndpointsFromAssembly"/>.
/// The class must implement one of the XRPC endpoint interfaces.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class XrpcEndpointAttribute : Attribute
{
    /// <summary>
    /// The Lexicon NSID of the endpoint (e.g., "com.example.myQuery").
    /// If not specified, the NSID is taken from the <see cref="IXrpcEndpoint.Nsid"/> property.
    /// </summary>
    public string? Nsid { get; init; }
}
