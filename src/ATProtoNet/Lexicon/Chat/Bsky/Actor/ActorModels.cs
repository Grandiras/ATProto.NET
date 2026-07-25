using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Chat.Bsky.Actor;

/// <summary>
/// Record type for chat.bsky.actor.declaration — declares chat preferences.
/// </summary>
public sealed class ChatDeclarationRecord
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
}

/// <summary>
/// Request for chat.bsky.actor.deleteAccount.
/// </summary>
public sealed class DeleteChatAccountRequest;

/// <summary>
/// Allowed values for <see cref="ChatDeclarationRecord.AllowIncoming"/>.
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
