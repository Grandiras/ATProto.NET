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
    [JsonPropertyName("jobId")]
    public required string JobId { get; init; }

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
    public const string Created = "JOB_STATE_CREATED";
    public const string Encoding = "JOB_STATE_ENCODING";
    public const string Scanning = "JOB_STATE_SCANNING";
    public const string Completed = "JOB_STATE_COMPLETED";
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
    [JsonPropertyName("jobStatus")]
    public required JobStatus JobStatus { get; init; }
}

/// <summary>
/// Response from uploadVideo.
/// </summary>
public sealed class UploadVideoResponse
{
    [JsonPropertyName("jobStatus")]
    public required JobStatus JobStatus { get; init; }
}

/// <summary>
/// Response from getUploadLimits.
/// </summary>
public sealed class GetUploadLimitsResponse
{
    [JsonPropertyName("canUpload")]
    public required bool CanUpload { get; init; }

    [JsonPropertyName("remainingDailyVideos")]
    public int? RemainingDailyVideos { get; init; }

    [JsonPropertyName("remainingDailyBytes")]
    public long? RemainingDailyBytes { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
