using System.Text;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Actions for repository record permissions.
/// </summary>
[Flags]
public enum RepoAction
{
    /// <summary>
    /// No specific action. Rejected by <see cref="AtProtoScopes.Repo(string, RepoAction)"/> and
    /// <see cref="AtProtoScopes.Repo(IReadOnlyList{string}, RepoAction)"/> — an omitted action
    /// list means <see cref="All"/>, so a zero-action grant cannot be expressed.
    /// </summary>
    None = 0,

    /// <summary>Permission to create records.</summary>
    Create = 1,

    /// <summary>Permission to update records.</summary>
    Update = 2,

    /// <summary>Permission to delete records.</summary>
    Delete = 4,

    /// <summary>All actions (create, update, delete). This is the default when no actions are specified.</summary>
    All = Create | Update | Delete,
}

/// <summary>
/// Actions for account attribute permissions.
/// </summary>
public enum AccountAction
{
    /// <summary>Read-only access to the account attribute.</summary>
    Read,

    /// <summary>Full read and write access to the account attribute.</summary>
    Manage,
}

/// <summary>
/// Actions for identity attribute permissions.
/// </summary>
public enum IdentityAction
{
    /// <summary>Full control over the identity attribute.</summary>
    Manage,

    /// <summary>Submit-only access to the identity attribute.</summary>
    Submit,
}

/// <summary>
/// Actions a <c>space:</c> permission grants over the <em>records</em> in a space.
/// </summary>
/// <remarks>
/// Read access is all-or-nothing at the space boundary — there is no partial, per-record,
/// per-collection, or per-author read grant — so <see cref="Read"/> and
/// <see cref="ReadSelf"/> ignore the collection list, while the write actions are constrained
/// by it.
/// </remarks>
[Flags]
public enum SpaceAction
{
    /// <summary>
    /// No specific action. Rejected by <see cref="AtProtoScopes.Space"/> — an omitted action
    /// list means <see cref="All"/>, so a zero-action grant cannot be expressed.
    /// </summary>
    None = 0,

    /// <summary>
    /// Read the holder's <b>own</b> repo in the space, and nothing else in it.
    /// </summary>
    /// <remarks>
    /// The narrower read grant. It confers the read and sync methods for the holder's own repo
    /// but <b>not</b> <c>getDelegationToken</c>, so an application holding only this cannot
    /// obtain a space credential and cannot reach the rest of the space — suitable for a
    /// personal export or backup tool that should not see other members' data.
    /// </remarks>
    ReadSelf = 1,

    /// <summary>
    /// Read the whole space.
    /// </summary>
    /// <remarks>
    /// Confers the read and sync methods on the holder's own PDS <em>and</em> access to
    /// <c>getDelegationToken</c>, which an application exchanges for the space credential that
    /// reads every member's repo. Implies <see cref="ReadSelf"/>.
    /// </remarks>
    Read = 2,

    /// <summary>Create records in the granted collections.</summary>
    Create = 4,

    /// <summary>Update records in the granted collections.</summary>
    Update = 8,

    /// <summary>Delete records in the granted collections.</summary>
    Delete = 16,

    /// <summary>
    /// The default grant: read the space, and create, update, and delete records in it.
    /// <see cref="ReadSelf"/> is omitted because <see cref="Read"/> already implies it.
    /// </summary>
    All = Read | Create | Update | Delete,
}

/// <summary>
/// Operations a <c>space:</c> permission grants over the <em>spaces themselves</em>, as opposed
/// to the records in them.
/// </summary>
/// <remarks>
/// <para>Omitted by default, so an ordinary record-access grant confers no administrative
/// capability at all.</para>
/// <para>The protocol does not enumerate what each verb permits, because space management is
/// implementation-defined; each implementation maps the verbs onto its own administrative
/// surface. In <c>com.atproto.simplespace</c>, for instance, <see cref="Update"/> authorizes
/// <c>updateSpace</c> as well as <c>addMember</c> and <c>removeMember</c>.</para>
/// </remarks>
[Flags]
public enum SpaceManage
{
    /// <summary>No management capability.</summary>
    None = 0,

    /// <summary>
    /// Create spaces of the granted type under the granted authority.
    /// </summary>
    /// <remarks>
    /// Unlike every other operation this concerns a space that does not yet exist, so scoping it
    /// to a concrete space key is unusual — it is typically granted with the key left wild.
    /// </remarks>
    Create = 1,

    /// <summary>Update the granted spaces' configuration.</summary>
    Update = 2,

