using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.Chat.Bsky.Convo;

/// <summary>
/// Client for chat.bsky.convo.* XRPC endpoints.
/// Handles direct message conversations: listing, sending, reading, and managing messages.
/// <para>
/// All requests are automatically proxied via <c>atproto-proxy</c> header to the chat service.
/// Requires the <c>transition:chat.bsky</c> OAuth scope.
/// </para>
/// </summary>
public sealed class ConvoClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal ConvoClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────
    //  Conversation listing & retrieval
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Lists conversations for the authenticated user.
    /// </summary>
    public Task<ListConvosResponse> ListConvosAsync(
        int? limit = null, string? cursor = null, bool? readOnly = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
            ["readOnly"] = readOnly?.ToString()?.ToLowerInvariant(),
            ["status"] = status,
        };

        return _xrpc.QueryAsync<ListConvosResponse>(
            "chat.bsky.convo.listConvos", ServiceProxy.BskyChatHeader, parameters, cancellationToken);
    }

    /// <summary>
    /// Gets a specific conversation by ID.
    /// </summary>
    public Task<GetConvoResponse> GetConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["convoId"] = convoId,
        };

        return _xrpc.QueryAsync<GetConvoResponse>(
            "chat.bsky.convo.getConvo", ServiceProxy.BskyChatHeader, parameters, cancellationToken);
    }

    /// <summary>
    /// Gets (or creates) a conversation for the given members.
    /// </summary>
    public Task<GetConvoForMembersResponse> GetConvoForMembersAsync(
        IReadOnlyList<string> members,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["members"] = string.Join(",", members),
        };

        return _xrpc.QueryAsync<GetConvoForMembersResponse>(
            "chat.bsky.convo.getConvoForMembers", ServiceProxy.BskyChatHeader, parameters, cancellationToken);
    }

    /// <summary>
    /// Checks whether a conversation can be created with specified members.
    /// </summary>
    public Task<GetConvoAvailabilityResponse> GetConvoAvailabilityAsync(
        IReadOnlyList<string> members,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["members"] = string.Join(",", members),
        };

        return _xrpc.QueryAsync<GetConvoAvailabilityResponse>(
            "chat.bsky.convo.getConvoAvailability", ServiceProxy.BskyChatHeader, parameters, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Messages
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Gets messages in a conversation.
    /// </summary>
    public Task<GetMessagesResponse> GetMessagesAsync(
        string convoId, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["convoId"] = convoId,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetMessagesResponse>(
            "chat.bsky.convo.getMessages", ServiceProxy.BskyChatHeader, parameters, cancellationToken);
    }

    /// <summary>
    /// Sends a message in a conversation.
    /// </summary>
    public Task<MessageView> SendMessageAsync(
        string convoId, MessageInput message,
        CancellationToken cancellationToken = default)
    {
        var request = new SendMessageRequest
        {
            ConvoId = convoId,
            Message = message,
        };

        return _xrpc.ProcedureAsync<SendMessageRequest, MessageView>(
            "chat.bsky.convo.sendMessage", request, ServiceProxy.BskyChatHeader,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends a batch of messages (potentially to different conversations).
    /// </summary>
    public Task<SendMessageBatchResponse> SendMessageBatchAsync(
        List<BatchMessageItem> items,
        CancellationToken cancellationToken = default)
    {
        var request = new SendMessageBatchRequest { Items = items };

        return _xrpc.ProcedureAsync<SendMessageBatchRequest, SendMessageBatchResponse>(
            "chat.bsky.convo.sendMessageBatch", request, ServiceProxy.BskyChatHeader,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Deletes a message for the authenticated user only.
    /// </summary>
    public Task<DeletedMessageView> DeleteMessageForSelfAsync(
        string convoId, string messageId,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteMessageForSelfRequest
        {
            ConvoId = convoId,
            MessageId = messageId,
        };

        return _xrpc.ProcedureAsync<DeleteMessageForSelfRequest, DeletedMessageView>(
            "chat.bsky.convo.deleteMessageForSelf", request, ServiceProxy.BskyChatHeader,
            cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Conversation management
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Leaves a conversation.
    /// </summary>
    public Task<LeaveConvoResponse> LeaveConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var request = new LeaveConvoRequest { ConvoId = convoId };

        return _xrpc.ProcedureAsync<LeaveConvoRequest, LeaveConvoResponse>(
            "chat.bsky.convo.leaveConvo", request, ServiceProxy.BskyChatHeader,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Mutes a conversation.
    /// </summary>
    public Task<ConvoView> MuteConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var request = new MuteConvoRequest { ConvoId = convoId };

        return _xrpc.ProcedureAsync<MuteConvoRequest, ConvoView>(
            "chat.bsky.convo.muteConvo", request, ServiceProxy.BskyChatHeader,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unmutes a conversation.
    /// </summary>
    public Task<ConvoView> UnmuteConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var request = new UnmuteConvoRequest { ConvoId = convoId };

        return _xrpc.ProcedureAsync<UnmuteConvoRequest, ConvoView>(
            "chat.bsky.convo.unmuteConvo", request, ServiceProxy.BskyChatHeader,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Marks a conversation (or specific message) as read.
    /// </summary>
    public Task<ConvoView> UpdateReadAsync(
        string convoId, string? messageId = null,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateReadRequest
        {
            ConvoId = convoId,
            MessageId = messageId,
        };

        return _xrpc.ProcedureAsync<UpdateReadRequest, ConvoView>(
            "chat.bsky.convo.updateRead", request, ServiceProxy.BskyChatHeader,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Marks all conversations as read.
    /// </summary>
    public async Task UpdateAllReadAsync(
        CancellationToken cancellationToken = default)
    {
        await _xrpc.ProcedureAsync(
            "chat.bsky.convo.updateAllRead", ServiceProxy.BskyChatHeader,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Accepts a conversation request.
    /// </summary>
    public Task<AcceptConvoResponse> AcceptConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var request = new AcceptConvoRequest { ConvoId = convoId };

        return _xrpc.ProcedureAsync<AcceptConvoRequest, AcceptConvoResponse>(
            "chat.bsky.convo.acceptConvo", request, ServiceProxy.BskyChatHeader,
            cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Reactions
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a reaction to a message.
    /// </summary>
    public Task<MessageView> AddReactionAsync(
        string convoId, string messageId, string value,
        CancellationToken cancellationToken = default)
    {
        var request = new AddReactionRequest
        {
            ConvoId = convoId,
            MessageId = messageId,
            Value = value,
        };

        return _xrpc.ProcedureAsync<AddReactionRequest, MessageView>(
            "chat.bsky.convo.addReaction", request, ServiceProxy.BskyChatHeader,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Removes a reaction from a message.
    /// </summary>
    public Task<MessageView> RemoveReactionAsync(
        string convoId, string messageId, string value,
        CancellationToken cancellationToken = default)
    {
        var request = new RemoveReactionRequest
        {
            ConvoId = convoId,
            MessageId = messageId,
            Value = value,
        };

        return _xrpc.ProcedureAsync<RemoveReactionRequest, MessageView>(
            "chat.bsky.convo.removeReaction", request, ServiceProxy.BskyChatHeader,
            cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Log
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the conversation log (events for all conversations).
    /// </summary>
    public Task<GetLogResponse> GetLogAsync(
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetLogResponse>(
            "chat.bsky.convo.getLog", ServiceProxy.BskyChatHeader, parameters, cancellationToken);
    }
}
