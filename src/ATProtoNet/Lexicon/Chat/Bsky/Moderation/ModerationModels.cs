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
