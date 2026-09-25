using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Chat.Bsky.Group;

// ──────────────────────────────────────────────────────────
//  Known values
// ──────────────────────────────────────────────────────────

/// <summary>
/// Known values of who may use a join link (<c>chat.bsky.group.defs#joinRule</c>).
/// </summary>
public static class JoinRule
{
    /// <summary>Anyone with the link.</summary>
    public const string Anyone = "anyone";

    /// <summary>Only accounts the group's owner follows.</summary>
    public const string FollowedByOwner = "followedByOwner";
}

/// <summary>
/// Known values of <see cref="JoinLinkView.EnabledStatus"/> (<c>chat.bsky.group.defs#linkEnabledStatus</c>).
/// </summary>
public static class JoinLinkEnabledStatus
{
    /// <summary>The link can be used to join.</summary>
    public const string Enabled = "enabled";

    /// <summary>The link is switched off; it keeps its code and can be enabled again.</summary>
    public const string Disabled = "disabled";
}

/// <summary>
/// Known values of <see cref="RequestJoinResponse.Status"/>.
/// </summary>
public static class RequestJoinStatus
{
    /// <summary>The viewer is now a member.</summary>
    public const string Joined = "joined";

    /// <summary>The request waits for the owner's approval.</summary>
    public const string Pending = "pending";
}

// ──────────────────────────────────────────────────────────
//  Join links
// ──────────────────────────────────────────────────────────

/// <summary>
/// A group's join link, as its owner and members see it (<c>chat.bsky.group.defs#joinLinkView</c>).
/// </summary>
public sealed class JoinLinkView : LexObject
{
    /// <summary>The link's code.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>Whether the link can be used (see <see cref="JoinLinkEnabledStatus"/>).</summary>
    [JsonPropertyName("enabledStatus")]
    public required string EnabledStatus { get; init; }

    /// <summary>Whether the owner must approve each request to join.</summary>
    [JsonPropertyName("requireApproval")]
    public required bool RequireApproval { get; init; }

    /// <summary>Who may use the link (see <see cref="Group.JoinRule"/>).</summary>
    [JsonPropertyName("joinRule")]
    public required string JoinRule { get; init; }

    /// <summary>When the link was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// What a join link code leads to (the open union of <c>chat.bsky.group.getJoinLinkPreviews</c>
/// and <c>chat.bsky.embed.joinLink#view</c>): a <see cref="JoinLinkPreviewView"/>, or a
/// <see cref="DisabledJoinLinkPreviewView"/> or <see cref="InvalidJoinLinkPreviewView"/>. A preview
/// this SDK does not model reads as <see cref="UnknownJoinLinkPreview"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownJoinLinkPreview))]
[JsonDerivedType(typeof(JoinLinkPreviewView), "chat.bsky.group.defs#joinLinkPreviewView")]
[JsonDerivedType(typeof(DisabledJoinLinkPreviewView), "chat.bsky.group.defs#disabledJoinLinkPreviewView")]
[JsonDerivedType(typeof(InvalidJoinLinkPreviewView), "chat.bsky.group.defs#invalidJoinLinkPreviewView")]
public abstract class JoinLinkPreview : LexObject;

/// <summary>
/// A join link preview whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownJoinLinkPreview : JoinLinkPreview, IUnknownUnionVariant
{
    /// <summary>Creates an unknown join link preview from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownJoinLinkPreview(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Raw = UnknownUnionVariant.RequireObject(raw);
    }

    /// <inheritdoc/>
    public string Type { get; }

    /// <inheritdoc/>
    public JsonElement Raw { get; }
}

/// <summary>
/// The public preview of a group behind a join link, which may be shown even to signed-out viewers
/// (<c>chat.bsky.group.defs#joinLinkPreviewView</c>).
/// </summary>
public sealed class JoinLinkPreviewView : JoinLinkPreview
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The link's code.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>The group's display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The group's owner.</summary>
    [JsonPropertyName("owner")]
    public required ChatMemberView Owner { get; init; }

    /// <summary>The number of members.</summary>
    [JsonPropertyName("memberCount")]
    public required int MemberCount { get; init; }

    /// <summary>The most members the group may have.</summary>
    [JsonPropertyName("memberLimit")]
    public required int MemberLimit { get; init; }

    /// <summary>Whether the owner must approve each request to join.</summary>
    [JsonPropertyName("requireApproval")]
    public required bool RequireApproval { get; init; }

    /// <summary>Who may use the link (see <see cref="Group.JoinRule"/>).</summary>
    [JsonPropertyName("joinRule")]
    public required string JoinRule { get; init; }

    /// <summary>The group's conversation; present only when the signed-in viewer is a member.</summary>
    [JsonPropertyName("convo")]
    public ConvoView? Convo { get; init; }

    /// <summary>The viewer's relationship to the link, such as a pending request.</summary>
    [JsonPropertyName("viewer")]
    public JoinLinkViewerState? Viewer { get; init; }
}

