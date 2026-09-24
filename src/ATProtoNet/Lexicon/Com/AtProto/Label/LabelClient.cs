using ATProtoNet.Http;
using ATProtoNet.Identity;

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
        IEnumerable<Did>? sources = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .AddAll("uriPatterns", uriPatterns)
            .AddAll("sources", sources?.Select(did => did.Value))
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<QueryLabelsResponse>(
            "com.atproto.label.queryLabels", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every label matching the patterns, fetching pages as needed.
    /// </summary>
    /// <param name="uriPatterns">AT-URI patterns to match against label subjects.
    /// Supports prefix matching with '*' at the end.</param>
    /// <param name="sources">Optional list of labeler DIDs to filter by.</param>
    /// <param name="pageSize">Labels per request (1-250); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<Models.Label> EnumerateLabelsAsync(
        IEnumerable<string> uriPatterns,
        IEnumerable<Did>? sources = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        // Materialized once: the sequences are read again for every page.
        var patterns = uriPatterns.ToList();
        var sourceList = sources?.ToList();
        return Pagination.EnumerateAsync<QueryLabelsResponse, Models.Label>(
            (cursor, ct) => QueryLabelsAsync(patterns, sourceList, pageSize, cursor, ct),
            cancellationToken);
    }
}
