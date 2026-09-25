using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.App.Bsky.Video;

// ──────────────────────────────────────────────────────────────
//  Job status
// ──────────────────────────────────────────────────────────────

/// <summary>
/// The processing status of a video upload job.
/// </summary>
public sealed class JobStatus : LexObject
{
    /// <summary>The identifier of the processing job.</summary>
    [JsonPropertyName("jobId")]
    public required string JobId { get; init; }

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The job's processing state (see <see cref="JobState"/>).</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    /// <summary>Processing progress percentage (0-100).</summary>
    [JsonPropertyName("progress")]
    public int? Progress { get; init; }

    /// <summary>The processed video blob, available when state is JOB_STATE_COMPLETED.</summary>
    [JsonPropertyName("blob")]
    public BlobRef? Blob { get; init; }

    /// <summary>Error identifier when state is JOB_STATE_FAILED.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>Why the job failed, when state is JOB_STATE_FAILED (see <see cref="JobFailureCode"/>).</summary>
    [JsonPropertyName("failureCode")]
    public string? FailureCode { get; init; }

    /// <summary>Human-readable error message.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// Well-known job state constants.
/// </summary>
public static class JobState
{
    /// <summary>The <c>JOB_STATE_CREATED</c> video processing job state.</summary>
    public const string Created = "JOB_STATE_CREATED";

    /// <summary>The <c>JOB_STATE_ENCODING</c> video processing job state.</summary>
    public const string Encoding = "JOB_STATE_ENCODING";

    /// <summary>The <c>JOB_STATE_ENCODED</c> video processing job state.</summary>
    public const string Encoded = "JOB_STATE_ENCODED";

    /// <summary>The <c>JOB_STATE_SCANNING</c> video processing job state.</summary>
    public const string Scanning = "JOB_STATE_SCANNING";

    /// <summary>The <c>JOB_STATE_SCANNED</c> video processing job state.</summary>
    public const string Scanned = "JOB_STATE_SCANNED";

    /// <summary>The <c>JOB_STATE_UPLOADING</c> video processing job state.</summary>
    public const string Uploading = "JOB_STATE_UPLOADING";

    /// <summary>The <c>JOB_STATE_UPLOADED</c> video processing job state.</summary>
    public const string Uploaded = "JOB_STATE_UPLOADED";

    /// <summary>The <c>JOB_STATE_COMPLETED</c> video processing job state.</summary>
    public const string Completed = "JOB_STATE_COMPLETED";

    /// <summary>The <c>JOB_STATE_FAILED</c> video processing job state.</summary>
    public const string Failed = "JOB_STATE_FAILED";
}

/// <summary>
/// Known values of <see cref="JobStatus.FailureCode"/>.
/// </summary>
public static class JobFailureCode
{
    /// <summary>The upload is not a valid video, or breaks a limit.</summary>
    public const string ValidationFailure = "validation_failure";

    /// <summary>The video could not be encoded.</summary>
    public const string EncodingFailure = "encoding_failure";

    /// <summary>The encoded video could not be uploaded to the account's PDS.</summary>
    public const string PdsUploadFailure = "pds_upload_failure";

    /// <summary>The account's PDS does not accept a blob of the video's size.</summary>
    public const string PdsUploadUnsupportedBlobSize = "pds_upload_unsupported_blob_size";

    /// <summary>Any other failure.</summary>
    public const string GenericFailure = "generic_failure";
}

// ──────────────────────────────────────────────────────────────
//  API responses
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getJobStatus.
/// </summary>
public sealed class GetJobStatusResponse
{
    /// <summary>The status of the processing job.</summary>
    [JsonPropertyName("jobStatus")]
    public required JobStatus JobStatus { get; init; }
}

/// <summary>
/// Response from uploadVideo.
/// </summary>
public sealed class UploadVideoResponse
{
    /// <summary>The status of the processing job.</summary>
    [JsonPropertyName("jobStatus")]
    public required JobStatus JobStatus { get; init; }
}

/// <summary>
/// Response from getUploadLimits.
/// </summary>
public sealed class GetUploadLimitsResponse
{
    /// <summary>Whether the account may currently upload a video.</summary>
    [JsonPropertyName("canUpload")]
    public required bool CanUpload { get; init; }

    /// <summary>The number of videos the account may still upload today.</summary>
    [JsonPropertyName("remainingDailyVideos")]
    public int? RemainingDailyVideos { get; init; }

    /// <summary>The number of bytes the account may still upload today.</summary>
    [JsonPropertyName("remainingDailyBytes")]
    public long? RemainingDailyBytes { get; init; }

    /// <summary>A human-readable explanation of the limit.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>The error code, if the limits could not be determined.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
