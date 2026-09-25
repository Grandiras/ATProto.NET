using ATProtoNet.Http;

namespace ATProtoNet.Lexicon.Chat.Bsky.Notification;

/// <summary>
/// Client for chat.bsky.notification.* XRPC endpoints: the viewer's chat notification
/// preferences. They replace the deprecated <c>chat</c> entry of
/// <c>app.bsky.notification.defs#preferences</c>.
/// <para>
/// All requests are automatically proxied via <c>atproto-proxy</c> header to the chat service.
/// </para>
/// </summary>
public sealed class ChatNotificationClient
{
    private static readonly XrpcCallOptions ChatProxy = new() { Proxy = ServiceProxy.BskyChatHeader };

    private readonly XrpcClient _xrpc;

    internal ChatNotificationClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Gets the viewer's chat notification preferences, or the defaults when none are set.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ChatNotificationPreferences> GetPreferencesAsync(
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.QueryAsync<GetPreferencesResponse>(
            "chat.bsky.notification.getPreferences", options: ChatProxy, cancellationToken: cancellationToken);
        return output.Preferences;
    }

    /// <summary>
    /// Sets the viewer's chat notification preferences. A preference left <see langword="null"/>
    /// stays as it is.
    /// </summary>
    /// <param name="chat">Notifications for messages in accepted conversations.</param>
    /// <param name="chatRequest">Notifications for conversation requests.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The preferences after the change.</returns>
    public async Task<ChatNotificationPreferences> PutPreferencesAsync(
        ChatPreference? chat = null,
        ChatPreference? chatRequest = null,
        CancellationToken cancellationToken = default)
    {
        var request = new PutPreferencesRequest { Chat = chat, ChatRequest = chatRequest };

        var output = await _xrpc.ProcedureAsync<PutPreferencesResponse>(
            "chat.bsky.notification.putPreferences", request, options: ChatProxy,
            cancellationToken: cancellationToken);
        return output.Preferences;
    }
}
