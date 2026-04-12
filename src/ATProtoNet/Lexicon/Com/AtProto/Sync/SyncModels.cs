using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.Sync;

// ──────────────────────────────────────────────────────────────
//  com.atproto.sync.defs
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Known account hosting statuses as defined by the AT Protocol spec.
/// Used by <c>getRepoStatus</c> and <c>#account</c> firehose events.
/// </summary>
public static class AccountHostingStatus
{
    public const string Takendown = "takendown";
    public const string Suspended = "suspended";
    public const string Deleted = "deleted";
    public const string Deactivated = "deactivated";
    public const string Desynchronized = "desynchronized";
    public const string Throttled = "throttled";
}

/// <summary>
/// Known host statuses for relay upstream hosts.
/// Used by <c>listHosts</c> and <c>getHostStatus</c>.
/// </summary>
public static class HostStatus
{
    public const string Active = "active";
    public const string Idle = "idle";
    public const string Offline = "offline";
    public const string Throttled = "throttled";
    public const string Banned = "banned";
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.sync.getLatestCommit
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getLatestCommit.
/// </summary>
public sealed class GetLatestCommitResponse
{
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("rev")]
    public required string Rev { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.sync.listBlobs
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from listBlobs.
/// </summary>
public sealed class ListBlobsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("cids")]
    public required List<string> Cids { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.sync.listRepos
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A single repo entry from listRepos.
/// </summary>
public sealed class RepoInfo
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("head")]
    public required string Head { get; init; }

    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    [JsonPropertyName("active")]
    public bool? Active { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Response from listRepos.
/// </summary>
public sealed class ListReposResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("repos")]
    public required List<RepoInfo> Repos { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.sync.notifyOfUpdate / requestCrawl
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for notifyOfUpdate.
/// </summary>
public sealed class NotifyOfUpdateRequest
{
    [JsonPropertyName("hostname")]
    public required string Hostname { get; init; }
}

/// <summary>
/// Request body for requestCrawl.
/// </summary>
public sealed class RequestCrawlRequest
{
    [JsonPropertyName("hostname")]
    public required string Hostname { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.sync.getRepoStatus
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getRepoStatus. Returns the hosting status for a repository.
/// </summary>
public sealed class GetRepoStatusResponse
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("active")]
    public required bool Active { get; init; }

    /// <summary>
    /// If active=false, this optional field indicates a possible reason for why
    /// the account is not active. See <see cref="AccountHostingStatus"/> for known values.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>
    /// The current rev of the repo, if active=true.
    /// </summary>
    [JsonPropertyName("rev")]
    public string? Rev { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.sync.listHosts
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Information about an upstream host (PDS or relay) consumed by a relay.
/// </summary>
public sealed class HostInfo
{
    /// <summary>
    /// Hostname of the server (not a URL, no scheme).
    /// </summary>
    [JsonPropertyName("hostname")]
    public required string Hostname { get; init; }

    /// <summary>
    /// Recent repo stream event sequence number.
    /// </summary>
    [JsonPropertyName("seq")]
    public long? Seq { get; init; }

    /// <summary>
    /// Number of accounts associated with this host.
    /// </summary>
    [JsonPropertyName("accountCount")]
    public int? AccountCount { get; init; }

    /// <summary>
    /// Status of the host. See <see cref="HostStatus"/> for known values.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Response from listHosts. Enumerates upstream hosts consumed by a relay.
/// </summary>
public sealed class ListHostsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("hosts")]
    public required List<HostInfo> Hosts { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.sync.getHostStatus
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getHostStatus. Returns information about a specified upstream host.
/// </summary>
public sealed class GetHostStatusResponse
{
    [JsonPropertyName("hostname")]
    public required string Hostname { get; init; }

    /// <summary>
    /// Recent repo stream event sequence number.
    /// </summary>
    [JsonPropertyName("seq")]
    public long? Seq { get; init; }

    /// <summary>
    /// Number of accounts on the server associated with the upstream host.
    /// </summary>
    [JsonPropertyName("accountCount")]
    public int? AccountCount { get; init; }

    /// <summary>
    /// Status of the host. See <see cref="HostStatus"/> for known values.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.sync.listReposByCollection
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A repo entry from listReposByCollection (DID only).
/// </summary>
public sealed class CollectionRepoInfo
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// Response from listReposByCollection. Enumerates DIDs that have records
/// with a given collection NSID.
/// </summary>
public sealed class ListReposByCollectionResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("repos")]
    public required List<CollectionRepoInfo> Repos { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.sync.subscribeRepos (event stream messages)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Base type for firehose event stream messages.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CommitEvent), "#commit")]
[JsonDerivedType(typeof(SyncEvent), "#sync")]
[JsonDerivedType(typeof(IdentityEvent), "#identity")]
[JsonDerivedType(typeof(AccountEvent), "#account")]
[JsonDerivedType(typeof(HandleEvent), "#handle")]
[JsonDerivedType(typeof(TombstoneEvent), "#tombstone")]
[JsonDerivedType(typeof(InfoEvent), "#info")]
public abstract class FirehoseMessage
{
    /// <summary>Sequence number of this event.</summary>
    [JsonPropertyName("seq")]
    public long Seq { get; init; }

    /// <summary>Timestamp of the event.</summary>
    [JsonPropertyName("time")]
    public string? Time { get; init; }
}

/// <summary>
/// A commit event from the firehose. Indicates a repository commit.
/// </summary>
public sealed class CommitEvent : FirehoseMessage
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("commit")]
    public required string Commit { get; init; }

    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    [JsonPropertyName("since")]
    public string? Since { get; init; }

    [JsonPropertyName("tooBig")]
    public bool TooBig { get; init; }

    [JsonPropertyName("rebase")]
    public bool Rebase { get; init; }

    /// <summary>CAR-encoded blocks (base64 when serialized via JSON; binary in CBOR).</summary>
    [JsonPropertyName("blocks")]
    public byte[]? Blocks { get; init; }

    /// <summary>Operations included in this commit.</summary>
    [JsonPropertyName("ops")]
    public List<RepoOp>? Ops { get; init; }

    /// <summary>
    /// The root CID of the MST tree for the previous commit (indicated by the 'since'
    /// revision field). Corresponds to the 'data' field in the repo commit object.
    /// Required for the 'inductive' version of firehose (Sync v1.1).
    /// </summary>
    [JsonPropertyName("prevData")]
    public string? PrevData { get; init; }

    /// <summary>DEPRECATED — will soon always be empty. List of new blobs referenced by records in this commit.</summary>
    [JsonPropertyName("blobs")]
    public List<string>? Blobs { get; init; }
}

/// <summary>
/// A single operation within a commit.
/// </summary>
public sealed class RepoOp
{
    /// <summary>The operation action: "create", "update", or "delete".</summary>
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    /// <summary>The AT-URI path (collection/rkey) of the record.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>The CID of the record after this operation (null for deletes).</summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    /// <summary>
    /// For updates and deletes, the previous record CID (required for inductive firehose).
    /// For creations, this field should not be defined.
    /// </summary>
    [JsonPropertyName("prev")]
    public string? Prev { get; init; }
}

/// <summary>
/// A sync event from the firehose. Updates the repo to a new state without necessarily
/// including that state on the firehose. Used to recover from broken commit streams,
/// data loss incidents, or when the upstream host does not know recent state.
/// New in Sync v1.1.
/// </summary>
public sealed class SyncEvent : FirehoseMessage
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>CAR file containing the commit block. The CAR header must indicate the commit block as the first root.</summary>
    [JsonPropertyName("blocks")]
    public byte[]? Blocks { get; init; }

    /// <summary>The rev of the commit. Must match the rev in the commit object.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }
}

/// <summary>
/// An identity event – a DID document was updated.
/// </summary>
public sealed class IdentityEvent : FirehoseMessage
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }
}

/// <summary>
/// An account status event.
/// </summary>
public sealed class AccountEvent : FirehoseMessage
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Legacy handle event (deprecated in favor of identity event).
/// </summary>
public sealed class HandleEvent : FirehoseMessage
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }
}

/// <summary>
/// A tombstone event – a repository was deleted.
/// </summary>
public sealed class TombstoneEvent : FirehoseMessage
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// An informational event from the relay.
/// </summary>
public sealed class InfoEvent : FirehoseMessage
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
