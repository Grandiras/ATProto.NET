using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

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
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal VideoClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// Upload a video file for processing.
    /// </summary>
    /// <param name="data">The video data stream.</param>
    /// <param name="mimeType">The MIME type (e.g., "video/mp4").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The initial job status for the upload.</returns>
    public Task<UploadVideoResponse> UploadVideoAsync(
        Stream data, string mimeType = "video/mp4",
        CancellationToken cancellationToken = default)
    {
        return _xrpc.UploadBlobAsync<UploadVideoResponse>(
            "app.bsky.video.uploadVideo", data, mimeType, cancellationToken);
    }

    /// <summary>
    /// Get the processing status of a video upload job.
    /// </summary>
    /// <param name="jobId">The job identifier returned from upload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetJobStatusResponse> GetJobStatusAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?> { ["jobId"] = jobId };
        return _xrpc.QueryAsync<GetJobStatusResponse>(
            "app.bsky.video.getJobStatus", parameters, cancellationToken);
    }

    /// <summary>
    /// Get the current video upload limits for the authenticated account.
    /// </summary>
    public Task<GetUploadLimitsResponse> GetUploadLimitsAsync(
        CancellationToken cancellationToken = default)
    {
        return _xrpc.QueryAsync<GetUploadLimitsResponse>(
            "app.bsky.video.getUploadLimits", null, cancellationToken);
    }
}
