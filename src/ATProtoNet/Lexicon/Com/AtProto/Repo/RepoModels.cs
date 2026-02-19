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
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    [JsonPropertyName("cid")]
    public string Cid { get; init; } = string.Empty;

    [JsonPropertyName("commit")]
    public CommitMeta? Commit { get; init; }

    [JsonPropertyName("validationStatus")]
    public string? ValidationStatus { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.getRecord.
/// </summary>
public sealed class GetRecordResponse
{
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    [JsonPropertyName("value")]
    public JsonElement Value { get; init; }
}

/// <summary>
/// Typed response from com.atproto.repo.getRecord.
/// </summary>
public sealed class GetRecordResponse<T>
{
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    [JsonPropertyName("value")]
    public T Value { get; init; } = default!;
}

/// <summary>
/// Request body for com.atproto.repo.putRecord.
/// </summary>
public sealed class PutRecordRequest
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }

    [JsonPropertyName("validate")]
    public bool? Validate { get; init; }

    [JsonPropertyName("record")]
    public required object Record { get; init; }

    [JsonPropertyName("swapRecord")]
    public string? SwapRecord { get; init; }

    [JsonPropertyName("swapCommit")]
    public string? SwapCommit { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.putRecord.
/// </summary>
public sealed class PutRecordResponse
{
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    [JsonPropertyName("cid")]
    public string Cid { get; init; } = string.Empty;

    [JsonPropertyName("commit")]
    public CommitMeta? Commit { get; init; }

    [JsonPropertyName("validationStatus")]
    public string? ValidationStatus { get; init; }
}

/// <summary>
/// Request body for com.atproto.repo.deleteRecord.
/// </summary>
public sealed class DeleteRecordRequest
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }

    [JsonPropertyName("swapRecord")]
    public string? SwapRecord { get; init; }

    [JsonPropertyName("swapCommit")]
    public string? SwapCommit { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.deleteRecord.
/// </summary>
public sealed class DeleteRecordResponse
{
    [JsonPropertyName("commit")]
    public CommitMeta? Commit { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.listRecords.
/// </summary>
public sealed class ListRecordsResponse : ICursoredResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("records")]
    public List<RecordEntry> Records { get; init; } = [];
}

/// <summary>
/// A single record entry in a list response.
/// </summary>
public sealed class RecordEntry
{
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    [JsonPropertyName("cid")]
    public string Cid { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public JsonElement Value { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.describeRepo.
/// </summary>
public sealed class DescribeRepoResponse
{
    [JsonPropertyName("handle")]
    public string Handle { get; init; } = string.Empty;

    [JsonPropertyName("did")]
    public string Did { get; init; } = string.Empty;

    [JsonPropertyName("didDoc")]
    public object? DidDoc { get; init; }

    [JsonPropertyName("collections")]
    public List<string> Collections { get; init; } = [];

    [JsonPropertyName("handleIsCorrect")]
    public bool HandleIsCorrect { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.uploadBlob.
/// </summary>
public sealed class UploadBlobResponse
{
    [JsonPropertyName("blob")]
    public BlobRef Blob { get; init; } = new();
}

/// <summary>
/// Request body for com.atproto.repo.applyWrites.
/// </summary>
public sealed class ApplyWritesRequest
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("validate")]
    public bool? Validate { get; init; }

    [JsonPropertyName("writes")]
    public required List<ApplyWriteOperation> Writes { get; init; }

    [JsonPropertyName("swapCommit")]
    public string? SwapCommit { get; init; }
}

/// <summary>
/// A single write operation in an applyWrites batch.
/// </summary>
[JsonDerivedType(typeof(ApplyWriteCreate), "#create")]
[JsonDerivedType(typeof(ApplyWriteUpdate), "#update")]
[JsonDerivedType(typeof(ApplyWriteDelete), "#delete")]
public abstract class ApplyWriteOperation
{
    [JsonPropertyName("$type")]
    public abstract string Type { get; }
}

public sealed class ApplyWriteCreate : ApplyWriteOperation
{
    [JsonPropertyName("$type")]
    public override string Type => "com.atproto.repo.applyWrites#create";

    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    [JsonPropertyName("rkey")]
    public string? Rkey { get; init; }

    [JsonPropertyName("value")]
    public required object Value { get; init; }
}

public sealed class ApplyWriteUpdate : ApplyWriteOperation
{
    [JsonPropertyName("$type")]
    public override string Type => "com.atproto.repo.applyWrites#update";

    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }

    [JsonPropertyName("value")]
    public required object Value { get; init; }
}

public sealed class ApplyWriteDelete : ApplyWriteOperation
{
    [JsonPropertyName("$type")]
    public override string Type => "com.atproto.repo.applyWrites#delete";

    [JsonPropertyName("collection")]
    public required string Collection { get; init; }

    [JsonPropertyName("rkey")]
    public required string Rkey { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.applyWrites.
/// </summary>
public sealed class ApplyWritesResponse
{
    [JsonPropertyName("commit")]
    public CommitMeta? Commit { get; init; }

    [JsonPropertyName("results")]
    public List<ApplyWriteResult>? Results { get; init; }
}

public sealed class ApplyWriteResult
{
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    [JsonPropertyName("validationStatus")]
    public string? ValidationStatus { get; init; }
}

/// <summary>
/// Response from com.atproto.repo.listMissingBlobs.
/// </summary>
public sealed class ListMissingBlobsResponse : ICursoredResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("blobs")]
    public List<MissingBlob> Blobs { get; init; } = [];
}

public sealed class MissingBlob
{
    [JsonPropertyName("cid")]
    public string Cid { get; init; } = string.Empty;
}

/// <summary>
/// Commit metadata included in write operation responses.
/// </summary>
public sealed class CommitMeta
{
    [JsonPropertyName("cid")]
    public string Cid { get; init; } = string.Empty;

    [JsonPropertyName("rev")]
    public string Rev { get; init; } = string.Empty;
}
