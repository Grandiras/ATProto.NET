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

    /// <summary>The push platform (see <see cref="PushPlatform"/>).</summary>
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
/// Request body for unregisterPush.
/// </summary>
internal sealed class UnregisterPushRequest
{
    /// <summary>The DID of the push service.</summary>
    [JsonPropertyName("serviceDid")]
    public required Did ServiceDid { get; init; }

    /// <summary>The push notification token.</summary>
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    /// <summary>The push platform.</summary>
    [JsonPropertyName("platform")]
    public required string Platform { get; init; }

    /// <summary>The application identifier the push token belongs to.</summary>
    [JsonPropertyName("appId")]
    public required string AppId { get; init; }
}

/// <summary>
/// Known push platforms, for <see cref="RegisterPushRequest.Platform"/> and
/// <see cref="NotificationClient.UnregisterPushAsync"/>.
/// </summary>
public static class PushPlatform
{
    /// <summary>Apple Push Notification service.</summary>
    public const string Ios = "ios";

    /// <summary>Firebase Cloud Messaging.</summary>
    public const string Android = "android";

    /// <summary>Web Push.</summary>
    public const string Web = "web";
}

// ──────────────────────────────────────────────────────────────
//  Preferences
// ──────────────────────────────────────────────────────────────

/// <summary>
/// The account's notification preferences, per notification kind
/// (<c>app.bsky.notification.defs#preferences</c>).
/// </summary>
public sealed class NotificationPreferences : LexObject
{
    /// <summary>
    /// Chat notifications. Deprecated upstream in favor of the chat service's own preferences,
    /// and read as a default value only, so it is not required here.
    /// </summary>
    [JsonPropertyName("chat")]
    [Obsolete("Deprecated upstream: chat notification preferences belong to the chat service.")]
    public ChatPreference? Chat { get; init; }

    /// <summary>New followers.</summary>
    [JsonPropertyName("follow")]
    public required FilterablePreference Follow { get; init; }

    /// <summary>Likes of the account's posts.</summary>
    [JsonPropertyName("like")]
    public required FilterablePreference Like { get; init; }

    /// <summary>Likes of the account's reposts.</summary>
    [JsonPropertyName("likeViaRepost")]
    public required FilterablePreference LikeViaRepost { get; init; }

    /// <summary>Mentions.</summary>
    [JsonPropertyName("mention")]
    public required FilterablePreference Mention { get; init; }

    /// <summary>Quotes of the account's posts.</summary>
    [JsonPropertyName("quote")]
    public required FilterablePreference Quote { get; init; }

    /// <summary>Replies.</summary>
    [JsonPropertyName("reply")]
    public required FilterablePreference Reply { get; init; }

    /// <summary>Reposts of the account's posts.</summary>
    [JsonPropertyName("repost")]
    public required FilterablePreference Repost { get; init; }

    /// <summary>Reposts of the account's reposts.</summary>
    [JsonPropertyName("repostViaRepost")]
    public required FilterablePreference RepostViaRepost { get; init; }

    /// <summary>Someone joined through one of the account's starter packs.</summary>
    [JsonPropertyName("starterpackJoined")]
    public required NotificationPreference StarterpackJoined { get; init; }

    /// <summary>Posts by accounts the viewer subscribed to.</summary>
    [JsonPropertyName("subscribedPost")]
    public required NotificationPreference SubscribedPost { get; init; }

    /// <summary>A trusted verifier removed the account's verification.</summary>
    [JsonPropertyName("unverified")]
    public required NotificationPreference Unverified { get; init; }

    /// <summary>A trusted verifier verified the account.</summary>
    [JsonPropertyName("verified")]
    public required NotificationPreference Verified { get; init; }
}

/// <summary>
/// Whether a kind of notification is listed and pushed
/// (<c>app.bsky.notification.defs#preference</c>).
/// </summary>
public sealed class NotificationPreference : LexObject
{
    /// <summary>Whether the notifications appear in the notification list.</summary>
    [JsonPropertyName("list")]
    public required bool List { get; init; }

    /// <summary>Whether the notifications are pushed.</summary>
    [JsonPropertyName("push")]
    public required bool Push { get; init; }
}

/// <summary>
/// Whether a kind of notification is listed and pushed, and from whom
/// (<c>app.bsky.notification.defs#filterablePreference</c>).
/// </summary>
public sealed class FilterablePreference : LexObject
{
    /// <summary>Whose actions notify: see <see cref="NotificationInclude"/>.</summary>
    [JsonPropertyName("include")]
    public required string Include { get; init; }

    /// <summary>Whether the notifications appear in the notification list.</summary>
    [JsonPropertyName("list")]
    public required bool List { get; init; }

    /// <summary>Whether the notifications are pushed.</summary>
    [JsonPropertyName("push")]
    public required bool Push { get; init; }
}

/// <summary>
/// Known values of <see cref="FilterablePreference.Include"/>.
/// </summary>
public static class NotificationInclude
{
    /// <summary>Everyone.</summary>
    public const string All = "all";

    /// <summary>Only accounts the account follows.</summary>
    public const string Follows = "follows";
}

/// <summary>
/// The deprecated chat notification preference (<c>app.bsky.notification.defs#chatPreference</c>).
/// </summary>
public sealed class ChatPreference : LexObject
{
    /// <summary>Whose messages notify: <c>all</c> or <c>accepted</c>.</summary>
    [JsonPropertyName("include")]
    public required string Include { get; init; }

