using ATProtoNet.Http;
using ATProtoNet.Lexicon.Tools.Ozone.Communication;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Server;
using ATProtoNet.Lexicon.Tools.Ozone.Set;
using ATProtoNet.Lexicon.Tools.Ozone.Signature;
using ATProtoNet.Lexicon.Tools.Ozone.Team;

namespace ATProtoNet.Lexicon.Tools.Ozone;

/// <summary>
/// Top-level client for tools.ozone.* endpoints (Ozone moderation service).
/// </summary>
public sealed class OzoneClient
{
    internal OzoneClient(XrpcClient xrpc)
    {
        Moderation = new ModerationClient(xrpc);
        Communication = new CommunicationClient(xrpc);
        Team = new TeamClient(xrpc);
        Set = new SetClient(xrpc);
        Server = new OzoneServerClient(xrpc);
        Signature = new SignatureClient(xrpc);
    }

    /// <summary>
    /// Moderation event and subject management.
    /// </summary>
    public ModerationClient Moderation { get; }

    /// <summary>
    /// Communication template management.
    /// </summary>
    public CommunicationClient Communication { get; }

    /// <summary>
    /// Team member management.
    /// </summary>
    public TeamClient Team { get; }

    /// <summary>
    /// Named set (rule value) management.
    /// </summary>
    public SetClient Set { get; }

    /// <summary>
    /// Ozone server configuration.
    /// </summary>
    public OzoneServerClient Server { get; }

    /// <summary>
    /// Signature correlation and related account discovery.
    /// </summary>
    public SignatureClient Signature { get; }
}
