using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.Tools.Ozone.Signature;

/// <summary>
/// Client for tools.ozone.signature.* endpoints.
/// </summary>
public sealed class SignatureClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal SignatureClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// Find signature correlations between multiple DIDs.
    /// </summary>
    public Task<FindCorrelationResponse> FindCorrelationAsync(
        List<string> dids,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["dids"] = string.Join(",", dids),
        };
        return _xrpc.QueryAsync<FindCorrelationResponse>(
            "tools.ozone.signature.findCorrelation", parameters, cancellationToken);
    }

    /// <summary>
    /// Search accounts by signature properties.
    /// </summary>
    public Task<SearchAccountsResponse> SearchAccountsAsync(
        List<SigDetail> values,
        string? cursor = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        // SearchAccounts uses POST with a body
        var request = new SearchAccountsRequest
        {
            Values = values,
            Cursor = cursor,
            Limit = limit,
        };
        return _xrpc.ProcedureAsync<SearchAccountsRequest, SearchAccountsResponse>(
            "tools.ozone.signature.searchAccounts", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Find accounts related to a given DID by shared signatures.
    /// </summary>
    public Task<FindRelatedAccountsResponse> FindRelatedAccountsAsync(
        string did,
        string? cursor = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["did"] = did,
            ["cursor"] = cursor,
            ["limit"] = limit?.ToString(),
        };
        return _xrpc.QueryAsync<FindRelatedAccountsResponse>(
            "tools.ozone.signature.findRelatedAccounts", parameters, cancellationToken);
    }
}

internal sealed class SearchAccountsRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("values")]
    public required List<SigDetail> Values { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("limit")]
    public int? Limit { get; init; }
}
