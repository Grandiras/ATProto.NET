using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.Repo;

internal sealed record CreateRecordRequest(
    [property: JsonPropertyName("repo")] [property: JsonPropertyOrder(0)] AtIdentifier Repo,
    [property: JsonPropertyName("collection")] [property: JsonPropertyOrder(1)] Nsid Collection,
    [property: JsonPropertyName("record")] [property: JsonPropertyOrder(4)] object Record,
    [property: JsonPropertyName("rkey")] [property: JsonPropertyOrder(2)] RecordKey? Rkey = null,
    [property: JsonPropertyName("validate")] [property: JsonPropertyOrder(3)] bool? Validate = null,
    [property: JsonPropertyName("swapCommit")] [property: JsonPropertyOrder(5)] Cid? SwapCommit = null);

// Response from com.atproto.repo.createRecord, and from com.atproto.repo.putRecord, whose output is
// the same shape. The client methods return it as a RecordRef.
internal sealed class RecordWriteResponse
{
    // The AT-URI of the record (at://did/collection/rkey).
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    // The CID (content identifier) of the record version.
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    // The commit the write was applied in.
    [JsonPropertyName("commit")]
    public CommitMeta? Commit { get; init; }

    // Whether the server validated the record against a known Lexicon (valid or unknown).
    [JsonPropertyName("validationStatus")]
    public string? ValidationStatus { get; init; }
}

// Response from com.atproto.repo.getRecord, with the value deserialized straight into T; also one
// record of a typed com.atproto.repo.listRecords page. The client methods return it as a
// RecordView{T}.
internal sealed class GetRecordResponse<T>
{
    // The AT-URI of the record (at://did/collection/rkey).
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    // The CID (content identifier) of the record version.
    [JsonPropertyName("cid")]
    public Cid? Cid { get; init; }

    // The deserialised record value.
    [JsonPropertyName("value")]
    public T Value { get; init; } = default!;
}

internal sealed record PutRecordRequest(
    [property: JsonPropertyName("repo")] [property: JsonPropertyOrder(0)] AtIdentifier Repo,
    [property: JsonPropertyName("collection")] [property: JsonPropertyOrder(1)] Nsid Collection,
    [property: JsonPropertyName("rkey")] [property: JsonPropertyOrder(2)] RecordKey Rkey,
    [property: JsonPropertyName("record")] [property: JsonPropertyOrder(4)] object Record,
    [property: JsonPropertyName("validate")] [property: JsonPropertyOrder(3)] bool? Validate = null,
    [property: JsonPropertyName("swapRecord")] [property: JsonPropertyOrder(5)] Cid? SwapRecord = null,
    [property: JsonPropertyName("swapCommit")] [property: JsonPropertyOrder(6)] Cid? SwapCommit = null);

internal sealed record DeleteRecordRequest(
    [property: JsonPropertyName("repo")] AtIdentifier Repo,
    [property: JsonPropertyName("collection")] Nsid Collection,
    [property: JsonPropertyName("rkey")] RecordKey Rkey,
    [property: JsonPropertyName("swapRecord")] Cid? SwapRecord = null,
    [property: JsonPropertyName("swapCommit")] Cid? SwapCommit = null);

/// <summary>Response from com.atproto.repo.deleteRecord.</summary>
public sealed class DeleteRecordResponse
{
    /// <summary>The commit the write was applied in.</summary>
    [JsonPropertyName("commit")]
    public CommitMeta? Commit { get; init; }
}

/// <summary>Response from com.atproto.repo.listRecords.</summary>
public sealed record ListRecordsResponse : CursorPage<RecordEntry>
{
    /// <summary>The records in this page of results.</summary>
    [JsonPropertyName("records")]
    public IReadOnlyList<RecordEntry> Records { get; init; } = [];

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<RecordEntry> Items => Records;
}

// Response from com.atproto.repo.listRecords, with each value deserialized straight into T.
// RecordCollection{T} returns it as a RecordPage{T}.
internal sealed class ListRecordsResponse<T>
{
    // Pagination cursor; pass this back on the next request to continue where this page ended. null when
    // there are no further results.
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    // The records in this page of results.
    [JsonPropertyName("records")]
    public IReadOnlyList<GetRecordResponse<T>> Records { get; init; } = [];
}

/// <summary>A single record entry in a list response.</summary>
public sealed class RecordEntry : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>The record value.</summary>
    [JsonPropertyName("value")]
    public JsonElement Value { get; init; }
}

/// <summary>Response from com.atproto.repo.describeRepo.</summary>
public sealed class DescribeRepoResponse
{
    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The DID document for the account, as returned by the PDS.</summary>
    [JsonPropertyName("didDoc")]
    public object? DidDoc { get; init; }

    /// <summary>The NSIDs of the collections present in the repository.</summary>
    [JsonPropertyName("collections")]
    public IReadOnlyList<Nsid> Collections { get; init; } = [];

    /// <summary>Whether the handle currently resolves back to this DID.</summary>
    [JsonPropertyName("handleIsCorrect")]
    public bool HandleIsCorrect { get; init; }
}

