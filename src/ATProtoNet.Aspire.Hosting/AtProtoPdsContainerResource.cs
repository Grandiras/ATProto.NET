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
    : AtProtoPdsContainerResourceBase(name, jwtSecret)
{
    /// <summary>
    /// The admin password for this PDS, used for HTTP Basic authentication against
    /// <c>com.atproto.admin.*</c>. Pass it to <c>PdsAdminClient</c> (core <c>ATProtoNet</c>
    /// package) to administer the server programmatically.
    /// </summary>
    public ParameterResource AdminPasswordParameter { get; internal set; } = adminPassword;

    /// <summary>
    /// The hex-encoded secp256k1 private key this PDS uses as its PLC rotation key.
    /// </summary>
    /// <remarks>
    /// Changing this key strands the <c>did:plc</c> identities already created on the
    /// server, which is why it is persisted to the AppHost's user secrets rather than
    /// regenerated on every run.
    /// </remarks>
    public ParameterResource PlcRotationKeyParameter { get; internal set; } = plcRotationKey;
}
