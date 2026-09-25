namespace ATProtoNet.Tests.Lexicon.Upstream;

using AtProto = ATProtoNet.Lexicon.Com.AtProto;
using Bsky = ATProtoNet.Lexicon.App.Bsky;
using Chat = ATProtoNet.Lexicon.Chat.Bsky;
using DataModel = ATProtoNet.Models;
using Ozone = ATProtoNet.Lexicon.Tools.Ozone;
using Site = ATProtoNet.Lexicon.Site.Standard;

public partial class LexiconDriftTests
{
    /// <summary>
    /// The upstream def of each model whose name does not lead to it (see
    /// <see cref="ResolveByConvention"/>). A reference is <c>nsid#def</c>, a record's
    /// <c>nsid</c>, or a method's <c>nsid#input</c> / <c>nsid#output</c> / <c>nsid#params</c>.
    /// </summary>
    private static readonly Dictionary<Type, string> DefOf = new()
    {
        [typeof(DataModel.StrongRef)] = "com.atproto.repo.strongRef",
        [typeof(DataModel.Label)] = "com.atproto.label.defs#label",

        [typeof(Bsky.Actor.LabelerPreferenceItem)] = "app.bsky.actor.defs#labelerPrefItem",
        [typeof(Bsky.Embed.EmbedImage)] = "app.bsky.embed.images#image",
        [typeof(Bsky.Embed.ImageViewItem)] = "app.bsky.embed.images#viewImage",
        [typeof(Bsky.Embed.ExternalInfo)] = "app.bsky.embed.external#external",
        [typeof(Bsky.Embed.ExternalViewInfo)] = "app.bsky.embed.external#viewExternal",
        [typeof(Bsky.Embed.ExternalViewSource)] = "app.bsky.embed.external#viewExternalSource",
        [typeof(Bsky.Embed.ExternalViewSourceTheme)] = "app.bsky.embed.external#viewExternalSourceTheme",
        [typeof(Bsky.Embed.ColorRgb)] = "app.bsky.embed.external#colorRGB",
        [typeof(Bsky.Embed.VideoCaption)] = "app.bsky.embed.video#caption",
        [typeof(Bsky.Feed.ReplyRef)] = "app.bsky.feed.post#replyRef",
        [typeof(Bsky.Feed.SelfLabelValue)] = "com.atproto.label.defs#selfLabel",
        [typeof(Bsky.Feed.PostViewerState)] = "app.bsky.feed.defs#viewerState",
        [typeof(Bsky.Feed.FeedReplyRef)] = "app.bsky.feed.defs#replyRef",
        // getTimeline, getAuthorFeed, getFeed and getListFeed share one output shape.
        [typeof(Bsky.Feed.FeedResponse)] = "app.bsky.feed.getTimeline#output",
        [typeof(Bsky.Feed.LikeInfo)] = "app.bsky.feed.getLikes#like",
        [typeof(Bsky.Feed.DescribeFeedGeneratorFeed)] = "app.bsky.feed.describeFeedGenerator#feed",
        [typeof(Bsky.Graph.StarterPackFeedItem)] = "app.bsky.graph.starterpack#feedItem",
        [typeof(Bsky.Labeler.LabelValueDefinition)] = "com.atproto.label.defs#labelValueDefinition",
        [typeof(Bsky.Labeler.LabelValueDefinitionStrings)] = "com.atproto.label.defs#labelValueDefinitionStrings",
        [typeof(Bsky.Labeler.GetLabelerServicesResponse)] = "app.bsky.labeler.getServices#output",
        [typeof(Bsky.Notification.NotificationView)] = "app.bsky.notification.listNotifications#notification",
        [typeof(Bsky.RichText.FacetIndex)] = "app.bsky.richtext.facet#byteSlice",

        [typeof(Chat.Convo.ChatMemberView)] = "chat.bsky.actor.defs#profileViewBasic",
        [typeof(Chat.Convo.MessageSender)] = "chat.bsky.convo.defs#messageViewSender",
        [typeof(Chat.Convo.BatchMessageItem)] = "chat.bsky.convo.sendMessageBatch#batchItem",
        // A loose view over the getLog union: rev, convoId and message are what its variants share.
        [typeof(Chat.Convo.ConvoLogEntry)] = "chat.bsky.convo.defs#logCreateMessage",
        // muteConvo, unmuteConvo and updateRead share this output; so do addReaction and removeReaction.
        [typeof(Chat.Convo.ConvoOutput)] = "chat.bsky.convo.muteConvo#output",
        [typeof(Chat.Convo.MessageOutput)] = "chat.bsky.convo.addReaction#output",

        [typeof(AtProto.Admin.AccountInfo)] = "com.atproto.admin.defs#accountView",
        [typeof(AtProto.Admin.SubjectStatusDetail)] = "com.atproto.admin.defs#statusAttr",
        [typeof(AtProto.Admin.AdminDeleteAccountRequest)] = "com.atproto.admin.deleteAccount#input",
        [typeof(AtProto.Label.LabelsEvent)] = "com.atproto.label.subscribeLabels#labels",
        [typeof(AtProto.Repo.GetRecordResponse<>)] = "com.atproto.repo.getRecord#output",
        [typeof(AtProto.Repo.RecordEntry)] = "com.atproto.repo.listRecords#record",
        // One model for the three result variants; createResult declares all of its fields.
        [typeof(AtProto.Repo.ApplyWriteResult)] = "com.atproto.repo.applyWrites#createResult",
        [typeof(AtProto.Repo.MissingBlob)] = "com.atproto.repo.listMissingBlobs#recordBlob",
        // createSession, refreshSession and getSession share one output model.
        [typeof(AtProto.Server.SessionResponse)] = "com.atproto.server.createSession#output",
        [typeof(AtProto.Server.ServerLinks)] = "com.atproto.server.describeServer#links",
        [typeof(AtProto.Server.ServerContact)] = "com.atproto.server.describeServer#contact",
        [typeof(AtProto.Server.AppPassword)] = "com.atproto.server.createAppPassword#appPassword",
        [typeof(AtProto.Server.AppPasswordInfo)] = "com.atproto.server.listAppPasswords#appPassword",
        [typeof(AtProto.Sync.RepoInfo)] = "com.atproto.sync.listRepos#repo",
        [typeof(AtProto.Sync.HostInfo)] = "com.atproto.sync.listHosts#host",
        [typeof(AtProto.Sync.CollectionRepoInfo)] = "com.atproto.sync.listReposByCollection#repo",
        [typeof(AtProto.Sync.CommitEvent)] = "com.atproto.sync.subscribeRepos#commit",
        [typeof(AtProto.Sync.SyncEvent)] = "com.atproto.sync.subscribeRepos#sync",
        [typeof(AtProto.Sync.IdentityEvent)] = "com.atproto.sync.subscribeRepos#identity",
        [typeof(AtProto.Sync.AccountEvent)] = "com.atproto.sync.subscribeRepos#account",
        [typeof(AtProto.Sync.InfoEvent)] = "com.atproto.sync.subscribeRepos#info",

        [typeof(Site.Document.DocumentContributor)] = "site.standard.document#contributor",
        [typeof(Site.Publication.PublicationPreferences)] = "site.standard.publication#preferences",

        [typeof(Ozone.Communication.CommunicationTemplateView)] = "tools.ozone.communication.defs#templateView",
        [typeof(Ozone.Server.OzoneServerConfig)] = "tools.ozone.server.getConfig#output",
        [typeof(Ozone.Server.OzoneViewerConfig)] = "tools.ozone.server.getConfig#viewerConfig",
        [typeof(Ozone.Set.OzoneSetView)] = "tools.ozone.set.defs#setView",
        [typeof(Ozone.Team.TeamMember)] = "tools.ozone.team.defs#member",
        [typeof(Ozone.Safelink.SafelinkEvent)] = "tools.ozone.safelink.defs#event",
        [typeof(Ozone.Setting.SettingOption)] = "tools.ozone.setting.defs#option",
        [typeof(Ozone.Hosting.AccountHistoryEvent)] = "tools.ozone.hosting.getAccountHistory#event",
    };

