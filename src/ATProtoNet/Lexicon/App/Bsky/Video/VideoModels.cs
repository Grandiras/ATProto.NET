using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.App.Bsky.Video;

// ──────────────────────────────────────────────────────────────
//  Job status
// ──────────────────────────────────────────────────────────────

/// <summary>
/// The processing status of a video upload job.
/// </summary>
public sealed class JobStatus
{
    /// <summary>The identifier of the processing job.</summary>
    [JsonPropertyName("jobId")]
    public required string JobId { get; init; }

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>Job processing state (e.g., JOB_STATE_CREATED, JOB_STATE_ENCODING, JOB_STATE_COMPLETED, JOB_STATE_FAILED).</summary>
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

    /// <summary>The <c>JOB_STATE_SCANNING</c> video processing job state.</summary>
    public const string Scanning = "JOB_STATE_SCANNING";

    /// <summary>The <c>JOB_STATE_COMPLETED</c> video processing job state.</summary>
    public const string Completed = "JOB_STATE_COMPLETED";

    /// <summary>The <c>JOB_STATE_FAILED</c> video processing job state.</summary>
    public const string Failed = "JOB_STATE_FAILED";
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
