using System.Text.Json.Serialization;
using ATProtoNet.Server.Xrpc;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// The query parameters shared by every <c>com.atproto.space.*</c> method that reads one
/// account's repo within one space.
/// </summary>
/// <remarks>
/// The pair is the addressing unit of permissioned data. A public repository is named by a DID
/// alone; a permissioned one is named by <em>(space, repo)</em>, because an account holds one
/// repo per space rather than a single repository.
/// </remarks>
public abstract class SpaceRepoParameters
{
    /// <summary>The space, as an <c>at://{authority}/space/{type}/{skey}</c> URI.</summary>
    [JsonPropertyName("space")]
    public string? Space { get; init; }

    /// <summary>The DID of the account whose repo is addressed.</summary>
    [JsonPropertyName("repo")]
    public string? Repo { get; init; }
}

/// <summary>Query parameters for <c>com.atproto.space.listRepos</c>.</summary>
public sealed class ListSpaceReposParameters
{
    /// <summary>The space whose writer set is being listed.</summary>
    [JsonPropertyName("space")]
    public string? Space { get; init; }

    /// <summary>Maximum number of results (1 to 1000).</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    /// <summary>Pagination cursor from a previous page.</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

/// <summary>Query parameters for <c>com.atproto.space.getRecord</c>.</summary>
public sealed class GetSpaceRecordParameters : SpaceRepoParameters
{
    /// <summary>The record collection NSID.</summary>
    [JsonPropertyName("collection")]
    public string? Collection { get; init; }

    /// <summary>The record key.</summary>
    [JsonPropertyName("rkey")]
    public string? Rkey { get; init; }
}

/// <summary>Query parameters for <c>com.atproto.space.listRecords</c>.</summary>
public sealed class ListSpaceRecordsParameters : SpaceRepoParameters
{
    /// <summary>Restrict to one collection. Lists across all collections when omitted.</summary>
    [JsonPropertyName("collection")]
    public string? Collection { get; init; }

    /// <summary>Maximum number of results (1 to 1000).</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    /// <summary>Pagination cursor from a previous page.</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>Reverse the order of the returned records.</summary>
    [JsonPropertyName("reverse")]
    [JsonConverter(typeof(XrpcBooleanConverter))]
    public bool? Reverse { get; init; }

    /// <summary>Return only metadata, without inlined record values.</summary>
    [JsonPropertyName("excludeValues")]
    [JsonConverter(typeof(XrpcBooleanConverter))]
    public bool? ExcludeValues { get; init; }
}

/// <summary>Query parameters for <c>com.atproto.space.getLatestCommit</c>.</summary>
public sealed class GetSpaceLatestCommitParameters : SpaceRepoParameters;

/// <summary>Query parameters for <c>com.atproto.space.getRepo</c>.</summary>
public sealed class GetSpaceRepoParameters : SpaceRepoParameters
{
    /// <summary>Return only the commit and index roots, with no record blocks.</summary>
    [JsonPropertyName("excludeValues")]
    [JsonConverter(typeof(XrpcBooleanConverter))]
    public bool? ExcludeValues { get; init; }
}

/// <summary>Query parameters for <c>com.atproto.space.listRepoOps</c>.</summary>
public sealed class ListSpaceRepoOpsParameters : SpaceRepoParameters
{
    /// <summary>Return operations after this revision — the caller's own sync position.</summary>
    [JsonPropertyName("since")]
    public string? Since { get; init; }

    /// <summary>Maximum number of operations (1 to 1000).</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    /// <summary>Opaque pagination cursor. Takes precedence over <see cref="Since"/>.</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>Return operation metadata only, without inlined record values.</summary>
    [JsonPropertyName("excludeValues")]
    [JsonConverter(typeof(XrpcBooleanConverter))]
    public bool? ExcludeValues { get; init; }
}

/// <summary>Query parameters for <c>com.atproto.space.getBlob</c>.</summary>
public sealed class GetSpaceBlobParameters : SpaceRepoParameters
{
    /// <summary>The blob's CID.</summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; init; }
}

/// <summary>Query parameters for <c>com.atproto.space.listBlobs</c>.</summary>
public sealed class ListSpaceBlobsParameters : SpaceRepoParameters
{
    /// <summary>List blobs referenced since this revision of the permissioned repo.</summary>
    [JsonPropertyName("since")]
    public string? Since { get; init; }

    /// <summary>Maximum number of results (1 to 1000).</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    /// <summary>Pagination cursor from a previous page.</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

/// <summary>Query parameters for <c>com.atproto.simplespace.getSpace</c>.</summary>
public sealed class GetSimpleSpaceParameters
{
    /// <summary>The space.</summary>
    [JsonPropertyName("space")]
    public string? Space { get; init; }
}

/// <summary>Query parameters for <c>com.atproto.simplespace.listMembers</c>.</summary>
public sealed class ListSimpleSpaceMembersParameters
{
    /// <summary>The space.</summary>
    [JsonPropertyName("space")]
    public string? Space { get; init; }

    /// <summary>Maximum number of results (1 to 1000).</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    /// <summary>Pagination cursor from a previous page.</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}