    /// <summary>
    /// Models that are not an upstream object, and why.
    /// </summary>
    private static readonly Dictionary<Type, string> NotLexiconObjects = new()
    {
        [typeof(DataModel.BlobRef)] = "The data model's blob, not a Lexicon def.",
        [typeof(DataModel.BlobLink)] = "The data model's CID link, not a Lexicon def.",
        [typeof(DataModel.CidLink)] = "The data model's CID link, not a Lexicon def.",
        [typeof(AtProto.Identity.DidService)] =
            "A did:plc service entry; the Lexicon types a PLC operation's services as unknown.",
        [typeof(AtProto.Sync.FirehoseMessage)] =
            "The base of the subscribeRepos message variants, which are checked one by one.",
        [typeof(AtProto.Sync.HandleEvent)] =
            "Removed from subscribeRepos upstream in 2025; the firehose models are reworked by #126.",
        [typeof(AtProto.Sync.TombstoneEvent)] =
            "Removed from subscribeRepos upstream in 2025; the firehose models are reworked by #126.",
    };

    /// <summary>
    /// JSON names a model uses that its upstream def does not declare, keyed
    /// <c>Namespace.Type.jsonName</c> (without the <c>ATProtoNet.</c> prefix), and why.
    /// </summary>
    private static readonly Dictionary<string, string> UnknownProperties = new(StringComparer.Ordinal)
    {
        ["Lexicon.Com.AtProto.Sync.InfoEvent.seq"] = FirehoseBase,
        ["Lexicon.Com.AtProto.Sync.InfoEvent.time"] = FirehoseBase,
    };