/// <summary>
/// The preview of a disabled join link: only its code
/// (<c>chat.bsky.group.defs#disabledJoinLinkPreviewView</c>).
/// </summary>
public sealed class DisabledJoinLinkPreviewView : JoinLinkPreview
{
    /// <summary>The link's code.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }
}

/// <summary>
/// The preview of a code that is not a join link: only the code
/// (<c>chat.bsky.group.defs#invalidJoinLinkPreviewView</c>).
/// </summary>
public sealed class InvalidJoinLinkPreviewView : JoinLinkPreview
{
    /// <summary>The code that was asked for.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }
}

/// <summary>
/// The viewer's relationship to a join link (<c>chat.bsky.group.defs#joinLinkViewerState</c>).
/// </summary>
public sealed class JoinLinkViewerState : LexObject
{
    /// <summary>When the viewer asked to join, if the request is pending.</summary>
    [JsonPropertyName("requestedAt")]
    public AtDatetime? RequestedAt { get; init; }
}

// ──────────────────────────────────────────────────────────
//  Join requests
// ──────────────────────────────────────────────────────────

/// <summary>
/// A request to join a group, as its owner sees it (<c>chat.bsky.group.defs#joinRequestView</c>).
/// </summary>
public sealed class JoinRequestView : LexObject
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The account asking to join.</summary>
    [JsonPropertyName("requestedBy")]
    public required ChatMemberView RequestedBy { get; init; }

    /// <summary>When the request was made.</summary>
    [JsonPropertyName("requestedAt")]
    public required AtDatetime RequestedAt { get; init; }
}

/// <summary>
/// A request to join a group, as the requester sees it, with enough of the group to list it
/// (<c>chat.bsky.group.defs#joinRequestConvoView</c>).
/// </summary>
public sealed class JoinRequestConvoView : ConvoRequestView
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The group's display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The group's owner.</summary>
    [JsonPropertyName("owner")]
    public required ChatMemberView Owner { get; init; }

    /// <summary>The number of members.</summary>
    [JsonPropertyName("memberCount")]
    public required int MemberCount { get; init; }

    /// <summary>The most members the group may have.</summary>
    [JsonPropertyName("memberLimit")]
    public required int MemberLimit { get; init; }

    /// <summary>The viewer's relationship to the group's link, with when the request was made.</summary>
    [JsonPropertyName("viewer")]
    public required JoinLinkViewerState Viewer { get; init; }
}

// ──────────────────────────────────────────────────────────
//  Request models
// ──────────────────────────────────────────────────────────

/// <summary>Request body for chat.bsky.group.createGroup.</summary>
internal sealed class CreateGroupRequest
{
    /// <summary>The members to add besides the owner.</summary>
    [JsonPropertyName("members")]
    public required IReadOnlyList<Did> Members { get; init; }

    /// <summary>The group's display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>Request body for chat.bsky.group.editGroup.</summary>
internal sealed class EditGroupRequest
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The group's new display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>Request body for chat.bsky.group.addMembers.</summary>
internal sealed class AddMembersRequest
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The accounts to add.</summary>
    [JsonPropertyName("members")]
    public required IReadOnlyList<Did> Members { get; init; }
}

/// <summary>Request body for chat.bsky.group.removeMembers.</summary>
internal sealed class RemoveMembersRequest
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The members to remove.</summary>
    [JsonPropertyName("members")]
    public required IReadOnlyList<Did> Members { get; init; }
}

/// <summary>Request body for chat.bsky.group.createJoinLink.</summary>
internal sealed class CreateJoinLinkRequest
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>Whether the owner must approve each request to join.</summary>
    [JsonPropertyName("requireApproval")]
    public bool? RequireApproval { get; init; }

    /// <summary>Who may use the link.</summary>
    [JsonPropertyName("joinRule")]
    public required string JoinRule { get; init; }
}

