using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Com.AtProto.Repo;

/// <summary>
/// Request body for com.atproto.repo.createRecord.
/// </summary>
public sealed class CreateRecordRequest
{
    /// <summary>
    /// The handle or DID of the repo (account).
    /// </summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>
    /// The NSID of the record collection.
    /// </summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    /// <summary>
    /// The record key. If not specified, the server will generate one.
    /// </summary>
    [JsonPropertyName("rkey")]
    public string? Rkey { get; init; }

    /// <summary>
    /// Flag for opt-in/out of Lexicon schema validation.
    /// </summary>
    [JsonPropertyName("validate")]
    public bool? Validate { get; init; }

    /// <summary>
    /// The record data to create.
    /// </summary>
    [JsonPropertyName("record")]
    public required object Record { get; init; }

    /// <summary>
    /// Compare and swap with the previous commit rev.
    /// </summary>
    [JsonPropertyName("swapCommit")]
    public string? SwapCommit { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.createRecord.
/// </summary>
public sealed class CreateRecordResponse
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public string Cid { get; init; } = string.Empty;

    /// <summary>The commit the write was applied in.</summary>
    [JsonPropertyName("commit")]
    public CommitMeta? Commit { get; init; }

    /// <summary>
    /// Whether the server validated the record against a known Lexicon (<c>valid</c> or
    /// <c>unknown</c>).
    /// </summary>
    [JsonPropertyName("validationStatus")]
    public string? ValidationStatus { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.getRecord.
/// </summary>
public sealed class GetRecordResponse
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    /// <summary>The record value.</summary>
    [JsonPropertyName("value")]
    public JsonElement Value { get; init; }
}

/// <summary>
/// Typed response from com.atproto.repo.getRecord.
/// </summary>
public sealed class GetRecordResponse<T>
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    /// <summary>The deserialised record value.</summary>
    [JsonPropertyName("value")]
    public T Value { get; init; } = default!;
}

/// <summary>
/// Request body for com.atproto.repo.putRecord.
/// </summary>
public sealed class PutRecordRequest
{
    /// <summary>The handle or DID of the repository.</summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>
    /// The NSID of the collection the record belongs to (e.g. <c>app.bsky.feed.post</c>).
    /// </summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    /// <summary>The record key identifying the record within its collection.</summary>
    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }

    /// <summary>Whether the server should validate the record against its Lexicon schema.</summary>
    [JsonPropertyName("validate")]
    public bool? Validate { get; init; }

    /// <summary>The record value to write.</summary>
    [JsonPropertyName("record")]
    public required object Record { get; init; }

    /// <summary>
    /// Compare-and-swap guard: the CID the record must currently be at for the write to succeed.
    /// Pass <see langword="null"/> to require that the record does not exist.
    /// </summary>
    [JsonPropertyName("swapRecord")]
    public string? SwapRecord { get; init; }

    /// <summary>
    /// Compare-and-swap guard: the commit CID the repository must currently be at for the write to
    /// succeed.
    /// </summary>
    [JsonPropertyName("swapCommit")]
    public string? SwapCommit { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.putRecord.
/// </summary>
public sealed class PutRecordResponse
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public string Cid { get; init; } = string.Empty;

    /// <summary>The commit the write was applied in.</summary>
    [JsonPropertyName("commit")]
    public CommitMeta? Commit { get; init; }

    /// <summary>
    /// Whether the server validated the record against a known Lexicon (<c>valid</c> or
    /// <c>unknown</c>).
    /// </summary>
    [JsonPropertyName("validationStatus")]
    public string? ValidationStatus { get; init; }
}

/// <summary>
/// Request body for com.atproto.repo.deleteRecord.
/// </summary>
public sealed class DeleteRecordRequest
{
    /// <summary>The handle or DID of the repository.</summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>
    /// The NSID of the collection the record belongs to (e.g. <c>app.bsky.feed.post</c>).
    /// </summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    /// <summary>The record key identifying the record within its collection.</summary>
    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }

    /// <summary>
    /// Compare-and-swap guard: the CID the record must currently be at for the write to succeed.
    /// Pass <see langword="null"/> to require that the record does not exist.
    /// </summary>
    [JsonPropertyName("swapRecord")]
    public string? SwapRecord { get; init; }