    /// <summary>Delete the granted spaces.</summary>
    Delete = 4,
}

/// <summary>
/// Well-known OAuth scope values and granular permission builders for the AT Protocol.
/// See <see href="https://atproto.com/specs/oauth#authorization-scopes">AT Protocol OAuth Scopes</see>
/// and <see href="https://atproto.com/specs/permission">AT Protocol Permissions</see>.
/// </summary>
public static class AtProtoScopes
{
    // ─── Transitional scope constants ───────────────────────────────────

    /// <summary>
    /// Required scope for all atproto OAuth sessions. Confirms the client uses the atproto profile of OAuth.
    /// Inclusion of this scope is mandatory; sessions will be rejected without it.
    /// </summary>
    public const string AtProto = "atproto";

    /// <summary>
    /// Broad PDS account permissions, equivalent to the previous "App Password" authorization level.
    /// Includes: write any repository record type, upload blobs, read/write preferences,
    /// API proxying for most Lexicons, and service auth token generation.
    /// Does NOT include: account management (change handle/email, delete/deactivate/migrate account)
    /// or DM access (<c>chat.bsky.*</c> Lexicons).
    /// </summary>
    public const string TransitionGeneric = "transition:generic";

    /// <summary>
    /// Access to Bluesky DM (Direct Message) Lexicons (<c>chat.bsky.*</c>).
    /// This scope depends on and does not function without <see cref="TransitionGeneric"/>.
    /// </summary>
    public const string TransitionChatBsky = "transition:chat.bsky";

    /// <summary>
    /// Access to the account email address and confirmation status via
    /// <c>com.atproto.server.getSession</c>.
    /// </summary>
    public const string TransitionEmail = "transition:email";

    /// <summary>
    /// Default scope string: <c>"atproto transition:generic"</c>.
    /// Suitable for most applications that need to read/write records and upload blobs.
    /// </summary>
    public const string Default = $"{AtProto} {TransitionGeneric}";

    /// <summary>
    /// Full scope string including DM access: <c>"atproto transition:generic transition:chat.bsky"</c>.
    /// Use this when your application needs access to Bluesky Direct Messages.
    /// </summary>
    public const string WithChat = $"{AtProto} {TransitionGeneric} {TransitionChatBsky}";

    /// <summary>
    /// Minimal scope string: <c>"atproto"</c>.
    /// Use this for authentication-only clients that don't need to access PDS resources
    /// (e.g., "Login with AT Protocol" identity verification).
    /// </summary>
    public const string AuthOnly = AtProto;

    // ─── Granular permission builders ───────────────────────────────────

