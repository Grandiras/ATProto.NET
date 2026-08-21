namespace ATProtoNet.Server.Spaces;

/// <summary>
/// The Lexicon NSIDs the space server implements, grouped by who serves them.
/// </summary>
/// <remarks>
/// They are constants rather than literals because the routing needs them at attribute position,
/// where only a constant is allowed.
/// </remarks>
public static class SpaceNsids
{
    // ── Served by a repo host ─────────────────────────────────

    /// <summary><c>com.atproto.space.getRecord</c>.</summary>
    public const string GetRecord = "com.atproto.space.getRecord";

    /// <summary><c>com.atproto.space.listRecords</c>.</summary>
    public const string ListRecords = "com.atproto.space.listRecords";

    /// <summary><c>com.atproto.space.getLatestCommit</c>.</summary>
    public const string GetLatestCommit = "com.atproto.space.getLatestCommit";

    /// <summary><c>com.atproto.space.getRepo</c>.</summary>
    public const string GetRepo = "com.atproto.space.getRepo";

    /// <summary><c>com.atproto.space.listRepoOps</c>.</summary>
    public const string ListRepoOps = "com.atproto.space.listRepoOps";

    /// <summary><c>com.atproto.space.getBlob</c>.</summary>
    public const string GetBlob = "com.atproto.space.getBlob";

    /// <summary><c>com.atproto.space.listBlobs</c>.</summary>
    public const string ListBlobs = "com.atproto.space.listBlobs";

    // ── Served by a space authority ───────────────────────────

    /// <summary><c>com.atproto.space.getSpaceCredential</c>.</summary>
    public const string GetSpaceCredential = "com.atproto.space.getSpaceCredential";

    /// <summary><c>com.atproto.space.listRepos</c>.</summary>
    public const string ListRepos = "com.atproto.space.listRepos";

    /// <summary><c>com.atproto.space.registerNotify</c>.</summary>
    public const string RegisterNotify = "com.atproto.space.registerNotify";

    /// <summary><c>com.atproto.space.unregisterNotify</c>.</summary>
    public const string UnregisterNotify = "com.atproto.space.unregisterNotify";

    /// <summary><c>com.atproto.space.notifyWrite</c>.</summary>
    public const string NotifyWrite = "com.atproto.space.notifyWrite";

    /// <summary><c>com.atproto.space.notifySpaceDeleted</c>.</summary>
    public const string NotifySpaceDeleted = "com.atproto.space.notifySpaceDeleted";

    // ── com.atproto.simplespace ───────────────────────────────

    /// <summary><c>com.atproto.simplespace.createSpace</c>.</summary>
    public const string CreateSimpleSpace = "com.atproto.simplespace.createSpace";

    /// <summary><c>com.atproto.simplespace.updateSpace</c>.</summary>
    public const string UpdateSimpleSpace = "com.atproto.simplespace.updateSpace";

    /// <summary><c>com.atproto.simplespace.deleteSpace</c>.</summary>
    public const string DeleteSimpleSpace = "com.atproto.simplespace.deleteSpace";

    /// <summary><c>com.atproto.simplespace.getSpace</c>.</summary>
    public const string GetSimpleSpace = "com.atproto.simplespace.getSpace";

    /// <summary><c>com.atproto.simplespace.addMember</c>.</summary>
    public const string AddSimpleSpaceMember = "com.atproto.simplespace.addMember";

    /// <summary><c>com.atproto.simplespace.removeMember</c>.</summary>
    public const string RemoveSimpleSpaceMember = "com.atproto.simplespace.removeMember";

    /// <summary><c>com.atproto.simplespace.listMembers</c>.</summary>
    public const string ListSimpleSpaceMembers = "com.atproto.simplespace.listMembers";

    /// <summary><c>com.atproto.simplespace.checkUserAccess</c>.</summary>
    public const string CheckUserAccess = "com.atproto.simplespace.checkUserAccess";
}
