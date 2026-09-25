using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Lexicon.Chat.Bsky.Actor;
using ATProtoNet.Lexicon.Chat.Bsky.Embed;
using ATProtoNet.Lexicon.Chat.Bsky.Group;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Chat.Bsky.Convo;

// ──────────────────────────────────────────────────────────
//  Known values
// ──────────────────────────────────────────────────────────

/// <summary>
/// Known values of <see cref="ConvoView.Status"/> (<c>chat.bsky.convo.defs#convoStatus</c>): the
/// viewer's membership status, not the conversation's.
/// </summary>
public static class ConvoStatus
{
    /// <summary>The conversation is in the viewer's request inbox.</summary>
    public const string Request = "request";

    /// <summary>The viewer accepted the conversation.</summary>
    public const string Accepted = "accepted";
}

/// <summary>
/// Known values of the conversation kind filter (<c>chat.bsky.convo.defs#convoKind</c>). A
/// conversation's own kind is <see cref="ConvoView.Kind"/>.
/// </summary>
public static class ConvoKinds
{
    /// <summary>A conversation between two accounts.</summary>
    public const string Direct = "direct";

    /// <summary>A group conversation.</summary>
    public const string Group = "group";
}

/// <summary>
/// Known values of <see cref="GroupConvo.LockStatus"/> (<c>chat.bsky.convo.defs#convoLockStatus</c>).
/// </summary>
public static class ConvoLockStatus
{
    /// <summary>Members may add messages and reactions.</summary>
    public const string Unlocked = "unlocked";

    /// <summary>No new messages or reactions until the owner unlocks the conversation.</summary>
    public const string Locked = "locked";

    /// <summary>Locked for good: the conversation can no longer be unlocked.</summary>
    public const string LockedPermanently = "locked-permanently";
}

/// <summary>
/// Known values of the <c>readState</c> filter of <c>chat.bsky.convo.listConvos</c>.
/// </summary>
public static class ConvoReadState
{
    /// <summary>Only conversations with unread messages.</summary>
    public const string Unread = "unread";
}

// ──────────────────────────────────────────────────────────
//  Conversations
// ──────────────────────────────────────────────────────────

/// <summary>
/// An entry of <c>chat.bsky.convo.listConvoRequests</c>: a conversation request
/// (<see cref="ConvoView"/>) or a group join request the viewer made
/// (<see cref="JoinRequestConvoView"/>). An entry this SDK does not model reads as
/// <see cref="UnknownConvoRequestView"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownConvoRequestView))]
[JsonDerivedType(typeof(ConvoView), "chat.bsky.convo.defs#convoView")]
[JsonDerivedType(typeof(JoinRequestConvoView), "chat.bsky.group.defs#joinRequestConvoView")]
public abstract class ConvoRequestView : LexObject;

/// <summary>
/// A conversation request entry whose <c>$type</c> this SDK version does not model. It keeps the
/// raw object and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownConvoRequestView : ConvoRequestView, IUnknownUnionVariant
{
    /// <summary>Creates an unknown conversation request entry from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownConvoRequestView(string type, JsonElement raw)
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
/// A conversation, direct or group, as the viewer sees it (<c>chat.bsky.convo.defs#convoView</c>).
/// </summary>
public sealed class ConvoView : ConvoRequestView
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The conversation's revision, an opaque string the chat service assigns.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>
    /// The members. A direct conversation lists both; a group lists only the notable ones (the
    /// first few, the viewer, who added the viewer, and the authors of the last message and
    /// reaction). Use <see cref="ConvoClient.GetConvoMembersAsync"/> for all of them.
    /// </summary>
    [JsonPropertyName("members")]
    public required IReadOnlyList<ChatMemberView> Members { get; init; }

    /// <summary>
    /// The most recent message: a <see cref="MessageView"/>, <see cref="DeletedMessageView"/> or
    /// <see cref="SystemMessageView"/>.
    /// </summary>
    [JsonPropertyName("lastMessage")]
    public ConvoMessage? LastMessage { get; init; }

    /// <summary>The most recent reaction, with the message it is on.</summary>
    [JsonPropertyName("lastReaction")]
    public ConvoLastReaction? LastReaction { get; init; }

    /// <summary>Whether the viewer has muted this conversation.</summary>
    [JsonPropertyName("muted")]
    public bool Muted { get; init; }

    /// <summary>
    /// The viewer's membership status (see <see cref="ConvoStatus"/>): <c>request</c> or
    /// <c>accepted</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>The number of unread messages.</summary>
    [JsonPropertyName("unreadCount")]
    public int UnreadCount { get; init; }

    /// <summary>
    /// What kind of conversation this is: a <see cref="DirectConvo"/>, or a
    /// <see cref="GroupConvo"/> with the group's name, size, lock status and join link.
    /// </summary>
    [JsonPropertyName("kind")]
    public ConvoKind? Kind { get; init; }
}

/// <summary>
/// The kind of a conversation and the data specific to it (the open
/// <c>chat.bsky.convo.defs#convoView.kind</c> union). A kind this SDK does not model reads as
/// <see cref="UnknownConvoKind"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownConvoKind))]
[JsonDerivedType(typeof(DirectConvo), "chat.bsky.convo.defs#directConvo")]
[JsonDerivedType(typeof(GroupConvo), "chat.bsky.convo.defs#groupConvo")]
public abstract class ConvoKind : LexObject;

