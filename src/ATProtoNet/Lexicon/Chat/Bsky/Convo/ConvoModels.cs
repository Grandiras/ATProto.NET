using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Chat.Bsky.Convo;

// ──────────────────────────────────────────────────────────
//  View / Response models
// ──────────────────────────────────────────────────────────

/// <summary>
/// A conversation view returned by chat.bsky.convo endpoints.
/// </summary>
public sealed class ConvoView : LexObject
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The conversation's revision, an opaque string the chat service assigns.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>The members.</summary>
    [JsonPropertyName("members")]
    public required IReadOnlyList<ChatMemberView> Members { get; init; }

    /// <summary>The most recent message in the conversation.</summary>
    [JsonPropertyName("lastMessage")]
    public JsonElement? LastMessage { get; init; }

    /// <summary>Whether the viewer has muted this conversation.</summary>
    [JsonPropertyName("muted")]
    public bool Muted { get; init; }

    /// <summary>The status of the conversation (<c>request</c> or <c>accepted</c>).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>The number of unread messages.</summary>
    [JsonPropertyName("unreadCount")]
    public int UnreadCount { get; init; }
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
}

/// <summary>
/// A message view within a conversation.
/// </summary>
public sealed class MessageView : LexObject
{
    /// <summary>The identifier of the message.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The message's revision, an opaque string the chat service assigns.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>The message text.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Rich-text facets (mentions, links, tags) applied to the text.</summary>
    [JsonPropertyName("facets")]
    public IReadOnlyList<JsonElement>? Facets { get; init; }

    /// <summary>Embedded content attached to the message.</summary>
    [JsonPropertyName("embed")]
    public JsonElement? Embed { get; init; }

    /// <summary>The sender of the message.</summary>
    [JsonPropertyName("sender")]
    public required MessageSender Sender { get; init; }

    /// <summary>When the message was sent.</summary>
    [JsonPropertyName("sentAt")]
    public required AtDatetime SentAt { get; init; }
}

/// <summary>
/// A deleted message placeholder.
/// </summary>
public sealed class DeletedMessageView : LexObject
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
/// The sender of a message.
/// </summary>
public sealed class MessageSender : LexObject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }
}

/// <summary>
/// A log entry in a conversation log.
/// </summary>
public sealed class ConvoLogEntry : LexObject
{
    /// <summary>The Lexicon type discriminator for this object.</summary>
    [JsonPropertyName("$type")]
    public string? Type { get; init; }

    /// <summary>The revision of the change this entry records.</summary>
    [JsonPropertyName("rev")]
    public string? Rev { get; init; }

    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public string? ConvoId { get; init; }

    /// <summary>The message.</summary>
    [JsonPropertyName("message")]
    public JsonElement? Message { get; init; }
}

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
    public IReadOnlyList<JsonElement>? Facets { get; init; }

    /// <summary>Embedded content attached to the message.</summary>
    [JsonPropertyName("embed")]
    public JsonElement? Embed { get; init; }
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
/// Response from chat.bsky.convo.getMessages.
/// </summary>
public sealed class GetMessagesResponse : ICursorPage<JsonElement>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>
    /// The messages: <c>chat.bsky.convo.defs#messageView</c>, <c>#deletedMessageView</c> and
    /// other views, told apart by <c>$type</c>.
    /// </summary>
    [JsonPropertyName("messages")]
    public required IReadOnlyList<JsonElement> Messages { get; init; }

    /// <summary>Profiles of accounts the messages refer to that are not conversation members.</summary>
    [JsonPropertyName("relatedProfiles")]
    public IReadOnlyList<ChatMemberView>? RelatedProfiles { get; init; }

    IReadOnlyList<JsonElement> ICursorPage<JsonElement>.Items => Messages;
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
/// The output of chat.bsky.convo.muteConvo, unmuteConvo and updateRead, which the client unwraps.
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
