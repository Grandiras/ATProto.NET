using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Com.AtProto.Moderation;

/// <summary>
/// Client for com.atproto.moderation.* XRPC endpoints.
/// </summary>
public sealed class ModerationClient
{
    private readonly XrpcClient _xrpc;

    internal ModerationClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Submit a moderation report for a repo (account) or record.
    /// </summary>
    /// <param name="subject">The subject being reported.</param>
    /// <param name="reasonType">The reason type. Use constants from <see cref="ReportReasons"/>.</param>
    /// <param name="reason">Optional free-text description of the report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CreateReportResponse> CreateReportAsync(
        ReportSubject subject,
        string reasonType,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateReportRequest
        {
            ReasonType = reasonType,
            Subject = subject,
            Reason = reason,
        };

        return _xrpc.ProcedureAsync<CreateReportResponse>(
            "com.atproto.moderation.createReport", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Report a repo (account) for moderation.
    /// </summary>
    /// <param name="did">The DID of the account being reported.</param>
    /// <param name="reasonType">The reason type. Use constants from <see cref="ReportReasons"/>.</param>
    /// <param name="reason">Optional free-text description of the report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CreateReportResponse> ReportAccountAsync(
        Did did,
        string reasonType,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        return CreateReportAsync(
            new RepoSubject { Did = did },
            reasonType,
            reason,
            cancellationToken);
    }

    /// <summary>
    /// Report a specific record for moderation.
    /// </summary>
    /// <param name="uri">The AT URI of the record being reported.</param>
    /// <param name="cid">The CID of the record version being reported.</param>
    /// <param name="reasonType">The reason type. Use constants from <see cref="ReportReasons"/>.</param>
    /// <param name="reason">Optional free-text description of the report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CreateReportResponse> ReportRecordAsync(
        AtUri uri,
        Cid cid,
        string reasonType,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        return CreateReportAsync(
            new RecordSubject { Uri = uri, Cid = cid },
            reasonType,
            reason,
            cancellationToken);
    }
}
