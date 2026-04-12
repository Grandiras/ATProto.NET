using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.App.Bsky.Labeler;

/// <summary>
/// Client for <c>app.bsky.labeler.*</c> XRPC endpoints.
/// Handles fetching labeler service information and label definitions.
/// </summary>
public sealed class LabelerClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal LabelerClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// Fetches information about labeler services.
    /// </summary>
    /// <param name="dids">Array of labeler service DIDs to query.</param>
    /// <param name="detailed">Whether to return detailed views (includes policies).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Service views for the requested labelers.</returns>
    public Task<GetLabelerServicesResponse> GetServicesAsync(
        IReadOnlyList<string> dids,
        bool? detailed = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["dids"] = string.Join(",", dids),
            ["detailed"] = detailed?.ToString()?.ToLowerInvariant(),
        };

        return _xrpc.QueryAsync<GetLabelerServicesResponse>(
            "app.bsky.labeler.getServices", parameters, cancellationToken);
    }
}
