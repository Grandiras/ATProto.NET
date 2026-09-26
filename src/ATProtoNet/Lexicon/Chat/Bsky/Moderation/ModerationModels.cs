using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Lexicon.Chat.Bsky.Group;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Chat.Bsky.Moderation;

// ──────────────────────────────────────────────────────────
//  Conversations
// ──────────────────────────────────────────────────────────

/// <summary>
/// A conversation as a moderator sees it (<c>chat.bsky.moderation.defs#convoView</c>). Unlike
/// <see cref="ConvoView"/> it has no viewer data (mute, unread count, status, last message) and no
/// members; list those with <see cref="ChatModerationClient.GetConvoMembersAsync"/>.
/// </summary>
public sealed class ModerationConvoView : LexObject
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The conversation's revision, an opaque string the chat service assigns.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>
    /// What kind of conversation this is: a <see cref="ModerationDirectConvo"/> or a
    /// <see cref="ModerationGroupConvo"/>.
    /// </summary>
    [JsonPropertyName("kind")]
    public ModerationConvoKind? Kind { get; init; }
}

/// <summary>
/// The kind of a conversation, for moderation (the open
/// <c>chat.bsky.moderation.defs#convoView.kind</c> union). A kind this SDK does not model reads as
/// <see cref="UnknownModerationConvoKind"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownModerationConvoKind))]
[JsonDerivedType(typeof(ModerationDirectConvo), "chat.bsky.moderation.defs#directConvo")]
[JsonDerivedType(typeof(ModerationGroupConvo), "chat.bsky.moderation.defs#groupConvo")]
public abstract class ModerationConvoKind : LexObject;

