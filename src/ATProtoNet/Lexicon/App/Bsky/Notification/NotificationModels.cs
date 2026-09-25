using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Graph;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.App.Bsky.Notification;

// ──────────────────────────────────────────────────────────────
//  listNotifications
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A notification entry.
/// </summary>
public sealed class NotificationView : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>The account that authored the post.</summary>
    [JsonPropertyName("author")]
    public required ProfileView Author { get; init; }

    /// <summary>Why the notification was sent (see <see cref="NotificationReasons"/>).</summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary>Subject URI, if applicable (e.g., the post that was liked).</summary>
    [JsonPropertyName("reasonSubject")]
    public AtUri? ReasonSubject { get; init; }

    /// <summary>The record that triggered the notification.</summary>
    [JsonPropertyName("record")]
    public required JsonElement Record { get; init; }

    /// <summary>
    /// The starter pack the notification is about, for a <c>starterpack-joined</c> notification.
    /// </summary>
    [JsonPropertyName("starterPack")]
    public StarterPackViewBasic? StarterPack { get; init; }

    /// <summary>Whether the notification has been read.</summary>
    [JsonPropertyName("isRead")]
    public bool IsRead { get; init; }

    /// <summary>Timestamp at which the app view indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public required AtDatetime IndexedAt { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<Label>? Labels { get; init; }
}

/// <summary>
/// Response from listNotifications.
/// </summary>
public sealed class ListNotificationsResponse : ICursorPage<NotificationView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The notifications.</summary>
    [JsonPropertyName("notifications")]
    public required IReadOnlyList<NotificationView> Notifications { get; init; }

    /// <summary>No longer populated.</summary>
    [JsonPropertyName("priority")]
    [Obsolete("Deprecated upstream: the appview no longer populates this field.")]
    public bool? Priority { get; init; }

    /// <summary>The timestamp notifications were last marked seen at.</summary>
    [JsonPropertyName("seenAt")]
    public AtDatetime? SeenAt { get; init; }

    IReadOnlyList<NotificationView> ICursorPage<NotificationView>.Items => Notifications;
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
internal sealed class UpdateSeenRequest
{
    /// <summary>The timestamp to mark notifications seen up to.</summary>
    [JsonPropertyName("seenAt")]
    public required AtDatetime SeenAt { get; init; }
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
    public required Did ServiceDid { get; init; }

    /// <summary>The push notification token.</summary>
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    /// <summary>The push platform (<c>ios</c>, <c>android</c>, or <c>web</c>).</summary>
    [JsonPropertyName("platform")]
    public required string Platform { get; init; }

    /// <summary>The application identifier the push token belongs to.</summary>
    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    /// <summary>Whether the client knows the account to be age-restricted.</summary>
    [JsonPropertyName("ageRestricted")]
    public bool? AgeRestricted { get; init; }
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

    /// <summary>A trusted verifier verified the account.</summary>
    public const string Verified = "verified";

    /// <summary>A trusted verifier removed the account's verification.</summary>
    public const string Unverified = "unverified";

    /// <summary>Someone liked a repost the account made.</summary>
    public const string LikeViaRepost = "like-via-repost";

    /// <summary>Someone reposted a repost the account made.</summary>
    public const string RepostViaRepost = "repost-via-repost";

    /// <summary>An account the viewer subscribed to posted.</summary>
    public const string SubscribedPost = "subscribed-post";

    /// <summary>One of the account's contacts joined.</summary>
    public const string ContactMatch = "contact-match";
}

/// <summary>
/// Which of an account's activity the viewer is subscribed to
/// (<c>app.bsky.notification.defs#activitySubscription</c>).
/// </summary>
public sealed class ActivitySubscription : LexObject
{
    /// <summary>Whether the viewer is notified of the account's posts.</summary>
    [JsonPropertyName("post")]
    public required bool Post { get; init; }

    /// <summary>Whether the viewer is notified of the account's replies.</summary>
    [JsonPropertyName("reply")]
    public required bool Reply { get; init; }
}
