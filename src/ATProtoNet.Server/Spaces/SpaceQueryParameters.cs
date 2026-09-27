using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

// The query parameters shared by every com.atproto.space.* method that reads one account's repo within
// one space.
//
// The pair is the addressing unit of permissioned data. A public repository is named by a DID alone; a
// permissioned one is named by (space, repo), because an account holds one repo per space rather than a
// single repository.
//
// Each parameter binds through its type's parser, so a value that is not a space URI, a DID, an NSID, a
// record key, a TID or a CID is answered with InvalidRequest before the endpoint runs. A missing one
// binds as null and is refused by the endpoint.
internal abstract class SpaceRepoParameters
{
    // The space, as an at://{authority}/space/{type}/{skey} URI.
    [JsonPropertyName("space")]
    public SpaceUri? Space { get; init; }

    // The DID of the account whose repo is addressed.
    [JsonPropertyName("repo")]
    public Did? Repo { get; init; }
}

// Query parameters for com.atproto.space.listRepos.
internal sealed class ListSpaceReposParameters
{
    // The space whose writer set is being listed.
    [JsonPropertyName("space")]
    public SpaceUri? Space { get; init; }

    // Maximum number of results (1 to 1000).
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    // Pagination cursor from a previous page.
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

// Query parameters for com.atproto.space.getRecord.
internal sealed class GetSpaceRecordParameters : SpaceRepoParameters
{
    // The record collection NSID.
    [JsonPropertyName("collection")]
    public Nsid? Collection { get; init; }

    // The record key.
    [JsonPropertyName("rkey")]
    public RecordKey? Rkey { get; init; }
}

// Query parameters for com.atproto.space.listRecords.
internal sealed class ListSpaceRecordsParameters : SpaceRepoParameters
{
    // Restrict to one collection. Lists across all collections when omitted.
    [JsonPropertyName("collection")]
    public Nsid? Collection { get; init; }

    // Maximum number of results (1 to 1000).
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    // Pagination cursor from a previous page.
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    // Reverse the order of the returned records.
    [JsonPropertyName("reverse")]
    [JsonConverter(typeof(XrpcBooleanConverter))]
    public bool? Reverse { get; init; }

    // Return only metadata, without inlined record values.
    [JsonPropertyName("excludeValues")]
    [JsonConverter(typeof(XrpcBooleanConverter))]
    public bool? ExcludeValues { get; init; }
}

// Query parameters for com.atproto.space.getLatestCommit.
internal sealed class GetSpaceLatestCommitParameters : SpaceRepoParameters;

// Query parameters for com.atproto.space.getRepo.
internal sealed class GetSpaceRepoParameters : SpaceRepoParameters
{
    // Return only the commit and index roots, with no record blocks.
    [JsonPropertyName("excludeValues")]
    [JsonConverter(typeof(XrpcBooleanConverter))]
    public bool? ExcludeValues { get; init; }
}

// Query parameters for com.atproto.space.listRepoOps.
internal sealed class ListSpaceRepoOpsParameters : SpaceRepoParameters
{
    // Return operations after this revision — the caller's own sync position.
    [JsonPropertyName("since")]
    public Tid? Since { get; init; }

    // Maximum number of operations (1 to 1000).
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    // Opaque pagination cursor. Takes precedence over Since.
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    // Return operation metadata only, without inlined record values.
    [JsonPropertyName("excludeValues")]
    [JsonConverter(typeof(XrpcBooleanConverter))]
    public bool? ExcludeValues { get; init; }
}

// Query parameters for com.atproto.space.getBlob.
internal sealed class GetSpaceBlobParameters : SpaceRepoParameters
{
    // The blob's CID.
    [JsonPropertyName("cid")]
    public Cid? Cid { get; init; }
}

// Query parameters for com.atproto.space.listBlobs.
internal sealed class ListSpaceBlobsParameters : SpaceRepoParameters
{
    // List blobs referenced since this revision of the permissioned repo.
    [JsonPropertyName("since")]
    public Tid? Since { get; init; }

    // Maximum number of results (1 to 1000).
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    // Pagination cursor from a previous page.
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

// Query parameters for com.atproto.simplespace.getSpace.
internal sealed class GetSimpleSpaceParameters
{
    // The space.
    [JsonPropertyName("space")]
    public SpaceUri? Space { get; init; }
}

// Query parameters for com.atproto.simplespace.listMembers.
internal sealed class ListSimpleSpaceMembersParameters
{
    // The space.
    [JsonPropertyName("space")]
    public SpaceUri? Space { get; init; }

    // Maximum number of results (1 to 1000).
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    // Pagination cursor from a previous page.
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}