/// <summary>
/// A conversation kind whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownConvoKind : ConvoKind, IUnknownUnionVariant
{
    /// <summary>Creates an unknown conversation kind from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownConvoKind(string type, JsonElement raw)
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

/// <summary>A conversation between two accounts (<c>chat.bsky.convo.defs#directConvo</c>).</summary>
public sealed class DirectConvo : ConvoKind;

/// <summary>A group conversation (<c>chat.bsky.convo.defs#groupConvo</c>).</summary>
public sealed class GroupConvo : ConvoKind
{
    /// <summary>When the group was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>The group's join link, if it has one.</summary>
    [JsonPropertyName("joinLink")]
    public JoinLinkView? JoinLink { get; init; }

    /// <summary>The number of pending join requests, capped at 21. Only the owner sees it.</summary>
    [JsonPropertyName("joinRequestCount")]
    public int? JoinRequestCount { get; init; }

    /// <summary>
    /// Whether the group accepts new messages and reactions (see <see cref="ConvoLockStatus"/>).
    /// </summary>
    [JsonPropertyName("lockStatus")]
    public required string LockStatus { get; init; }

    /// <summary>
    /// Whether <see cref="LockStatus"/> is forced by moderation (an inactive owner account or a
    /// takedown) rather than set by the owner.
    /// </summary>
    [JsonPropertyName("lockStatusModerationOverride")]
    public required bool LockStatusModerationOverride { get; init; }

    /// <summary>The number of members.</summary>
    [JsonPropertyName("memberCount")]
    public required int MemberCount { get; init; }

    /// <summary>The most members the group may have.</summary>
    [JsonPropertyName("memberLimit")]
    public required int MemberLimit { get; init; }

    /// <summary>The group's display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The number of join requests the owner has not read. Only the owner sees it.</summary>
    [JsonPropertyName("unreadJoinRequestCount")]
    public int? UnreadJoinRequestCount { get; init; }
}

/// <summary>
/// The latest reaction in a conversation (the open <c>chat.bsky.convo.defs#convoView.lastReaction</c>
/// union). A variant this SDK does not model reads as <see cref="UnknownConvoLastReaction"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownConvoLastReaction))]
[JsonDerivedType(typeof(MessageAndReactionView), "chat.bsky.convo.defs#messageAndReactionView")]
public abstract class ConvoLastReaction : LexObject;

/// <summary>
/// A latest-reaction variant whose <c>$type</c> this SDK version does not model. It keeps the raw
/// object and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownConvoLastReaction : ConvoLastReaction, IUnknownUnionVariant
{
    /// <summary>Creates an unknown latest-reaction variant from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownConvoLastReaction(string type, JsonElement raw)
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
/// A reaction together with the message it is on (<c>chat.bsky.convo.defs#messageAndReactionView</c>).
/// </summary>
public sealed class MessageAndReactionView : ConvoLastReaction
{
    /// <summary>The message.</summary>
    [JsonPropertyName("message")]
    public required MessageView Message { get; init; }

    /// <summary>The reaction.</summary>
    [JsonPropertyName("reaction")]
    public required ReactionView Reaction { get; init; }
}

/// <summary>
/// A chat member (actor profile) within a conversation.
/// </summary>
public sealed class ChatMemberView : LexObject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    /// <summary>
    /// Counts and settings for what the account has published or allows, including who may chat
    /// with it.
    /// </summary>
    [JsonPropertyName("associated")]
    public ProfileAssociated? Associated { get; init; }

    /// <summary>The viewer's relationship to the member.</summary>
    [JsonPropertyName("viewer")]
    public ViewerState? Viewer { get; init; }

    /// <summary>The labels applied to the member's account.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>When the member's account was created.</summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; init; }

    /// <summary>Whether chat is disabled for this account.</summary>
    [JsonPropertyName("chatDisabled")]
    public bool? ChatDisabled { get; init; }

    /// <summary>The member's verification state.</summary>
    [JsonPropertyName("verification")]
    public VerificationState? Verification { get; init; }

    /// <summary>
    /// The member's place in the conversation: a <see cref="DirectConvoMember"/>, a current
    /// <see cref="GroupConvoMember"/> with its role, or a <see cref="PastGroupConvoMember"/>.
    /// </summary>
    [JsonPropertyName("kind")]
    public ChatMemberKind? Kind { get; init; }
}

// ──────────────────────────────────────────────────────────
//  Messages
// ──────────────────────────────────────────────────────────

/// <summary>
/// A message in a conversation (the open unions of <c>chat.bsky.convo.getMessages</c>,
/// <c>#convoView.lastMessage</c>, <c>#messageView.replyTo</c> and the log entries): a
/// <see cref="MessageView"/>, <see cref="DeletedMessageView"/>, <see cref="SystemMessageView"/>
/// or <see cref="MessageBeforeUserJoinedGroupView"/>. Each of those unions allows a subset; a
/// message this SDK does not model reads as <see cref="UnknownConvoMessage"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownConvoMessage))]
[JsonDerivedType(typeof(MessageView), "chat.bsky.convo.defs#messageView")]
[JsonDerivedType(typeof(DeletedMessageView), "chat.bsky.convo.defs#deletedMessageView")]
[JsonDerivedType(typeof(SystemMessageView), "chat.bsky.convo.defs#systemMessageView")]
[JsonDerivedType(typeof(MessageBeforeUserJoinedGroupView), "chat.bsky.convo.defs#messageBeforeUserJoinedGroupView")]
public abstract class ConvoMessage : LexObject;

/// <summary>
/// A message whose <c>$type</c> this SDK version does not model. It keeps the raw object and
/// writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownConvoMessage : ConvoMessage, IUnknownUnionVariant
{
    /// <summary>Creates an unknown message from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownConvoMessage(string type, JsonElement raw)
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
/// A message a member sent (<c>chat.bsky.convo.defs#messageView</c>).
/// </summary>
public sealed class MessageView : ConvoMessage
{
    /// <summary>The identifier of the message.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The message's revision, an opaque string the chat service assigns.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>The message text.</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>Rich-text facets (mentions, links, tags) applied to the text.</summary>
    [JsonPropertyName("facets")]
    public IReadOnlyList<Facet>? Facets { get; init; }

    /// <summary>
    /// Embedded content: a <see cref="MessageRecordEmbedView"/> (such as a quoted post) or a
    /// <see cref="JoinLinkEmbedView"/>.
    /// </summary>
    [JsonPropertyName("embed")]
    public MessageEmbedView? Embed { get; init; }

    /// <summary>The reactions to the message, oldest first.</summary>
    [JsonPropertyName("reactions")]
    public IReadOnlyList<ReactionView>? Reactions { get; init; }

    /// <summary>
    /// The message this one replies to: a <see cref="MessageView"/>, a
    /// <see cref="DeletedMessageView"/>, or a <see cref="MessageBeforeUserJoinedGroupView"/> when
    /// it predates the viewer joining the group. Only one level is embedded: its own
    /// <see cref="ReplyTo"/> is never set.
    /// </summary>
    [JsonPropertyName("replyTo")]
    public ConvoMessage? ReplyTo { get; init; }

    /// <summary>The sender of the message.</summary>
    [JsonPropertyName("sender")]
    public required MessageSender Sender { get; init; }

    /// <summary>When the message was sent.</summary>
    [JsonPropertyName("sentAt")]
    public required AtDatetime SentAt { get; init; }
}

/// <summary>
/// A deleted message placeholder (<c>chat.bsky.convo.defs#deletedMessageView</c>).
/// </summary>
public sealed class DeletedMessageView : ConvoMessage
{
    /// <summary>The identifier of the message.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The message's revision, an opaque string the chat service assigns.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>The sender of the message.</summary>
    [JsonPropertyName("sender")]
    public required MessageSender Sender { get; init; }

    /// <summary>When the message was sent.</summary>
    [JsonPropertyName("sentAt")]
    public required AtDatetime SentAt { get; init; }
}

/// <summary>
/// Stands in for the message a reply answers when that message was sent before the viewer joined
/// the group, so the viewer may not see it
/// (<c>chat.bsky.convo.defs#messageBeforeUserJoinedGroupView</c>). It carries no message data.
/// </summary>
public sealed class MessageBeforeUserJoinedGroupView : ConvoMessage;

/// <summary>
/// The sender of a message.
/// </summary>
public sealed class MessageSender : LexObject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }
}