/// <summary>Request body for chat.bsky.group.editJoinLink.</summary>
internal sealed class EditJoinLinkRequest
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>Whether the owner must approve each request to join.</summary>
    [JsonPropertyName("requireApproval")]
    public bool? RequireApproval { get; init; }

    /// <summary>Who may use the link.</summary>
    [JsonPropertyName("joinRule")]
    public string? JoinRule { get; init; }
}

/// <summary>Request body for chat.bsky.group.enableJoinLink.</summary>
internal sealed class EnableJoinLinkRequest
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>Request body for chat.bsky.group.disableJoinLink.</summary>
internal sealed class DisableJoinLinkRequest
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>Request body for chat.bsky.group.requestJoin.</summary>
internal sealed class RequestJoinRequest
{
    /// <summary>The join link's code.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }
}

/// <summary>Request body for chat.bsky.group.approveJoinRequest.</summary>
internal sealed class ApproveJoinRequestRequest
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The account whose request to approve.</summary>
    [JsonPropertyName("member")]
    public required Did Member { get; init; }
}

/// <summary>Request body for chat.bsky.group.rejectJoinRequest.</summary>
internal sealed class RejectJoinRequestRequest
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The account whose request to reject.</summary>
    [JsonPropertyName("member")]
    public required Did Member { get; init; }
}

/// <summary>Request body for chat.bsky.group.withdrawJoinRequest.</summary>
internal sealed class WithdrawJoinRequestRequest
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>Request body for chat.bsky.group.updateJoinRequestsRead.</summary>
internal sealed class UpdateJoinRequestsReadRequest
{
    /// <summary>The identifier of the group's conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

// ──────────────────────────────────────────────────────────
//  Response models
// ──────────────────────────────────────────────────────────

/// <summary>
/// Response from chat.bsky.group.addMembers.
/// </summary>
public sealed class AddMembersResponse
{
    /// <summary>The group after the change.</summary>
    [JsonPropertyName("convo")]
    public required ConvoView Convo { get; init; }

    /// <summary>The accounts that were added.</summary>
    [JsonPropertyName("addedMembers")]
    public IReadOnlyList<ChatMemberView>? AddedMembers { get; init; }
}

/// <summary>
/// Response from chat.bsky.group.listMutualGroups.
/// </summary>
public sealed class ListMutualGroupsResponse : ICursorPage<ConvoView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The groups both the viewer and the account are members of.</summary>
    [JsonPropertyName("convos")]
    public required IReadOnlyList<ConvoView> Convos { get; init; }

    IReadOnlyList<ConvoView> ICursorPage<ConvoView>.Items => Convos;
}

/// <summary>
/// Response from chat.bsky.group.getJoinLinkPreviews.
/// </summary>
public sealed class GetJoinLinkPreviewsResponse
{
    /// <summary>One preview per code asked for, in the same order.</summary>
    [JsonPropertyName("joinLinkPreviews")]
    public required IReadOnlyList<JoinLinkPreview> JoinLinkPreviews { get; init; }
}

/// <summary>
/// Response from chat.bsky.group.requestJoin.
/// </summary>
public sealed class RequestJoinResponse
{
    /// <summary>
    /// Whether the viewer joined or the request waits for approval (see
    /// <see cref="RequestJoinStatus"/>).
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>The group the viewer joined; present only when <see cref="Status"/> is <c>joined</c>.</summary>
    [JsonPropertyName("convo")]
    public ConvoView? Convo { get; init; }
}

/// <summary>
/// Response from chat.bsky.group.listJoinRequests.
/// </summary>
public sealed class ListJoinRequestsResponse : ICursorPage<JoinRequestView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The pending requests.</summary>
    [JsonPropertyName("requests")]
    public required IReadOnlyList<JoinRequestView> Requests { get; init; }

    IReadOnlyList<JoinRequestView> ICursorPage<JoinRequestView>.Items => Requests;
}

/// <summary>
/// The <c>{joinLink}</c> output of chat.bsky.group.createJoinLink, editJoinLink, enableJoinLink and
/// disableJoinLink, which the client unwraps.
/// </summary>
internal sealed class JoinLinkOutput
{
    /// <summary>The join link after the change.</summary>
    [JsonPropertyName("joinLink")]
    public required JoinLinkView JoinLink { get; init; }
}
