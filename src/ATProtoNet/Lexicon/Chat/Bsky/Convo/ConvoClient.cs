using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Chat.Bsky.Convo;

/// <summary>
/// Client for chat.bsky.convo.* XRPC endpoints.
/// Handles conversations, direct and group: listing, sending, reading, and managing messages.
/// Creating groups and managing their members and join links is <see cref="Group.GroupClient"/>.
/// <para>
/// All requests are automatically proxied via <c>atproto-proxy</c> header to the chat service.
/// Requires the <c>transition:chat.bsky</c> OAuth scope.
/// </para>
/// </summary>
public sealed class ConvoClient
{
    private static readonly XrpcCallOptions ChatProxy = new() { Proxy = ServiceProxy.BskyChatHeader };

    private readonly XrpcClient _xrpc;

    internal ConvoClient(XrpcClient xrpc) => _xrpc = xrpc;

    // ── Conversation listing & retrieval ─────────────────────

    /// <summary>Lists one page of conversations, direct and group, for the authenticated user.</summary>
    /// <param name="readState">Only conversations in this read state (see <see cref="ConvoReadState"/>).</param>
    /// <param name="status">
    /// Only conversations with this status (see <see cref="ConvoStatus"/>). For requests, prefer
    /// <see cref="ListConvoRequestsAsync"/>, which also lists the viewer's group join requests.
    /// </param>
    /// <param name="kind">Only conversations of this kind (see <see cref="ConvoKinds"/>).</param>
    /// <param name="lockStatus">Only conversations with this lock status (see <see cref="ConvoLockStatus"/>).</param>
    /// <param name="limit">Maximum number of conversations (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    public Task<ListConvosResponse> ListConvosAsync(
        string? readState = null,
        string? status = null,
        string? kind = null,
        string? lockStatus = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<ListConvosResponse>(
            "chat.bsky.convo.listConvos",
            new XrpcParams()
                .Add("readState", readState)
                .Add("status", status)
                .Add("kind", kind)
                .Add("lockStatus", lockStatus)
                .Add("limit", limit)
                .Add("cursor", cursor),
            options: ChatProxy, cancellationToken: cancellationToken);

    /// <summary>Lists one page of the viewer's requests: incoming conversation requests, and the group join requests the viewer made.</summary>
    /// <param name="limit">Maximum number of requests (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    public Task<ListConvoRequestsResponse> ListConvoRequestsAsync(
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<ListConvoRequestsResponse>(
            "chat.bsky.convo.listConvoRequests",
            new XrpcParams()
                .Add("limit", limit)
                .Add("cursor", cursor),
            options: ChatProxy, cancellationToken: cancellationToken);

    /// <summary>Counts the unlocked, unmuted conversations with something unread.</summary>
    /// <param name="includeGroupChats">
    /// Whether to count groups; <see langword="null"/> for the server default (yes).
    /// </param>
    public Task<GetUnreadCountsResponse> GetUnreadCountsAsync(
        bool? includeGroupChats = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetUnreadCountsResponse>(
            "chat.bsky.convo.getUnreadCounts",
            new XrpcParams()
                .Add("includeGroupChats", includeGroupChats),
            options: ChatProxy, cancellationToken: cancellationToken);

    /// <summary>Gets a specific conversation by ID.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    public Task<GetConvoResponse> GetConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetConvoResponse>(
            "chat.bsky.convo.getConvo",
            new XrpcParams().Add("convoId", convoId), options: ChatProxy, cancellationToken: cancellationToken);

