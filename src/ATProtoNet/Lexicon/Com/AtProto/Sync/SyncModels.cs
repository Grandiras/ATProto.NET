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
    /// <summary>The <c>takendown</c> account hosting status.</summary>
    public const string Takendown = "takendown";

    /// <summary>The <c>suspended</c> account hosting status.</summary>
    public const string Suspended = "suspended";

    /// <summary>The <c>deleted</c> account hosting status.</summary>
    public const string Deleted = "deleted";

    /// <summary>The <c>deactivated</c> account hosting status.</summary>
    public const string Deactivated = "deactivated";

    /// <summary>The <c>desynchronized</c> account hosting status.</summary>
    public const string Desynchronized = "desynchronized";

    /// <summary>The <c>throttled</c> account hosting status.</summary>
    public const string Throttled = "throttled";
}

/// <summary>
/// Known host statuses for relay upstream hosts.
/// Used by <c>listHosts</c> and <c>getHostStatus</c>.
/// </summary>
public static class HostStatus
{
    /// <summary>The <c>active</c> host status.</summary>
    public const string Active = "active";

    /// <summary>The <c>idle</c> host status.</summary>
    public const string Idle = "idle";

    /// <summary>The <c>offline</c> host status.</summary>
    public const string Offline = "offline";

    /// <summary>The <c>throttled</c> host status.</summary>
    public const string Throttled = "throttled";

    /// <summary>The <c>banned</c> host status.</summary>
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
    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>The repository revision (a TID) this data was read at.</summary>
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
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The CIDs.</summary>
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
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The CID of the current repository head commit.</summary>
    [JsonPropertyName("head")]
    public required string Head { get; init; }

    /// <summary>The repository revision (a TID) this data was read at.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>
    /// Whether the account is active (not deactivated, suspended, or taken down).
    /// </summary>
    [JsonPropertyName("active")]
    public bool? Active { get; init; }

    /// <summary>The hosting status of the repository, if it is not active.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Response from listRepos.
/// </summary>
public sealed class ListReposResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The repositories.</summary>
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
    /// <summary>The hostname of the host to crawl or that was updated.</summary>
    [JsonPropertyName("hostname")]
    public required string Hostname { get; init; }
}

/// <summary>
/// Request body for requestCrawl.
/// </summary>
public sealed class RequestCrawlRequest
{
    /// <summary>The hostname of the host to crawl or that was updated.</summary>
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
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>
    /// Whether the account is active (not deactivated, suspended, or taken down).
    /// </summary>
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
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The upstream hosts known to this relay.</summary>
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
    /// <summary>The hostname of the host to crawl or that was updated.</summary>
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
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// Response from listReposByCollection. Enumerates DIDs that have records
/// with a given collection NSID.
/// </summary>
public sealed class ListReposByCollectionResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The repositories.</summary>
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
    /// <summary>The DID of the repository the commit belongs to.</summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>The commit the write was applied in.</summary>
    [JsonPropertyName("commit")]
    public required string Commit { get; init; }

    /// <summary>The repository revision (a TID) this data was read at.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>The revision the diff is relative to, if this is a partial commit.</summary>
    [JsonPropertyName("since")]
    public string? Since { get; init; }

    /// <summary>
    /// Whether the commit was too large to include inline; the repository must be fetched
    /// separately.
    /// </summary>
    [JsonPropertyName("tooBig")]
    public bool TooBig { get; init; }

    /// <summary>
    /// Whether the commit is a rebase. Deprecated and always <see langword="false"/>.
    /// </summary>
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
    /// <summary>The DID (decentralized identifier) of the account.</summary>
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
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }
}

/// <summary>
/// An account status event.
/// </summary>
public sealed class AccountEvent : FirehoseMessage
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>
    /// Whether the account is active (not deactivated, suspended, or taken down).
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; init; }

    /// <summary>The new hosting status of the account.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Legacy handle event (deprecated in favor of identity event).
/// </summary>
public sealed class HandleEvent : FirehoseMessage
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required string Handle { get; init; }
}

/// <summary>
/// A tombstone event – a repository was deleted.
/// </summary>
public sealed class TombstoneEvent : FirehoseMessage
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// An informational event from the relay.
/// </summary>
public sealed class InfoEvent : FirehoseMessage
{
    /// <summary>The name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The message.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
