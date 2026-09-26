using System.Buffers;
using System.Net;
using ATProtoNet.Http;

namespace ATProtoNet.Lexicon.App.Bsky.Video;

/// <summary>
/// Client for app.bsky.video.* XRPC endpoints.
/// Handles video upload, processing status, and upload limits.
/// </summary>
/// <remarks>
/// Video uploads are typically handled by a sidecar service (e.g., <c>https://video.bsky.app</c>)
/// before the final blob is written to the PDS. Use <see cref="AtProtoClient.SetProxy"/> to
/// route requests through the video service when needed.
/// </remarks>
public sealed class VideoClient
{
    private const string OctetStream = "application/octet-stream";

    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AbortTimeout = TimeSpan.FromSeconds(10);

    private readonly XrpcClient _xrpc;

    internal VideoClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Upload a video in parts, wait for the service to process it, and return the finished job,
    /// whose <see cref="JobStatus.Blob"/> goes into a video embed.
    /// </summary>
    /// <param name="data">
    /// The video, read once from its current position. A stream that cannot seek needs
    /// <see cref="VideoUploadOptions.Length"/>. The stream is not disposed.
    /// </param>
    /// <param name="mimeType">The MIME type (e.g., "video/mp4").</param>
    /// <param name="options">Upload settings; <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">
    /// Cancellation token. Cancelling before the upload is finished aborts the upload session;
    /// cancelling while processing only stops waiting.
    /// </param>
    /// <returns>The completed job; its <see cref="JobStatus.Blob"/> is set.</returns>
    /// <remarks>
    /// <para>This runs the multipart flow: <see cref="StartUploadAsync"/> declares the size and
    /// the service chooses the part size, each part goes up with <see cref="UploadPartAsync"/>,
    /// <see cref="FinishUploadAsync"/> hands the video to a processing job, and
    /// <see cref="GetJobStatusAsync"/> is polled until the job completes or fails. One part is
    /// held in memory at a time.</para>
    /// <para>Parts are idempotent, so a transient failure (see
    /// <see cref="VideoUploadOptions.MaxAttempts"/>) is retried with exponential backoff; any
    /// other error aborts the session, releasing its share of the daily quota, and is
    /// rethrown.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The stream cannot seek and no length was given, or it ends before that length.
    /// </exception>
    /// <exception cref="XrpcException">
    /// The service refused the upload; match <see cref="XrpcException.Error"/> with
    /// <see cref="VideoErrors"/>.
    /// </exception>
    /// <exception cref="VideoUploadException">The processing job failed.</exception>
    public async Task<JobStatus> UploadVideoAsync(
        Stream data,
        string mimeType = "video/mp4",
        VideoUploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        options ??= VideoUploadOptions.Default;
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxAttempts, 1, "options.MaxAttempts");
        ArgumentOutOfRangeException.ThrowIfLessThan(options.RetryDelay, TimeSpan.Zero, "options.RetryDelay");
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PollInterval, TimeSpan.Zero, "options.PollInterval");
        if (!data.CanRead)
            throw new ArgumentException("The video stream is not readable.", nameof(data));

        var size = options.Length
            ?? (data.CanSeek
                ? data.Length - data.Position
                : throw new ArgumentException(
                    "The video stream cannot seek, so its length must be given in VideoUploadOptions.Length.",
                    nameof(data)));
        if (size <= 0)
            throw new ArgumentException("The video is empty.", nameof(data));

        var session = await WithRetriesAsync(
            ct => StartUploadAsync(
                size, mimeType, options.FileName, (long?)options.Duration?.TotalMilliseconds,
                options.Width, options.Height, ct),
            RetryKind.Start, options, cancellationToken).ConfigureAwait(false);

        FinishUploadResponse finished;
        try
        {
            var partSize = CheckPlan(session, size);
            var buffer = ArrayPool<byte>.Shared.Rent(partSize);
            try
            {
                for (var part = 1; part <= session.PartCount; part++)
                {
                    var length = (int)Math.Min(partSize, size - ((part - 1) * session.PartSizeBytes));
                    await ReadPartAsync(data, buffer, length, cancellationToken).ConfigureAwait(false);

                    var partNumber = part;
                    await WithRetriesAsync(
                        ct => UploadPartAsync(
                            session.JobId, partNumber, new MemoryStream(buffer, 0, length, writable: false), ct),
                        RetryKind.Idempotent, options, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            finished = await WithRetriesAsync(
                ct => FinishUploadAsync(session.JobId, ct),
                RetryKind.Finish, options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryAbortAsync(session.JobId).ConfigureAwait(false);
            throw;
        }

        return await WaitForJobAsync(finished.CompletedJobId, finished.JobStatus, options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Upload a whole video in one request (<c>app.bsky.video.uploadVideo</c>) and return the
    /// processing job without waiting for it. <see cref="UploadVideoAsync"/> uploads in parts,
    /// with retries, and waits for processing.
    /// </summary>
    /// <param name="data">The video data stream.</param>
    /// <param name="mimeType">The MIME type (e.g., "video/mp4").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The initial job status for the upload.</returns>
    public Task<UploadVideoResponse> UploadVideoInOneRequestAsync(
        Stream data, string mimeType = "video/mp4",
        CancellationToken cancellationToken = default)
    {
        return _xrpc.UploadAsync<UploadVideoResponse>(
            "app.bsky.video.uploadVideo", data, mimeType, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the processing status of a video upload job.
    /// </summary>
    /// <param name="jobId">The job identifier returned from upload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetJobStatusResponse> GetJobStatusAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("jobId", jobId);
        return _xrpc.QueryAsync<GetJobStatusResponse>(
            "app.bsky.video.getJobStatus", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the current video upload limits for the authenticated account.
    /// </summary>
    public Task<GetUploadLimitsResponse> GetUploadLimitsAsync(
        CancellationToken cancellationToken = default)
    {
        return _xrpc.QueryAsync<GetUploadLimitsResponse>(
            "app.bsky.video.getUploadLimits", null, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Multipart upload
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Start a multipart upload session. The service answers with the part size and count.
    /// </summary>
    /// <param name="sizeBytes">The exact size of the whole video, in bytes.</param>
    /// <param name="mimeType">The MIME type (e.g., "video/mp4").</param>
    /// <param name="name">The file name, if any.</param>
    /// <param name="durationMs">
    /// The duration in milliseconds, if known; advisory, used only to reject a video early.
    /// </param>
    /// <param name="width">The width in pixels, if known; advisory.</param>
    /// <param name="height">The height in pixels, if known; advisory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<StartUploadResponse> StartUploadAsync(
        long sizeBytes,
        string mimeType,
        string? name = null,
        long? durationMs = null,
        int? width = null,
        int? height = null,
        CancellationToken cancellationToken = default)
    {
        var request = new StartUploadRequest
        {
            SizeBytes = sizeBytes,
            MimeType = mimeType,
            Name = name,
            DurationMs = durationMs,
            Width = width,
            Height = height,
        };

        return _xrpc.ProcedureAsync<StartUploadResponse>(
            "app.bsky.video.startUpload", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Upload one part of a multipart upload. Parts may be sent in any order, and resent.
    /// </summary>
    /// <param name="jobId">The upload session, from <see cref="StartUploadAsync"/>.</param>
    /// <param name="partNumber">The part's number, from 1.</param>
    /// <param name="data">
    /// The part's bytes, from the stream's position to its end. Its length must be exactly the
    /// size the session expects for the part, so the stream should be seekable: the service
    /// needs a <c>Content-Length</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<UploadPartResponse> UploadPartAsync(
        string jobId, int partNumber, Stream data, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("jobId", jobId)
            .Add("partNumber", partNumber);

        return _xrpc.UploadAsync<UploadPartResponse>(
            "app.bsky.video.uploadPart", data, OctetStream, parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Finish a multipart upload and hand the video to a processing job. Safe to retry.
    /// </summary>
    /// <param name="jobId">The upload session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The processing job to poll with <see cref="GetJobStatusAsync"/>. Validation of the video
    /// itself fails later, as a <see cref="JobState.Failed"/> job.
    /// </returns>
    public Task<FinishUploadResponse> FinishUploadAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        var request = new FinishUploadRequest { JobId = jobId };
        return _xrpc.ProcedureAsync<FinishUploadResponse>(
            "app.bsky.video.finishUpload", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the state of a multipart upload session, including the parts stored so far.
    /// </summary>
    /// <param name="jobId">The upload session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetUploadStatusResponse> GetUploadStatusAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("jobId", jobId);
        return _xrpc.QueryAsync<GetUploadStatusResponse>(
            "app.bsky.video.getUploadStatus", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Abort a multipart upload session that is still open, releasing its share of the daily
    /// quota. A session that already ended keeps, and reports, its outcome.
    /// </summary>
    /// <param name="jobId">The upload session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<AbortUploadResponse> AbortUploadAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        var request = new AbortUploadRequest { JobId = jobId };
        return _xrpc.ProcedureAsync<AbortUploadResponse>(
            "app.bsky.video.abortUpload", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Checks the service's plan against the declared size and returns the buffer size one part
    /// needs. The size bounds the buffer, whatever part size the service names.
    /// </summary>
    private static int CheckPlan(StartUploadResponse session, long size)
    {
        if (session.PartSizeBytes <= 0 || session.PartCount != ((size - 1) / session.PartSizeBytes) + 1)
        {
            throw new VideoUploadException(
                $"The video service planned {session.PartCount} parts of {session.PartSizeBytes} bytes, "
                + $"which does not fit a {size}-byte video.");
        }

        var partSize = Math.Min(session.PartSizeBytes, size);
        if (partSize > Array.MaxLength)
            throw new VideoUploadException($"The video service's part size of {partSize} bytes is too large to buffer.");

        return (int)partSize;
    }

    private static async Task ReadPartAsync(Stream data, byte[] buffer, int length, CancellationToken cancellationToken)
    {
        try
        {
            await data.ReadExactlyAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException ex)
        {
            throw new ArgumentException("The video stream ended before the declared length.", nameof(data), ex);
        }
    }

    private async Task<JobStatus> WaitForJobAsync(
        string jobId, JobStatus status, VideoUploadOptions options, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (status.State == JobState.Completed)
            {
                return status.Blob is not null
                    ? status
                    : throw new VideoUploadException($"Video job {jobId} completed without a blob.", status);
            }

            if (status.State == JobState.Failed)
            {
                var reason = status.FailureCode ?? status.Error ?? "no reason given";
                throw new VideoUploadException(
                    $"Video job {jobId} failed ({reason}){(status.Message is { } message ? $": {message}" : ".")}",
                    status);
            }

            await Task.Delay(options.PollInterval, _xrpc.TimeProvider, cancellationToken).ConfigureAwait(false);
            var response = await WithRetriesAsync(
                ct => GetJobStatusAsync(jobId, ct), RetryKind.Idempotent, options, cancellationToken)
                .ConfigureAwait(false);
            status = response.JobStatus;
        }
    }

    /// <summary>
    /// Runs a call, retrying transient failures with exponential backoff.
    /// </summary>
    private async Task<T> WithRetriesAsync<T>(
        Func<CancellationToken, Task<T>> call,
        RetryKind kind,
        VideoUploadOptions options,
        CancellationToken cancellationToken)
    {
        var delay = options.RetryDelay;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < options.MaxAttempts && IsTransient(ex, kind, cancellationToken))
            {
                await Task.Delay(delay, _xrpc.TimeProvider, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
    }

    private static bool IsTransient(Exception exception, RetryKind kind, CancellationToken cancellationToken)
    {
        // Refused outright, so nothing happened on the service and any call may be resent.
        if (exception is XrpcException refused
            && (refused.Is(VideoErrors.ServiceOverloaded) || refused.StatusCode == HttpStatusCode.TooManyRequests))
        {
            return true;
        }

        // Anything else may have taken effect before it failed, which only an idempotent call
        // can shrug off: a resent startUpload would open a second session.
        if (kind == RetryKind.Start)
            return false;

        return exception switch
        {
            XrpcException xrpc => (int)xrpc.StatusCode >= 500
                || (kind == RetryKind.Finish && xrpc.Is(VideoErrors.UploadNotReady)),
            HttpRequestException or TimeoutException => true,

            // HttpClient.Timeout surfaces as a cancellation the caller did not ask for.
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false,
        };
    }

    /// <summary>
    /// Aborts a session after a failure, so it stops counting against the daily quota. Best
    /// effort: an open session also expires on its own.
    /// </summary>
    private async Task TryAbortAsync(string jobId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(AbortTimeout);
            await AbortUploadAsync(jobId, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AtProtoException or HttpRequestException or OperationCanceledException or TimeoutException)
        {
        }
    }

    /// <summary>Which failures a call may be retried after.</summary>
    private enum RetryKind
    {
        /// <summary>startUpload: only a refusal, since it is not idempotent.</summary>
        Start,

        /// <summary>uploadPart, getJobStatus: any transient failure.</summary>
        Idempotent,

        /// <summary>finishUpload: any transient failure, and a finish already in progress.</summary>
        Finish,
    }
}
