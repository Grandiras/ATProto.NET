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

// ──────────────────────────────────────────────────────────────
//  Multipart upload
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for startUpload.
/// </summary>
internal sealed class StartUploadRequest
{
    /// <summary>The exact size of the whole video, in bytes.</summary>
    [JsonPropertyName("sizeBytes")]
    public required long SizeBytes { get; init; }

    /// <summary>The video's MIME type.</summary>
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; init; }

    /// <summary>The file name, if the client has one.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The advisory duration in milliseconds.</summary>
    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; init; }

    /// <summary>The advisory width in pixels.</summary>
    [JsonPropertyName("width")]
    public int? Width { get; init; }

    /// <summary>The advisory height in pixels.</summary>
    [JsonPropertyName("height")]
    public int? Height { get; init; }
}

/// <summary>
/// Response from startUpload: how to split the video, and until when the session is open.
/// </summary>
public sealed class StartUploadResponse
{
    /// <summary>The upload session's identifier.</summary>
    [JsonPropertyName("jobId")]
    public required string JobId { get; init; }

    /// <summary>The size of every part but the last, in bytes. The last part holds the rest.</summary>
    [JsonPropertyName("partSizeBytes")]
    public required long PartSizeBytes { get; init; }

    /// <summary>The number of parts, numbered from 1.</summary>
    [JsonPropertyName("partCount")]
    public required int PartCount { get; init; }

    /// <summary>When the session expires if it is not finished.</summary>
    [JsonPropertyName("expiresAt")]
    public required AtDatetime ExpiresAt { get; init; }
}

/// <summary>
/// Response from uploadPart.
/// </summary>
public sealed class UploadPartResponse
{
    /// <summary>The part that was stored.</summary>
    [JsonPropertyName("partNumber")]
    public required int PartNumber { get; init; }

    /// <summary>The part's size in bytes.</summary>
    [JsonPropertyName("sizeBytes")]
    public required long SizeBytes { get; init; }
}

/// <summary>
/// Request body for finishUpload.
/// </summary>
internal sealed class FinishUploadRequest
{
    /// <summary>The upload session to finish.</summary>
    [JsonPropertyName("jobId")]
    public required string JobId { get; init; }
}

/// <summary>
/// Response from finishUpload.
/// </summary>
public sealed class FinishUploadResponse
{
    /// <summary>
    /// The processing job to poll with <see cref="VideoClient.GetJobStatusAsync"/>. When the
    /// service recognizes a video it already processed, this is that video's job rather than the
    /// upload's.
    /// </summary>
    [JsonPropertyName("completedJobId")]
    public required string CompletedJobId { get; init; }

    /// <summary>The processing job's status.</summary>
    [JsonPropertyName("jobStatus")]
    public required JobStatus JobStatus { get; init; }
}

/// <summary>
/// Response from getUploadStatus: the authoritative state of an upload session.
/// </summary>
public sealed class GetUploadStatusResponse
{
    /// <summary>The upload session's identifier.</summary>
    [JsonPropertyName("jobId")]
    public required string JobId { get; init; }

    /// <summary>The size of every part but the last, in bytes.</summary>
    [JsonPropertyName("partSizeBytes")]
    public required long PartSizeBytes { get; init; }

    /// <summary>The number of parts.</summary>
    [JsonPropertyName("partCount")]
    public required int PartCount { get; init; }

    /// <summary>The numbers of the parts the service has stored.</summary>
    [JsonPropertyName("receivedParts")]
    public required IReadOnlyList<int> ReceivedParts { get; init; }

    /// <summary>When the session expires if it is not finished.</summary>
    [JsonPropertyName("expiresAt")]
    public required AtDatetime ExpiresAt { get; init; }

    /// <summary>The session's state (see <see cref="UploadState"/>).</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    /// <summary>The processing job, once the session completed.</summary>
    [JsonPropertyName("completedJobId")]
    public string? CompletedJobId { get; init; }

    /// <summary>The processing job's status, once the session completed.</summary>
    [JsonPropertyName("jobStatus")]
    public JobStatus? JobStatus { get; init; }

    /// <summary>Why the session failed, when it did.</summary>
    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }
}

/// <summary>
/// Request body for abortUpload.
/// </summary>
internal sealed class AbortUploadRequest
{
    /// <summary>The upload session to abort.</summary>
    [JsonPropertyName("jobId")]
    public required string JobId { get; init; }
}

/// <summary>
/// Response from abortUpload: the session's final state, which is <see cref="UploadState.Aborted"/>
/// unless it had already ended otherwise.
/// </summary>
public sealed class AbortUploadResponse
{
    /// <summary>The session's state (see <see cref="UploadState"/>).</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    /// <summary>The processing job, when the session had already completed.</summary>
    [JsonPropertyName("completedJobId")]
    public string? CompletedJobId { get; init; }

    /// <summary>Why the session failed, when it had.</summary>
    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }
}

/// <summary>
/// Known values of a multipart upload session's state (<see cref="GetUploadStatusResponse.State"/>).
/// </summary>
public static class UploadState
{
    /// <summary>The session is open for parts.</summary>
    public const string Created = "created";

    /// <summary>A finishUpload call is assembling the parts.</summary>
    public const string Finishing = "finishing";

    /// <summary>The parts were assembled and handed to a processing job.</summary>
    public const string Completed = "completed";

