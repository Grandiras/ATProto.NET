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
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    [JsonPropertyName("members")]
    public required List<ChatMemberView> Members { get; init; }

    [JsonPropertyName("lastMessage")]
    public JsonElement? LastMessage { get; init; }

    [JsonPropertyName("muted")]
    public bool Muted { get; init; }

    [JsonPropertyName("opened")]
    public bool? Opened { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("unreadCount")]
    public int UnreadCount { get; init; }
}

/// <summary>
/// A chat member (actor profile) within a conversation.
/// </summary>
public sealed class ChatMemberView
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    [JsonPropertyName("associated")]
    public JsonElement? Associated { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    [JsonPropertyName("chatDisabled")]
    public bool? ChatDisabled { get; init; }
}

/// <summary>
/// A message view within a conversation.
/// </summary>
public sealed class MessageView
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("facets")]
    public List<JsonElement>? Facets { get; init; }

    [JsonPropertyName("embed")]
    public JsonElement? Embed { get; init; }

    [JsonPropertyName("sender")]
    public required MessageSender Sender { get; init; }

    [JsonPropertyName("sentAt")]
    public required string SentAt { get; init; }
}

/// <summary>
/// A deleted message placeholder.
/// </summary>
public sealed class DeletedMessageView
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("rev")]
    public required string Rev { get; init; }

    [JsonPropertyName("sender")]
    public required MessageSender Sender { get; init; }

    [JsonPropertyName("sentAt")]
    public required string SentAt { get; init; }
}

/// <summary>
/// The sender of a message.
/// </summary>
public sealed class MessageSender
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// A log entry in a conversation log.
/// </summary>
public sealed class ConvoLogEntry
{
    [JsonPropertyName("$type")]
    public string? Type { get; init; }

    [JsonPropertyName("rev")]
    public string? Rev { get; init; }

    [JsonPropertyName("convoId")]
    public string? ConvoId { get; init; }

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
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    [JsonPropertyName("message")]
    public required MessageInput Message { get; init; }
}

/// <summary>
/// Input for a message to be sent.
/// </summary>
public sealed class MessageInput
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("facets")]
    public List<JsonElement>? Facets { get; init; }

    [JsonPropertyName("embed")]
    public JsonElement? Embed { get; init; }
}

/// <summary>
/// A message within a batch send request.
/// </summary>
public sealed class BatchMessageItem
{
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    [JsonPropertyName("message")]
    public required MessageInput Message { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.sendMessageBatch.
/// </summary>
public sealed class SendMessageBatchRequest
{
    [JsonPropertyName("items")]
    public required List<BatchMessageItem> Items { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.deleteMessageForSelf.
/// </summary>
public sealed class DeleteMessageForSelfRequest
{
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.leaveConvo.
/// </summary>
public sealed class LeaveConvoRequest
{
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.muteConvo.
/// </summary>
public sealed class MuteConvoRequest
{
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.unmuteConvo.
/// </summary>
public sealed class UnmuteConvoRequest
{
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.updateRead.
/// </summary>
public sealed class UpdateReadRequest
{
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.acceptConvo.
/// </summary>
public sealed class AcceptConvoRequest
{
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.addReaction.
/// </summary>
public sealed class AddReactionRequest
{
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

/// <summary>
/// Request body for chat.bsky.convo.removeReaction.
/// </summary>
public sealed class RemoveReactionRequest
{
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }

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
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("convos")]
    public required List<ConvoView> Convos { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.getConvo.
/// </summary>
public sealed class GetConvoResponse
{
    [JsonPropertyName("convo")]
    public required ConvoView Convo { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.getConvoForMembers.
/// </summary>
public sealed class GetConvoForMembersResponse
{
    [JsonPropertyName("convo")]
    public required ConvoView Convo { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.getConvoAvailability.
/// </summary>
public sealed class GetConvoAvailabilityResponse
{
    [JsonPropertyName("canConvo")]
    public bool CanConvo { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.getMessages.
/// </summary>
public sealed class GetMessagesResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("messages")]
    public required List<JsonElement> Messages { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.sendMessageBatch.
/// </summary>
public sealed class SendMessageBatchResponse
{
    [JsonPropertyName("items")]
    public required List<MessageView> Items { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.leaveConvo.
/// </summary>
public sealed class LeaveConvoResponse
{
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    [JsonPropertyName("rev")]
    public required string Rev { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.acceptConvo.
/// </summary>
public sealed class AcceptConvoResponse
{
    [JsonPropertyName("convo")]
    public ConvoView? Convo { get; init; }

    [JsonPropertyName("rev")]
    public string? Rev { get; init; }
}

/// <summary>
/// Response from chat.bsky.convo.getLog.
/// </summary>
public sealed class GetLogResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("logs")]
    public required List<ConvoLogEntry> Logs { get; init; }
}