/// <summary>
/// A conversation kind whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownModerationConvoKind : ModerationConvoKind, IUnknownUnionVariant
{
    /// <summary>Creates an unknown conversation kind from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownModerationConvoKind(string type, JsonElement raw)
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

/// <summary>A direct conversation, for moderation (<c>chat.bsky.moderation.defs#directConvo</c>).</summary>
public sealed class ModerationDirectConvo : ModerationConvoKind;

/// <summary>
/// A group conversation, for moderation (<c>chat.bsky.moderation.defs#groupConvo</c>). Unlike
/// <see cref="GroupConvo"/> it has no viewer data, and always has the join request count.
/// </summary>
public sealed class ModerationGroupConvo : ModerationConvoKind
{
    /// <summary>When the group was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>The group's join link, if it has one.</summary>
    [JsonPropertyName("joinLink")]
    public JoinLinkView? JoinLink { get; init; }

    /// <summary>The number of pending join requests, capped at 21.</summary>
    [JsonPropertyName("joinRequestCount")]
    public required int JoinRequestCount { get; init; }

    /// <summary>
    /// Whether the group accepts new messages and reactions (see <see cref="ConvoLockStatus"/>).
    /// </summary>
    [JsonPropertyName("lockStatus")]
    public required string LockStatus { get; init; }

    /// <summary>The number of members.</summary>
    [JsonPropertyName("memberCount")]
    public required int MemberCount { get; init; }

    /// <summary>The most members the group may have.</summary>
    [JsonPropertyName("memberLimit")]
    public required int MemberLimit { get; init; }

    /// <summary>The group's display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

// ──────────────────────────────────────────────────────────
//  Actors
// ──────────────────────────────────────────────────────────

/// <summary>
/// An account's chat activity over one period
/// (<c>chat.bsky.moderation.getActorMetadata#metadata</c>).
/// </summary>
public sealed class ChatActorMetadata : LexObject
{
    /// <summary>The number of messages the account sent.</summary>
    [JsonPropertyName("messagesSent")]
    public required int MessagesSent { get; init; }

    /// <summary>The number of messages the account received.</summary>
    [JsonPropertyName("messagesReceived")]
    public required int MessagesReceived { get; init; }

    /// <summary>The number of conversations the account is in.</summary>
    [JsonPropertyName("convos")]
    public required int Convos { get; init; }

    /// <summary>The number of conversations the account started.</summary>
    [JsonPropertyName("convosStarted")]
    public required int ConvosStarted { get; init; }
}

// ──────────────────────────────────────────────────────────
//  Request and response models
// ──────────────────────────────────────────────────────────

/// <summary>Request body for chat.bsky.moderation.updateActorAccess.</summary>
internal sealed class UpdateActorAccessRequest
{
    /// <summary>The account.</summary>
    [JsonPropertyName("actor")]
    public required Did Actor { get; init; }

    /// <summary>Whether the account may use chat.</summary>
    [JsonPropertyName("allowAccess")]
    public required bool AllowAccess { get; init; }

    /// <summary>A reference the moderation service records with the change.</summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; init; }
}

/// <summary>
/// Response from chat.bsky.moderation.getActorMetadata.
/// </summary>
public sealed class GetActorMetadataResponse
{
    /// <summary>The account's activity over the last day.</summary>
    [JsonPropertyName("day")]
    public required ChatActorMetadata Day { get; init; }

    /// <summary>The account's activity over the last month.</summary>
    [JsonPropertyName("month")]
    public required ChatActorMetadata Month { get; init; }

    /// <summary>The account's activity over all time.</summary>
    [JsonPropertyName("all")]
    public required ChatActorMetadata All { get; init; }
}

/// <summary>
/// Response from chat.bsky.moderation.getMessageContext.
/// </summary>
public sealed class GetMessageContextResponse
{
    /// <summary>
    /// The message and those around it, oldest first: <see cref="MessageView"/> and
    /// <see cref="SystemMessageView"/>.
    /// </summary>
    [JsonPropertyName("messages")]
    public required IReadOnlyList<ConvoMessage> Messages { get; init; }
}

/// <summary>
/// Response from chat.bsky.moderation.getConvos.
/// </summary>
public sealed class GetConvosResponse
{
    /// <summary>The conversations found; unknown identifiers are left out.</summary>
    [JsonPropertyName("convos")]
    public required IReadOnlyList<ModerationConvoView> Convos { get; init; }
}

/// <summary>The output of chat.bsky.moderation.getConvo, which the client unwraps.</summary>
internal sealed class GetConvoResponse
{
    /// <summary>The conversation.</summary>
    [JsonPropertyName("convo")]
    public required ModerationConvoView Convo { get; init; }
}

// ──────────────────────────────────────────────────────────
//  Moderation event stream (chat.bsky.moderation.subscribeModEvents)
// ──────────────────────────────────────────────────────────

/// <summary>
/// An event of the chat moderation stream (the open <c>chat.bsky.moderation.subscribeModEvents</c>
/// message union), such as a <see cref="ConvoFirstMessageEvent"/> or a
/// <see cref="GroupChatMemberAddedEvent"/>. Every event carries its revision and creation time; an
/// event this SDK does not model reads as <see cref="UnknownChatModerationEvent"/>.
/// </summary>
/// <remarks>
/// Read the stream with <see cref="Streaming.ChatModerationEventConsumer"/>.
/// </remarks>
[AtProtoUnion(typeof(UnknownChatModerationEvent))]
[JsonDerivedType(typeof(ConvoFirstMessageEvent), "chat.bsky.moderation.subscribeModEvents#eventConvoFirstMessage")]
[JsonDerivedType(typeof(GroupChatCreatedEvent), "chat.bsky.moderation.subscribeModEvents#eventGroupChatCreated")]
[JsonDerivedType(typeof(GroupChatMemberAddedEvent), "chat.bsky.moderation.subscribeModEvents#eventGroupChatMemberAdded")]
[JsonDerivedType(typeof(GroupChatMemberJoinedEvent), "chat.bsky.moderation.subscribeModEvents#eventGroupChatMemberJoined")]
[JsonDerivedType(typeof(GroupChatJoinRequestEvent), "chat.bsky.moderation.subscribeModEvents#eventGroupChatJoinRequest")]
[JsonDerivedType(typeof(GroupChatJoinRequestApprovedEvent), "chat.bsky.moderation.subscribeModEvents#eventGroupChatJoinRequestApproved")]
[JsonDerivedType(typeof(GroupChatJoinRequestRejectedEvent), "chat.bsky.moderation.subscribeModEvents#eventGroupChatJoinRequestRejected")]
[JsonDerivedType(typeof(ChatAcceptedEvent), "chat.bsky.moderation.subscribeModEvents#eventChatAccepted")]
[JsonDerivedType(typeof(GroupChatMemberLeftEvent), "chat.bsky.moderation.subscribeModEvents#eventGroupChatMemberLeft")]
[JsonDerivedType(typeof(GroupChatUpdatedEvent), "chat.bsky.moderation.subscribeModEvents#eventGroupChatUpdated")]
[JsonDerivedType(typeof(RateLimitExceededEvent), "chat.bsky.moderation.subscribeModEvents#eventRateLimitExceeded")]
public abstract class ChatModerationEvent : LexObject
{
    /// <summary>
    /// The event's revision, an opaque string the chat service assigns; the stream cursor to
    /// resume after it.
    /// </summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>When the event happened.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// A moderation event whose <c>$type</c> this SDK version does not model. It keeps the raw object;
/// see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownChatModerationEvent : ChatModerationEvent, IUnknownUnionVariant
{
    /// <summary>
    /// Creates an unknown event from its discriminator and raw object.
    /// <see cref="ChatModerationEvent.Rev"/> and <see cref="ChatModerationEvent.CreatedAt"/> are
    /// read from the object, and are empty when it lacks them.
    /// </summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    [SetsRequiredMembers]
    public UnknownChatModerationEvent(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Raw = UnknownUnionVariant.RequireObject(raw);
        Rev = StringProperty(Raw, "rev") ?? "";
        CreatedAt = StringProperty(Raw, "createdAt") is { } createdAt ? AtDatetime.FromWire(createdAt) : default;
    }

    // Not wire properties: the SDK writes an unknown variant as its Raw object.

    /// <inheritdoc/>
    [JsonIgnore]
    public string Type { get; }

    /// <inheritdoc/>
    [JsonIgnore]
    public JsonElement Raw { get; }

    private static string? StringProperty(JsonElement raw, string name) =>
        raw.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// The first message was sent in a conversation (<c>#eventConvoFirstMessage</c>).
/// </summary>
public sealed class ConvoFirstMessageEvent : ChatModerationEvent
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The identifier of the message, if the service included it.</summary>
    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }

    /// <summary>The message's recipients. The sender is <see cref="User"/>, not one of them.</summary>
    [JsonPropertyName("recipients")]
    public required IReadOnlyList<Did> Recipients { get; init; }

    /// <summary>The message's author.</summary>
    [JsonPropertyName("user")]
    public required Did User { get; init; }
}

/// <summary>
/// The group fields most moderation events share: the conversation, who acted, and the group's
/// state at the time of the event.
/// </summary>
public abstract class GroupChatModerationEvent : ChatModerationEvent
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>When the conversation was originally created.</summary>
    [JsonPropertyName("convoCreatedAt")]
    public required AtDatetime ConvoCreatedAt { get; init; }

    /// <summary>The account that performed the action; see each event for who that is.</summary>
    [JsonPropertyName("actorDid")]
    public required Did ActorDid { get; init; }

    /// <summary>The group's owner.</summary>
    [JsonPropertyName("ownerDid")]
    public required Did OwnerDid { get; init; }

    /// <summary>The group's name.</summary>
    [JsonPropertyName("groupName")]
    public required string GroupName { get; init; }

    /// <summary>The group's member count at the time of the event.</summary>
    [JsonPropertyName("groupMemberCount")]
    public required long GroupMemberCount { get; init; }
}

/// <summary>
/// A group chat was created (<c>#eventGroupChatCreated</c>). <see cref="GroupChatModerationEvent.ActorDid"/>
/// is the owner, and <see cref="GroupChatModerationEvent.GroupName"/> the name set at creation.
/// </summary>
public sealed class GroupChatCreatedEvent : GroupChatModerationEvent
{
    /// <summary>Everyone added when the group was created.</summary>
    [JsonPropertyName("initialMemberDids")]
    public required IReadOnlyList<Did> InitialMemberDids { get; init; }
}

/// <summary>
/// The owner added a member to a group chat, who starts in the request state
/// (<c>#eventGroupChatMemberAdded</c>).
/// </summary>
public sealed class GroupChatMemberAddedEvent : GroupChatModerationEvent
{
    /// <summary>The member who was added.</summary>
    [JsonPropertyName("subjectDid")]
    public required Did SubjectDid { get; init; }

    /// <summary>Whether the added member follows the owner.</summary>
    [JsonPropertyName("subjectFollowsOwner")]
    public required bool SubjectFollowsOwner { get; init; }

    /// <summary>How many members have not accepted the conversation yet.</summary>
    [JsonPropertyName("requestMembersCount")]
    public required long RequestMembersCount { get; init; }
}

/// <summary>
/// Someone joined a group chat through a join link that needs no approval
/// (<c>#eventGroupChatMemberJoined</c>). <see cref="GroupChatModerationEvent.ActorDid"/> is the
/// new member.
/// </summary>
public sealed class GroupChatMemberJoinedEvent : GroupChatModerationEvent
{
    /// <summary>The code of the join link used.</summary>
    [JsonPropertyName("joinLinkCode")]
    public required string JoinLinkCode { get; init; }

    /// <summary>Whether the new member follows the owner.</summary>
    [JsonPropertyName("subjectFollowsOwner")]
    public required bool SubjectFollowsOwner { get; init; }
}

/// <summary>
/// Someone asked to join a group chat through a join link that needs approval
/// (<c>#eventGroupChatJoinRequest</c>). <see cref="GroupChatModerationEvent.ActorDid"/> is the
/// requester.
/// </summary>
public sealed class GroupChatJoinRequestEvent : GroupChatModerationEvent
{
    /// <summary>The code of the join link used.</summary>
    [JsonPropertyName("joinLinkCode")]
    public required string JoinLinkCode { get; init; }

    /// <summary>Whether the requester follows the owner.</summary>
    [JsonPropertyName("subjectFollowsOwner")]
    public required bool SubjectFollowsOwner { get; init; }
}

/// <summary>
/// The owner approved a join request (<c>#eventGroupChatJoinRequestApproved</c>).
/// </summary>
public sealed class GroupChatJoinRequestApprovedEvent : GroupChatModerationEvent
{
    /// <summary>The member whose request was approved.</summary>
    [JsonPropertyName("subjectDid")]
    public required Did SubjectDid { get; init; }
}

/// <summary>
/// The owner rejected a join request (<c>#eventGroupChatJoinRequestRejected</c>).
/// </summary>
public sealed class GroupChatJoinRequestRejectedEvent : GroupChatModerationEvent
{
    /// <summary>The account whose request was rejected.</summary>
    [JsonPropertyName("subjectDid")]
    public required Did SubjectDid { get; init; }
}

/// <summary>
/// A member left a group chat or was removed from it (<c>#eventGroupChatMemberLeft</c>).
/// <see cref="GroupChatModerationEvent.ActorDid"/> is the member when they left, or the owner
/// when they were removed.
/// </summary>
public sealed class GroupChatMemberLeftEvent : GroupChatModerationEvent
{
    /// <summary>The member who left or was removed.</summary>
    [JsonPropertyName("subjectDid")]
    public required Did SubjectDid { get; init; }

    /// <summary>How the member left: <c>voluntary</c> or <c>kicked</c>.</summary>
    [JsonPropertyName("leaveMethod")]
    public required string LeaveMethod { get; init; }
}

/// <summary>
/// A group chat's metadata or status changed (<c>#eventGroupChatUpdated</c>).
/// </summary>
public sealed class GroupChatUpdatedEvent : GroupChatModerationEvent
{
    /// <summary>
    /// What changed: <c>name_changed</c>, <c>locked</c>, <c>locked_permanently</c>,
    /// <c>unlocked</c>, <c>join_link_created</c>, <c>join_link_disabled</c> or
    /// <c>join_link_settings_changed</c>.
    /// </summary>
    [JsonPropertyName("updateType")]
    public required string UpdateType { get; init; }

    /// <summary>The previous name, when <see cref="UpdateType"/> is <c>name_changed</c>.</summary>
    [JsonPropertyName("oldName")]
    public string? OldName { get; init; }

    /// <summary>The new name, when <see cref="UpdateType"/> is <c>name_changed</c>.</summary>
    [JsonPropertyName("newName")]
    public string? NewName { get; init; }

    /// <summary>
    /// Why the group was locked, when <see cref="UpdateType"/> is <c>locked</c>: for example
    /// <c>owner_action</c>, <c>owner_left</c> or <c>label_applied</c>.
    /// </summary>
    [JsonPropertyName("lockReason")]
    public string? LockReason { get; init; }

    /// <summary>The join link's code, for a join-link update.</summary>
    [JsonPropertyName("joinLinkCode")]
    public string? JoinLinkCode { get; init; }

    /// <summary>Whether the join link needs the owner's approval, for a join-link update.</summary>
    [JsonPropertyName("joinLinkRequiresApproval")]
    public bool? JoinLinkRequiresApproval { get; init; }

    /// <summary>Whether the join link is limited to the owner's followers, for a join-link update.</summary>
    [JsonPropertyName("joinLinkFollowersOnly")]
    public bool? JoinLinkFollowersOnly { get; init; }
}

/// <summary>
/// Someone accepted a conversation, explicitly or by sending a message
/// (<c>#eventChatAccepted</c>). The group fields are present only for group conversations.
/// </summary>
public sealed class ChatAcceptedEvent : ChatModerationEvent
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>When the conversation was originally created.</summary>
    [JsonPropertyName("convoCreatedAt")]
    public required AtDatetime ConvoCreatedAt { get; init; }

    /// <summary>The account that accepted the conversation.</summary>
    [JsonPropertyName("actorDid")]
    public required Did ActorDid { get; init; }

    /// <summary>How the conversation was accepted: <c>explicit</c> or <c>message</c>.</summary>
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    /// <summary>The group's owner, for a group conversation.</summary>
    [JsonPropertyName("ownerDid")]
    public Did? OwnerDid { get; init; }

    /// <summary>The group's name, for a group conversation.</summary>
    [JsonPropertyName("groupName")]
    public string? GroupName { get; init; }

    /// <summary>The group's member count at the time of the event, for a group conversation.</summary>
    [JsonPropertyName("groupMemberCount")]
    public long? GroupMemberCount { get; init; }
}

/// <summary>
/// An account exceeded a rate limit (<c>#eventRateLimitExceeded</c>).
/// </summary>
public sealed class RateLimitExceededEvent : ChatModerationEvent
{
    /// <summary>The account that hit the limit.</summary>
    [JsonPropertyName("actorDid")]
    public required Did ActorDid { get; init; }

    /// <summary>The NSID of the rate-limited endpoint.</summary>
    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; init; }
}
