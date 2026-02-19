using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.App.Bsky.Notification;

// ──────────────────────────────────────────────────────────────
//  listNotifications
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A notification entry.
/// </summary>
public sealed class NotificationView
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("author")]
    public required ProfileView Author { get; init; }

    /// <summary>
    /// Reason for the notification: "like", "repost", "follow", "mention",
    /// "reply", "quote", "starterpack-joined".
    /// </summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary>Subject URI, if applicable (e.g., the post that was liked).</summary>
    [JsonPropertyName("reasonSubject")]
    public string? ReasonSubject { get; init; }

    [JsonPropertyName("record")]
    public required JsonElement Record { get; init; }

    [JsonPropertyName("isRead")]
    public bool IsRead { get; init; }

    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }
}

/// <summary>
/// Response from listNotifications.
/// </summary>
public sealed class ListNotificationsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("notifications")]
    public required List<NotificationView> Notifications { get; init; }

    [JsonPropertyName("priority")]
    public bool? Priority { get; init; }

    [JsonPropertyName("seenAt")]
    public string? SeenAt { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  getUnreadCount
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getUnreadCount.
/// </summary>
public sealed class GetUnreadCountResponse
{
    [JsonPropertyName("count")]
    public int Count { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  updateSeen
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for updateSeen.
/// </summary>
public sealed class UpdateSeenRequest
{
    [JsonPropertyName("seenAt")]
    public required string SeenAt { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  registerPush
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for registerPush.
/// </summary>
public sealed class RegisterPushRequest
{
    [JsonPropertyName("serviceDid")]
    public required string ServiceDid { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("platform")]
    public required string Platform { get; init; }

    [JsonPropertyName("appId")]
    public required string AppId { get; init; }
}

/// <summary>
/// Well-known notification reasons.
/// </summary>
public static class NotificationReasons
{
    public const string Like = "like";
    public const string Repost = "repost";
    public const string Follow = "follow";
    public const string Mention = "mention";
    public const string Reply = "reply";
    public const string Quote = "quote";
    public const string StarterpackJoined = "starterpack-joined";
}
