using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Draft;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Report;
using ATProtoNet.Serialization;

namespace ATProtoNet.Conformance.Legacy;

/// <summary>Where the 0.8 clients do not follow the Lexicon's shape one to one.</summary>
internal static class LegacyXrpcMap
{
    /// <summary>The client property of a Lexicon group, where it is not the capitalised group name.</summary>
    public static string Group(string group) => group switch
    {
        "ageassurance" => "AgeAssurance",
        _ => LegacyXrpc.Pascal(group),
    };

    /// <summary>The standard.site collections: their name in the method names of <c>client.Site</c>.</summary>
    public static IReadOnlyDictionary<string, (string Singular, string Plural)> SiteRecords { get; } = new Dictionary<string, (string, string)>
    {
        ["site.standard.publication"] = ("Publication", "Publications"),
        ["site.standard.document"] = ("Document", "Documents"),
        ["site.standard.graph.subscription"] = ("Subscription", "Subscriptions"),
        ["site.standard.graph.recommend"] = ("Recommendation", "Recommendations"),
    };

    private static readonly Dictionary<(string Nsid, string Name), string> Renames = new()
    {
        [("app.bsky.feed.searchPosts", "tag")] = "tags",
        [("app.bsky.graph.searchStarterPacksV2", "q")] = "query",
        [("chat.bsky.moderation.updateActorAccess", "ref")] = "reference",
    };

    /// <summary>The 0.8 parameter name of a Lexicon parameter.</summary>
    public static string Rename(string nsid, string name) => Renames.GetValueOrDefault((nsid, name), name);

    /// <summary>Methods whose 0.8 result is the one field of the Lexicon's output object: that field.</summary>
    public static IReadOnlyDictionary<string, string> Unwrapped { get; } = new Dictionary<string, string>
    {
        ["com.atproto.temp.dereferenceScope"] = "scope",
        ["chat.bsky.notification.getPreferences"] = "preferences",
        ["chat.bsky.notification.putPreferences"] = "preferences",
        ["app.bsky.notification.getPreferences"] = "preferences",
        ["app.bsky.notification.putPreferencesV2"] = "preferences",
        ["chat.bsky.convo.muteConvo"] = "convo",
        ["chat.bsky.convo.unmuteConvo"] = "convo",
        ["chat.bsky.convo.updateRead"] = "convo",
        ["chat.bsky.convo.removeReaction"] = "message",
        ["com.atproto.repo.uploadBlob"] = "blob",
        ["chat.bsky.convo.lockConvo"] = "convo",
        ["chat.bsky.convo.unlockConvo"] = "convo",
        ["chat.bsky.convo.addReaction"] = "message",
        ["chat.bsky.group.createGroup"] = "convo",
        ["chat.bsky.group.editGroup"] = "convo",
        ["chat.bsky.group.removeMembers"] = "convo",
        ["chat.bsky.group.approveJoinRequest"] = "convo",
        ["chat.bsky.group.createJoinLink"] = "joinLink",
        ["chat.bsky.group.editJoinLink"] = "joinLink",
        ["chat.bsky.group.enableJoinLink"] = "joinLink",
        ["chat.bsky.group.disableJoinLink"] = "joinLink",
        ["chat.bsky.moderation.getConvo"] = "convo",
    };

    /// <summary>Calls whose 0.8 signature is not the Lexicon's input flattened.</summary>
    public static IReadOnlyDictionary<string, Func<AtProtoClient, JsonObject, CancellationToken, Task<object?>>> Custom { get; } =
        new Dictionary<string, Func<AtProtoClient, JsonObject, CancellationToken, Task<object?>>>
        {
            // Filters travel as one object, flat on the wire.
            ["app.bsky.feed.searchPostsV2"] = (c, p, ct) => Box(c.Bsky.Feed.SearchPostsV2Async(
                p.Get<string>("query"), p.Get<PostSearchFilters>(), p.Get<string>("sort"), p.Get<int?>("limit"), p.Get<string>("cursor"), ct)),
            ["tools.ozone.report.queryReports"] = (c, p, ct) => Box(c.Ozone.Report.QueryReportsAsync(
                p.Get<string>("status")!, p.Get<ReportFilter>(), p.Get<int?>("limit"), p.Get<string>("cursor"), ct)),
            ["tools.ozone.moderation.queryStatuses"] = (c, p, ct) => Box(c.Ozone.Moderation.QueryStatusesAsync(
                p.Get<SubjectStatusFilter>(), p.Get<int?>("limit"), p.Get<string>("cursor"), ct)),

            ["com.atproto.repo.uploadBlob"] = (c, p, ct) => Box(c.Repo.UploadBlobAsync(
                Convert.FromBase64String(p["data"]!["$bytes"]!.GetValue<string>()), p.Get<string>("mimeType")!, ct)),

            // The draft travels as a draft with its id.
            ["app.bsky.draft.updateDraft"] = (c, p, ct) => Box(c.Bsky.Draft.UpdateDraftAsync(
                p.Obj("draft").Get<Tid>("id")!, p.Obj("draft").Get<Draft>("draft")!, ct)),
            ["app.bsky.notification.putActivitySubscription"] = (c, p, ct) => Box(c.Bsky.Notification.PutActivitySubscriptionAsync(
                p.Get<Did>("subject")!, p.Obj("activitySubscription").Get<bool>("post"), p.Obj("activitySubscription").Get<bool>("reply"), ct)),
            ["tools.ozone.report.createActivity"] = (c, p, ct) => Box(c.Ozone.Report.CreateActivityForEventAsync(
                p.Get<long>("eventId"), p.Get<ReportActivity>("activity")!, p.Get<string>("internalNote"), p.Get<string>("publicNote"), p.Get<bool?>("isAutomated"), ct)),
        };

    private static async Task<object?> Box<T>(Task<T> task) => await task;

    private static async Task<object?> Box(Task task)
    {
        await task;
        return null;
    }

    private static JsonObject Obj(this JsonObject json, string name) => json[name]!.AsObject();

    private static T? Get<T>(this JsonObject json) => json.Deserialize<T>(AtProtoJsonDefaults.Options);

    private static T? Get<T>(this JsonObject json, string name) => json[name].Deserialize<T>(AtProtoJsonDefaults.Options);
}
