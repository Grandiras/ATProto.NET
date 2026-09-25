using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Chat.Bsky.Actor;

/// <summary>
/// Record type for chat.bsky.actor.declaration — declares chat preferences.
/// </summary>
public sealed class ChatDeclarationRecord : LexObject
{
    /// <summary>The Lexicon type discriminator (<c>chat.bsky.actor.declaration</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "chat.bsky.actor.declaration";

    /// <summary>
    /// Who may start a conversation with this account (<c>all</c>, <c>none</c>, or
    /// <c>following</c>).
    /// </summary>
    [JsonPropertyName("allowIncoming")]
    public required string AllowIncoming { get; init; }

    /// <summary>
    /// Who may add this account to a group conversation (<c>all</c>, <c>none</c>, or
    /// <c>following</c>).
    /// </summary>
    [JsonPropertyName("allowGroupInvites")]
    public string? AllowGroupInvites { get; init; }
}

/// <summary>
/// Known values of <see cref="ChatDeclarationRecord.AllowIncoming"/> and
/// <see cref="ChatDeclarationRecord.AllowGroupInvites"/>.
/// </summary>
public static class ChatAllowIncoming
{
    /// <summary>The <c>all</c> incoming-chat policy.</summary>
    public const string All = "all";

    /// <summary>The <c>none</c> incoming-chat policy.</summary>
    public const string None = "none";

    /// <summary>The <c>following</c> incoming-chat policy.</summary>
    public const string Following = "following";
}

/// <summary>
/// Known values of a group member's role (<c>chat.bsky.actor.defs#memberRole</c>).
/// </summary>
public static class ChatMemberRole
{
    /// <summary>The group's owner, who manages its members, name and join link.</summary>
    public const string Owner = "owner";

    /// <summary>A regular member.</summary>
    public const string Standard = "standard";
}

/// <summary>
/// A member's place in a conversation (the open <c>chat.bsky.actor.defs#profileViewBasic.kind</c>
/// union). A kind this SDK does not model reads as <see cref="UnknownChatMemberKind"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownChatMemberKind))]
[JsonDerivedType(typeof(DirectConvoMember), "chat.bsky.actor.defs#directConvoMember")]
[JsonDerivedType(typeof(GroupConvoMember), "chat.bsky.actor.defs#groupConvoMember")]
[JsonDerivedType(typeof(PastGroupConvoMember), "chat.bsky.actor.defs#pastGroupConvoMember")]
public abstract class ChatMemberKind : LexObject;

/// <summary>
/// A member kind whose <c>$type</c> this SDK version does not model. It keeps the raw object and
/// writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownChatMemberKind : ChatMemberKind, IUnknownUnionVariant
{
    /// <summary>Creates an unknown member kind from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownChatMemberKind(string type, JsonElement raw)
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

/// <summary>A member of a direct conversation (<c>chat.bsky.actor.defs#directConvoMember</c>).</summary>
public sealed class DirectConvoMember : ChatMemberKind;

/// <summary>A current member of a group (<c>chat.bsky.actor.defs#groupConvoMember</c>).</summary>
public sealed class GroupConvoMember : ChatMemberKind
{
    /// <summary>Who added the member; absent when the member joined through the join link.</summary>
    [JsonPropertyName("addedBy")]
    public ChatMemberView? AddedBy { get; init; }

    /// <summary>The member's role in the group (see <see cref="ChatMemberRole"/>).</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }
}

/// <summary>A former member of a group (<c>chat.bsky.actor.defs#pastGroupConvoMember</c>).</summary>
public sealed class PastGroupConvoMember : ChatMemberKind;

/// <summary>
/// Response from chat.bsky.actor.getStatus.
/// </summary>
public sealed class GetStatusResponse
{
    /// <summary>Whether the viewer's account is disabled and cannot actively take part in chats.</summary>
    [JsonPropertyName("chatDisabled")]
    public required bool ChatDisabled { get; init; }

    /// <summary>Whether the viewer may create groups; new accounts may not.</summary>
    [JsonPropertyName("canCreateGroups")]
    public required bool CanCreateGroups { get; init; }

    /// <summary>The most members a group may have.</summary>
    [JsonPropertyName("groupMemberLimit")]
    public required int GroupMemberLimit { get; init; }
}
