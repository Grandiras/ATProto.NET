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
    /// Search one page of the accounts that match any of the given threat-signature values.
    /// </summary>
    /// <param name="values">The signature values to search for (see <see cref="SigDetail.Value"/>).</param>
    /// <param name="limit">Maximum number of accounts (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SearchAccountsResponse> SearchAccountsAsync(
        IEnumerable<string> values,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var parameters = new XrpcParams()
            .AddAll("values", values)
            .Add("limit", limit)
            .Add("cursor", cursor);
        return _xrpc.QueryAsync<SearchAccountsResponse>(
            "tools.ozone.signature.searchAccounts", parameters, cancellationToken: cancellationToken);
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