/// <summary>
/// A reaction to a message (<c>chat.bsky.convo.defs#reactionView</c>).
/// </summary>
public sealed class ReactionView : LexObject
{
    /// <summary>The reaction, a single emoji.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>Who reacted.</summary>
    [JsonPropertyName("sender")]
    public required ReactionViewSender Sender { get; init; }

    /// <summary>When the reaction was added.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// The account that added a reaction (<c>chat.bsky.convo.defs#reactionViewSender</c>).
/// </summary>
public sealed class ReactionViewSender : LexObject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }
}

// ──────────────────────────────────────────────────────────
//  System messages
// ──────────────────────────────────────────────────────────

/// <summary>
/// A message the chat service adds to a group conversation when something happens to it, such as a
/// member joining or the group being renamed (<c>chat.bsky.convo.defs#systemMessageView</c>).
/// </summary>
public sealed class SystemMessageView : ConvoMessage
{
    /// <summary>The identifier of the message.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The message's revision, an opaque string the chat service assigns.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>When the message was added.</summary>
    [JsonPropertyName("sentAt")]
    public required AtDatetime SentAt { get; init; }

    /// <summary>What happened, such as a <see cref="SystemMessageDataAddMember"/>.</summary>
    [JsonPropertyName("data")]
    public required SystemMessageData Data { get; init; }
}

/// <summary>
/// An account a system message refers to (<c>chat.bsky.convo.defs#systemMessageReferredUser</c>).
/// Its profile is in the response's related profiles.
/// </summary>
public sealed class SystemMessageReferredUser : LexObject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }
}

/// <summary>
/// What a <see cref="SystemMessageView"/> reports (the open
/// <c>chat.bsky.convo.defs#systemMessageView.data</c> union). An event this SDK does not model reads
/// as <see cref="UnknownSystemMessageData"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownSystemMessageData))]
[JsonDerivedType(typeof(SystemMessageDataAddMember), "chat.bsky.convo.defs#systemMessageDataAddMember")]
[JsonDerivedType(typeof(SystemMessageDataRemoveMember), "chat.bsky.convo.defs#systemMessageDataRemoveMember")]
[JsonDerivedType(typeof(SystemMessageDataMemberJoin), "chat.bsky.convo.defs#systemMessageDataMemberJoin")]
[JsonDerivedType(typeof(SystemMessageDataMemberLeave), "chat.bsky.convo.defs#systemMessageDataMemberLeave")]
[JsonDerivedType(typeof(SystemMessageDataLockConvo), "chat.bsky.convo.defs#systemMessageDataLockConvo")]
[JsonDerivedType(typeof(SystemMessageDataUnlockConvo), "chat.bsky.convo.defs#systemMessageDataUnlockConvo")]
[JsonDerivedType(typeof(SystemMessageDataLockConvoPermanently), "chat.bsky.convo.defs#systemMessageDataLockConvoPermanently")]
[JsonDerivedType(typeof(SystemMessageDataEditGroup), "chat.bsky.convo.defs#systemMessageDataEditGroup")]
[JsonDerivedType(typeof(SystemMessageDataCreateJoinLink), "chat.bsky.convo.defs#systemMessageDataCreateJoinLink")]
[JsonDerivedType(typeof(SystemMessageDataEditJoinLink), "chat.bsky.convo.defs#systemMessageDataEditJoinLink")]
[JsonDerivedType(typeof(SystemMessageDataEnableJoinLink), "chat.bsky.convo.defs#systemMessageDataEnableJoinLink")]
[JsonDerivedType(typeof(SystemMessageDataDisableJoinLink), "chat.bsky.convo.defs#systemMessageDataDisableJoinLink")]
public abstract class SystemMessageData : LexObject;

