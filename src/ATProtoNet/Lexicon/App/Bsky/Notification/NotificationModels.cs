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
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>The account that authored the post.</summary>
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

    /// <summary>The record that triggered the notification.</summary>
    [JsonPropertyName("record")]
    public required JsonElement Record { get; init; }

    /// <summary>Whether the notification has been read.</summary>
    [JsonPropertyName("isRead")]
    public bool IsRead { get; init; }

    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }
}

/// <summary>
/// Response from listNotifications.
/// </summary>
public sealed class ListNotificationsResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The notifications.</summary>
    [JsonPropertyName("notifications")]
    public required List<NotificationView> Notifications { get; init; }

    /// <summary>Whether only priority notifications were returned.</summary>
    [JsonPropertyName("priority")]
    public bool? Priority { get; init; }

    /// <summary>The timestamp notifications were last marked seen at (ISO 8601).</summary>
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
    /// <summary>The number of unread notifications.</summary>
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
    /// <summary>The timestamp to mark notifications seen up to (ISO 8601).</summary>
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
    /// <summary>The DID of the service.</summary>
    [JsonPropertyName("serviceDid")]
    public required string ServiceDid { get; init; }

    /// <summary>The push notification token.</summary>
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    /// <summary>The push platform (<c>ios</c>, <c>android</c>, or <c>web</c>).</summary>
    [JsonPropertyName("platform")]
    public required string Platform { get; init; }

    /// <summary>The application identifier the push token belongs to.</summary>
    [JsonPropertyName("appId")]
    public required string AppId { get; init; }
}

/// <summary>
/// Well-known notification reasons.
/// </summary>
public static class NotificationReasons
{
    /// <summary>The <c>like</c> notification reason.</summary>
    public const string Like = "like";

    /// <summary>The <c>repost</c> notification reason.</summary>
    public const string Repost = "repost";

    /// <summary>The <c>follow</c> notification reason.</summary>
    public const string Follow = "follow";

    /// <summary>The <c>mention</c> notification reason.</summary>
    public const string Mention = "mention";

    /// <summary>The <c>reply</c> notification reason.</summary>
    public const string Reply = "reply";

    /// <summary>The <c>quote</c> notification reason.</summary>
    public const string Quote = "quote";

    /// <summary>The <c>starterpack-joined</c> notification reason.</summary>
    public const string StarterpackJoined = "starterpack-joined";
}