/// <summary>Response from com.atproto.repo.uploadBlob.</summary>
public sealed class UploadBlobResponse
{
    /// <summary>The uploaded blob reference.</summary>
    [JsonPropertyName("blob")]
    public BlobRef Blob { get; init; } = new();
}

internal sealed record ApplyWritesRequest(
    [property: JsonPropertyName("repo")] [property: JsonPropertyOrder(0)] AtIdentifier Repo,
    [property: JsonPropertyName("writes")] [property: JsonPropertyOrder(2)] IReadOnlyList<ApplyWriteOperation> Writes,
    [property: JsonPropertyName("validate")] [property: JsonPropertyOrder(1)] bool? Validate = null,
    [property: JsonPropertyName("swapCommit")] [property: JsonPropertyOrder(3)] Cid? SwapCommit = null);

/// <summary>A single write operation in an applyWrites batch.</summary>
/// <remarks>The Lexicon marks this union closed, so an unrecognized <c>$type</c> is an error.</remarks>
[AtProtoUnion(Closed = true)]
[JsonDerivedType(typeof(ApplyWriteCreate), "com.atproto.repo.applyWrites#create")]
[JsonDerivedType(typeof(ApplyWriteUpdate), "com.atproto.repo.applyWrites#update")]
[JsonDerivedType(typeof(ApplyWriteDelete), "com.atproto.repo.applyWrites#delete")]
public abstract class ApplyWriteOperation : LexObject;

/// <summary>A create operation within an <c>applyWrites</c> batch.</summary>
public sealed class ApplyWriteCreate : ApplyWriteOperation
{
    /// <summary>The NSID of the collection the record belongs to (e.g. <c>app.bsky.feed.post</c>).</summary>
    [JsonPropertyName("collection")]
    public required Nsid Collection { get; init; }

    /// <summary>The record key identifying the record within its collection.</summary>
    [JsonPropertyName("rkey")]
    public RecordKey? Rkey { get; init; }

    /// <summary>The record value to create.</summary>
    [JsonPropertyName("value")]
    public required object Value { get; init; }
}

/// <summary>An update operation within an <c>applyWrites</c> batch.</summary>
public sealed class ApplyWriteUpdate : ApplyWriteOperation
{
    /// <summary>The NSID of the collection the record belongs to (e.g. <c>app.bsky.feed.post</c>).</summary>
    [JsonPropertyName("collection")]
    public required Nsid Collection { get; init; }

    /// <summary>The record key identifying the record within its collection.</summary>
    [JsonPropertyName("rkey")]
    public required RecordKey Rkey { get; init; }

    /// <summary>The record value to write.</summary>
    [JsonPropertyName("value")]
    public required object Value { get; init; }
}

/// <summary>A delete operation within an <c>applyWrites</c> batch.</summary>
public sealed class ApplyWriteDelete : ApplyWriteOperation
{
    /// <summary>The NSID of the collection the record belongs to (e.g. <c>app.bsky.feed.post</c>).</summary>
    [JsonPropertyName("collection")]
    public required Nsid Collection { get; init; }

    /// <summary>The record key identifying the record within its collection.</summary>
    [JsonPropertyName("rkey")]
    public required RecordKey Rkey { get; init; }
}

/// <summary>Response from com.atproto.repo.applyWrites.</summary>
public sealed class ApplyWritesResponse
{
    /// <summary>The commit the write was applied in.</summary>
    [JsonPropertyName("commit")]
    public CommitMeta? Commit { get; init; }

    /// <summary>The per-write results, in the same order as the request.</summary>
    [JsonPropertyName("results")]
    public IReadOnlyList<ApplyWriteResult>? Results { get; init; }
}

/// <summary>The result of a single write within an <c>applyWrites</c> batch.</summary>
public sealed class ApplyWriteResult : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public AtUri? Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public Cid? Cid { get; init; }

    /// <summary>Whether the server validated the record against a known Lexicon (<c>valid</c> or <c>unknown</c>).</summary>
    [JsonPropertyName("validationStatus")]
    public string? ValidationStatus { get; init; }
}

/// <summary>Response from com.atproto.repo.listMissingBlobs.</summary>
public sealed record ListMissingBlobsResponse : CursorPage<MissingBlob>
{
    /// <summary>The blob references.</summary>
    [JsonPropertyName("blobs")]
    public IReadOnlyList<MissingBlob> Blobs { get; init; } = [];

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<MissingBlob> Items => Blobs;
}

/// <summary>A blob referenced by a record that has not been uploaded yet.</summary>
public sealed class MissingBlob : LexObject
{
    /// <summary>The CID of the missing blob.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>The AT-URI of the record that references the blob.</summary>
    [JsonPropertyName("recordUri")]
    public required AtUri RecordUri { get; init; }
}

/// <summary>Commit metadata included in write operation responses.</summary>
public sealed class CommitMeta : LexObject
{
    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>The repository revision (a TID) this data was read at.</summary>
    [JsonPropertyName("rev")]
    public required Tid Rev { get; init; }
}
