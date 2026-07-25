using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Chat.Bsky.Convo;

// ──────────────────────────────────────────────────────────
//  View / Response models
// ──────────────────────────────────────────────────────────

/// <summary>
/// A conversation view returned by chat.bsky.convo endpoints.
/// </summary>
public sealed class ConvoView
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The repository revision (a TID) this data was read at.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>The members.</summary>
    [JsonPropertyName("members")]
    public required List<ChatMemberView> Members { get; init; }

    /// <summary>The most recent message in the conversation.</summary>
    [JsonPropertyName("lastMessage")]
    public JsonElement? LastMessage { get; init; }

    /// <summary>Whether the viewer has muted this conversation.</summary>
    [JsonPropertyName("muted")]
    public bool Muted { get; init; }

    /// <summary>Whether the conversation has been opened by the viewer.</summary>
    [JsonPropertyName("opened")]
    public bool? Opened { get; init; }

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
public sealed class ChatMemberView
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    /// <summary>
    /// Counts and flags for content associated with the actor (lists, feed generators, chat
    /// availability).
    /// </summary>
    [JsonPropertyName("associated")]
    public JsonElement? Associated { get; init; }

    /// <summary>The labels applied to the member's account.</summary>
    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    /// <summary>Whether chat is disabled for this account.</summary>
    [JsonPropertyName("chatDisabled")]
    public bool? ChatDisabled { get; init; }
}

/// <summary>
/// A message view within a conversation.
/// </summary>
public sealed class MessageView
{
    /// <summary>The identifier of the message.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The repository revision (a TID) this data was read at.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>The message text.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Rich-text facets (mentions, links, tags) applied to the text.</summary>
    [JsonPropertyName("facets")]
    public List<JsonElement>? Facets { get; init; }

    /// <summary>Embedded content attached to the message.</summary>
    [JsonPropertyName("embed")]
    public JsonElement? Embed { get; init; }

    /// <summary>The sender of the message.</summary>
    [JsonPropertyName("sender")]
    public required MessageSender Sender { get; init; }

    /// <summary>Timestamp at which the message was sent (ISO 8601).</summary>
    [JsonPropertyName("sentAt")]
    public required string SentAt { get; init; }
}

/// <summary>
/// A deleted message placeholder.
/// </summary>
public sealed class DeletedMessageView
{
    /// <summary>The identifier of the message.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The repository revision (a TID) this data was read at.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    /// <summary>The sender of the message.</summary>
    [JsonPropertyName("sender")]
    public required MessageSender Sender { get; init; }

    /// <summary>Timestamp at which the message was sent (ISO 8601).</summary>
    [JsonPropertyName("sentAt")]
    public required string SentAt { get; init; }
}

/// <summary>
/// The sender of a message.
/// </summary>
public sealed class MessageSender
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// A log entry in a conversation log.
/// </summary>
public sealed class ConvoLogEntry
{
    /// <summary>The Lexicon type discriminator for this object.</summary>
    [JsonPropertyName("$type")]
    public string? Type { get; init; }

    /// <summary>The repository revision (a TID) this data was read at.</summary>
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
public sealed class SendMessageRequest
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
public sealed class MessageInput
{
    /// <summary>The message text.</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>Rich-text facets (mentions, links, tags) applied to the text.</summary>
    [JsonPropertyName("facets")]
    public List<JsonElement>? Facets { get; init; }

    /// <summary>Embedded content attached to the message.</summary>
    [JsonPropertyName("embed")]
    public JsonElement? Embed { get; init; }
}

/// <summary>
/// A message within a batch send request.
/// </summary>
public sealed class BatchMessageItem
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
public sealed class SendMessageBatchRequest
{
    /// <summary>The messages to send.</summary>
    [JsonPropertyName("items")]
    public required List<BatchMessageItem> Items { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.deleteMessageForSelf.
/// </summary>
public sealed class DeleteMessageForSelfRequest
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
public sealed class LeaveConvoRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.muteConvo.
/// </summary>
public sealed class MuteConvoRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.unmuteConvo.
/// </summary>
public sealed class UnmuteConvoRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.updateRead.
/// </summary>
public sealed class UpdateReadRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The identifier of the message.</summary>
    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.acceptConvo.
/// </summary>
public sealed class AcceptConvoRequest
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.addReaction.
/// </summary>
public sealed class AddReactionRequest
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
public sealed class RemoveReactionRequest
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
public sealed class ListConvosResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The conversations.</summary>
    [JsonPropertyName("convos")]
    public required List<ConvoView> Convos { get; init; }
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
    /// <summary>Whether the viewer may start a conversation with this account.</summary>
    [JsonPropertyName("canConvo")]
    public bool CanConvo { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.getMessages.
/// </summary>
public sealed class GetMessagesResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The messages.</summary>
    [JsonPropertyName("messages")]
    public required List<JsonElement> Messages { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.sendMessageBatch.
/// </summary>
public sealed class SendMessageBatchResponse
{
    /// <summary>The sent messages.</summary>
    [JsonPropertyName("items")]
    public required List<MessageView> Items { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.leaveConvo.
/// </summary>
public sealed class LeaveConvoResponse
{
    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The repository revision (a TID) this data was read at.</summary>
    [JsonPropertyName("rev")]
    public required string Rev { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.acceptConvo.
/// </summary>
public sealed class AcceptConvoResponse
{
    /// <summary>The conversation.</summary>
    [JsonPropertyName("convo")]
    public ConvoView? Convo { get; init; }

    /// <summary>The repository revision (a TID) this data was read at.</summary>
    [JsonPropertyName("rev")]
    public string? Rev { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.getLog.
/// </summary>
public sealed class GetLogResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The conversation log entries.</summary>
    [JsonPropertyName("logs")]
    public required List<ConvoLogEntry> Logs { get; init; }
}