/// <summary>
/// A system message event whose <c>$type</c> this SDK version does not model. It keeps the raw
/// object and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownSystemMessageData : SystemMessageData, IUnknownUnionVariant
{
    /// <summary>Creates an unknown system message event from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownSystemMessageData(string type, JsonElement raw)
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

/// <summary>A member was added to the group (<c>#systemMessageDataAddMember</c>).</summary>
public sealed class SystemMessageDataAddMember : SystemMessageData
{
    /// <summary>The member who was added.</summary>
    [JsonPropertyName("member")]
    public required SystemMessageReferredUser Member { get; init; }

    /// <summary>
    /// The role the member was added with (see <see cref="ChatMemberRole"/>); the member's current
    /// role may differ.
    /// </summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>Who added the member.</summary>
    [JsonPropertyName("addedBy")]
    public required SystemMessageReferredUser AddedBy { get; init; }
}

/// <summary>A member was removed from the group (<c>#systemMessageDataRemoveMember</c>).</summary>
public sealed class SystemMessageDataRemoveMember : SystemMessageData
{
    /// <summary>The member who was removed.</summary>
    [JsonPropertyName("member")]
    public required SystemMessageReferredUser Member { get; init; }

    /// <summary>Who removed the member.</summary>
    [JsonPropertyName("removedBy")]
    public required SystemMessageReferredUser RemovedBy { get; init; }
}

/// <summary>A member joined the group through its join link (<c>#systemMessageDataMemberJoin</c>).</summary>
public sealed class SystemMessageDataMemberJoin : SystemMessageData
{
    /// <summary>The member who joined.</summary>
    [JsonPropertyName("member")]
    public required SystemMessageReferredUser Member { get; init; }

    /// <summary>
    /// The role the member joined with (see <see cref="ChatMemberRole"/>); the member's current
    /// role may differ.
    /// </summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>Who approved the join request; absent when the link needs no approval.</summary>
    [JsonPropertyName("approvedBy")]
    public SystemMessageReferredUser? ApprovedBy { get; init; }
}

/// <summary>A member left the group (<c>#systemMessageDataMemberLeave</c>).</summary>
public sealed class SystemMessageDataMemberLeave : SystemMessageData
{
    /// <summary>The member who left.</summary>
    [JsonPropertyName("member")]
    public required SystemMessageReferredUser Member { get; init; }
}

/// <summary>The group was locked (<c>#systemMessageDataLockConvo</c>).</summary>
public sealed class SystemMessageDataLockConvo : SystemMessageData
{
    /// <summary>Who locked the group.</summary>
    [JsonPropertyName("lockedBy")]
    public required SystemMessageReferredUser LockedBy { get; init; }
}

/// <summary>The group was unlocked (<c>#systemMessageDataUnlockConvo</c>).</summary>
public sealed class SystemMessageDataUnlockConvo : SystemMessageData
{
    /// <summary>Who unlocked the group.</summary>
    [JsonPropertyName("unlockedBy")]
    public required SystemMessageReferredUser UnlockedBy { get; init; }
}

/// <summary>The group was locked for good (<c>#systemMessageDataLockConvoPermanently</c>).</summary>
public sealed class SystemMessageDataLockConvoPermanently : SystemMessageData
{
    /// <summary>Who locked the group.</summary>
    [JsonPropertyName("lockedBy")]
    public required SystemMessageReferredUser LockedBy { get; init; }
}

/// <summary>The group's details were edited (<c>#systemMessageDataEditGroup</c>).</summary>
public sealed class SystemMessageDataEditGroup : SystemMessageData
{
    /// <summary>The group's name before the edit.</summary>
    [JsonPropertyName("oldName")]
    public string? OldName { get; init; }

    /// <summary>The group's name after the edit.</summary>
    [JsonPropertyName("newName")]
    public string? NewName { get; init; }
}

/// <summary>The group's join link was created (<c>#systemMessageDataCreateJoinLink</c>).</summary>
public sealed class SystemMessageDataCreateJoinLink : SystemMessageData;

/// <summary>The group's join link settings were edited (<c>#systemMessageDataEditJoinLink</c>).</summary>
public sealed class SystemMessageDataEditJoinLink : SystemMessageData;

/// <summary>The group's join link was enabled (<c>#systemMessageDataEnableJoinLink</c>).</summary>
public sealed class SystemMessageDataEnableJoinLink : SystemMessageData;

/// <summary>The group's join link was disabled (<c>#systemMessageDataDisableJoinLink</c>).</summary>
public sealed class SystemMessageDataDisableJoinLink : SystemMessageData;

// ──────────────────────────────────────────────────────────
//  Conversation log (chat.bsky.convo.getLog)
// ──────────────────────────────────────────────────────────

/// <summary>
/// An entry of the conversation log (the open <c>chat.bsky.convo.getLog</c> union), such as a
/// <see cref="LogCreateMessage"/> or a <see cref="LogAddMember"/>. Every entry names its
/// conversation and revision; an entry this SDK does not model reads as
/// <see cref="UnknownConvoLogEntry"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownConvoLogEntry))]
[JsonDerivedType(typeof(LogBeginConvo), "chat.bsky.convo.defs#logBeginConvo")]
[JsonDerivedType(typeof(LogAcceptConvo), "chat.bsky.convo.defs#logAcceptConvo")]
[JsonDerivedType(typeof(LogLeaveConvo), "chat.bsky.convo.defs#logLeaveConvo")]
[JsonDerivedType(typeof(LogMuteConvo), "chat.bsky.convo.defs#logMuteConvo")]
[JsonDerivedType(typeof(LogUnmuteConvo), "chat.bsky.convo.defs#logUnmuteConvo")]
[JsonDerivedType(typeof(LogCreateMessage), "chat.bsky.convo.defs#logCreateMessage")]
[JsonDerivedType(typeof(LogDeleteMessage), "chat.bsky.convo.defs#logDeleteMessage")]
[JsonDerivedType(typeof(LogReadMessage), "chat.bsky.convo.defs#logReadMessage")]
[JsonDerivedType(typeof(LogAddReaction), "chat.bsky.convo.defs#logAddReaction")]
[JsonDerivedType(typeof(LogRemoveReaction), "chat.bsky.convo.defs#logRemoveReaction")]
[JsonDerivedType(typeof(LogReadConvo), "chat.bsky.convo.defs#logReadConvo")]
[JsonDerivedType(typeof(LogAddMember), "chat.bsky.convo.defs#logAddMember")]
[JsonDerivedType(typeof(LogRemoveMember), "chat.bsky.convo.defs#logRemoveMember")]
[JsonDerivedType(typeof(LogMemberJoin), "chat.bsky.convo.defs#logMemberJoin")]
[JsonDerivedType(typeof(LogMemberLeave), "chat.bsky.convo.defs#logMemberLeave")]
[JsonDerivedType(typeof(LogLockConvo), "chat.bsky.convo.defs#logLockConvo")]
[JsonDerivedType(typeof(LogUnlockConvo), "chat.bsky.convo.defs#logUnlockConvo")]
[JsonDerivedType(typeof(LogLockConvoPermanently), "chat.bsky.convo.defs#logLockConvoPermanently")]
[JsonDerivedType(typeof(LogEditGroup), "chat.bsky.convo.defs#logEditGroup")]
[JsonDerivedType(typeof(LogCreateJoinLink), "chat.bsky.convo.defs#logCreateJoinLink")]
[JsonDerivedType(typeof(LogEditJoinLink), "chat.bsky.convo.defs#logEditJoinLink")]
[JsonDerivedType(typeof(LogEnableJoinLink), "chat.bsky.convo.defs#logEnableJoinLink")]
[JsonDerivedType(typeof(LogDisableJoinLink), "chat.bsky.convo.defs#logDisableJoinLink")]
[JsonDerivedType(typeof(LogIncomingJoinRequest), "chat.bsky.convo.defs#logIncomingJoinRequest")]
[JsonDerivedType(typeof(LogApproveJoinRequest), "chat.bsky.convo.defs#logApproveJoinRequest")]
[JsonDerivedType(typeof(LogRejectJoinRequest), "chat.bsky.convo.defs#logRejectJoinRequest")]
[JsonDerivedType(typeof(LogOutgoingJoinRequest), "chat.bsky.convo.defs#logOutgoingJoinRequest")]
[JsonDerivedType(typeof(LogWithdrawIncomingJoinRequest), "chat.bsky.convo.defs#logWithdrawIncomingJoinRequest")]
[JsonDerivedType(typeof(LogWithdrawOutgoingJoinRequest), "chat.bsky.convo.defs#logWithdrawOutgoingJoinRequest")]
[JsonDerivedType(typeof(LogReadJoinRequests), "chat.bsky.convo.defs#logReadJoinRequests")]
public abstract class ConvoLogEntry : LexObject
{
    /// <summary>The revision of the change this entry records, an opaque string the chat service assigns.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// A log entry whose <c>$type</c> this SDK version does not model. It keeps the raw object and
/// writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownConvoLogEntry : ConvoLogEntry, IUnknownUnionVariant
{
    /// <summary>
    /// Creates an unknown log entry from its discriminator and raw object. <see cref="ConvoLogEntry.Rev"/>
    /// and <see cref="ConvoLogEntry.ConvoId"/> are read from the object, and are empty when it lacks
    /// them.
    /// </summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    [SetsRequiredMembers]
    public UnknownConvoLogEntry(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Raw = UnknownUnionVariant.RequireObject(raw);
        Rev = StringProperty(Raw, "rev");
        ConvoId = StringProperty(Raw, "convoId");
    }

    // Not wire properties: the SDK writes an unknown variant as its Raw object.

    /// <inheritdoc/>
    [JsonIgnore]
    public string Type { get; }

    /// <inheritdoc/>
    [JsonIgnore]
    public JsonElement Raw { get; }

    private static string StringProperty(JsonElement raw, string name) =>
        raw.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : "";
}

/// <summary>
/// A conversation with the viewer started, direct or group; also sent to a member added to a
/// group (<c>#logBeginConvo</c>).
/// </summary>
public sealed class LogBeginConvo : ConvoLogEntry;

/// <summary>
/// The viewer accepted a conversation, which leaves the request inbox (<c>#logAcceptConvo</c>).
/// </summary>
public sealed class LogAcceptConvo : ConvoLogEntry;

/// <summary>The viewer left a conversation (<c>#logLeaveConvo</c>).</summary>
public sealed class LogLeaveConvo : ConvoLogEntry;

/// <summary>The viewer muted a conversation (<c>#logMuteConvo</c>).</summary>
public sealed class LogMuteConvo : ConvoLogEntry;

/// <summary>The viewer unmuted a conversation (<c>#logUnmuteConvo</c>).</summary>
public sealed class LogUnmuteConvo : ConvoLogEntry;

/// <summary>A member sent a message; not sent for system messages (<c>#logCreateMessage</c>).</summary>
public sealed class LogCreateMessage : ConvoLogEntry
{
    /// <summary>The message: a <see cref="MessageView"/> or <see cref="DeletedMessageView"/>.</summary>
    [JsonPropertyName("message")]
    public required ConvoMessage Message { get; init; }

    /// <summary>The profiles the message refers to.</summary>
    [JsonPropertyName("relatedProfiles")]
    public IReadOnlyList<ChatMemberView>? RelatedProfiles { get; init; }
}

/// <summary>A member's message was deleted; not sent for system messages (<c>#logDeleteMessage</c>).</summary>
public sealed class LogDeleteMessage : ConvoLogEntry
{
    /// <summary>The message: a <see cref="MessageView"/> or <see cref="DeletedMessageView"/>.</summary>
    [JsonPropertyName("message")]
    public required ConvoMessage Message { get; init; }
}

/// <summary>
/// A conversation was read up to a message (<c>#logReadMessage</c>). Deprecated upstream in favour
/// of <see cref="LogReadConvo"/>.
/// </summary>
public sealed class LogReadMessage : ConvoLogEntry
{
    /// <summary>
    /// The last message read: a <see cref="MessageView"/>, <see cref="DeletedMessageView"/> or
    /// <see cref="SystemMessageView"/>.
    /// </summary>
    [JsonPropertyName("message")]
    public required ConvoMessage Message { get; init; }
}

/// <summary>A reaction was added to a message (<c>#logAddReaction</c>).</summary>
public sealed class LogAddReaction : ConvoLogEntry
{
    /// <summary>The message: a <see cref="MessageView"/> or <see cref="DeletedMessageView"/>.</summary>
    [JsonPropertyName("message")]
    public required ConvoMessage Message { get; init; }

    /// <summary>The reaction that was added.</summary>
    [JsonPropertyName("reaction")]
    public required ReactionView Reaction { get; init; }

    /// <summary>The profiles the message and reaction refer to.</summary>
    [JsonPropertyName("relatedProfiles")]
    public IReadOnlyList<ChatMemberView>? RelatedProfiles { get; init; }
}

/// <summary>A reaction was removed from a message (<c>#logRemoveReaction</c>).</summary>
public sealed class LogRemoveReaction : ConvoLogEntry
{
    /// <summary>The message: a <see cref="MessageView"/> or <see cref="DeletedMessageView"/>.</summary>
    [JsonPropertyName("message")]
    public required ConvoMessage Message { get; init; }

    /// <summary>The reaction that was removed.</summary>
    [JsonPropertyName("reaction")]
    public required ReactionView Reaction { get; init; }

    /// <summary>The profiles the message and reaction refer to.</summary>
    [JsonPropertyName("relatedProfiles")]
    public IReadOnlyList<ChatMemberView>? RelatedProfiles { get; init; }
}

/// <summary>A conversation was read up to a message (<c>#logReadConvo</c>).</summary>
public sealed class LogReadConvo : ConvoLogEntry
{
    /// <summary>
    /// The last message read: a <see cref="MessageView"/>, <see cref="DeletedMessageView"/> or
    /// <see cref="SystemMessageView"/>.
    /// </summary>
    [JsonPropertyName("message")]
    public required ConvoMessage Message { get; init; }
}

/// <summary>
/// A member was added to a group (<c>#logAddMember</c>). The added member also gets a
/// <see cref="LogBeginConvo"/>.
/// </summary>
public sealed class LogAddMember : ConvoLogEntry
{
    /// <summary>The system message, with <see cref="SystemMessageDataAddMember"/> data.</summary>
    [JsonPropertyName("message")]
    public required SystemMessageView Message { get; init; }

    /// <summary>The profiles the system message refers to.</summary>
    [JsonPropertyName("relatedProfiles")]
    public required IReadOnlyList<ChatMemberView> RelatedProfiles { get; init; }
}

/// <summary>
/// A member was removed from a group (<c>#logRemoveMember</c>). The removed member gets a
/// <see cref="LogLeaveConvo"/> instead.
/// </summary>
public sealed class LogRemoveMember : ConvoLogEntry
{
    /// <summary>The system message, with <see cref="SystemMessageDataRemoveMember"/> data.</summary>
    [JsonPropertyName("message")]
    public required SystemMessageView Message { get; init; }

    /// <summary>The profiles the system message refers to.</summary>
    [JsonPropertyName("relatedProfiles")]
    public required IReadOnlyList<ChatMemberView> RelatedProfiles { get; init; }
}

/// <summary>
/// A member joined a group through its join link (<c>#logMemberJoin</c>). The new member also
/// gets a <see cref="LogBeginConvo"/>.
/// </summary>
public sealed class LogMemberJoin : ConvoLogEntry
{
    /// <summary>The system message, with <see cref="SystemMessageDataMemberJoin"/> data.</summary>
    [JsonPropertyName("message")]
    public required SystemMessageView Message { get; init; }

    /// <summary>The profiles the system message refers to.</summary>
    [JsonPropertyName("relatedProfiles")]
    public required IReadOnlyList<ChatMemberView> RelatedProfiles { get; init; }
}

/// <summary>
/// A member left a group (<c>#logMemberLeave</c>). The member who left gets a
/// <see cref="LogLeaveConvo"/> instead.
/// </summary>
public sealed class LogMemberLeave : ConvoLogEntry
{
    /// <summary>The system message, with <see cref="SystemMessageDataMemberLeave"/> data.</summary>
    [JsonPropertyName("message")]
    public required SystemMessageView Message { get; init; }

    /// <summary>The profiles the system message refers to.</summary>
    [JsonPropertyName("relatedProfiles")]
    public required IReadOnlyList<ChatMemberView> RelatedProfiles { get; init; }
}

/// <summary>A group was locked (<c>#logLockConvo</c>).</summary>
public sealed class LogLockConvo : ConvoLogEntry
{
    /// <summary>The system message, with <see cref="SystemMessageDataLockConvo"/> data.</summary>
    [JsonPropertyName("message")]
    public required SystemMessageView Message { get; init; }

    /// <summary>The profiles the system message refers to.</summary>
    [JsonPropertyName("relatedProfiles")]
    public required IReadOnlyList<ChatMemberView> RelatedProfiles { get; init; }
}

/// <summary>A group was unlocked (<c>#logUnlockConvo</c>).</summary>
public sealed class LogUnlockConvo : ConvoLogEntry
{
    /// <summary>The system message, with <see cref="SystemMessageDataUnlockConvo"/> data.</summary>
    [JsonPropertyName("message")]
    public required SystemMessageView Message { get; init; }

    /// <summary>The profiles the system message refers to.</summary>
    [JsonPropertyName("relatedProfiles")]
    public required IReadOnlyList<ChatMemberView> RelatedProfiles { get; init; }
}

/// <summary>A group was locked for good (<c>#logLockConvoPermanently</c>).</summary>
public sealed class LogLockConvoPermanently : ConvoLogEntry
{
    /// <summary>The system message, with <see cref="SystemMessageDataLockConvoPermanently"/> data.</summary>
    [JsonPropertyName("message")]
    public required SystemMessageView Message { get; init; }

    /// <summary>The profiles the system message refers to.</summary>
    [JsonPropertyName("relatedProfiles")]
    public required IReadOnlyList<ChatMemberView> RelatedProfiles { get; init; }
}

/// <summary>A group's details were edited (<c>#logEditGroup</c>).</summary>
public sealed class LogEditGroup : ConvoLogEntry
{
    /// <summary>The system message, with <see cref="SystemMessageDataEditGroup"/> data.</summary>
    [JsonPropertyName("message")]
    public required SystemMessageView Message { get; init; }
}

/// <summary>A group's join link was created (<c>#logCreateJoinLink</c>).</summary>
public sealed class LogCreateJoinLink : ConvoLogEntry
{
    /// <summary>The system message, with <see cref="SystemMessageDataCreateJoinLink"/> data.</summary>
    [JsonPropertyName("message")]
    public required SystemMessageView Message { get; init; }
}

/// <summary>A group's join link settings were edited (<c>#logEditJoinLink</c>).</summary>
public sealed class LogEditJoinLink : ConvoLogEntry
{
    /// <summary>The system message, with <see cref="SystemMessageDataEditJoinLink"/> data.</summary>
    [JsonPropertyName("message")]
    public required SystemMessageView Message { get; init; }
}

/// <summary>A group's join link was enabled (<c>#logEnableJoinLink</c>).</summary>
public sealed class LogEnableJoinLink : ConvoLogEntry
{
    /// <summary>The system message, with <see cref="SystemMessageDataEnableJoinLink"/> data.</summary>
    [JsonPropertyName("message")]
    public required SystemMessageView Message { get; init; }
}

/// <summary>A group's join link was disabled (<c>#logDisableJoinLink</c>).</summary>
public sealed class LogDisableJoinLink : ConvoLogEntry
{
    /// <summary>The system message, with <see cref="SystemMessageDataDisableJoinLink"/> data.</summary>
    [JsonPropertyName("message")]
    public required SystemMessageView Message { get; init; }
}

/// <summary>
/// Someone asked to join a group the viewer owns (<c>#logIncomingJoinRequest</c>). Only the owner
/// gets it.
/// </summary>
public sealed class LogIncomingJoinRequest : ConvoLogEntry
{
    /// <summary>The account asking to join.</summary>
    [JsonPropertyName("member")]
    public required ChatMemberView Member { get; init; }
}

/// <summary>
/// The viewer approved a join request (<c>#logApproveJoinRequest</c>). Only the owner gets it; the
/// new member gets a <see cref="LogBeginConvo"/>.
/// </summary>
public sealed class LogApproveJoinRequest : ConvoLogEntry
{
    /// <summary>The account that asked to join.</summary>
    [JsonPropertyName("member")]
    public required ChatMemberView Member { get; init; }
}

/// <summary>The viewer rejected a join request (<c>#logRejectJoinRequest</c>). Only the owner gets it.</summary>
public sealed class LogRejectJoinRequest : ConvoLogEntry
{
    /// <summary>The account that asked to join.</summary>
    [JsonPropertyName("member")]
    public required ChatMemberView Member { get; init; }
}

/// <summary>The viewer asked to join a group (<c>#logOutgoingJoinRequest</c>). Only the requester gets it.</summary>
public sealed class LogOutgoingJoinRequest : ConvoLogEntry;

/// <summary>
/// Someone withdrew their request to join a group the viewer owns
/// (<c>#logWithdrawIncomingJoinRequest</c>). Only the owner gets it.
/// </summary>
public sealed class LogWithdrawIncomingJoinRequest : ConvoLogEntry
{
    /// <summary>The account that withdrew its request.</summary>
    [JsonPropertyName("member")]
    public required ChatMemberView Member { get; init; }
}

/// <summary>
/// The viewer withdrew their own join request (<c>#logWithdrawOutgoingJoinRequest</c>). Only the
/// requester gets it.
/// </summary>
public sealed class LogWithdrawOutgoingJoinRequest : ConvoLogEntry;

/// <summary>
/// The group owner marked the join requests read (<c>#logReadJoinRequests</c>). Only the owner gets
/// it.
/// </summary>
public sealed class LogReadJoinRequests : ConvoLogEntry;

// ──────────────────────────────────────────────────────────
//  Request models
// ──────────────────────────────────────────────────────────

/// <summary>
/// Request body for chat.bsky.convo.sendMessage.
/// </summary>
internal sealed class SendMessageRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The message.</summary>
    [JsonPropertyName("message")]
    public required MessageInput Message { get; init; }
}

/// <summary>
/// Input for a message to be sent.
/// </summary>
public sealed class MessageInput : LexObject
{
    /// <summary>The message text.</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>Rich-text facets (mentions, links, tags) applied to the text.</summary>
    [JsonPropertyName("facets")]
    public IReadOnlyList<Facet>? Facets { get; init; }

    /// <summary>
    /// Embedded content: a <see cref="MessageRecordEmbed"/> (such as a quoted post) or a
    /// <see cref="JoinLinkEmbed"/>.
    /// </summary>
    [JsonPropertyName("embed")]
    public MessageEmbed? Embed { get; init; }

    /// <summary>The message this one replies to, which must be in the same conversation.</summary>
    [JsonPropertyName("replyTo")]
    public MessageReplyRef? ReplyTo { get; init; }
}

/// <summary>
/// A reference to the message a new message replies to (<c>chat.bsky.convo.defs#replyRef</c>).
/// </summary>
public sealed class MessageReplyRef : LexObject
{
    /// <summary>The identifier of the message, in the same conversation.</summary>
    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }
}

