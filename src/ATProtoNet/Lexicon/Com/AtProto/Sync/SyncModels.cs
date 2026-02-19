using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.Sync;

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
//  com.atproto.sync.subscribeRepos (event stream messages)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Base type for firehose event stream messages.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CommitEvent), "#commit")]
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
