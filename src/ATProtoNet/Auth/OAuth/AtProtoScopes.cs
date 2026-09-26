using System.Buffers;
using System.Text;
using ATProtoNet.Identity;

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
/// <remarks>
/// The permission spec no longer has an <c>action</c> parameter on <c>identity</c> scopes, and
/// authorization servers reject a scope that carries one. Use
/// <see cref="AtProtoScopes.Identity(string)"/>.
/// </remarks>
[Obsolete("The permission spec dropped the action parameter of identity scopes, and authorization servers reject " +
    "'identity:*?action=...'. Use AtProtoScopes.Identity(attr), which grants control of the attribute.")]
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
/// <c>updateSpace</c> as well as <c>putMember</c> and <c>removeMember</c>.</para>
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
    /// <b>Legacy.</b> Broad PDS account permissions, equivalent to the previous "App Password" authorization level.
    /// Includes: write any repository record type, upload blobs, read/write preferences,
    /// API proxying for most Lexicons, and service auth token generation.
    /// Does NOT include: account management (change handle/email, delete/deactivate/migrate account)
    /// or DM access (<c>chat.bsky.*</c> Lexicons).
    /// </summary>
    /// <remarks>
    /// The transitional scopes remain supported, but the specification intends to deprecate and
    /// eventually remove them, and the consent screen presents this one as access to nearly
    /// everything. Request granular permissions instead: <see cref="Repo(string, RepoAction)"/>,
    /// <see cref="Rpc(string, string)"/>, <see cref="Blob(string)"/>, a permission set through
    /// <see cref="Include"/>, or one of the <see cref="Presets"/>.
    /// </remarks>
    public const string TransitionGeneric = "transition:generic";

    /// <summary>
    /// <b>Legacy.</b> Access to Bluesky DM (Direct Message) Lexicons (<c>chat.bsky.*</c>).
    /// This scope depends on and does not function without <see cref="TransitionGeneric"/>.
    /// Prefer <see cref="PermissionSets.FullChatClient"/> (<see cref="Presets.BlueskyAppWithChat"/>).
    /// </summary>
    public const string TransitionChatBsky = "transition:chat.bsky";

    /// <summary>
    /// <b>Legacy.</b> Access to the account email address and confirmation status via
    /// <c>com.atproto.server.getSession</c>. Prefer <c>account:email</c>
    /// (<see cref="Account(string, AccountAction)"/>).
    /// </summary>
    public const string TransitionEmail = "transition:email";

    /// <summary>
    /// Default scope string: <c>"atproto transition:generic"</c>, the legacy broad grant (see
    /// <see cref="TransitionGeneric"/>). It covers most applications that read and write records
    /// and upload blobs; new applications should request granular permissions or a
    /// <see cref="Presets">preset</see> instead.
    /// </summary>
    public const string Default = $"{AtProto} {TransitionGeneric}";

    /// <summary>
    /// Full scope string including DM access: <c>"atproto transition:generic transition:chat.bsky"</c>,
    /// built on the legacy transitional scopes. <see cref="Presets.BlueskyAppWithChat"/> is its
    /// granular counterpart.
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
    /// <param name="aud">
    /// The target service as a DID with its service fragment (<c>did:web:api.bsky.app#bsky_appview</c>),
    /// or <c>"*"</c> for any service.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Both <paramref name="lxm"/> and <paramref name="aud"/> are wildcards, or
    /// <paramref name="aud"/> is neither <c>"*"</c> nor a DID with a service fragment; a bare DID
    /// makes the scope invalid.
    /// </exception>
    public static string Rpc(string lxm, string aud)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lxm);
        ArgumentException.ThrowIfNullOrWhiteSpace(aud);
        ValidateAudience(aud, allowWildcard: true);
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
    /// <param name="aud">
    /// The target service as a DID with its service fragment, or <c>"*"</c> for any service.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="lxms"/> is empty, or <paramref name="aud"/> is neither <c>"*"</c> nor a DID
    /// with a service fragment.
    /// </exception>
    public static string Rpc(IReadOnlyList<string> lxms, string aud)
    {
        ArgumentNullException.ThrowIfNull(lxms);
        ArgumentException.ThrowIfNullOrWhiteSpace(aud);
        ValidateAudience(aud, allowWildcard: true);
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
    public static string Identity(string attr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attr);
        return $"identity:{attr}";
    }

    /// <summary>
    /// Constructs an <c>identity</c> permission scope with an <c>action</c> parameter, which the
    /// permission spec no longer has.
    /// </summary>
    /// <param name="attr">The identity attribute (<c>"handle"</c> or <c>"*"</c> for full control).</param>
    /// <param name="action">The action type.</param>
    [Obsolete("The permission spec dropped the action parameter of identity scopes, and authorization servers reject " +
        "'identity:*?action=submit'. Use Identity(attr).")]
    public static string Identity(string attr, IdentityAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attr);
        var scope = $"identity:{attr}";
        return action == IdentityAction.Manage ? scope : $"{scope}?action=submit";
    }

    /// <summary>
    /// Constructs an <c>include</c> scope string that references a published permission set.
    /// Permission sets are Lexicon schemas that bundle multiple granular permissions under a single NSID.
    /// <para>Example: <c>AtProtoScopes.Include(PermissionSets.FullApp, "did:web:api.bsky.app#bsky_appview")</c>
    /// → <c>"include:app.bsky.authFullApp?aud=did:web:api.bsky.app%23bsky_appview"</c></para>
    /// </summary>
    /// <param name="permissionSetNsid">The NSID of the permission set Lexicon.</param>
    /// <param name="aud">
    /// The service the set's <c>inheritAud</c> permissions are for, as a DID with its service
    /// fragment (<see cref="BlueskyAppView"/>, for instance). Omit it for a set without such
    /// permissions.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="aud"/> is not a DID with a service fragment; a wildcard is not allowed here.
    /// </exception>
    public static string Include(string permissionSetNsid, string? aud = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionSetNsid);
        var scope = $"include:{permissionSetNsid}";
        if (string.IsNullOrWhiteSpace(aud))
            return scope;

        ValidateAudience(aud, allowWildcard: false);
        return $"{scope}?aud={EncodeScopeValue(aud)}";
    }

    // ─── Service audiences ─────────────────────────────────────────────

    /// <summary>
    /// The Bluesky AppView as an audience, <c>did:web:api.bsky.app#bsky_appview</c>: the
    /// <c>aud</c> of Bluesky's <c>app.bsky</c> permission sets and of <c>rpc</c> grants for
    /// AppView methods.
    /// </summary>
    public const string BlueskyAppView = "did:web:api.bsky.app#bsky_appview";

    /// <summary>
    /// The Bluesky chat service as an audience, <c>did:web:api.bsky.chat#bsky_chat</c>: the
    /// <c>aud</c> of <see cref="PermissionSets.FullChatClient"/>.
    /// </summary>
    public const string BlueskyChat = "did:web:api.bsky.chat#bsky_chat";

    // ─── Presets ───────────────────────────────────────────────────────

    /// <summary>
    /// Complete scope strings for common Bluesky clients, built on Bluesky's published permission
    /// sets instead of the transitional scopes, for <see cref="OAuthOptions.Scope"/> and the
    /// client metadata's <c>scope</c>.
    /// </summary>
    /// <remarks>
    /// Permission sets cannot grant <c>blob</c> or <c>account</c> permissions, so a preset that
    /// uploads media adds <c>blob:*/*</c> itself. Combine a preset with further scopes using
    /// <see cref="Combine"/>.
    /// </remarks>
    public static class Presets
    {
        /// <summary>
        /// A full Bluesky client: <see cref="PermissionSets.FullApp"/> at the AppView, and media
        /// uploads. <c>atproto include:app.bsky.authFullApp?aud=did:web:api.bsky.app%23bsky_appview blob:*/*</c>
        /// </summary>
        public const string BlueskyApp =
            "atproto include:app.bsky.authFullApp?aud=did:web:api.bsky.app%23bsky_appview blob:*/*";

        /// <summary>
        /// <see cref="BlueskyApp"/> plus every chat conversation
        /// (<see cref="PermissionSets.FullChatClient"/> at the chat service).
        /// </summary>
        public const string BlueskyAppWithChat =
            BlueskyApp + " include:chat.bsky.authFullChatClient?aud=did:web:api.bsky.chat%23bsky_chat";

        /// <summary>
        /// Read-only Bluesky access: <see cref="PermissionSets.ViewAll"/> at the AppView.
        /// </summary>
        public const string BlueskyReadOnly =
            "atproto include:app.bsky.authViewAll?aud=did:web:api.bsky.app%23bsky_appview";

        /// <summary>
        /// Creating posts with images and videos, and nothing else:
        /// <see cref="PermissionSets.CreatePosts"/> at the AppView, and media uploads.
        /// </summary>
        public const string BlueskyPosting =
            "atproto include:app.bsky.authCreatePosts?aud=did:web:api.bsky.app%23bsky_appview blob:*/*";
    }

    // ─── Published permission set NSIDs ────────────────────────────────

    /// <summary>
    /// The NSIDs of published permission sets, for use with <see cref="Include"/>: Bluesky's
    /// <c>app.bsky.auth*</c> and <c>chat.bsky.authFullChatClient</c>, and Standard.site's
    /// <c>site.standard.auth*</c>.
    /// </summary>
    /// <remarks>
    /// An authorization server resolves each set from the Lexicon its authority publishes, so an
    /// NSID nobody published makes the whole <c>include:</c> scope unresolvable. The SDK's tests
    /// check these constants against a snapshot of the published sets.
    /// </remarks>
    public static class PermissionSets
    {
        /// <summary>
        /// Full Bluesky app functionality: all public content and interactions, private
        /// preferences and subscriptions, and other Bluesky-specific data.
        /// </summary>
        public const string FullApp = "app.bsky.authFullApp";

        /// <summary>Update the Bluesky profile, the account status and the notification declaration.</summary>
        public const string ManageProfile = "app.bsky.authManageProfile";

        /// <summary>
        /// Create posts, with their threadgates and postgates, and upload videos; posts cannot be
        /// updated or deleted. Images still need a <c>blob</c> permission.
        /// </summary>
        public const string CreatePosts = "app.bsky.authCreatePosts";

        /// <summary>Delete public account history: posts, reposts and likes.</summary>
        public const string DeleteContent = "app.bsky.authDeleteContent";

        /// <summary>View and configure the Bluesky app's notifications.</summary>
        public const string ManageNotifications = "app.bsky.authManageNotifications";

        /// <summary>Manage feed generator declaration records.</summary>
        public const string ManageFeedDeclarations = "app.bsky.authManageFeedDeclarations";

        /// <summary>Manage the labeler declaration record of a hosted labeling service.</summary>
        public const string ManageLabelerService = "app.bsky.authManageLabelerService";

        /// <summary>Manage personal moderation: blocks, mutes, moderation lists and services, and preferences.</summary>
        public const string ManageModeration = "app.bsky.authManageModeration";

        /// <summary>Read-only access to all content, and to the account's notifications and preferences.</summary>
        public const string ViewAll = "app.bsky.authViewAll";

        /// <summary>A full Bluesky chat client: every conversation and the chat settings.</summary>
        public const string FullChatClient = "chat.bsky.authFullChatClient";

        /// <summary>Standard.site: manage publications, documents, subscriptions and recommendations.</summary>
        public const string StandardSiteFull = "site.standard.authFull";

        /// <summary>Standard.site: manage publication subscriptions and document recommendations.</summary>
        public const string StandardSiteSocial = "site.standard.authSocial";
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

    /// <summary>
    /// Checks an <c>aud</c> the way the reference scope parser does: <c>*</c> where the
    /// resource allows it, otherwise a DID with a service fragment, since a service is addressed
    /// by its DID document entry, not by the DID alone. A fragment already encoded as
    /// <c>%23</c> is accepted.
    /// </summary>
    private static void ValidateAudience(string aud, bool allowWildcard)
    {
        if (aud == "*")
        {
            if (allowWildcard)
                return;
            throw new ArgumentException("An include scope's aud must name a service; '*' is not allowed.", nameof(aud));
        }

        var decoded = aud.Replace("%23", "#", StringComparison.OrdinalIgnoreCase);
        var hash = decoded.IndexOf('#');
        if (hash > 0 && hash < decoded.Length - 1 &&
            Did.TryParse(decoded[..hash], out _) &&
            decoded.AsSpan(hash + 1).IndexOfAnyExcept(ServiceFragmentChars) < 0)
        {
            return;
        }

        throw new ArgumentException(
            $"'{aud}' is not a service reference: the aud must be a DID with a service fragment, " +
            $"such as '{BlueskyAppView}'" + (allowWildcard ? ", or '*'." : "."),
            nameof(aud));
    }

    private static readonly SearchValues<char> ServiceFragmentChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~");

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