    private const string FirehoseBase =
        "FirehoseMessage gives every variant seq and time, which #info lacks; reworked by #126.";

    /// <summary>
    /// Upstream properties a model deliberately does not declare, keyed like
    /// <see cref="UnknownProperties"/>, and why. They still round-trip through
    /// <c>LexObject.ExtensionData</c>.
    /// </summary>
    private static readonly Dictionary<string, string> OmittedProperties = new(StringComparer.Ordinal)
    {
        ["Lexicon.App.Bsky.Feed.PostRecord.entities"] = "Deprecated upstream: replaced by facets.",
        ["Lexicon.App.Bsky.Actor.GetSuggestionsResponse.recId"] = "Deprecated upstream: use recIdStr.",
        ["Lexicon.App.Bsky.Graph.GetSuggestedFollowsByActorResponse.recId"] = "Deprecated upstream: use recIdStr.",

        // Group chats, reactions and replies: typed views, each with an open union, come with #128.
        ["Lexicon.Chat.Bsky.Convo.ConvoView.kind"] = GroupChats,
        ["Lexicon.Chat.Bsky.Convo.ConvoView.lastReaction"] = GroupChats,
        ["Lexicon.Chat.Bsky.Convo.ChatMemberView.kind"] = GroupChats,
        ["Lexicon.Chat.Bsky.Convo.MessageView.reactions"] = GroupChats,
        ["Lexicon.Chat.Bsky.Convo.MessageView.replyTo"] = GroupChats,
        ["Lexicon.Chat.Bsky.Convo.MessageInput.replyTo"] = GroupChats,
        ["Lexicon.Chat.Bsky.Convo.ConvoLogEntry.relatedProfiles"] = GroupChats,
    };

    private const string GroupChats = "Group chats and the typed chat views are #128.";

    /// <summary>
    /// Constants in a knownValues class that upstream does not list, keyed <c>Class:value</c>,
    /// and why. An <see cref="ObsoleteAttribute"/> constant needs no entry.
    /// </summary>
    private static readonly Dictionary<string, string> ExtraKnownValues = new(StringComparer.Ordinal)
    {
        ["StandardLabelValues:spam"] = BlueskyModeration,
        ["StandardLabelValues:impersonation"] = BlueskyModeration,
        ["StandardLabelValues:misleading"] = BlueskyModeration,
    };

    private const string BlueskyModeration =
        "Not a global label value, but one Bluesky's moderation labeler (mod.bsky.app) applies.";

    /// <summary>
    /// Upstream knownValues a constant class deliberately lacks, keyed <c>Class:value</c>; a key
    /// ending in <c>*</c> matches a prefix. And why.
    /// </summary>
    private static readonly Dictionary<string, string> MissingKnownValues = new(StringComparer.Ordinal);