    /// <summary>
    /// Compare-and-swap guard: the commit CID the repository must currently be at for the write to
    /// succeed.
    /// </summary>
    [JsonPropertyName("swapCommit")]
    public string? SwapCommit { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.deleteRecord.
/// </summary>
public sealed class DeleteRecordResponse
{
    /// <summary>The commit the write was applied in.</summary>
    [JsonPropertyName("commit")]
    public CommitMeta? Commit { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.listRecords.
/// </summary>
public sealed class ListRecordsResponse : ICursoredResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The records in this page of results.</summary>
    [JsonPropertyName("records")]
    public List<RecordEntry> Records { get; init; } = [];
}

/// <summary>
/// A single record entry in a list response.
/// </summary>
public sealed class RecordEntry
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public string Cid { get; init; } = string.Empty;

    /// <summary>The record value.</summary>
    [JsonPropertyName("value")]
    public JsonElement Value { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.describeRepo.
/// </summary>
public sealed class DescribeRepoResponse
{
    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public string Handle { get; init; } = string.Empty;

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public string Did { get; init; } = string.Empty;

    /// <summary>The DID document for the account, as returned by the PDS.</summary>
    [JsonPropertyName("didDoc")]
    public object? DidDoc { get; init; }

    /// <summary>The NSIDs of the collections present in the repository.</summary>
    [JsonPropertyName("collections")]
    public List<string> Collections { get; init; } = [];

    /// <summary>Whether the handle currently resolves back to this DID.</summary>
    [JsonPropertyName("handleIsCorrect")]
    public bool HandleIsCorrect { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.uploadBlob.
/// </summary>
public sealed class UploadBlobResponse
{
    /// <summary>The uploaded blob reference.</summary>
    [JsonPropertyName("blob")]
    public BlobRef Blob { get; init; } = new();
}

/// <summary>
/// Request body for com.atproto.repo.applyWrites.
/// </summary>
public sealed class ApplyWritesRequest
{
    /// <summary>The handle or DID of the repository.</summary>
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    /// <summary>Whether the server should validate the record against its Lexicon schema.</summary>
    [JsonPropertyName("validate")]
    public bool? Validate { get; init; }

    /// <summary>The write operations to apply atomically.</summary>
    [JsonPropertyName("writes")]
    public required List<ApplyWriteOperation> Writes { get; init; }

    /// <summary>
    /// Compare-and-swap guard: the commit CID the repository must currently be at for the write to
    /// succeed.
    /// </summary>
    [JsonPropertyName("swapCommit")]
    public string? SwapCommit { get; init; }
}

/// <summary>
/// A single write operation in an applyWrites batch.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ApplyWriteCreate), "com.atproto.repo.applyWrites#create")]
[JsonDerivedType(typeof(ApplyWriteUpdate), "com.atproto.repo.applyWrites#update")]
[JsonDerivedType(typeof(ApplyWriteDelete), "com.atproto.repo.applyWrites#delete")]
public abstract class ApplyWriteOperation
{
}

/// <summary>A create operation within an <c>applyWrites</c> batch.</summary>
public sealed class ApplyWriteCreate : ApplyWriteOperation
{
    /// <summary>
    /// The NSID of the collection the record belongs to (e.g. <c>app.bsky.feed.post</c>).
    /// </summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    /// <summary>The record key identifying the record within its collection.</summary>
    [JsonPropertyName("rkey")]
    public string? Rkey { get; init; }

    /// <summary>The record value to create.</summary>
    [JsonPropertyName("value")]
    public required object Value { get; init; }
}

/// <summary>An update operation within an <c>applyWrites</c> batch.</summary>
public sealed class ApplyWriteUpdate : ApplyWriteOperation
{
    /// <summary>
    /// The NSID of the collection the record belongs to (e.g. <c>app.bsky.feed.post</c>).
    /// </summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    /// <summary>The record key identifying the record within its collection.</summary>
    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }

    /// <summary>The record value to write.</summary>
    [JsonPropertyName("value")]
    public required object Value { get; init; }
}

/// <summary>A delete operation within an <c>applyWrites</c> batch.</summary>
public sealed class ApplyWriteDelete : ApplyWriteOperation
{
    /// <summary>
    /// The NSID of the collection the record belongs to (e.g. <c>app.bsky.feed.post</c>).
    /// </summary>
    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    /// <summary>The record key identifying the record within its collection.</summary>
    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.applyWrites.
/// </summary>
public sealed class ApplyWritesResponse
{
    /// <summary>The commit the write was applied in.</summary>
    [JsonPropertyName("commit")]
    public CommitMeta? Commit { get; init; }

    /// <summary>The per-write results, in the same order as the request.</summary>
    [JsonPropertyName("results")]
    public List<ApplyWriteResult>? Results { get; init; }
}

/// <summary>The result of a single write within an <c>applyWrites</c> batch.</summary>
public sealed class ApplyWriteResult
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    /// <summary>
    /// Whether the server validated the record against a known Lexicon (<c>valid</c> or
    /// <c>unknown</c>).
    /// </summary>
    [JsonPropertyName("validationStatus")]
    public string? ValidationStatus { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.listMissingBlobs.
/// </summary>
public sealed class ListMissingBlobsResponse : ICursoredResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The blob references.</summary>
    [JsonPropertyName("blobs")]
    public List<MissingBlob> Blobs { get; init; } = [];
}

/// <summary>A blob referenced by a record that has not been uploaded yet.</summary>
public sealed class MissingBlob
{
    /// <summary>The CID of the missing blob.</summary>
    [JsonPropertyName("cid")]
    public string Cid { get; init; } = string.Empty;
}

/// <summary>
/// Commit metadata included in write operation responses.
/// </summary>
public sealed class CommitMeta
{
    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public string Cid { get; init; } = string.Empty;

    /// <summary>The repository revision (a TID) this data was read at.</summary>
    [JsonPropertyName("rev")]
    public string Rev { get; init; } = string.Empty;
}