    /// <summary>
    /// Constructs a <c>repo</c> permission scope for a single record collection.
    /// <para>Example: <c>AtProtoScopes.Repo("app.bsky.feed.post")</c> → <c>"repo:app.bsky.feed.post"</c></para>
    /// <para>Example: <c>AtProtoScopes.Repo("app.bsky.feed.post", RepoAction.Create | RepoAction.Delete)</c>
    /// → <c>"repo:app.bsky.feed.post?action=create&amp;action=delete"</c></para>
    /// </summary>
    /// <param name="collection">The record collection NSID, or <c>"*"</c> for all record types.</param>
    /// <param name="actions">
    /// The permitted actions. Defaults to all actions (create, update, delete).
    /// <see cref="RepoAction.None"/> is rejected — the scope grammar has no way to say "no
    /// actions", and an omitted action list means the full default set, so a zero-action grant
    /// cannot be expressed.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="collection"/> is empty, or <paramref name="actions"/> is
    /// <see cref="RepoAction.None"/>.
    /// </exception>
    public static string Repo(string collection, RepoAction actions = RepoAction.All)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);

        var sb = new StringBuilder("repo:");
        sb.Append(collection);
        AppendRepoActions(sb, actions, hasExistingParams: false);
        return sb.ToString();
    }

    /// <summary>
    /// Constructs a <c>repo</c> permission scope for multiple record collections.
    /// <para>Example: <c>AtProtoScopes.Repo(["app.bsky.feed.post", "app.bsky.feed.like"])</c>
    /// → <c>"repo?collection=app.bsky.feed.post&amp;collection=app.bsky.feed.like"</c></para>
    /// </summary>
    /// <param name="collections">The record collection NSIDs.</param>
    /// <param name="actions">
    /// The permitted actions. Defaults to all actions (create, update, delete).
    /// <see cref="RepoAction.None"/> is rejected — the scope grammar has no way to say "no
    /// actions", and an omitted action list means the full default set, so a zero-action grant
    /// cannot be expressed.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="collections"/> is empty, or <paramref name="actions"/> is
    /// <see cref="RepoAction.None"/>.
    /// </exception>
    public static string Repo(IReadOnlyList<string> collections, RepoAction actions = RepoAction.All)
    {
        ArgumentNullException.ThrowIfNull(collections);
        if (collections.Count == 0)
            throw new ArgumentException("At least one collection is required.", nameof(collections));
        if (collections.Count == 1)
            return Repo(collections[0], actions);

        var sb = new StringBuilder("repo?");
        for (var i = 0; i < collections.Count; i++)
        {
            if (i > 0) sb.Append('&');
            sb.Append("collection=").Append(collections[i]);
        }

        AppendRepoActions(sb, actions, hasExistingParams: true);
        return sb.ToString();
    }

    /// <summary>
    /// Constructs a <c>space:</c> permission scope, granting access to a set of permissioned
    /// spaces and stating what the grant permits within them.
    /// </summary>
    /// <param name="spaceType">The space type NSID, or <c>"*"</c> for any type.</param>
    /// <param name="authority">
    /// The space authority DID, <c>"self"</c> for the granting user's own DID, or <c>"*"</c> for
    /// any authority. Defaults to <c>"self"</c>, so a bare grant covers only the user's own
    /// spaces of that type — reaching a forum or group anchored elsewhere requires naming its
    /// authority, or <c>"*"</c>.
    /// </param>
    /// <param name="skey">The space key, or <c>"*"</c> (the default) for any key.</param>
    /// <param name="collections">
    /// The record collections the write actions may target, or <c>"*"</c> for any. Defaults to
    /// the collections the space type's own declaration lists — the same way a bare
    /// <c>repo:</c> scope permits the collections it names.
    /// </param>
    /// <param name="actions">
    /// The permitted record actions. Defaults to <see cref="SpaceAction.All"/>: read the space,
    /// and create, update, and delete records in it. <see cref="SpaceAction.None"/> is rejected —
    /// the scope grammar has no way to say "no record actions", and an omitted action list means
    /// the full default set, so a zero-action grant cannot be expressed.
    /// </param>
    /// <param name="manage">
    /// The permitted space-management operations. None by default.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="spaceType"/> is empty, or <paramref name="actions"/> is
    /// <see cref="SpaceAction.None"/>.
    /// </exception>
    /// <remarks>
    /// <para>The default collection set is resolved from the space type's declaration as it
    /// stands when the grant is <em>evaluated</em>, not frozen at consent time. If the
    /// declaration later adds a collection, existing bare grants widen to include it. An
    /// application that does not want its authorized collections to move with the declaration
    /// should enumerate them explicitly.</para>
    /// <para>A scope requesting wildcards on both <paramref name="spaceType"/> and
    /// <paramref name="authority"/> is a very broad grant, and consent screens are expected to
    /// warn about it prominently.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // The user's own bookmarks — the typical personal-data grant.
    /// AtProtoScopes.Space("com.example.bookmarks");
    /// // → "space:com.example.bookmarks"
    ///
    /// // Every forum the user is in, under any authority, read-only.
    /// AtProtoScopes.Space("com.atmoboards.forum", authority: "*", actions: SpaceAction.Read);
    /// // → "space:com.atmoboards.forum?authority=*&amp;action=read"
    ///
    /// // Administer the user's own forums without reading other members' records.
    /// AtProtoScopes.Space(
    ///     "com.atmoboards.forum",
    ///     actions: SpaceAction.ReadSelf,
    ///     manage: SpaceManage.Update | SpaceManage.Delete);
    /// // → "space:com.atmoboards.forum?action=read_self&amp;manage=update&amp;manage=delete"
    /// </code>
    /// </example>
    public static string Space(
        string spaceType,
        string? authority = null,
        string? skey = null,
        IReadOnlyList<string>? collections = null,
        SpaceAction actions = SpaceAction.All,
        SpaceManage manage = SpaceManage.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceType);

        // An omitted action list means SpaceAction.All, so emitting nothing for None would hand
        // back a full read/write grant — the opposite of what was asked for. The grammar has no
        // marker for an empty action list, so the request is inexpressible rather than narrow.
        if (actions is SpaceAction.None)
        {
            throw new ArgumentException(
                "SpaceAction.None cannot be expressed: an omitted action list means SpaceAction.All, " +
                "so a zero-action grant would widen to full read/write. Use SpaceAction.ReadSelf for " +
                "the narrowest record grant.",
                nameof(actions));
        }

        var sb = new StringBuilder("space:").Append(spaceType);
        var hasParams = false;

        // Each parameter is emitted only when it differs from the scope grammar's own default,
        // so the common grants stay short enough to read on a consent screen.
        if (authority is not null && authority != "self")
            AppendParam(sb, ref hasParams, "authority", EncodeScopeValue(authority));

        if (skey is not null && skey != "*")
            AppendParam(sb, ref hasParams, "skey", skey);

        if (collections is not null)
        {
            foreach (var collection in NormalizeCollections(collections))
                AppendParam(sb, ref hasParams, "collection", collection);
        }

        if (actions is not SpaceAction.All)
        {
            // Declaration order, which is the order the scope grammar normalizes to.
            if (actions.HasFlag(SpaceAction.ReadSelf))
                AppendParam(sb, ref hasParams, "action", "read_self");
            if (actions.HasFlag(SpaceAction.Read))
                AppendParam(sb, ref hasParams, "action", "read");
            if (actions.HasFlag(SpaceAction.Create))
                AppendParam(sb, ref hasParams, "action", "create");
            if (actions.HasFlag(SpaceAction.Update))
                AppendParam(sb, ref hasParams, "action", "update");
            if (actions.HasFlag(SpaceAction.Delete))
                AppendParam(sb, ref hasParams, "action", "delete");
        }

        if (manage.HasFlag(SpaceManage.Create))
            AppendParam(sb, ref hasParams, "manage", "create");
        if (manage.HasFlag(SpaceManage.Update))
            AppendParam(sb, ref hasParams, "manage", "update");
        if (manage.HasFlag(SpaceManage.Delete))
            AppendParam(sb, ref hasParams, "manage", "delete");

        return sb.ToString();
    }

    /// <summary>
    /// A wildcard <c>collection</c> absorbs the rest; otherwise the grammar normalizes the list
    /// to a sorted, de-duplicated set.
    /// </summary>
    private static IEnumerable<string> NormalizeCollections(IReadOnlyList<string> collections)
    {
        if (collections.Contains("*"))
            return ["*"];
        if (collections.Count <= 1)
            return collections;

        return [.. new SortedSet<string>(collections, StringComparer.Ordinal)];
    }

    private static void AppendParam(StringBuilder sb, ref bool hasParams, string name, string value)
    {
        sb.Append(hasParams ? '&' : '?').Append(name).Append('=').Append(value);
        hasParams = true;
    }

    /// <summary>
    /// Constructs an <c>rpc</c> permission scope for a single API endpoint (Lexicon method).
    /// <para>Example: <c>AtProtoScopes.Rpc("app.bsky.feed.searchPosts", "did:web:api.bsky.app#bsky_appview")</c>
    /// → <c>"rpc:app.bsky.feed.searchPosts?aud=did:web:api.bsky.app%23bsky_appview"</c></para>
    /// </summary>
    /// <param name="lxm">The Lexicon method NSID, or <c>"*"</c> for all methods.</param>
    /// <param name="aud">The target service DID (with optional fragment), or <c>"*"</c> for any service.</param>
    /// <exception cref="ArgumentException">Both <paramref name="lxm"/> and <paramref name="aud"/> are wildcards.</exception>
    public static string Rpc(string lxm, string aud)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lxm);
        ArgumentException.ThrowIfNullOrWhiteSpace(aud);
        if (lxm == "*" && aud == "*")
            throw new ArgumentException("Both lxm and aud cannot be wildcards simultaneously.");

        return $"rpc:{lxm}?aud={EncodeScopeValue(aud)}";
    }

    /// <summary>
    /// Constructs an <c>rpc</c> permission scope for multiple API endpoints (Lexicon methods).
    /// <para>Example: <c>AtProtoScopes.Rpc(["app.bsky.feed.searchPosts", "app.bsky.feed.getTimeline"], "did:web:api.bsky.app#bsky_appview")</c>
    /// → <c>"rpc?lxm=app.bsky.feed.searchPosts&amp;lxm=app.bsky.feed.getTimeline&amp;aud=did:web:api.bsky.app%23bsky_appview"</c></para>
    /// </summary>
    /// <param name="lxms">The Lexicon method NSIDs.</param>
    /// <param name="aud">The target service DID (with optional fragment), or <c>"*"</c> for any service.</param>
    public static string Rpc(IReadOnlyList<string> lxms, string aud)
    {
        ArgumentNullException.ThrowIfNull(lxms);
        ArgumentException.ThrowIfNullOrWhiteSpace(aud);
        if (lxms.Count == 0)
            throw new ArgumentException("At least one lxm is required.", nameof(lxms));
        if (lxms.Count == 1)
            return Rpc(lxms[0], aud);

        var sb = new StringBuilder("rpc?");
        for (var i = 0; i < lxms.Count; i++)
        {
            if (i > 0) sb.Append('&');
            sb.Append("lxm=").Append(lxms[i]);
        }

        sb.Append("&aud=").Append(EncodeScopeValue(aud));
        return sb.ToString();
    }

    /// <summary>
    /// Constructs a <c>blob</c> permission scope for a single MIME type pattern.
    /// <para>Example: <c>AtProtoScopes.Blob("*/*")</c> → <c>"blob:*/*"</c></para>
    /// <para>Example: <c>AtProtoScopes.Blob("video/*")</c> → <c>"blob:video/*"</c></para>
    /// </summary>
    /// <param name="accept">The MIME type pattern (e.g. <c>"*/*"</c>, <c>"video/*"</c>, <c>"text/html"</c>).</param>
    public static string Blob(string accept = "*/*")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accept);
        return $"blob:{accept}";
    }

    /// <summary>
    /// Constructs a <c>blob</c> permission scope for multiple MIME type patterns.
    /// <para>Example: <c>AtProtoScopes.Blob(["video/*", "text/html"])</c>
    /// → <c>"blob?accept=video/*&amp;accept=text/html"</c></para>
    /// </summary>
    /// <param name="accepts">The MIME type patterns.</param>
    public static string Blob(IReadOnlyList<string> accepts)
    {
        ArgumentNullException.ThrowIfNull(accepts);
        if (accepts.Count == 0)
            throw new ArgumentException("At least one accept type is required.", nameof(accepts));
        if (accepts.Count == 1)
            return Blob(accepts[0]);

        var sb = new StringBuilder("blob?");
        for (var i = 0; i < accepts.Count; i++)
        {
            if (i > 0) sb.Append('&');
            sb.Append("accept=").Append(accepts[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Constructs an <c>account</c> permission scope.
    /// <para>Example: <c>AtProtoScopes.Account("email")</c> → <c>"account:email"</c></para>
    /// <para>Example: <c>AtProtoScopes.Account("repo", AccountAction.Manage)</c> → <c>"account:repo?action=manage"</c></para>
    /// </summary>
    /// <param name="attr">The account attribute (<c>"email"</c>, <c>"repo"</c>, or <c>"status"</c>).</param>
    /// <param name="action">The access level. Defaults to <see cref="AccountAction.Read"/>.</param>
    public static string Account(string attr, AccountAction action = AccountAction.Read)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attr);
        var scope = $"account:{attr}";
        return action == AccountAction.Read ? scope : $"{scope}?action=manage";
    }

    /// <summary>
    /// Constructs an <c>identity</c> permission scope.
    /// <para>Example: <c>AtProtoScopes.Identity("handle")</c> → <c>"identity:handle"</c></para>
    /// <para>Example: <c>AtProtoScopes.Identity("*")</c> → <c>"identity:*"</c> (full DID document control)</para>
    /// </summary>
    /// <param name="attr">The identity attribute (<c>"handle"</c> or <c>"*"</c> for full control).</param>
    /// <param name="action">The action type. Defaults to <see cref="IdentityAction.Manage"/>.</param>
    public static string Identity(string attr, IdentityAction action = IdentityAction.Manage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attr);
        var scope = $"identity:{attr}";
        return action == IdentityAction.Manage ? scope : $"{scope}?action=submit";
    }

    /// <summary>
    /// Constructs an <c>include</c> scope string that references a published permission set.
    /// Permission sets are Lexicon schemas that bundle multiple granular permissions under a single NSID.
    /// <para>Example: <c>AtProtoScopes.Include("app.bsky.authBasicFeatures", "did:web:api.bsky.app#svc_appview")</c>
    /// → <c>"include:app.bsky.authBasicFeatures?aud=did:web:api.bsky.app%23svc_appview"</c></para>
    /// </summary>
    /// <param name="permissionSetNsid">The NSID of the permission set Lexicon.</param>
    /// <param name="aud">Optional audience DID passed to permissions with <c>inheritAud</c>.</param>
    public static string Include(string permissionSetNsid, string? aud = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionSetNsid);
        var scope = $"include:{permissionSetNsid}";
        return string.IsNullOrWhiteSpace(aud) ? scope : $"{scope}?aud={EncodeScopeValue(aud)}";
    }

    // ─── Well-known Bluesky permission set NSIDs ────────────────────────

    /// <summary>
    /// Well-known Bluesky permission set NSIDs for use with <see cref="Include"/>.
    /// These correspond to the <c>app.bsky.auth*</c> permission set Lexicons.
    /// </summary>
    public static class PermissionSets
    {
        /// <summary>Full Bluesky Social app functionality. Superset of all other permission sets.</summary>
        public const string FullApp = "app.bsky.authFullApp";

        /// <summary>Manage Bluesky profile (read/update/delete profile, actor status, notification declaration).</summary>
        public const string ManageProfile = "app.bsky.authManageProfile";

        /// <summary>Create posts only (not update/delete). Usually needs blob permission as well.</summary>
        public const string CreatePosts = "app.bsky.authCreatePosts";

        /// <summary>Delete posts only (not create/update). For "delete old posts" automation.</summary>
        public const string DeletePosts = "app.bsky.authDeletePosts";

        /// <summary>Full create/update/delete permissions for posts.</summary>
        public const string ManagePosts = "app.bsky.authManagePosts";

        /// <summary>Manage follows (create/update/delete).</summary>
        public const string ManageFollows = "app.bsky.authManageFollows";

        /// <summary>Manage lists and starter packs.</summary>
        public const string ManageListsAndPacks = "app.bsky.authManageListsAndPacks";

        /// <summary>View notifications (unread count, list, mark seen).</summary>
        public const string ViewNotifications = "app.bsky.authViewNotifs";

        /// <summary>Full notification management including preferences and push registration.</summary>
        public const string ManageNotifications = "app.bsky.authManageNotifs";

        /// <summary>Manage hosted feed generators (declarative feeds).</summary>
        public const string ManageFeedDeclarations = "app.bsky.authManageFeedDeclarations";

        /// <summary>Manage hosted labeling service (e.g., Ozone).</summary>
        public const string ManageLabelerService = "app.bsky.authManageLabelerService";

        /// <summary>Manage Bluesky preferences (get/put).</summary>
        public const string ManagePreferences = "app.bsky.authManagePrefs";

        /// <summary>Manage personal moderation (blocks, mutes, services).</summary>
        public const string ManageModeration = "app.bsky.authManageModeration";

        /// <summary>Read-only access to all content (profiles, feeds, search, etc.).</summary>
        public const string ViewAll = "app.bsky.authViewAll";
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Combines multiple scope strings into a single space-delimited scope value.
    /// Duplicate scopes are removed.
    /// </summary>
    /// <param name="scopes">Individual scope values to combine.</param>
    /// <returns>A space-delimited scope string.</returns>
    public static string Combine(params string[] scopes)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);

        foreach (var scope in scopes)
        {
            foreach (var part in scope.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                unique.Add(part);
            }
        }

        return string.Join(' ', unique);
    }

    /// <summary>
    /// Encodes characters that have structural meaning in AT Protocol scope strings.
    /// Specifically encodes <c>#</c> as <c>%23</c> (common in DID fragments).
    /// </summary>
    private static string EncodeScopeValue(string value) =>
        value.Replace("#", "%23");

    private static void AppendRepoActions(StringBuilder sb, RepoAction actions, bool hasExistingParams)
    {
        // An omitted action list means RepoAction.All, so emitting nothing for None would hand
        // back a full create/update/delete grant — the opposite of what was asked for. The
        // grammar has no marker for an empty action list, so the request is inexpressible
        // rather than narrow.
        if (actions is RepoAction.None)
        {
            throw new ArgumentException(
                "RepoAction.None cannot be expressed: an omitted action list means RepoAction.All, " +
                "so a zero-action grant would widen to full create/update/delete. Omit the repo " +
                "scope entirely, or name the narrowest action the client actually needs.",
                nameof(actions));
        }

        if (actions is RepoAction.All)
            return;

        var separator = hasExistingParams ? '&' : '?';
        if (actions.HasFlag(RepoAction.Create))
        {
            sb.Append(separator).Append("action=create");
            separator = '&';
        }

        if (actions.HasFlag(RepoAction.Update))
        {
            sb.Append(separator).Append("action=update");
            separator = '&';
        }

        if (actions.HasFlag(RepoAction.Delete))
            sb.Append(separator).Append("action=delete");
    }
}
