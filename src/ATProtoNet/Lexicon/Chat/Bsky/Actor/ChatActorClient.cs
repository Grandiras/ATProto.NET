using ATProtoNet.Http;

namespace ATProtoNet.Lexicon.Chat.Bsky.Actor;

/// <summary>
/// Client for chat.bsky.actor.* XRPC endpoints.
/// Handles chat account operations: status, deletion, and data export.
/// </summary>
public sealed class ChatActorClient
{
    private static readonly XrpcCallOptions ChatProxy = new() { Proxy = ServiceProxy.BskyChatHeader };

    private readonly XrpcClient _xrpc;

    internal ChatActorClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Gets the viewer's chat status: whether chat is disabled for the account, whether it may
    /// create groups, and how many members a group may have.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetStatusResponse>(
            "chat.bsky.actor.getStatus", options: ChatProxy, cancellationToken: cancellationToken);

    /// <summary>
    /// Deletes the chat account data for the authenticated user.
    /// </summary>
    public async Task DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        await _xrpc.ProcedureAsync(
            "chat.bsky.actor.deleteAccount", options: ChatProxy, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Exports the chat account data for the authenticated user.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The export as JSON Lines (<c>application/jsonl</c>): one JSON object per line. Dispose it
    /// once read.
    /// </returns>
    public Task<XrpcStreamResponse> ExportAccountDataAsync(CancellationToken cancellationToken = default) =>
        _xrpc.DownloadAsync(
            "chat.bsky.actor.exportAccountData", options: ChatProxy, cancellationToken: cancellationToken);
}
