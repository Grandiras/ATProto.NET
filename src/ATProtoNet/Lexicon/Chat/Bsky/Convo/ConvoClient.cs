using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Identity;

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
    private static readonly XrpcCallOptions ChatProxy = new() { Proxy = ServiceProxy.BskyChatHeader };

    private readonly XrpcClient _xrpc;

    internal ConvoClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    // ──────────────────────────────────────────────────────────
    //  Conversation listing & retrieval
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Lists one page of conversations for the authenticated user.
    /// </summary>
    /// <param name="readOnly">Read-state filter, sent as <c>readOnly</c>.</param>
    /// <param name="status">Only conversations with this status (<c>request</c> or <c>accepted</c>).</param>
    /// <param name="limit">Maximum number of conversations (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListConvosResponse> ListConvosAsync(
        bool? readOnly = null,
        string? status = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("readOnly", readOnly)
            .Add("status", status)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<ListConvosResponse>(
            "chat.bsky.convo.listConvos", parameters, options: ChatProxy, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerates every conversation of the authenticated user, fetching pages as needed.
    /// </summary>
    /// <param name="readOnly">Read-state filter, sent as <c>readOnly</c>.</param>
    /// <param name="status">Only conversations with this status (<c>request</c> or <c>accepted</c>).</param>
    /// <param name="pageSize">Conversations per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ConvoView> EnumerateConvosAsync(
        bool? readOnly = null,
        string? status = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListConvosResponse, ConvoView>(
            (cursor, ct) => ListConvosAsync(readOnly, status, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Gets a specific conversation by ID.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetConvoResponse> GetConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("convoId", convoId);

        return _xrpc.QueryAsync<GetConvoResponse>(
            "chat.bsky.convo.getConvo", parameters, options: ChatProxy, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Gets (or creates) a conversation for the given members.
    /// </summary>
    /// <param name="members">The DIDs of the members (at most 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetConvoForMembersResponse> GetConvoForMembersAsync(
        IEnumerable<Did> members,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .AddAll("members", members.Select(did => did.Value));

        return _xrpc.QueryAsync<GetConvoForMembersResponse>(
            "chat.bsky.convo.getConvoForMembers", parameters, options: ChatProxy, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Checks whether a conversation can be created with specified members.
    /// </summary>
    /// <param name="members">The DIDs of the members (at most 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetConvoAvailabilityResponse> GetConvoAvailabilityAsync(
        IEnumerable<Did> members,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .AddAll("members", members.Select(did => did.Value));

        return _xrpc.QueryAsync<GetConvoAvailabilityResponse>(
            "chat.bsky.convo.getConvoAvailability", parameters, options: ChatProxy, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Messages
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Gets one page of the messages in a conversation.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="limit">Maximum number of messages (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetMessagesResponse> GetMessagesAsync(
        string convoId,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("convoId", convoId)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetMessagesResponse>(
            "chat.bsky.convo.getMessages", parameters, options: ChatProxy, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerates every message in a conversation, fetching pages as needed.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="pageSize">Messages per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The messages, as <see cref="GetMessagesResponse.Messages"/> carries them.</returns>
    public IAsyncEnumerable<JsonElement> EnumerateMessagesAsync(
        string convoId,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetMessagesResponse, JsonElement>(
            (cursor, ct) => GetMessagesAsync(convoId, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Sends a message in a conversation.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<MessageView> SendMessageAsync(
        string convoId, MessageInput message,
        CancellationToken cancellationToken = default)
    {
        var request = new SendMessageRequest
        {
            ConvoId = convoId,
            Message = message,
        };

        return _xrpc.ProcedureAsync<MessageView>(
            "chat.bsky.convo.sendMessage", request, options: ChatProxy,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends a batch of messages (potentially to different conversations).
    /// </summary>
    /// <param name="items">The messages to send (at most 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SendMessageBatchResponse> SendMessageBatchAsync(
        IEnumerable<BatchMessageItem> items,
        CancellationToken cancellationToken = default)
    {
        var request = new SendMessageBatchRequest { Items = [.. items] };

        return _xrpc.ProcedureAsync<SendMessageBatchResponse>(
            "chat.bsky.convo.sendMessageBatch", request, options: ChatProxy,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Deletes a message for the authenticated user only.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="messageId">The message's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<DeletedMessageView> DeleteMessageForSelfAsync(
        string convoId, string messageId,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteMessageForSelfRequest
        {
            ConvoId = convoId,
            MessageId = messageId,
        };

        return _xrpc.ProcedureAsync<DeletedMessageView>(
            "chat.bsky.convo.deleteMessageForSelf", request, options: ChatProxy,
            cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Conversation management
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Leaves a conversation.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<LeaveConvoResponse> LeaveConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var request = new LeaveConvoRequest { ConvoId = convoId };

        return _xrpc.ProcedureAsync<LeaveConvoResponse>(
            "chat.bsky.convo.leaveConvo", request, options: ChatProxy,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Mutes a conversation.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ConvoView> MuteConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var request = new MuteConvoRequest { ConvoId = convoId };

        return _xrpc.ProcedureAsync<ConvoView>(
            "chat.bsky.convo.muteConvo", request, options: ChatProxy,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unmutes a conversation.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ConvoView> UnmuteConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var request = new UnmuteConvoRequest { ConvoId = convoId };

        return _xrpc.ProcedureAsync<ConvoView>(
            "chat.bsky.convo.unmuteConvo", request, options: ChatProxy,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Marks a conversation (or specific message) as read.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="messageId">The last message read; <see langword="null"/> for the whole conversation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ConvoView> UpdateReadAsync(
        string convoId, string? messageId = null,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateReadRequest
        {
            ConvoId = convoId,
            MessageId = messageId,
        };

        return _xrpc.ProcedureAsync<ConvoView>(
            "chat.bsky.convo.updateRead", request, options: ChatProxy,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Marks all conversations as read.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UpdateAllReadAsync(
        CancellationToken cancellationToken = default)
    {
        await _xrpc.ProcedureAsync(
            "chat.bsky.convo.updateAllRead", options: ChatProxy,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Accepts a conversation request.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<AcceptConvoResponse> AcceptConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var request = new AcceptConvoRequest { ConvoId = convoId };

        return _xrpc.ProcedureAsync<AcceptConvoResponse>(
            "chat.bsky.convo.acceptConvo", request, options: ChatProxy,
            cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Reactions
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a reaction to a message.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="messageId">The message's identifier.</param>
    /// <param name="value">The reaction, a single emoji.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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

        return _xrpc.ProcedureAsync<MessageView>(
            "chat.bsky.convo.addReaction", request, options: ChatProxy,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Removes a reaction from a message.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="messageId">The message's identifier.</param>
    /// <param name="value">The reaction to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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

        return _xrpc.ProcedureAsync<MessageView>(
            "chat.bsky.convo.removeReaction", request, options: ChatProxy,
            cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Log
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Gets one page of the conversation log (events for all conversations).
    /// </summary>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetLogResponse> GetLogAsync(
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetLogResponse>(
            "chat.bsky.convo.getLog", parameters, options: ChatProxy, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerates the conversation log, fetching pages until the chat service has no newer
    /// entries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ConvoLogEntry> EnumerateLogAsync(
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetLogResponse, ConvoLogEntry>(
            (cursor, ct) => GetLogAsync(cursor, ct),
            cancellationToken);
}
