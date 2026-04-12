using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.Tools.Ozone.Server;

/// <summary>
/// Client for tools.ozone.server.* endpoints.
/// </summary>
public sealed class OzoneServerClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal OzoneServerClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// Get Ozone server configuration.
    /// </summary>
    public Task<OzoneServerConfig> GetConfigAsync(
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<OzoneServerConfig>(
            "tools.ozone.server.getConfig", cancellationToken: cancellationToken);
}