    /// <summary>Gets (or creates) a conversation for the given members.</summary>
    /// <param name="members">The DIDs of the members (at most 10).</param>
    public Task<GetConvoForMembersResponse> GetConvoForMembersAsync(
        IEnumerable<Did> members,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetConvoForMembersResponse>(
            "chat.bsky.convo.getConvoForMembers",
            new XrpcParams()
                .AddAll("members", members.Select(did => did.Value)),
            options: ChatProxy, cancellationToken: cancellationToken);

    /// <summary>Checks whether a conversation can be created with specified members.</summary>
    /// <param name="members">The DIDs of the members (at most 10).</param>
    public Task<GetConvoAvailabilityResponse> GetConvoAvailabilityAsync(
        IEnumerable<Did> members,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetConvoAvailabilityResponse>(
            "chat.bsky.convo.getConvoAvailability",
            new XrpcParams()
                .AddAll("members", members.Select(did => did.Value)),
            options: ChatProxy, cancellationToken: cancellationToken);

    /// <summary>Gets one page of a conversation's members. <see cref="ConvoView.Members"/> lists only some of a group's members.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="limit">Maximum number of members (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    public Task<GetConvoMembersResponse> GetConvoMembersAsync(
        string convoId,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetConvoMembersResponse>(
            "chat.bsky.convo.getConvoMembers",
            new XrpcParams()
                .Add("convoId", convoId)
                .Add("limit", limit)
                .Add("cursor", cursor),
            options: ChatProxy, cancellationToken: cancellationToken);

    // ── Messages ─────────────────────────────────────────────

    /// <summary>Gets one page of the messages in a conversation.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="limit">Maximum number of messages (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    public Task<GetMessagesResponse> GetMessagesAsync(
        string convoId,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetMessagesResponse>(
            "chat.bsky.convo.getMessages",
            new XrpcParams()
                .Add("convoId", convoId)
                .Add("limit", limit)
                .Add("cursor", cursor),
            options: ChatProxy, cancellationToken: cancellationToken);

    /// <summary>Enumerates every message in a conversation, fetching pages as needed.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="pageSize">Messages per request (1-100); <see langword="null"/> for the server default.</param>
    /// <returns>
    /// The messages: <see cref="MessageView"/>, <see cref="DeletedMessageView"/> and, in groups,
    /// <see cref="SystemMessageView"/>.
    /// </returns>
    public IAsyncEnumerable<ConvoMessage> EnumerateMessagesAsync(
        string convoId,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetMessagesResponse, ConvoMessage>(
            (cursor, ct) => GetMessagesAsync(convoId, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>Sends a message in a conversation.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="message">The message to send.</param>
    public Task<MessageView> SendMessageAsync(
        string convoId, MessageInput message,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<MessageView>(
            "chat.bsky.convo.sendMessage",
            new SendMessageRequest(ConvoId: convoId, Message: message), options: ChatProxy,
            cancellationToken: cancellationToken);

    /// <summary>Sends a batch of messages (potentially to different conversations).</summary>
    /// <param name="items">The messages to send (at most 100).</param>
    public Task<SendMessageBatchResponse> SendMessageBatchAsync(
        IEnumerable<BatchMessageItem> items,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<SendMessageBatchResponse>(
            "chat.bsky.convo.sendMessageBatch", new SendMessageBatchRequest(Items: [.. items]), options: ChatProxy,
            cancellationToken: cancellationToken);

    /// <summary>Deletes a message for the authenticated user only.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="messageId">The message's identifier.</param>
    public Task<DeletedMessageView> DeleteMessageForSelfAsync(
        string convoId, string messageId,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<DeletedMessageView>(
            "chat.bsky.convo.deleteMessageForSelf",
            new DeleteMessageForSelfRequest(ConvoId: convoId, MessageId: messageId), options: ChatProxy,
            cancellationToken: cancellationToken);

    // ── Conversation management ──────────────────────────────

    /// <summary>Leaves a conversation.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    public Task<LeaveConvoResponse> LeaveConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<LeaveConvoResponse>(
            "chat.bsky.convo.leaveConvo", new ConvoIdRequest(convoId), options: ChatProxy,
            cancellationToken: cancellationToken);

    /// <summary>Mutes a conversation.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <returns>The conversation after the change.</returns>
    public async Task<ConvoView> MuteConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<ConvoOutput>(
            "chat.bsky.convo.muteConvo", new ConvoIdRequest(convoId), options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.Convo;
    }

    /// <summary>Unmutes a conversation.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <returns>The conversation after the change.</returns>
    public async Task<ConvoView> UnmuteConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<ConvoOutput>(
            "chat.bsky.convo.unmuteConvo", new ConvoIdRequest(convoId), options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.Convo;
    }

    /// <summary>Marks a conversation (or specific message) as read.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="messageId">The last message read; <see langword="null"/> for the whole conversation.</param>
    /// <returns>The conversation after the change.</returns>
    public async Task<ConvoView> UpdateReadAsync(
        string convoId, string? messageId = null,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<ConvoOutput>(
            "chat.bsky.convo.updateRead",
            new UpdateReadRequest(ConvoId: convoId, MessageId: messageId),
            options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.Convo;
    }

    /// <summary>Marks all conversations as read.</summary>
    /// <param name="status">
    /// Only conversations with this status (<c>request</c> or <c>accepted</c>); <see langword="null"/>
    /// for all.
    /// </param>
    public Task<UpdateAllReadResponse> UpdateAllReadAsync(
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        // Always a JSON body, even an empty one: the method declares an application/json input.
        return _xrpc.ProcedureAsync<UpdateAllReadResponse>(
            "chat.bsky.convo.updateAllRead", new UpdateAllReadRequest(Status: status), options: ChatProxy,
            cancellationToken: cancellationToken);
    }

    /// <summary>Accepts a conversation request.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    public Task<AcceptConvoResponse> AcceptConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<AcceptConvoResponse>(
            "chat.bsky.convo.acceptConvo", new ConvoIdRequest(convoId), options: ChatProxy,
            cancellationToken: cancellationToken);

    /// <summary>Locks a group, so members can add no more messages or reactions. Owner only.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    /// <returns>The conversation after the change.</returns>
    public async Task<ConvoView> LockConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<ConvoOutput>(
            "chat.bsky.convo.lockConvo", new ConvoIdRequest(convoId), options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.Convo;
    }

    /// <summary>Unlocks a group. Owner only; fails with <c>ConvoLockedByModeration</c> while moderation holds the lock.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    /// <returns>The conversation after the change.</returns>
    public async Task<ConvoView> UnlockConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<ConvoOutput>(
            "chat.bsky.convo.unlockConvo", new ConvoIdRequest(convoId), options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.Convo;
    }

    // ── Reactions ────────────────────────────────────────────

    /// <summary>Adds a reaction to a message.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="messageId">The message's identifier.</param>
    /// <param name="value">The reaction, a single emoji.</param>
    /// <returns>The message after the change.</returns>
    public async Task<MessageView> AddReactionAsync(
        string convoId, string messageId, string value,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<MessageOutput>(
            "chat.bsky.convo.addReaction", new ConvoReactionRequest(convoId, messageId, value), options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.Message;
    }

    /// <summary>Removes a reaction from a message.</summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="messageId">The message's identifier.</param>
    /// <param name="value">The reaction to remove.</param>
    /// <returns>The message after the change.</returns>
    public async Task<MessageView> RemoveReactionAsync(
        string convoId, string messageId, string value,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<MessageOutput>(
            "chat.bsky.convo.removeReaction", new ConvoReactionRequest(convoId, messageId, value), options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.Message;
    }

    // ── Log ──────────────────────────────────────────────────

    /// <summary>Gets one page of the conversation log (events for all conversations).</summary>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    public Task<GetLogResponse> GetLogAsync(
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetLogResponse>(
            "chat.bsky.convo.getLog",
            new XrpcParams().Add("cursor", cursor), options: ChatProxy, cancellationToken: cancellationToken);
}