    /// <summary>The rows of <see cref="KnownValueLocations"/>, for the theory.</summary>
    public static TheoryData<string, string> KnownValueClasses
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (constantsClass, location) in KnownValueLocations)
                data.Add(constantsClass, location);
            return data;
        }
    }

    /// <summary>
    /// Each knownValues constant class and where upstream lists its values (see
    /// <see cref="KnownValuesAt"/>).
    /// </summary>
    private static readonly (string Class, string Location)[] KnownValueLocations =
    [
        ("ATProtoNet.Lexicon.App.Bsky.Actor.VerificationStatus", "app.bsky.actor.defs#verificationState:verifiedStatus"),
        ("ATProtoNet.Lexicon.App.Bsky.Embed.VideoPresentation", "app.bsky.embed.video:presentation"),
        ("ATProtoNet.Lexicon.App.Bsky.Feed.FeedContentMode", "app.bsky.feed.defs#generatorView:contentMode"),
        ("ATProtoNet.Lexicon.App.Bsky.Graph.ListPurpose", "app.bsky.graph.defs#listPurpose"),
        ("ATProtoNet.Lexicon.App.Bsky.Labeler.LabelBlurs", "com.atproto.label.defs#labelValueDefinition:blurs"),
        ("ATProtoNet.Lexicon.App.Bsky.Labeler.LabelDefaultSetting", "com.atproto.label.defs#labelValueDefinition:defaultSetting"),
        ("ATProtoNet.Lexicon.App.Bsky.Labeler.LabelSeverity", "com.atproto.label.defs#labelValueDefinition:severity"),
        ("ATProtoNet.Lexicon.App.Bsky.Labeler.StandardLabelValues", "com.atproto.label.defs#labelValue"),
        ("ATProtoNet.Lexicon.App.Bsky.Notification.NotificationReasons", "app.bsky.notification.listNotifications#notification:reason"),
        ("ATProtoNet.Lexicon.App.Bsky.Video.JobFailureCode", "app.bsky.video.defs#jobStatus:failureCode"),
        ("ATProtoNet.Lexicon.App.Bsky.Video.JobState", "app.bsky.video.defs#jobStatus:state"),
        ("ATProtoNet.Lexicon.Chat.Bsky.Actor.ChatAllowIncoming", "chat.bsky.actor.declaration:allowIncoming"),
        ("ATProtoNet.Lexicon.Com.AtProto.Moderation.ReportReasons", "com.atproto.moderation.defs#reasonType"),
        ("ATProtoNet.Lexicon.Com.AtProto.Sync.AccountHostingStatus", "com.atproto.sync.getRepoStatus#output:status"),
        ("ATProtoNet.Lexicon.Com.AtProto.Sync.HostStatus", "com.atproto.sync.defs#hostStatus"),
        ("ATProtoNet.Lexicon.Tools.Ozone.Hosting.AccountHistoryEventType", "tools.ozone.hosting.getAccountHistory#params:events"),
        ("ATProtoNet.Lexicon.Tools.Ozone.Moderation.ScheduledActionStatus", "tools.ozone.moderation.defs#scheduledActionView:status"),
        ("ATProtoNet.Lexicon.Tools.Ozone.Moderation.SubjectReviewState", "tools.ozone.moderation.defs#subjectReviewState"),
        ("ATProtoNet.Lexicon.Tools.Ozone.Report.ReportStatus", "tools.ozone.report.defs#reportView:status"),
        ("ATProtoNet.Lexicon.Tools.Ozone.Report.ReportSubjectType", "tools.ozone.report.queryReports#params:subjectType"),
        ("ATProtoNet.Lexicon.Tools.Ozone.Safelink.SafelinkActionType", "tools.ozone.safelink.defs#actionType"),
        ("ATProtoNet.Lexicon.Tools.Ozone.Safelink.SafelinkEventType", "tools.ozone.safelink.defs#eventType"),
        ("ATProtoNet.Lexicon.Tools.Ozone.Safelink.SafelinkPatternType", "tools.ozone.safelink.defs#patternType"),
        ("ATProtoNet.Lexicon.Tools.Ozone.Safelink.SafelinkReasonType", "tools.ozone.safelink.defs#reasonType"),
        ("ATProtoNet.Lexicon.Tools.Ozone.Setting.SettingScope", "tools.ozone.setting.defs#option:scope"),
        ("ATProtoNet.Lexicon.Tools.Ozone.Team.TeamMemberRole", "tools.ozone.team.defs#member:role"),
    ];
}
