using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Chat.Bsky.Actor;

/// <summary>
/// Record type for chat.bsky.actor.declaration — declares chat preferences.
/// </summary>
public sealed class ChatDeclarationRecord
{
    [JsonPropertyName("$type")]
    public string Type => "chat.bsky.actor.declaration";

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
    public const string All = "all";
    public const string None = "none";
    public const string Following = "following";
}
