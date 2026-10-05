using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;

namespace ATProtoNet.Lexicon.Chat.Bsky.Group;

/// <summary>
/// Client for chat.bsky.group.* XRPC endpoints: group conversations, their members, join links and
/// join requests.
/// <para>
/// A group is a <see cref="ConvoView"/> whose <see cref="ConvoView.Kind"/> is a
/// <see cref="GroupConvo"/>; messages, reactions, muting and leaving go through
/// <see cref="ConvoClient"/> as for a direct conversation. Owner-only methods fail with
/// <c>InsufficientRole</c> for other members.
/// </para>
/// <para>
/// All requests are automatically proxied via <c>atproto-proxy</c> header to the chat service.
/// Requires the <c>transition:chat.bsky</c> OAuth scope.
/// </para>
/// </summary>
public sealed class GroupClient
{
    private static readonly XrpcCallOptions ChatProxy = new() { Proxy = ServiceProxy.BskyChatHeader };

    private readonly XrpcClient _xrpc;

    internal GroupClient(XrpcClient xrpc) => _xrpc = xrpc;

    // ── Groups and membership ────────────────────────────────

    /// <summary>Creates a group with the viewer as its owner. The members are added with a request they must accept. Not idempotent: each call creates a new group, even with the same members.</summary>
    /// <param name="name">The group's display name (at most 50 graphemes).</param>
    /// <param name="members">
    /// The accounts to add besides the owner. Bluesky allows up to 100 members in all, owner
    /// included.
    /// </param>
    /// <returns>The new group.</returns>
    public async Task<ConvoView> CreateGroupAsync(
        string name, IEnumerable<Did> members,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<ConvoOutput>(
            "chat.bsky.group.createGroup",
            new CreateGroupRequest(Name: name, Members: [.. members]),
            options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.Convo;
    }

    /// <summary>Renames a group. Owner only.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    /// <param name="name">The group's new display name (at most 50 graphemes).</param>
    /// <returns>The group after the change.</returns>
    public async Task<ConvoView> EditGroupAsync(
        string convoId, string name,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<ConvoOutput>(
            "chat.bsky.group.editGroup", new EditGroupRequest(ConvoId: convoId, Name: name), options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.Convo;
    }

    /// <summary>Adds members to a group. Owner only. Each is added with a request they must accept.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    /// <param name="members">The accounts to add (at least one).</param>
    public Task<AddMembersResponse> AddMembersAsync(
        string convoId, IEnumerable<Did> members,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<AddMembersResponse>(
            "chat.bsky.group.addMembers", new GroupMembersRequest(convoId, [.. members]), options: ChatProxy,
            cancellationToken: cancellationToken);

    /// <summary>Removes members from a group. Owner only.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    /// <param name="members">The members to remove (at least one).</param>
    /// <returns>The group after the change.</returns>
    public async Task<ConvoView> RemoveMembersAsync(
        string convoId, IEnumerable<Did> members,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<ConvoOutput>(
            "chat.bsky.group.removeMembers", new GroupMembersRequest(convoId, [.. members]), options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.Convo;
    }

    /// <summary>Lists one page of the groups both the viewer and another account are members of.</summary>
    /// <param name="subject">The other account.</param>
    /// <param name="limit">Maximum number of groups (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    public Task<ListMutualGroupsResponse> ListMutualGroupsAsync(
        Did subject,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<ListMutualGroupsResponse>(
            "chat.bsky.group.listMutualGroups",
            new XrpcParams()
                .Add("subject", subject)
                .Add("limit", limit)
                .Add("cursor", cursor),
            options: ChatProxy, cancellationToken: cancellationToken);

    // ── Join links ───────────────────────────────────────────

    /// <summary>Creates the group's join link. Owner only. A group has at most one enabled link.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    /// <param name="joinRule">Who may use the link (see <see cref="JoinRule"/>).</param>
    /// <param name="requireApproval">
    /// Whether the owner must approve each request to join; <see langword="null"/> for the server
    /// default (no approval).
    /// </param>
    /// <returns>The new link.</returns>
    public async Task<JoinLinkView> CreateJoinLinkAsync(
        string convoId, string joinRule,
        bool? requireApproval = null,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<JoinLinkOutput>(
            "chat.bsky.group.createJoinLink",
            new CreateJoinLinkRequest(ConvoId: convoId, JoinRule: joinRule, RequireApproval: requireApproval),
            options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.JoinLink;
    }

    /// <summary>Changes the settings of the group's join link. Owner only. A setting left <see langword="null"/> stays as it is.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    /// <param name="joinRule">Who may use the link (see <see cref="JoinRule"/>).</param>
    /// <param name="requireApproval">Whether the owner must approve each request to join.</param>
    /// <returns>The link after the change.</returns>
    public async Task<JoinLinkView> EditJoinLinkAsync(
        string convoId,
        string? joinRule = null,
        bool? requireApproval = null,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<JoinLinkOutput>(
            "chat.bsky.group.editJoinLink",
            new EditJoinLinkRequest(ConvoId: convoId, JoinRule: joinRule, RequireApproval: requireApproval),
            options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.JoinLink;
    }

    /// <summary>Enables the group's disabled join link again, with its old code. Owner only.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    /// <returns>The link after the change.</returns>
    public async Task<JoinLinkView> EnableJoinLinkAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<JoinLinkOutput>(
            "chat.bsky.group.enableJoinLink", new ConvoIdRequest(convoId), options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.JoinLink;
    }

    /// <summary>Disables the group's join link. Owner only.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    /// <returns>The link after the change.</returns>
    public async Task<JoinLinkView> DisableJoinLinkAsync(
        string convoId,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<JoinLinkOutput>(
            "chat.bsky.group.disableJoinLink", new ConvoIdRequest(convoId), options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.JoinLink;
    }

    /// <summary>Gets the public previews of the groups behind join link codes. Works signed out, too.</summary>
    /// <param name="codes">The link codes (1-50).</param>
    /// <returns>
    /// One preview per code, in the same order: a <see cref="JoinLinkPreviewView"/>, or a
    /// <see cref="DisabledJoinLinkPreviewView"/> or <see cref="InvalidJoinLinkPreviewView"/>.
    /// </returns>
    public Task<GetJoinLinkPreviewsResponse> GetJoinLinkPreviewsAsync(
        IEnumerable<string> codes,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetJoinLinkPreviewsResponse>(
            "chat.bsky.group.getJoinLinkPreviews",
            new XrpcParams().AddAll("codes", codes), options: ChatProxy, cancellationToken: cancellationToken);

    // ── Join requests ────────────────────────────────────────

    /// <summary>Joins a group through its join link or, when the link needs approval, asks the owner to let the viewer in.</summary>
    /// <param name="code">The join link's code.</param>
    /// <returns>
    /// <see cref="RequestJoinStatus.Joined"/> with the group, or
    /// <see cref="RequestJoinStatus.Pending"/>.
    /// </returns>
    public Task<RequestJoinResponse> RequestJoinAsync(
        string code,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<RequestJoinResponse>(
            "chat.bsky.group.requestJoin", new RequestJoinRequest(Code: code), options: ChatProxy,
            cancellationToken: cancellationToken);

    /// <summary>Withdraws the viewer's pending request to join a group.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    public Task WithdrawJoinRequestAsync(
        string convoId,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "chat.bsky.group.withdrawJoinRequest", new ConvoIdRequest(convoId), options: ChatProxy,
            cancellationToken: cancellationToken);

    /// <summary>Lists one page of the pending requests to join a group. Owner only.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    /// <param name="limit">Maximum number of requests (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    public Task<ListJoinRequestsResponse> ListJoinRequestsAsync(
        string convoId,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<ListJoinRequestsResponse>(
            "chat.bsky.group.listJoinRequests",
            new XrpcParams()
                .Add("convoId", convoId)
                .Add("limit", limit)
                .Add("cursor", cursor),
            options: ChatProxy, cancellationToken: cancellationToken);

    /// <summary>Approves a request to join a group. Owner only.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    /// <param name="member">The account whose request to approve.</param>
    /// <returns>The group after the change.</returns>
    public async Task<ConvoView> ApproveJoinRequestAsync(
        string convoId, Did member,
        CancellationToken cancellationToken = default)
    {
        var output = await _xrpc.ProcedureAsync<ConvoOutput>(
            "chat.bsky.group.approveJoinRequest", new GroupMemberRequest(convoId, member), options: ChatProxy,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return output.Convo;
    }

    /// <summary>Rejects a request to join a group. Owner only.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    /// <param name="member">The account whose request to reject.</param>
    public Task RejectJoinRequestAsync(
        string convoId, Did member,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "chat.bsky.group.rejectJoinRequest", new GroupMemberRequest(convoId, member), options: ChatProxy,
            cancellationToken: cancellationToken);

    /// <summary>Marks all of a group's join requests read. Owner only.</summary>
    /// <param name="convoId">The group's conversation identifier.</param>
    public Task UpdateJoinRequestsReadAsync(
        string convoId,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "chat.bsky.group.updateJoinRequestsRead", new ConvoIdRequest(convoId), options: ChatProxy,
            cancellationToken: cancellationToken);
}
