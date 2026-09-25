using System.Text.Json.Serialization;
using ATProtoNet.Models;

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