/// <summary>
/// A message within a batch send request.
/// </summary>
public sealed class BatchMessageItem : LexObject
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The message.</summary>
    [JsonPropertyName("message")]
    public required MessageInput Message { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.sendMessageBatch.
/// </summary>
internal sealed class SendMessageBatchRequest
{
    /// <summary>The messages to send.</summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<BatchMessageItem> Items { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.deleteMessageForSelf.
/// </summary>
internal sealed class DeleteMessageForSelfRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The identifier of the message.</summary>
    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.leaveConvo.
/// </summary>
internal sealed class LeaveConvoRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.muteConvo.
/// </summary>
internal sealed class MuteConvoRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.unmuteConvo.
/// </summary>
internal sealed class UnmuteConvoRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.lockConvo.
/// </summary>
internal sealed class LockConvoRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.unlockConvo.
/// </summary>
internal sealed class UnlockConvoRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.updateRead.
/// </summary>
internal sealed class UpdateReadRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The identifier of the message.</summary>
    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.updateAllRead.
/// </summary>
internal sealed class UpdateAllReadRequest
{
    /// <summary>Only conversations with this status (<c>request</c> or <c>accepted</c>).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.acceptConvo.
/// </summary>
internal sealed class AcceptConvoRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.addReaction.
/// </summary>
internal sealed class AddReactionRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The identifier of the message.</summary>
    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }

    /// <summary>The reaction emoji.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.removeReaction.
/// </summary>
internal sealed class RemoveReactionRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The identifier of the message.</summary>
    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }

    /// <summary>The record value.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

// ──────────────────────────────────────────────────────────
//  Response models
// ──────────────────────────────────────────────────────────

/// <summary>
/// Response from chat.bsky.convo.listConvos.
/// </summary>
public sealed class ListConvosResponse : ICursorPage<ConvoView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The conversations.</summary>
    [JsonPropertyName("convos")]
    public required IReadOnlyList<ConvoView> Convos { get; init; }

    IReadOnlyList<ConvoView> ICursorPage<ConvoView>.Items => Convos;
}

