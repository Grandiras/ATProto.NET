using ATProtoNet.Http;

namespace ATProtoNet.Lexicon.Chat.Bsky.Actor;

/// <summary>
/// Client for chat.bsky.actor.* XRPC endpoints.
/// Handles chat account operations: declaration, deletion, and data export.
/// </summary>
public sealed class ChatActorClient
{
    private readonly XrpcClient _xrpc;

    internal ChatActorClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Deletes the chat account data for the authenticated user.
    /// </summary>
    public async Task DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        await _xrpc.ProcedureAsync(
            "chat.bsky.actor.deleteAccount", ServiceProxy.BskyChatHeader, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Exports the chat account data for the authenticated user.
    /// Returns a stream of JSONL data.
    /// </summary>
    public Task<byte[]> ExportAccountDataAsync(CancellationToken cancellationToken = default)
    {
        return _xrpc.QueryAsync<byte[]>(
            "chat.bsky.actor.exportAccountData", ServiceProxy.BskyChatHeader, cancellationToken: cancellationToken);
    }
}
