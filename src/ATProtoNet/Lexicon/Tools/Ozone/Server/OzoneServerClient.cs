using ATProtoNet.Http;

namespace ATProtoNet.Lexicon.Tools.Ozone.Server;

/// <summary>
/// Client for tools.ozone.server.* endpoints.
/// </summary>
public sealed class OzoneServerClient
{
    private readonly XrpcClient _xrpc;

    internal OzoneServerClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Get Ozone server configuration.
    /// </summary>
    public Task<OzoneServerConfig> GetConfigAsync(
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<OzoneServerConfig>(
            "tools.ozone.server.getConfig", cancellationToken: cancellationToken);
}
