using ATProtoNet.Http;
using ATProtoNet.Lexicon.Tools.Ozone.Communication;
using ATProtoNet.Lexicon.Tools.Ozone.Hosting;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Queue;
using ATProtoNet.Lexicon.Tools.Ozone.Report;
using ATProtoNet.Lexicon.Tools.Ozone.Safelink;
using ATProtoNet.Lexicon.Tools.Ozone.Server;
using ATProtoNet.Lexicon.Tools.Ozone.Set;
using ATProtoNet.Lexicon.Tools.Ozone.Setting;
using ATProtoNet.Lexicon.Tools.Ozone.Signature;
using ATProtoNet.Lexicon.Tools.Ozone.Team;
using ATProtoNet.Lexicon.Tools.Ozone.Verification;

namespace ATProtoNet.Lexicon.Tools.Ozone;

/// <summary>
/// Top-level client for tools.ozone.* endpoints (Ozone moderation service).
/// </summary>
public sealed class OzoneClient
{
    internal OzoneClient(XrpcClient xrpc)
    {
        Moderation = new ModerationClient(xrpc);
        Report = new ReportClient(xrpc);
        Queue = new QueueClient(xrpc);
        Communication = new CommunicationClient(xrpc);
        Team = new TeamClient(xrpc);
        Set = new SetClient(xrpc);
        Setting = new SettingClient(xrpc);
        Safelink = new SafelinkClient(xrpc);
        Verification = new VerificationClient(xrpc);
        Hosting = new HostingClient(xrpc);
        Server = new OzoneServerClient(xrpc);
        Signature = new SignatureClient(xrpc);
    }

    /// <summary>
    /// Moderation event and subject management.
    /// </summary>
    public ModerationClient Moderation { get; }

    /// <summary>
    /// Individual reports: querying, assignment, activities, closing and statistics.
    /// </summary>
    public ReportClient Report { get; }

    /// <summary>
    /// Moderation queues, report routing and queue assignments.
    /// </summary>
    public QueueClient Queue { get; }

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
    /// Instance and personal settings.
    /// </summary>
    public SettingClient Setting { get; }

    /// <summary>
    /// URL safety rules and their audit log.
    /// </summary>
    public SafelinkClient Safelink { get; }

    /// <summary>
    /// Verifications the Ozone service issues.
    /// </summary>
    public VerificationClient Verification { get; }

    /// <summary>
    /// Account history from the account's host.
    /// </summary>
    public HostingClient Hosting { get; }

    /// <summary>
    /// Ozone server configuration.
    /// </summary>
    public OzoneServerClient Server { get; }

    /// <summary>
    /// Signature correlation and related account discovery.
    /// </summary>
    public SignatureClient Signature { get; }
}