    /// <summary>Whether the notifications are pushed.</summary>
    [JsonPropertyName("push")]
    public required bool Push { get; init; }
}

/// <summary>
/// Response from getPreferences.
/// </summary>
internal sealed class GetPreferencesResponse
{
    /// <summary>The preferences.</summary>
    [JsonPropertyName("preferences")]
    public required NotificationPreferences Preferences { get; init; }
}

/// <summary>
/// Request body for putPreferencesV2: the preferences to change. A <see langword="null"/>
/// property keeps its current value.
/// </summary>
public sealed class PutPreferencesV2Request
{
    /// <summary>
    /// Chat notifications. Deprecated upstream: the service does not keep the value.
    /// </summary>
    [JsonPropertyName("chat")]
    [Obsolete("Deprecated upstream: set chat notification preferences on the chat service.")]
    public ChatPreference? Chat { get; init; }

    /// <summary>New followers.</summary>
    [JsonPropertyName("follow")]
    public FilterablePreference? Follow { get; init; }

    /// <summary>Likes of the account's posts.</summary>
    [JsonPropertyName("like")]
    public FilterablePreference? Like { get; init; }

    /// <summary>Likes of the account's reposts.</summary>
    [JsonPropertyName("likeViaRepost")]
    public FilterablePreference? LikeViaRepost { get; init; }

    /// <summary>Mentions.</summary>
    [JsonPropertyName("mention")]
    public FilterablePreference? Mention { get; init; }

    /// <summary>Quotes of the account's posts.</summary>
    [JsonPropertyName("quote")]
    public FilterablePreference? Quote { get; init; }

    /// <summary>Replies.</summary>
    [JsonPropertyName("reply")]
    public FilterablePreference? Reply { get; init; }

    /// <summary>Reposts of the account's posts.</summary>
    [JsonPropertyName("repost")]
    public FilterablePreference? Repost { get; init; }

    /// <summary>Reposts of the account's reposts.</summary>
    [JsonPropertyName("repostViaRepost")]
    public FilterablePreference? RepostViaRepost { get; init; }

    /// <summary>Someone joined through one of the account's starter packs.</summary>
    [JsonPropertyName("starterpackJoined")]
    public NotificationPreference? StarterpackJoined { get; init; }

    /// <summary>Posts by accounts the viewer subscribed to.</summary>
    [JsonPropertyName("subscribedPost")]
    public NotificationPreference? SubscribedPost { get; init; }

    /// <summary>A trusted verifier removed the account's verification.</summary>
    [JsonPropertyName("unverified")]
    public NotificationPreference? Unverified { get; init; }

    /// <summary>A trusted verifier verified the account.</summary>
    [JsonPropertyName("verified")]
    public NotificationPreference? Verified { get; init; }
}

/// <summary>
/// Response from putPreferencesV2.
/// </summary>
internal sealed class PutPreferencesV2Response
{
    /// <summary>The preferences after the change.</summary>
    [JsonPropertyName("preferences")]
    public required NotificationPreferences Preferences { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Activity subscriptions
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from listActivitySubscriptions.
/// </summary>
public sealed class ListActivitySubscriptionsResponse : ICursorPage<ProfileView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The accounts the viewer is subscribed to.</summary>
    [JsonPropertyName("subscriptions")]
    public required IReadOnlyList<ProfileView> Subscriptions { get; init; }

    IReadOnlyList<ProfileView> ICursorPage<ProfileView>.Items => Subscriptions;
}

/// <summary>
/// Request body for putActivitySubscription.
/// </summary>
internal sealed class PutActivitySubscriptionRequest
{
    /// <summary>The account to subscribe to.</summary>
    [JsonPropertyName("subject")]
    public required Did Subject { get; init; }

    /// <summary>Which of its activity to be notified of.</summary>
    [JsonPropertyName("activitySubscription")]
    public required ActivitySubscription ActivitySubscription { get; init; }
}

/// <summary>
/// Response from putActivitySubscription.
/// </summary>
public sealed class PutActivitySubscriptionResponse
{
    /// <summary>The account subscribed to.</summary>
    [JsonPropertyName("subject")]
    public required Did Subject { get; init; }

    /// <summary>The subscription as stored; <see langword="null"/> when it was removed.</summary>
    [JsonPropertyName("activitySubscription")]
    public ActivitySubscription? ActivitySubscription { get; init; }
}

/// <summary>
/// The account's choice of who may subscribe to its activity. Collection:
/// <c>app.bsky.notification.declaration</c>, record key <c>self</c>.
/// </summary>
public sealed class NotificationDeclarationRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection records of this type are stored in (<c>app.bsky.notification.declaration</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.notification.declaration");

    /// <summary>The Lexicon type discriminator (<c>app.bsky.notification.declaration</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

    /// <summary>
    /// Who may subscribe to the account's activity (see <see cref="AllowedSubscribers"/>). An
    /// account without the record allows its followers.
    /// </summary>
    [JsonPropertyName("allowSubscriptions")]
    public required string AllowSubscriptions { get; init; }
}

/// <summary>
/// Known values of <see cref="NotificationDeclarationRecord.AllowSubscriptions"/>.
/// </summary>
public static class AllowedSubscribers
{
    /// <summary>Followers may subscribe (the default without a record).</summary>
    public const string Followers = "followers";

    /// <summary>Only mutual follows may subscribe.</summary>
    public const string Mutuals = "mutuals";

    /// <summary>Nobody may subscribe.</summary>
    public const string None = "none";
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