/// <summary>
/// Response from chat.bsky.convo.listConvoRequests.
/// </summary>
public sealed class ListConvoRequestsResponse : ICursorPage<ConvoRequestView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>
    /// The requests: incoming conversation requests (<see cref="ConvoView"/>) and the viewer's own
    /// group join requests (<see cref="JoinRequestConvoView"/>).
    /// </summary>
    [JsonPropertyName("requests")]
    public required IReadOnlyList<ConvoRequestView> Requests { get; init; }

    IReadOnlyList<ConvoRequestView> ICursorPage<ConvoRequestView>.Items => Requests;
}

/// <summary>
/// Response from chat.bsky.convo.getConvo.
/// </summary>
public sealed class GetConvoResponse
{
    /// <summary>The conversation.</summary>
    [JsonPropertyName("convo")]
    public required ConvoView Convo { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.getConvoForMembers.
/// </summary>
public sealed class GetConvoForMembersResponse
{
    /// <summary>The conversation.</summary>
    [JsonPropertyName("convo")]
    public required ConvoView Convo { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.getConvoAvailability.
/// </summary>
public sealed class GetConvoAvailabilityResponse
{
    /// <summary>Whether the viewer may chat with the given members.</summary>
    [JsonPropertyName("canChat")]
    public required bool CanChat { get; init; }

    /// <summary>The existing conversation with those members, if there is one.</summary>
    [JsonPropertyName("convo")]
    public ConvoView? Convo { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.getConvoMembers; chat.bsky.moderation.getConvoMembers answers
/// the same shape.
/// </summary>
public sealed class GetConvoMembersResponse : ICursorPage<ChatMemberView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The members.</summary>
    [JsonPropertyName("members")]
    public required IReadOnlyList<ChatMemberView> Members { get; init; }

    IReadOnlyList<ChatMemberView> ICursorPage<ChatMemberView>.Items => Members;
}

/// <summary>
/// Response from chat.bsky.convo.getUnreadCounts.
/// </summary>
public sealed class GetUnreadCountsResponse
{
    /// <summary>
    /// Unlocked accepted conversations with unread messages or, for a group owner, unread join
    /// requests. Capped at 100, which means more than 99.
    /// </summary>
    [JsonPropertyName("unreadAcceptedConvos")]
    public required int UnreadAcceptedConvos { get; init; }

    /// <summary>
    /// Unlocked conversation requests with unread messages. Capped at 100, which means more than 99.
    /// </summary>
    [JsonPropertyName("unreadRequestConvos")]
    public required int UnreadRequestConvos { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.getMessages.
/// </summary>
public sealed class GetMessagesResponse : ICursorPage<ConvoMessage>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>
    /// The messages: <see cref="MessageView"/>, <see cref="DeletedMessageView"/> and, in groups,
    /// <see cref="SystemMessageView"/>.
    /// </summary>
    [JsonPropertyName("messages")]
    public required IReadOnlyList<ConvoMessage> Messages { get; init; }

    /// <summary>
    /// The profiles of everyone who wrote or reacted to the messages, and of the accounts the
    /// system messages refer to.
    /// </summary>
    [JsonPropertyName("relatedProfiles")]
    public IReadOnlyList<ChatMemberView>? RelatedProfiles { get; init; }

    IReadOnlyList<ConvoMessage> ICursorPage<ConvoMessage>.Items => Messages;
}

/// <summary>
/// Response from chat.bsky.convo.sendMessageBatch.
/// </summary>
public sealed class SendMessageBatchResponse
{
    /// <summary>The sent messages.</summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<MessageView> Items { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.leaveConvo.
/// </summary>
public sealed class LeaveConvoResponse
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The conversation's revision after leaving it.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.acceptConvo.
/// </summary>
public sealed class AcceptConvoResponse
{
    /// <summary>
    /// The conversation's revision after accepting it; absent when it was already accepted.
    /// </summary>
    [JsonPropertyName("rev")]
    public string? Rev { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.updateAllRead.
/// </summary>
public sealed class UpdateAllReadResponse
{
    /// <summary>The number of conversations that were marked read.</summary>
    [JsonPropertyName("updatedCount")]
    public required int UpdatedCount { get; init; }
}

/// <summary>
/// The <c>{convo}</c> output of chat.bsky.convo.muteConvo, unmuteConvo, updateRead, lockConvo and
/// unlockConvo, and of the chat.bsky.group methods that answer the group, which the clients unwrap.
/// </summary>
internal sealed class ConvoOutput
{
    /// <summary>The conversation after the change.</summary>
    [JsonPropertyName("convo")]
    public required ConvoView Convo { get; init; }
}

/// <summary>
/// The output of chat.bsky.convo.addReaction and removeReaction, which the client unwraps.
/// </summary>
internal sealed class MessageOutput
{
    /// <summary>The message after the change.</summary>
    [JsonPropertyName("message")]
    public required MessageView Message { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.getLog.
/// </summary>
public sealed class GetLogResponse : ICursorPage<ConvoLogEntry>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The conversation log entries.</summary>
    [JsonPropertyName("logs")]
    public required IReadOnlyList<ConvoLogEntry> Logs { get; init; }

    IReadOnlyList<ConvoLogEntry> ICursorPage<ConvoLogEntry>.Items => Logs;
}
