using ATProtoNet.Http;

namespace ATProtoNet.Lexicon.Com.AtProto.Label;

/// <summary>
/// Client for com.atproto.label.* XRPC endpoints.
/// </summary>
public sealed class LabelClient
{
    private readonly XrpcClient _xrpc;

    internal LabelClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
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
        var parameters = new XrpcParams()
            .AddAll("uriPatterns", uriPatterns)
            .AddAll("sources", sources)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<QueryLabelsResponse>(
            "com.atproto.label.queryLabels", parameters, cancellationToken);
    }
}