    /// <summary>The session failed; see <see cref="GetUploadStatusResponse.FailureReason"/>.</summary>
    public const string Failed = "failed";

    /// <summary>The session was aborted.</summary>
    public const string Aborted = "aborted";

    /// <summary>The session expired before it was finished.</summary>
    public const string Expired = "expired";
}

/// <summary>
/// Error names the <c>app.bsky.video.*</c> upload methods declare, for matching with
/// <see cref="Http.XrpcException.Is"/>.
/// </summary>
public static class VideoErrors
{
    /// <summary>The declared or detected MIME type is not supported.</summary>
    public const string UnsupportedContentType = "UnsupportedContentType";

    /// <summary>The video is larger than the per-file cap or the remaining daily byte allowance.</summary>
    public const string VideoTooLarge = "VideoTooLarge";

    /// <summary>The declared duration is longer than the limit.</summary>
    public const string VideoTooLong = "VideoTooLong";

    /// <summary>The declared dimensions have an unsupported aspect ratio.</summary>
    public const string BadAspectRatio = "BadAspectRatio";

    /// <summary>The daily video or byte allowance is used up.</summary>
    public const string DailyLimitExceeded = "DailyLimitExceeded";

    /// <summary>The account has as many open upload sessions as it may.</summary>
    public const string TooManyOpenUploads = "TooManyOpenUploads";

    /// <summary>The account may not upload video.</summary>
    public const string UploadForbidden = "UploadForbidden";

    /// <summary>The service is draining or at capacity; retrying later may succeed.</summary>
    public const string ServiceOverloaded = "ServiceOverloaded";

    /// <summary>The upload session is unknown, or too old to be remembered.</summary>
    public const string UploadNotFound = "UploadNotFound";

    /// <summary>The upload session expired.</summary>
    public const string UploadExpired = "UploadExpired";

    /// <summary>The part number is outside the session's range.</summary>
    public const string InvalidPartNumber = "InvalidPartNumber";

    /// <summary>The part's <c>Content-Length</c> is not the size the session expects for it.</summary>
    public const string PartSizeMismatch = "PartSizeMismatch";

    /// <summary>A finishUpload call is in progress; check getUploadStatus and retry.</summary>
    public const string UploadNotReady = "UploadNotReady";

    /// <summary>The upload session failed.</summary>
    public const string UploadFailed = "UploadFailed";

    /// <summary>The upload session was aborted.</summary>
    public const string UploadAborted = "UploadAborted";

    /// <summary>The upload session already completed.</summary>
    public const string UploadAlreadyCompleted = "UploadAlreadyCompleted";

    /// <summary>Not every part was stored; the message lists the missing part numbers.</summary>
    public const string MissingParts = "MissingParts";
}

/// <summary>
/// Settings for <see cref="VideoClient.UploadVideoAsync"/>.
/// </summary>
public sealed record VideoUploadOptions
{
    /// <summary>The settings used when none are passed.</summary>
    public static VideoUploadOptions Default { get; } = new();

    /// <summary>
    /// The exact number of bytes to upload from the stream. Required for a stream that cannot
    /// seek; by default, a seekable stream is uploaded from its position to its end.
    /// </summary>
    public long? Length { get; init; }

    /// <summary>The file name to declare to the service, if any.</summary>
    public string? FileName { get; init; }

    /// <summary>
    /// The video's duration, if known. The service uses it only to reject a video that is too
    /// long before it is uploaded; it measures the video itself afterwards.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>The video's width in pixels, if known. Advisory, like <see cref="Duration"/>.</summary>
    public int? Width { get; init; }

    /// <summary>The video's height in pixels, if known. Advisory, like <see cref="Duration"/>.</summary>
    public int? Height { get; init; }

    /// <summary>
    /// How many times to send each request before giving up on a transient failure: a lost
    /// connection, a timeout, a 5xx or 429 response, or <see cref="VideoErrors.ServiceOverloaded"/>.
    /// Default 4. Other errors are not retried, and neither is starting the session, which is
    /// not idempotent, unless the service refused it with a 429 or
    /// <see cref="VideoErrors.ServiceOverloaded"/>.
    /// </summary>
    public int MaxAttempts { get; init; } = 4;

    /// <summary>
    /// The wait before the first retry. It doubles after each further failure, up to 30 seconds.
    /// Default 1 second.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long to wait between processing-status checks. Default 1 second.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// A video upload that did not produce a usable video: its processing job failed, or ended
/// without a blob, or the service answered with an inconsistent upload plan.
/// </summary>
/// <remarks>
/// XRPC errors from the upload itself (a size limit, an expired session, …) surface as
/// <see cref="Http.XrpcException"/>; match their names with <see cref="VideoErrors"/>.
/// </remarks>
public sealed class VideoUploadException : AtProtoException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="jobStatus">The processing job's final status, when there is one.</param>
    public VideoUploadException(string message, JobStatus? jobStatus = null)
        : base(message)
    {
        JobStatus = jobStatus;
    }

    /// <summary>The processing job's final status, when the failure came from processing.</summary>
    public JobStatus? JobStatus { get; }

    /// <summary>
    /// Why processing failed (see <see cref="JobFailureCode"/>), when the service said so.
    /// </summary>
    public string? FailureCode => JobStatus?.FailureCode;
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
