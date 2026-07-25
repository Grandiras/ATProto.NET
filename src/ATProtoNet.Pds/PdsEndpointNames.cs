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
        CreateRecord,
        GetRecord,
        PutRecord,
        DeleteRecord,
        ListRecords,
        UploadBlob,
        GetBlob,
    ];
}
