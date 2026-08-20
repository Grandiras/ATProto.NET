using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;
using ATProtoNet.Spaces;

namespace ATProtoNet.Lexicon.Com.AtProto.Space;

// ──────────────────────────────────────────────────────────────
//  com.atproto.space.getDelegationToken
// ──────────────────────────────────────────────────────────────

/// <summary>Response from <c>getDelegationToken</c>.</summary>
public sealed class GetDelegationTokenResponse
{
    /// <summary>
    /// A signed JWT delegation token, single-use and short-lived (60 seconds by default),
    /// addressed to the space authority.
    /// </summary>
    [JsonPropertyName("token")]
    public required string Token { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.space.getSpaceCredential
// ──────────────────────────────────────────────────────────────

/// <summary>Request body for <c>getSpaceCredential</c>.</summary>
public sealed class GetSpaceCredentialRequest
{
    /// <summary>Reference to the space.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }

    /// <summary>
    /// Optional client attestation JWT establishing the app's identity. Required only when the
    /// space gates on app identity.
    /// </summary>
    [JsonPropertyName("clientAttestation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientAttestation { get; init; }
}

/// <summary>Response from <c>getSpaceCredential</c>.</summary>
public sealed class GetSpaceCredentialResponse
{
    /// <summary>
    /// A signed JWT space credential, bound through its <c>cnf.jkt</c> claim to the key that
    /// signed the request's DPoP proof.
    /// </summary>
    [JsonPropertyName("credential")]
    public required string Credential { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.space.listSpaces
// ──────────────────────────────────────────────────────────────

/// <summary>A space the authenticated user holds a repo in.</summary>
public sealed class SpaceView
{
    /// <summary>URI of the space.</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>Parses <see cref="Uri"/> into its authority, type, and key components.</summary>
    public SpaceUri ToSpaceUri() => SpaceUri.Parse(Uri);
}

/// <summary>Response from <c>listSpaces</c>.</summary>
public sealed class ListSpacesResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The spaces.</summary>
    [JsonPropertyName("spaces")]
    public required List<SpaceView> Spaces { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.space.listRepos
// ──────────────────────────────────────────────────────────────

/// <summary>A repo that holds data in a space, as claimed by the space authority.</summary>
public sealed class SpaceRepoView
{
    /// <summary>The DID of a repo that holds data in the space.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>
    /// The repo's current revision (a TID), as last reported to the authority. May lag the repo
    /// host, which is the source of truth.
    /// </summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>
    /// The repo's current commit hash (<c>sha256</c> of the LtHash state), as last reported to
    /// the authority.
    /// </summary>
    [JsonPropertyName("hash")]
    [JsonConverter(typeof(LexBytesJsonConverter))]
    public required byte[] Hash { get; init; }
}

/// <summary>Response from <c>listRepos</c>: a space's writer set.</summary>
public sealed class ListSpaceReposResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The repos that hold data in the space.</summary>
    [JsonPropertyName("repos")]
    public required List<SpaceRepoView> Repos { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.space.getRecord / listRecords
// ──────────────────────────────────────────────────────────────

/// <summary>Response from <c>getRecord</c>.</summary>
public sealed class GetSpaceRecordResponse
{
    /// <summary>The record's space URI.</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The record's CID.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>The record's value.</summary>
    [JsonPropertyName("value")]
    public required JsonElement Value { get; init; }
}

/// <summary>A record listed from a permissioned repo.</summary>
public sealed class SpaceRecordView
{
    /// <summary>The record collection NSID.</summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    /// <summary>The record key.</summary>
    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }

    /// <summary>The record's CID.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>
    /// The record's value. Inlined by default; omitted when <c>excludeValues</c> was set.
    /// </summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }

    /// <summary>The record's path within the repo, <c>{collection}/{rkey}</c>.</summary>
    public string Path => $"{Collection}/{Rkey}";
}

/// <summary>Response from <c>listRecords</c>.</summary>
public sealed class ListSpaceRecordsResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The records.</summary>
    [JsonPropertyName("records")]
    public required List<SpaceRecordView> Records { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.space.listBlobs
// ──────────────────────────────────────────────────────────────

/// <summary>Response from <c>listBlobs</c>.</summary>
public sealed class ListSpaceBlobsResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The CIDs of the blobs referenced by the repo's records in this space.</summary>
    [JsonPropertyName("cids")]
    public required List<string> Cids { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.space.getLatestCommit
// ──────────────────────────────────────────────────────────────

/// <summary>Response from <c>getLatestCommit</c>.</summary>
public sealed class GetSpaceLatestCommitResponse
{
    /// <summary>The account's current signed commit.</summary>
    [JsonPropertyName("commit")]
    public required SignedSpaceCommit Commit { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.space.listRepoOps
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A single operation in a permissioned repo's oplog.
/// </summary>
/// <remarks>
/// <see cref="Cid"/> is <see langword="null"/> for a delete and <see cref="Prev"/> is
/// <see langword="null"/> for a create. Operations sharing a <see cref="Rev"/> were applied
/// atomically as one batch.
/// </remarks>
public sealed class SpaceRepoOpEntry
{
    /// <summary>The revision (a TID) this operation was written at.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>The record collection NSID.</summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    /// <summary>The record key.</summary>
    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }

    /// <summary>The record's new CID, or <see langword="null"/> for a delete.</summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    /// <summary>The record's previous CID, or <see langword="null"/> for a create.</summary>
    [JsonPropertyName("prev")]
    public string? Prev { get; init; }

    /// <summary>
    /// The record's current value, inlined for create and update operations. Omitted when
    /// <c>excludeValues</c> was set, for deletes, or when a later operation superseded it.
    /// </summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }

    /// <summary>Projects this entry onto the form <see cref="SpaceRepoCommit.ApplyOp"/> consumes.</summary>
    public SpaceRepoOp ToRepoOp() => new(Collection, Rkey, Cid, Prev);
}

/// <summary>Response from <c>listRepoOps</c>.</summary>
public sealed class ListSpaceRepoOpsResponse
{
    /// <summary>The operations after the requested revision, in order.</summary>
    [JsonPropertyName("ops")]
    public required List<SpaceRepoOpEntry> Ops { get; init; }

    /// <summary>
    /// The account's current signed commit. Included when the response reaches the head of the
    /// oplog; omitted on backfill responses.
    /// </summary>
    [JsonPropertyName("commit")]
    public SignedSpaceCommit? Commit { get; init; }

    /// <summary>
    /// Pass as <c>cursor</c> to fetch the next page. Absent once the response reaches the head
    /// of the oplog.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.space.createRecord / putRecord / deleteRecord
// ──────────────────────────────────────────────────────────────

/// <summary>Known values for a write's <c>validationStatus</c>.</summary>
public static class SpaceValidationStatus
{
    /// <summary>The record was validated against a known Lexicon.</summary>
    public const string Valid = "valid";

    /// <summary>The record's Lexicon is unknown to the host, so it was not validated.</summary>
    public const string Unknown = "unknown";
}

/// <summary>Request body for <c>createRecord</c>.</summary>
public sealed class CreateSpaceRecordRequest
{
    /// <summary>Reference to the space.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }

    /// <summary>The DID of the repo to write to (the authenticated member).</summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>The NSID of the record collection.</summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    /// <summary>The record key. Generated by the host when omitted.</summary>
    [JsonPropertyName("rkey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rkey { get; init; }

    /// <summary>
    /// <see langword="false"/> to skip Lexicon schema validation, <see langword="true"/> to
    /// require it, or <see langword="null"/> to validate only for known Lexicons.
    /// </summary>
    [JsonPropertyName("validate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Validate { get; init; }

    /// <summary>The record itself. Must contain a <c>$type</c> field.</summary>
    [JsonPropertyName("record")]
    public required object Record { get; init; }
}

/// <summary>Request body for <c>putRecord</c>.</summary>
public sealed class PutSpaceRecordRequest
{
    /// <summary>Reference to the space.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }

    /// <summary>The DID of the repo to write to (the authenticated member).</summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>The NSID of the record collection.</summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    /// <summary>The record key.</summary>
    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }

    /// <summary>
    /// <see langword="false"/> to skip Lexicon schema validation, <see langword="true"/> to
    /// require it, or <see langword="null"/> to validate only for known Lexicons.
    /// </summary>
    [JsonPropertyName("validate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Validate { get; init; }

    /// <summary>The record to write.</summary>
    [JsonPropertyName("record")]
    public required object Record { get; init; }
}

/// <summary>Request body for <c>deleteRecord</c>.</summary>
public sealed class DeleteSpaceRecordRequest
{
    /// <summary>Reference to the space.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }

    /// <summary>The DID of the repo to delete from (the authenticated member).</summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>The NSID of the record collection.</summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    /// <summary>The record key.</summary>
    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }
}

/// <summary>Result of a single-record write into a space.</summary>
public sealed class SpaceWriteResult
{
    /// <summary>URI of the written record.</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The record's CID.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>Whether the record validated against a known Lexicon. See <see cref="SpaceValidationStatus"/>.</summary>
    [JsonPropertyName("validationStatus")]
    public string? ValidationStatus { get; init; }

    /// <summary>Parses <see cref="Uri"/> into its space, author, collection, and record key.</summary>
    public SpaceRecordUri ToRecordUri() => SpaceRecordUri.Parse(Uri);
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.space.applyWrites
// ──────────────────────────────────────────────────────────────

/// <summary>Base type for the operations in an <c>applyWrites</c> batch.</summary>
[JsonDerivedType(typeof(SpaceCreateOp), "com.atproto.space.applyWrites#create")]
[JsonDerivedType(typeof(SpaceUpdateOp), "com.atproto.space.applyWrites#update")]
[JsonDerivedType(typeof(SpaceDeleteOp), "com.atproto.space.applyWrites#delete")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
public abstract class SpaceWriteOp
{
    /// <summary>The NSID of the record collection.</summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }
}

/// <summary>Creates a new record in the batch.</summary>
public sealed class SpaceCreateOp : SpaceWriteOp
{
    /// <summary>The record key. Generated by the host when omitted.</summary>
    [JsonPropertyName("rkey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rkey { get; init; }

    /// <summary>The record value.</summary>
    [JsonPropertyName("value")]
    public required object Value { get; init; }
}

/// <summary>Updates an existing record in the batch.</summary>
public sealed class SpaceUpdateOp : SpaceWriteOp
{
    /// <summary>The record key.</summary>
    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }

    /// <summary>The record value.</summary>
    [JsonPropertyName("value")]
    public required object Value { get; init; }
}

/// <summary>Deletes an existing record in the batch.</summary>
public sealed class SpaceDeleteOp : SpaceWriteOp
{
    /// <summary>The record key.</summary>
    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }
}

/// <summary>Request body for <c>applyWrites</c>.</summary>
public sealed class ApplySpaceWritesRequest
{
    /// <summary>Reference to the space.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }

    /// <summary>The DID of the repo to write to (the authenticated member).</summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>
    /// <see langword="false"/> to skip Lexicon schema validation across all operations,
    /// <see langword="true"/> to require it, or <see langword="null"/> to validate only for
    /// known Lexicons.
    /// </summary>
    [JsonPropertyName("validate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Validate { get; init; }

    /// <summary>The operations, applied atomically.</summary>
    [JsonPropertyName("writes")]
    public required List<SpaceWriteOp> Writes { get; init; }
}

/// <summary>One entry in an <c>applyWrites</c> result, in the order the writes were given.</summary>
public sealed class SpaceWriteOpResult
{
    /// <summary>The discriminator naming which kind of result this is.</summary>
    [JsonPropertyName("$type")]
    public string? Type { get; init; }

    /// <summary>URI of the written record. Absent for a delete.</summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    /// <summary>The record's CID. Absent for a delete.</summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    /// <summary>Whether the record validated against a known Lexicon. See <see cref="SpaceValidationStatus"/>.</summary>
    [JsonPropertyName("validationStatus")]
    public string? ValidationStatus { get; init; }
}

/// <summary>Response from <c>applyWrites</c>.</summary>
public sealed class ApplySpaceWritesResponse
{
    /// <summary>The per-operation results, or <see langword="null"/> when the host returned none.</summary>
    [JsonPropertyName("results")]
    public List<SpaceWriteOpResult>? Results { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.space.registerNotify / unregisterNotify
// ──────────────────────────────────────────────────────────────

/// <summary>Request body for <c>registerNotify</c>.</summary>
public sealed class RegisterNotifyRequest
{
    /// <summary>Reference to the space.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }

    /// <summary>
    /// Service identifier of the subscriber: a DID with an optional service fragment naming the
    /// entry in its DID document to deliver to
    /// (e.g. <c>did:web:syncer.example.com#atproto_space_syncer</c>).
    /// </summary>
    [JsonPropertyName("service")]
    public required string Service { get; init; }
}

/// <summary>Response from <c>registerNotify</c>.</summary>
public sealed class RegisterNotifyResponse
{
    /// <summary>
    /// When the registration expires. May be later than the expiry of the space credential the
    /// request was authenticated with; renew before this time to stay subscribed.
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public required string ExpiresAt { get; init; }
}

/// <summary>Request body for <c>unregisterNotify</c>.</summary>
public sealed class UnregisterNotifyRequest
{
    /// <summary>Reference to the space.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }

    /// <summary>Service identifier of the subscriber to remove, as passed to <c>registerNotify</c>.</summary>
    [JsonPropertyName("service")]
    public required string Service { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.space.notifyWrite / notifySpaceDeleted
// ──────────────────────────────────────────────────────────────

/// <summary>Request body for <c>notifyWrite</c>.</summary>
public sealed class NotifyWriteRequest
{
    /// <summary>Reference to the space.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }

    /// <summary>The DID of the account whose repo advanced.</summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>The revision (a TID) of the write.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>
    /// The repo's current commit hash (<c>sha256</c> of the LtHash state) after the write.
    /// Lets the space host maintain each repo's hash for <c>listRepos</c>.
    /// </summary>
    [JsonPropertyName("hash")]
    [JsonConverter(typeof(LexBytesJsonConverter))]
    public required byte[] Hash { get; init; }
}

/// <summary>Request body for <c>notifySpaceDeleted</c>.</summary>
public sealed class NotifySpaceDeletedRequest
{
    /// <summary>Reference to the deleted space.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Errors
// ──────────────────────────────────────────────────────────────

/// <summary>
/// The named errors the <c>com.atproto.space.*</c> endpoints return.
/// </summary>
/// <remarks>
/// <para><see cref="RepoNotFound"/> deliberately does not distinguish a member that has never
/// written from an account that is not a member at all — the protocol carries no reader set, so
/// a repo host has nothing else to say.</para>
/// <para>An authority may answer <see cref="NotAuthorized"/> in place of
/// <see cref="UserNotAuthorized"/> or <see cref="AppNotAuthorized"/> when it does not wish to
/// disclose which perimeter failed.</para>
/// </remarks>
public static class SpaceErrors
{
    /// <summary>The space does not exist, or the caller may not learn that it does.</summary>
    public const string SpaceNotFound = "SpaceNotFound";

    /// <summary>The space was deleted; a syncer holding a copy should drop it.</summary>
    public const string SpaceDeleted = "SpaceDeleted";

    /// <summary>The account holds no repo in the space.</summary>
    public const string RepoNotFound = "RepoNotFound";

    /// <summary>The account has been taken down.</summary>
    public const string RepoTakendown = "RepoTakendown";

    /// <summary>The account has been suspended.</summary>
    public const string RepoSuspended = "RepoSuspended";

    /// <summary>The account has been deactivated.</summary>
    public const string RepoDeactivated = "RepoDeactivated";

    /// <summary>No record exists at the requested collection and record key.</summary>
    public const string RecordNotFound = "RecordNotFound";

    /// <summary>A record already exists at the requested collection and record key.</summary>
    public const string RecordAlreadyExists = "RecordAlreadyExists";

    /// <summary>The credential request was refused on the basis of the requesting user.</summary>
    public const string UserNotAuthorized = "UserNotAuthorized";

    /// <summary>The credential request was refused on the basis of the requesting app.</summary>
    public const string AppNotAuthorized = "AppNotAuthorized";

    /// <summary>The credential request was refused without attributing the refusal.</summary>
    public const string NotAuthorized = "NotAuthorized";

    /// <summary>The presented delegation token was malformed, expired, or not verifiable.</summary>
    public const string InvalidDelegationToken = "InvalidDelegationToken";

    /// <summary>The presented client attestation was malformed, expired, or not verifiable.</summary>
    public const string InvalidClientAttestation = "InvalidClientAttestation";

    /// <summary>The blob is not held by the repo in this space.</summary>
    public const string BlobNotFound = "BlobNotFound";

    /// <summary>A registered service identifier could not be resolved to a delivery endpoint.</summary>
    public const string ServiceNotResolvable = "ServiceNotResolvable";
}
