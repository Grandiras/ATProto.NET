using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;

namespace ATProtoNet.Lexicon.Chat.Bsky.Moderation;

/// <summary>
/// Client for chat.bsky.moderation.* XRPC endpoints, which moderation services such as Ozone use to
/// look into conversations and restrict chat access.
/// <para>
/// Unlike the rest of <see cref="ChatClients"/>, these calls carry no fixed <c>atproto-proxy</c>
/// header: the chat service answers them only for moderation services, not for ordinary accounts
/// proxied through a PDS. Like the <c>tools.ozone.*</c> clients, they follow the client-wide
/// default from <see cref="AtProtoClient.SetProxy"/>, so point it at the moderation service (for
/// example Ozone's <c>#atproto_labeler</c>) or call the chat service directly.
/// </para>
/// </summary>
public sealed class ChatModerationClient
{
    private readonly XrpcClient _xrpc;

    internal ChatModerationClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Gets an account's chat activity over the last day, the last month and all time.
    /// </summary>
    /// <param name="actor">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetActorMetadataResponse> GetActorMetadataAsync(
        Did actor,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("actor", actor);

        return _xrpc.QueryAsync<GetActorMetadataResponse>(
            "chat.bsky.moderation.getActorMetadata", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Gets a message with the messages around it, for reviewing a report.
    /// </summary>
    /// <param name="messageId">The message's identifier.</param>
    /// <param name="convoId">
    /// The conversation the message is in. Optional for now; upstream says it will become required.
    /// </param>
    /// <param name="before">
    /// How many member messages before it to include; <see langword="null"/> for the server
    /// default (5).
    /// </param>
    /// <param name="after">
    /// How many member messages after it to include; <see langword="null"/> for the server default
    /// (5).
    /// </param>
    /// <param name="maxInterleavedSystemMessages">
    /// The most system messages to include between two returned messages (0-1000);
    /// <see langword="null"/> for the server default (10).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetMessageContextResponse> GetMessageContextAsync(
        string messageId,
        string? convoId = null,
        int? before = null,
        int? after = null,
        int? maxInterleavedSystemMessages = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("convoId", convoId)
            .Add("messageId", messageId)
            .Add("before", before)
            .Add("after", after)
            .Add("maxInterleavedSystemMessages", maxInterleavedSystemMessages);

        return _xrpc.QueryAsync<GetMessageContextResponse>(
            "chat.bsky.moderation.getMessageContext", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Gets a conversation the moderator need not be a member of.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ModerationConvoView> GetConvoAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("convoId", convoId);

        var output = await _xrpc.QueryAsync<GetConvoResponse>(
            "chat.bsky.moderation.getConvo", parameters, cancellationToken: cancellationToken);
        return output.Convo;
    }

    /// <summary>
    /// Gets several conversations the moderator need not be a member of.
    /// </summary>
    /// <param name="convoIds">The conversations' identifiers (1-100); unknown ones are left out.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetConvosResponse> GetConvosAsync(
        IEnumerable<string> convoIds,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .AddAll("convoIds", convoIds);

        return _xrpc.QueryAsync<GetConvosResponse>(
            "chat.bsky.moderation.getConvos", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Gets one page of a conversation's members; the moderator need not be a member.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="limit">Maximum number of members (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetConvoMembersResponse> GetConvoMembersAsync(
        string convoId,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("convoId", convoId)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetConvoMembersResponse>(
            "chat.bsky.moderation.getConvoMembers", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerates every member of a conversation, fetching pages as needed; the moderator need not
    /// be a member.
    /// </summary>
    /// <param name="convoId">The conversation's identifier.</param>
    /// <param name="pageSize">Members per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ChatMemberView> EnumerateConvoMembersAsync(
        string convoId,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetConvoMembersResponse, ChatMemberView>(
            (cursor, ct) => GetConvoMembersAsync(convoId, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Allows or revokes an account's access to chat.
    /// </summary>
    /// <param name="actor">The account.</param>
    /// <param name="allowAccess">Whether the account may use chat.</param>
    /// <param name="reference">
    /// The Lexicon's <c>ref</c>: a reference the moderation service records with the change, such
    /// as the moderation event behind it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task UpdateActorAccessAsync(
        Did actor, bool allowAccess,
        string? reference = null,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateActorAccessRequest
        {
            Actor = actor,
            AllowAccess = allowAccess,
            Ref = reference,
        };

        return _xrpc.ProcedureAsync(
            "chat.bsky.moderation.updateActorAccess", request, cancellationToken: cancellationToken);
    }
}
