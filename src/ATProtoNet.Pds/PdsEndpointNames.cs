namespace ATProtoNet.Pds;

/// <summary>
/// The NSIDs of the XRPC endpoints mapped by
/// <see cref="PdsHostingExtensions.MapAtProtoPds(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)"/>.
/// Use these constants with <see cref="PdsEndpointOptions"/> instead of raw strings so
/// typos become compile errors.
/// </summary>
public static class PdsEndpointNames
{
    /// <summary><c>com.atproto.server.createAccount</c> (POST).</summary>
    public const string CreateAccount = "com.atproto.server.createAccount";

    /// <summary><c>com.atproto.server.createSession</c> (POST).</summary>
    public const string CreateSession = "com.atproto.server.createSession";

    /// <summary><c>com.atproto.server.getSession</c> (GET).</summary>
    public const string GetSession = "com.atproto.server.getSession";

    /// <summary><c>com.atproto.server.refreshSession</c> (POST).</summary>
    public const string RefreshSession = "com.atproto.server.refreshSession";

    /// <summary><c>com.atproto.server.describeServer</c> (GET).</summary>
    public const string DescribeServer = "com.atproto.server.describeServer";

    /// <summary><c>com.atproto.server.createInviteCode</c> (POST, admin Basic auth).</summary>
    public const string CreateInviteCode = "com.atproto.server.createInviteCode";

    /// <summary><c>com.atproto.server.createInviteCodes</c> (POST, admin Basic auth).</summary>
    public const string CreateInviteCodes = "com.atproto.server.createInviteCodes";

    /// <summary><c>com.atproto.server.getAccountInviteCodes</c> (GET, Bearer auth).</summary>
    public const string GetAccountInviteCodes = "com.atproto.server.getAccountInviteCodes";

    /// <summary><c>com.atproto.admin.getInviteCodes</c> (GET, admin Basic auth).</summary>
    public const string GetInviteCodes = "com.atproto.admin.getInviteCodes";

    /// <summary><c>com.atproto.admin.disableInviteCodes</c> (POST, admin Basic auth).</summary>
    public const string DisableInviteCodes = "com.atproto.admin.disableInviteCodes";

    /// <summary><c>com.atproto.repo.createRecord</c> (POST).</summary>
    public const string CreateRecord = "com.atproto.repo.createRecord";

    /// <summary><c>com.atproto.repo.getRecord</c> (GET).</summary>
    public const string GetRecord = "com.atproto.repo.getRecord";

    /// <summary><c>com.atproto.repo.putRecord</c> (POST).</summary>
    public const string PutRecord = "com.atproto.repo.putRecord";

    /// <summary><c>com.atproto.repo.deleteRecord</c> (POST).</summary>
    public const string DeleteRecord = "com.atproto.repo.deleteRecord";

    /// <summary><c>com.atproto.repo.listRecords</c> (GET).</summary>
    public const string ListRecords = "com.atproto.repo.listRecords";

    /// <summary><c>com.atproto.repo.uploadBlob</c> (POST).</summary>
    public const string UploadBlob = "com.atproto.repo.uploadBlob";

    /// <summary><c>com.atproto.sync.getBlob</c> (GET).</summary>
    public const string GetBlob = "com.atproto.sync.getBlob";

    /// <summary><c>com.atproto.identity.resolveHandle</c> (GET).</summary>
    public const string ResolveHandle = "com.atproto.identity.resolveHandle";

    /// <summary><c>com.atproto.sync.getRepo</c> (GET) — the repository as a CAR file.</summary>
    public const string GetRepo = "com.atproto.sync.getRepo";

    /// <summary><c>com.atproto.sync.getLatestCommit</c> (GET).</summary>
    public const string GetLatestCommit = "com.atproto.sync.getLatestCommit";

    /// <summary><c>com.atproto.sync.getRepoStatus</c> (GET).</summary>
    public const string GetRepoStatus = "com.atproto.sync.getRepoStatus";

    /// <summary><c>com.atproto.sync.listRepos</c> (GET).</summary>
    public const string ListRepos = "com.atproto.sync.listRepos";

    /// <summary><c>com.atproto.sync.getRecord</c> (GET) — a record plus its MST inclusion proof.</summary>
    public const string SyncGetRecord = "com.atproto.sync.getRecord";

    /// <summary><c>com.atproto.sync.getBlocks</c> (GET).</summary>
    public const string GetBlocks = "com.atproto.sync.getBlocks";

    /// <summary><c>com.atproto.sync.listBlobs</c> (GET).</summary>
    public const string ListBlobs = "com.atproto.sync.listBlobs";

    /// <summary><c>com.atproto.sync.subscribeRepos</c> (WebSocket) — the firehose.</summary>
    public const string SubscribeRepos = "com.atproto.sync.subscribeRepos";

    /// <summary>
    /// Every endpoint NSID mapped by <c>MapAtProtoPds()</c>, in mapping order.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        CreateAccount,
        CreateSession,
        GetSession,
        RefreshSession,
        DescribeServer,
        CreateInviteCode,
        CreateInviteCodes,
        GetAccountInviteCodes,
        GetInviteCodes,
        DisableInviteCodes,
        CreateRecord,
        GetRecord,
        PutRecord,
        DeleteRecord,
        ListRecords,
        UploadBlob,
        GetBlob,
        ResolveHandle,
        GetRepo,
        GetLatestCommit,
        GetRepoStatus,
        ListRepos,
        SyncGetRecord,
        GetBlocks,
        ListBlobs,
        SubscribeRepos,
    ];
}
