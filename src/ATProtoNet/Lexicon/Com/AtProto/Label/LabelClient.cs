using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.Com.AtProto.Label;

/// <summary>
/// Client for com.atproto.label.* XRPC endpoints.
/// </summary>
public sealed class LabelClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal LabelClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// Query labels by subject URIs or DIDs.
    /// </summary>
    /// <param name="uriPatterns">AT-URI patterns to match against label subjects.
    /// Supports prefix matching with '*' at the end.</param>
    /// <param name="sources">Optional list of labeler DIDs to filter by.
    /// If empty, returns labels from all sources.</param>
    /// <param name="limit">Maximum results per page (default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<QueryLabelsResponse> QueryLabelsAsync(
        IEnumerable<string> uriPatterns,
        IEnumerable<string>? sources = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["uriPatterns"] = string.Join(",", uriPatterns),
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        if (sources is not null)
            parameters["sources"] = string.Join(",", sources);

        return _xrpc.QueryAsync<QueryLabelsResponse>(
            "com.atproto.label.queryLabels", parameters, cancellationToken);
    }
}
