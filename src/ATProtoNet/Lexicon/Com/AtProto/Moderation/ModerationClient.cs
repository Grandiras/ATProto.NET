using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.Com.AtProto.Moderation;

/// <summary>
/// Client for com.atproto.moderation.* XRPC endpoints.
/// </summary>
public sealed class ModerationClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal ModerationClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// Submit a moderation report for a repo (account) or record.
    /// </summary>
    /// <param name="reasonType">The reason type. Use constants from <see cref="ReportReasons"/>.</param>
    /// <param name="subject">The subject being reported.</param>
    /// <param name="reason">Optional free-text description of the report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CreateReportResponse> CreateReportAsync(
        string reasonType,
        ReportSubject subject,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateReportRequest
        {
            ReasonType = reasonType,
            Subject = subject,
            Reason = reason,
        };

        return _xrpc.ProcedureAsync<CreateReportRequest, CreateReportResponse>(
            "com.atproto.moderation.createReport", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Report a repo (account) for moderation.
    /// </summary>
    public Task<CreateReportResponse> ReportAccountAsync(
        string did,
        string reasonType,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        return CreateReportAsync(
            reasonType,
            new RepoSubject { Did = did },
            reason,
            cancellationToken);
    }

    /// <summary>
    /// Report a specific record for moderation.
    /// </summary>
    public Task<CreateReportResponse> ReportRecordAsync(
        string uri,
        string cid,
        string reasonType,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        return CreateReportAsync(
            reasonType,
            new RecordSubject { Uri = uri, Cid = cid },
            reason,
            cancellationToken);
    }
}
