using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.App.Bsky.Labeler;

/// <summary>
/// Client for <c>app.bsky.labeler.*</c> XRPC endpoints.
/// Handles fetching labeler service information and label definitions.
/// </summary>
public sealed class LabelerClient
{
    private readonly XrpcClient _xrpc;

    internal LabelerClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Fetches information about labeler services.
    /// </summary>
    /// <param name="dids">The DIDs of the labeler services to query.</param>
    /// <param name="detailed">Whether to return detailed views (includes policies).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Service views for the requested labelers.</returns>
    public Task<GetLabelerServicesResponse> GetServicesAsync(
        IEnumerable<Did> dids,
        bool? detailed = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .AddAll("dids", dids.Select(did => did.Value))
            .Add("detailed", detailed);

        return _xrpc.QueryAsync<GetLabelerServicesResponse>(
            "app.bsky.labeler.getServices", parameters, cancellationToken: cancellationToken);
    }
}
