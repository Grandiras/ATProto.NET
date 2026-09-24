using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Tools.Ozone.Signature;

/// <summary>
/// Client for tools.ozone.signature.* endpoints.
/// </summary>
public sealed class SignatureClient
{
    private readonly XrpcClient _xrpc;

    internal SignatureClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Find signature correlations between multiple DIDs.
    /// </summary>
    /// <param name="dids">The accounts to correlate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<FindCorrelationResponse> FindCorrelationAsync(
        IEnumerable<Did> dids,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .AddAll("dids", dids.Select(did => did.Value));
        return _xrpc.QueryAsync<FindCorrelationResponse>(
            "tools.ozone.signature.findCorrelation", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Search one page of accounts by signature properties.
    /// </summary>
    /// <param name="values">The signature values to search for.</param>
    /// <param name="limit">Maximum number of accounts (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SearchAccountsResponse> SearchAccountsAsync(
        IEnumerable<SigDetail> values,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        // SearchAccounts uses POST with a body
        var request = new SearchAccountsRequest
        {
            Values = [.. values],
            Cursor = cursor,
            Limit = limit,
        };
        return _xrpc.ProcedureAsync<SearchAccountsResponse>(
            "tools.ozone.signature.searchAccounts", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Find one page of the accounts related to a given DID by shared signatures.
    /// </summary>
    /// <param name="did">The account to find relatives of.</param>
    /// <param name="limit">Maximum number of accounts (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<FindRelatedAccountsResponse> FindRelatedAccountsAsync(
        Did did,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("did", did)
            .Add("cursor", cursor)
            .Add("limit", limit);
        return _xrpc.QueryAsync<FindRelatedAccountsResponse>(
            "tools.ozone.signature.findRelatedAccounts", parameters, cancellationToken: cancellationToken);
    }
}

internal sealed class SearchAccountsRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("values")]
    public required IReadOnlyList<SigDetail> Values { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("limit")]
    public int? Limit { get; init; }
}
