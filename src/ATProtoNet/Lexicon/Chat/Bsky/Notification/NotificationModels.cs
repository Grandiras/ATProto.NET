using System.Text.Json.Serialization;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Chat.Bsky.Notification;

/// <summary>
/// Known values of <see cref="ChatPreference.Include"/>.
/// </summary>
public static class ChatPreferenceInclude
{
    /// <summary>Notify about messages from everyone.</summary>
    public const string All = "all";

    /// <summary>Notify only about messages from accounts the viewer follows.</summary>
    public const string Follows = "follows";
}

/// <summary>
/// The viewer's chat notification preferences (<c>chat.bsky.notification.defs#preferences</c>).
/// </summary>
public sealed class ChatNotificationPreferences : LexObject
{
    /// <summary>Notifications for messages in accepted conversations.</summary>
    [JsonPropertyName("chat")]
    public required ChatPreference Chat { get; init; }

    /// <summary>Notifications for conversation requests.</summary>
    [JsonPropertyName("chatRequest")]
    public required ChatPreference ChatRequest { get; init; }
}

/// <summary>
/// Which chat notifications to get, and whether to push them
/// (<c>chat.bsky.notification.defs#chatPreference</c>).
/// </summary>
public sealed class ChatPreference : LexObject
{
    /// <summary>Whose messages to notify about (see <see cref="ChatPreferenceInclude"/>).</summary>
    [JsonPropertyName("include")]
    public required string Include { get; init; }

    /// <summary>Whether to send push notifications.</summary>
    [JsonPropertyName("push")]
    public required bool Push { get; init; }
}

/// <summary>Request body for chat.bsky.notification.putPreferences.</summary>
internal sealed class PutPreferencesRequest
{
    /// <summary>The preference for accepted conversations, or <see langword="null"/> to keep it.</summary>
    [JsonPropertyName("chat")]
    public ChatPreference? Chat { get; init; }

    /// <summary>The preference for conversation requests, or <see langword="null"/> to keep it.</summary>
    [JsonPropertyName("chatRequest")]
    public ChatPreference? ChatRequest { get; init; }
}

/// <summary>The output of chat.bsky.notification.getPreferences, which the client unwraps.</summary>
internal sealed class GetPreferencesResponse
{
    /// <summary>The preferences.</summary>
    [JsonPropertyName("preferences")]
    public required ChatNotificationPreferences Preferences { get; init; }
}

/// <summary>The output of chat.bsky.notification.putPreferences, which the client unwraps.</summary>
internal sealed class PutPreferencesResponse
{
    /// <summary>The preferences after the change.</summary>
    [JsonPropertyName("preferences")]
    public required ChatNotificationPreferences Preferences { get; init; }
}
