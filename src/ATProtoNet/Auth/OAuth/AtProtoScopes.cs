using System.Text;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Actions for repository record permissions.
/// </summary>
[Flags]
public enum RepoAction
{
    /// <summary>No specific action.</summary>
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
    /// <param name="actions">The permitted actions. Defaults to all actions (create, update, delete).</param>
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
    /// <param name="actions">The permitted actions. Defaults to all actions (create, update, delete).</param>
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
        if (actions is RepoAction.All or RepoAction.None)
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
