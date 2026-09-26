using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.App.Bsky.Graph;
using ATProtoNet.Lexicon.App.Bsky.Notification;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.App.Bsky.Actor;

// ──────────────────────────────────────────────────────────────
//  Profile types
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Detailed profile view (returned by getProfile).
/// </summary>
public sealed class ProfileViewDetailed : LexObject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Free-form pronouns text.</summary>
    [JsonPropertyName("pronouns")]
    public string? Pronouns { get; init; }

    /// <summary>A website URI shown on the profile.</summary>
    [JsonPropertyName("website")]
    public string? Website { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    /// <summary>The banner image.</summary>
    [JsonPropertyName("banner")]
    public string? Banner { get; init; }

    /// <summary>The number of accounts this actor follows.</summary>
    [JsonPropertyName("followsCount")]
    public int? FollowsCount { get; init; }

    /// <summary>The number of accounts following this actor.</summary>
    [JsonPropertyName("followersCount")]
    public int? FollowersCount { get; init; }

    /// <summary>The number of posts authored by this actor.</summary>
    [JsonPropertyName("postsCount")]
    public int? PostsCount { get; init; }

    /// <summary>
    /// Counts and settings for what the account has published or allows: lists, feed generators,
    /// starter packs, a labeler, chat and activity subscriptions.
    /// </summary>
    [JsonPropertyName("associated")]
    public ProfileAssociated? Associated { get; init; }

    /// <summary>The starter pack the account joined through, if any.</summary>
    [JsonPropertyName("joinedViaStarterPack")]
    public StarterPackViewBasic? JoinedViaStarterPack { get; init; }

    /// <summary>Timestamp at which the app view indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public AtDatetime? IndexedAt { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public ViewerState? Viewer { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>A reference to the post pinned to the profile.</summary>
    [JsonPropertyName("pinnedPost")]
    public StrongRef? PinnedPost { get; init; }

    /// <summary>The account's verification state, from verifications by trusted verifiers.</summary>
    [JsonPropertyName("verification")]
    public VerificationState? Verification { get; init; }

    /// <summary>The account's current status, such as being live.</summary>
    [JsonPropertyName("status")]
    public StatusView? Status { get; init; }

    /// <summary>Debug information the appview attaches for internal development; its shape is not specified.</summary>
    [JsonPropertyName("debug")]
    public JsonElement? Debug { get; init; }
}

/// <summary>
/// Basic profile view (used in actor lists, follows, etc.).
/// </summary>
public sealed class ProfileView : LexObject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Free-form pronouns text.</summary>
    [JsonPropertyName("pronouns")]
    public string? Pronouns { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    /// <summary>
    /// Counts and settings for what the account has published or allows: lists, feed generators,
    /// starter packs, a labeler, chat and activity subscriptions.
    /// </summary>
    [JsonPropertyName("associated")]
    public ProfileAssociated? Associated { get; init; }

    /// <summary>Timestamp at which the app view indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public AtDatetime? IndexedAt { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public ViewerState? Viewer { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>The account's verification state, from verifications by trusted verifiers.</summary>
    [JsonPropertyName("verification")]
    public VerificationState? Verification { get; init; }

    /// <summary>The account's current status, such as being live.</summary>
    [JsonPropertyName("status")]
    public StatusView? Status { get; init; }

    /// <summary>Debug information the appview attaches for internal development; its shape is not specified.</summary>
    [JsonPropertyName("debug")]
    public JsonElement? Debug { get; init; }
}

/// <summary>
/// Minimal profile view (used inline in posts, etc.).
/// </summary>
public sealed class ProfileViewBasic : LexObject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>Free-form pronouns text.</summary>
    [JsonPropertyName("pronouns")]
    public string? Pronouns { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    /// <summary>
    /// Counts and settings for what the account has published or allows: lists, feed generators,
    /// starter packs, a labeler, chat and activity subscriptions.
    /// </summary>
    [JsonPropertyName("associated")]
    public ProfileAssociated? Associated { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public ViewerState? Viewer { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; init; }

    /// <summary>The account's verification state, from verifications by trusted verifiers.</summary>
    [JsonPropertyName("verification")]
    public VerificationState? Verification { get; init; }

    /// <summary>The account's current status, such as being live.</summary>
    [JsonPropertyName("status")]
    public StatusView? Status { get; init; }

    /// <summary>Debug information the appview attaches for internal development; its shape is not specified.</summary>
    [JsonPropertyName("debug")]
    public JsonElement? Debug { get; init; }
}

/// <summary>
/// Viewer relationship state between the authenticated user and a viewed actor.
/// </summary>
public sealed class ViewerState : LexObject
{
    /// <summary>
    /// Whether the viewer has fully muted this account, directly or through a mute list. It is
    /// <see langword="false"/> when the mute covers only reposts or quote posts.
    /// </summary>
    [JsonPropertyName("muted")]
    public bool? Muted { get; init; }

    /// <summary>
    /// Whether the viewer has muted only this account's reposts. Exclusive with
    /// <see cref="Muted"/>.
    /// </summary>
    [JsonPropertyName("mutedOnlyReposts")]
    public bool? MutedOnlyReposts { get; init; }

    /// <summary>
    /// Whether the viewer has muted only this account's quote posts. Exclusive with
    /// <see cref="Muted"/>.
    /// </summary>
    [JsonPropertyName("mutedOnlyQuoteposts")]
    public bool? MutedOnlyQuoteposts { get; init; }

    /// <summary>The mute list responsible for muting this actor, if muted by a list.</summary>
    [JsonPropertyName("mutedByList")]
    public ListViewBasic? MutedByList { get; init; }

    /// <summary>Whether the subject blocks the viewer.</summary>
    [JsonPropertyName("blockedBy")]
    public bool? BlockedBy { get; init; }

    /// <summary>The AT-URI of the viewer's block record, if the viewer blocks this actor.</summary>
    [JsonPropertyName("blocking")]
    public AtUri? Blocking { get; init; }

    /// <summary>The block list responsible for blocking this actor, if blocked by a list.</summary>
    [JsonPropertyName("blockingByList")]
    public ListViewBasic? BlockingByList { get; init; }

    /// <summary>
    /// The AT-URI of the viewer's follow record, if the viewer follows this actor.
    /// </summary>
    [JsonPropertyName("following")]
    public AtUri? Following { get; init; }

    /// <summary>
    /// The AT-URI of the subject's follow record, if the subject follows the viewer.
    /// </summary>
    [JsonPropertyName("followedBy")]
    public AtUri? FollowedBy { get; init; }

    /// <summary>A sample of followers the viewer also follows. Present only in selected cases.</summary>
    [JsonPropertyName("knownFollowers")]
    public KnownFollowers? KnownFollowers { get; init; }

    /// <summary>
    /// Which of the account's activity the viewer is subscribed to. Present only in selected
    /// cases.
    /// </summary>
    [JsonPropertyName("activitySubscription")]
    public ActivitySubscription? ActivitySubscription { get; init; }
}

/// <summary>
/// Known followers between the viewer and the subject.
/// </summary>
public sealed class KnownFollowers : LexObject
{
    /// <summary>The total number of known followers.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>The follower profiles.</summary>
    [JsonPropertyName("followers")]
    public required IReadOnlyList<ProfileViewBasic> Followers { get; init; }
}

/// <summary>
/// What an account has published or allows (<c>app.bsky.actor.defs#profileAssociated</c>).
/// </summary>
public sealed class ProfileAssociated : LexObject
{
    /// <summary>The number of lists the account has created.</summary>
    [JsonPropertyName("lists")]
    public int? Lists { get; init; }

    /// <summary>The number of feed generators the account has declared.</summary>
    [JsonPropertyName("feedgens")]
    public int? Feedgens { get; init; }

    /// <summary>The number of starter packs the account has created.</summary>
    [JsonPropertyName("starterPacks")]
    public int? StarterPacks { get; init; }

    /// <summary>Whether the account runs a labeler.</summary>
    [JsonPropertyName("labeler")]
    public bool? Labeler { get; init; }

    /// <summary>Who may start a chat with the account.</summary>
    [JsonPropertyName("chat")]
    public ProfileAssociatedChat? Chat { get; init; }

    /// <summary>Who may subscribe to the account's activity.</summary>
    [JsonPropertyName("activitySubscription")]
    public ProfileAssociatedActivitySubscription? ActivitySubscription { get; init; }

    /// <summary>The account's Germ DM declaration, if it has one.</summary>
    [JsonPropertyName("germ")]
    public ProfileAssociatedGerm? Germ { get; init; }
}

/// <summary>
/// An account's chat settings (<c>app.bsky.actor.defs#profileAssociatedChat</c>).
/// </summary>
public sealed class ProfileAssociatedChat : LexObject
{
    /// <summary>
    /// Who may start a conversation: <c>all</c>, <c>none</c> or <c>following</c> (see
    /// <see cref="Chat.Bsky.Actor.ChatAllowIncoming"/>).
    /// </summary>
    [JsonPropertyName("allowIncoming")]
    public required string AllowIncoming { get; init; }

    /// <summary>
    /// Who may add the account to a group conversation: <c>all</c>, <c>none</c> or
    /// <c>following</c> (see <see cref="Chat.Bsky.Actor.ChatAllowIncoming"/>).
    /// </summary>
    [JsonPropertyName("allowGroupInvites")]
    public string? AllowGroupInvites { get; init; }
}

/// <summary>
/// An account's Germ DM declaration (<c>app.bsky.actor.defs#profileAssociatedGerm</c>).
/// </summary>
public sealed class ProfileAssociatedGerm : LexObject
{
    /// <summary>The URL that starts a Germ conversation with the account.</summary>
    [JsonPropertyName("messageMeUrl")]
    public required string MessageMeUrl { get; init; }

    /// <summary>Who the "message me" button is shown to: <c>usersIFollow</c> or <c>everyone</c>.</summary>
    [JsonPropertyName("showButtonTo")]
    public required string ShowButtonTo { get; init; }
}

/// <summary>
/// Who may subscribe to an account's activity
/// (<c>app.bsky.actor.defs#profileAssociatedActivitySubscription</c>).
/// </summary>
public sealed class ProfileAssociatedActivitySubscription : LexObject
{
    /// <summary>Who may subscribe: <c>followers</c>, <c>mutuals</c> or <c>none</c>.</summary>
    [JsonPropertyName("allowSubscriptions")]
    public required string AllowSubscriptions { get; init; }
}

/// <summary>
/// An account's verification information (<c>app.bsky.actor.defs#verificationState</c>).
/// </summary>
public sealed class VerificationState : LexObject
{
    /// <summary>
    /// The verifications trusted verifiers issued for the account. Verifications by untrusted
    /// verifiers are not included.
    /// </summary>
    [JsonPropertyName("verifications")]
    public required IReadOnlyList<VerificationView> Verifications { get; init; }

    /// <summary>The account's status as a verified account (see <see cref="VerificationStatus"/>).</summary>
    [JsonPropertyName("verifiedStatus")]
    public required string VerifiedStatus { get; init; }

    /// <summary>The account's status as a trusted verifier (see <see cref="VerificationStatus"/>).</summary>
    [JsonPropertyName("trustedVerifierStatus")]
    public required string TrustedVerifierStatus { get; init; }
}

/// <summary>
/// One verification of an account (<c>app.bsky.actor.defs#verificationView</c>).
/// </summary>
public sealed class VerificationView : LexObject
{
    /// <summary>The account that issued the verification.</summary>
    [JsonPropertyName("issuer")]
    public required Did Issuer { get; init; }

    /// <summary>The issuer's display name.</summary>
    [JsonPropertyName("issuerDisplayName")]
    public string? IssuerDisplayName { get; init; }

    /// <summary>The issuer's handle.</summary>
    [JsonPropertyName("issuerHandle")]
    public Handle? IssuerHandle { get; init; }

    /// <summary>The AT-URI of the verification record.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>Whether the verification passes validation.</summary>
    [JsonPropertyName("isValid")]
    public required bool IsValid { get; init; }

    /// <summary>When the verification was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// Known values of <see cref="VerificationState.VerifiedStatus"/> and
/// <see cref="VerificationState.TrustedVerifierStatus"/>.
/// </summary>
public static class VerificationStatus
{
    /// <summary>The account holds a valid verification, or is a valid trusted verifier.</summary>
    public const string Valid = "valid";

    /// <summary>The account's verifications, or its verifier status, no longer validate.</summary>
    public const string Invalid = "invalid";

    /// <summary>The account is not verified, or is not a trusted verifier.</summary>
    public const string None = "none";
}

/// <summary>
/// An account's current status, such as being live (<c>app.bsky.actor.defs#statusView</c>).
/// </summary>
public sealed class StatusView : LexObject
{
    /// <summary>The AT-URI of the status record.</summary>
    [JsonPropertyName("uri")]
    public AtUri? Uri { get; init; }

    /// <summary>The CID of the status record.</summary>
    [JsonPropertyName("cid")]
    public Cid? Cid { get; init; }

    /// <summary>The status, for example <c>app.bsky.actor.status#live</c>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>The status record.</summary>
    [JsonPropertyName("record")]
    public required JsonElement Record { get; init; }

    /// <summary>An embed attached to the status, such as a link to the live stream.</summary>
    [JsonPropertyName("embed")]
    public EmbedView? Embed { get; init; }

    /// <summary>The labels applied to the status.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>When the status expires, if it was given an expiry.</summary>
    [JsonPropertyName("expiresAt")]
    public AtDatetime? ExpiresAt { get; init; }

    /// <summary>Whether the status has not expired yet. Present only when it has an expiry.</summary>
    [JsonPropertyName("isActive")]
    public bool? IsActive { get; init; }

    /// <summary>Whether a moderator has disabled the account's access to going live.</summary>
    [JsonPropertyName("isDisabled")]
    public bool? IsDisabled { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  getProfile / getProfiles
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getProfiles (batch profile lookup).
/// </summary>
public sealed class GetProfilesResponse
{
    /// <summary>The detailed profile views.</summary>
    [JsonPropertyName("profiles")]
    public required IReadOnlyList<ProfileViewDetailed> Profiles { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Preferences
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getPreferences.
/// </summary>
public sealed class GetPreferencesResponse
{
    /// <summary>The account's preferences, one object per kind.</summary>
    [JsonPropertyName("preferences")]
    public required IReadOnlyList<Preference> Preferences { get; init; }
}

/// <summary>
/// Request for putPreferences.
/// </summary>
internal sealed class PutPreferencesRequest
{
    /// <summary>The account's preferences, one object per kind.</summary>
    [JsonPropertyName("preferences")]
    public required IReadOnlyList<Preference> Preferences { get; init; }
}

/// <summary>
/// One of an account's preferences (the open <c>app.bsky.actor.defs#preferences</c> union). A
/// preference this SDK does not model reads as <see cref="UnknownPreference"/>, which
/// <see cref="ActorClient.PutPreferencesAsync"/> writes back unchanged.
/// </summary>
/// <remarks>
/// <c>putPreferences</c> replaces the whole set, so read the preferences, change the ones you
/// need and put the full list back. The properties of each preference are settable for that.
/// </remarks>
[AtProtoUnion(typeof(UnknownPreference))]
[JsonDerivedType(typeof(AdultContentPreference), "app.bsky.actor.defs#adultContentPref")]
[JsonDerivedType(typeof(ContentLabelPreference), "app.bsky.actor.defs#contentLabelPref")]
[JsonDerivedType(typeof(SavedFeedsPreference), "app.bsky.actor.defs#savedFeedsPref")]
[JsonDerivedType(typeof(SavedFeedsPreferenceV2), "app.bsky.actor.defs#savedFeedsPrefV2")]
[JsonDerivedType(typeof(PersonalDetailsPreference), "app.bsky.actor.defs#personalDetailsPref")]
[JsonDerivedType(typeof(DeclaredAgePreference), "app.bsky.actor.defs#declaredAgePref")]
[JsonDerivedType(typeof(FeedViewPreference), "app.bsky.actor.defs#feedViewPref")]
[JsonDerivedType(typeof(ThreadViewPreference), "app.bsky.actor.defs#threadViewPref")]
[JsonDerivedType(typeof(InterestsPreference), "app.bsky.actor.defs#interestsPref")]
[JsonDerivedType(typeof(MutedWordsPreference), "app.bsky.actor.defs#mutedWordsPref")]
[JsonDerivedType(typeof(HiddenPostsPreference), "app.bsky.actor.defs#hiddenPostsPref")]
[JsonDerivedType(typeof(BskyAppStatePreference), "app.bsky.actor.defs#bskyAppStatePref")]
[JsonDerivedType(typeof(LabelersPreference), "app.bsky.actor.defs#labelersPref")]
[JsonDerivedType(typeof(PostInteractionSettingsPreference), "app.bsky.actor.defs#postInteractionSettingsPref")]
[JsonDerivedType(typeof(VerificationPreferences), "app.bsky.actor.defs#verificationPrefs")]
[JsonDerivedType(typeof(LiveEventPreferences), "app.bsky.actor.defs#liveEventPreferences")]
public abstract class Preference : LexObject;

/// <summary>
/// A preference whose <c>$type</c> this SDK version does not model. It keeps the raw object and
/// writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownPreference : Preference, IUnknownUnionVariant
{
    /// <summary>Creates an unknown preference from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownPreference(string type, JsonElement raw)
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

/// <summary>Whether adult content is shown (<c>#adultContentPref</c>).</summary>
public sealed class AdultContentPreference : Preference
{
    /// <summary>Whether adult content is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

/// <summary>How content with one label is shown (<c>#contentLabelPref</c>).</summary>
public sealed class ContentLabelPreference : Preference
{
    /// <summary>The labeler this preference applies to; <see langword="null"/> for all labelers.</summary>
    [JsonPropertyName("labelerDid")]
    public Did? LabelerDid { get; set; }

    /// <summary>The label value.</summary>
    [JsonPropertyName("label")]
    public required string Label { get; set; }

    /// <summary>How labelled content is shown: <c>ignore</c>, <c>show</c>, <c>warn</c> or <c>hide</c>.</summary>
    [JsonPropertyName("visibility")]
    public required string Visibility { get; set; }
}

/// <summary>Saved and pinned feeds, the original form (<c>#savedFeedsPref</c>).</summary>
public sealed class SavedFeedsPreference : Preference
{
    /// <summary>The pinned feeds.</summary>
    [JsonPropertyName("pinned")]
    public required IReadOnlyList<AtUri> Pinned { get; set; }

    /// <summary>The saved feeds.</summary>
    [JsonPropertyName("saved")]
    public required IReadOnlyList<AtUri> Saved { get; set; }

    /// <summary>The position of the Following timeline among the pinned feeds.</summary>
    [JsonPropertyName("timelineIndex")]
    public int? TimelineIndex { get; set; }
}

/// <summary>Saved and pinned feeds, lists and the timeline (<c>#savedFeedsPrefV2</c>).</summary>
public sealed class SavedFeedsPreferenceV2 : Preference
{
    /// <summary>The saved items, in order.</summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<SavedFeed> Items { get; set; }
}

/// <summary>A saved feed, list or timeline (<c>app.bsky.actor.defs#savedFeed</c>).</summary>
public sealed class SavedFeed : LexObject
{
    /// <summary>A client-assigned identifier for the item.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>What is saved: <c>feed</c>, <c>list</c> or <c>timeline</c>.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>The feed or list AT-URI, or the timeline's name (<c>following</c>).</summary>
    [JsonPropertyName("value")]
    public required string Value { get; set; }

    /// <summary>Whether the item is pinned.</summary>
    [JsonPropertyName("pinned")]
    public required bool Pinned { get; set; }
}

/// <summary>The account owner's personal details (<c>#personalDetailsPref</c>).</summary>
public sealed class PersonalDetailsPreference : Preference
{
    /// <summary>The account owner's birth date.</summary>
    [JsonPropertyName("birthDate")]
    public AtDatetime? BirthDate { get; set; }
}

/// <summary>
/// The age thresholds the account owner's declared birth date passes (<c>#declaredAgePref</c>).
/// Read-only; its absence means no declaration was made.
/// </summary>
public sealed class DeclaredAgePreference : Preference
{
    /// <summary>Whether the account owner declared they are over 13.</summary>
    [JsonPropertyName("isOverAge13")]
    public bool? IsOverAge13 { get; set; }

    /// <summary>Whether the account owner declared they are over 16.</summary>
    [JsonPropertyName("isOverAge16")]
    public bool? IsOverAge16 { get; set; }

    /// <summary>Whether the account owner declared they are over 18.</summary>
    [JsonPropertyName("isOverAge18")]
    public bool? IsOverAge18 { get; set; }
}

/// <summary>What one feed hides (<c>#feedViewPref</c>).</summary>
public sealed class FeedViewPreference : Preference
{
    /// <summary>The feed's AT-URI, or an identifier that describes the feed.</summary>
    [JsonPropertyName("feed")]
    public required string Feed { get; set; }

    /// <summary>Whether replies are hidden.</summary>
    [JsonPropertyName("hideReplies")]
    public bool? HideReplies { get; set; }

    /// <summary>Whether replies by accounts the owner does not follow are hidden (the default).</summary>
    [JsonPropertyName("hideRepliesByUnfollowed")]
    public bool? HideRepliesByUnfollowed { get; set; }

    /// <summary>Replies with fewer likes than this are hidden.</summary>
    [JsonPropertyName("hideRepliesByLikeCount")]
    public int? HideRepliesByLikeCount { get; set; }

    /// <summary>Whether reposts are hidden.</summary>
    [JsonPropertyName("hideReposts")]
    public bool? HideReposts { get; set; }

    /// <summary>Whether quote posts are hidden.</summary>
    [JsonPropertyName("hideQuotePosts")]
    public bool? HideQuotePosts { get; set; }
}

/// <summary>How threads are shown (<c>#threadViewPref</c>).</summary>
public sealed class ThreadViewPreference : Preference
{
    /// <summary>
    /// The reply order: <c>oldest</c>, <c>newest</c>, <c>most-likes</c>, <c>random</c> or
    /// <c>hotness</c>.
    /// </summary>
    [JsonPropertyName("sort")]
    public string? Sort { get; set; }
}

/// <summary>The account owner's interests (<c>#interestsPref</c>).</summary>
public sealed class InterestsPreference : Preference
{
    /// <summary>Tags describing the owner's interests, gathered during onboarding.</summary>
    [JsonPropertyName("tags")]
    public required IReadOnlyList<string> Tags { get; set; }

    /// <summary>When the owner last updated their interests.</summary>
    [JsonPropertyName("updatedAt")]
    public AtDatetime? UpdatedAt { get; set; }
}

/// <summary>The account owner's muted words (<c>#mutedWordsPref</c>).</summary>
public sealed class MutedWordsPreference : Preference
{
    /// <summary>The muted words.</summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<MutedWord> Items { get; set; }
}

/// <summary>A muted word (<c>app.bsky.actor.defs#mutedWord</c>).</summary>
public sealed class MutedWord : LexObject
{
    /// <summary>A client-assigned identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The muted word itself.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; set; }

    /// <summary>Where the word is matched: <c>content</c> and/or <c>tag</c>.</summary>
    [JsonPropertyName("targets")]
    public required IReadOnlyList<string> Targets { get; set; }

    /// <summary>
    /// Whose posts the word applies to: <c>all</c> (the default) or <c>exclude-following</c>.
    /// </summary>
    [JsonPropertyName("actorTarget")]
    public string? ActorTarget { get; set; }

    /// <summary>When the mute expires.</summary>
    [JsonPropertyName("expiresAt")]
    public AtDatetime? ExpiresAt { get; set; }
}

/// <summary>Posts the account owner has hidden (<c>#hiddenPostsPref</c>).</summary>
public sealed class HiddenPostsPreference : Preference
{
    /// <summary>The AT-URIs of the hidden posts.</summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<AtUri> Items { get; set; }
}

/// <summary>
/// State specific to the Bluesky app (<c>#bskyAppStatePref</c>). Other apps should not use it.
/// </summary>
public sealed class BskyAppStatePreference : Preference
{
    /// <summary>The progress guide the owner is going through, if any.</summary>
    [JsonPropertyName("activeProgressGuide")]
    public BskyAppProgressGuide? ActiveProgressGuide { get; set; }

    /// <summary>Tokens of the nudges (modals, tours, highlight dots) to show the owner.</summary>
    [JsonPropertyName("queuedNudges")]
    public IReadOnlyList<string>? QueuedNudges { get; set; }

    /// <summary>The new-user experiences the owner has encountered.</summary>
    [JsonPropertyName("nuxs")]
    public IReadOnlyList<Nux>? Nuxs { get; set; }

    /// <summary>Whether the owner takes part in the beta features program.</summary>
    [JsonPropertyName("isBetaUser")]
    public bool? IsBetaUser { get; set; }
}

/// <summary>An active Bluesky app progress guide (<c>app.bsky.actor.defs#bskyAppProgressGuide</c>).</summary>
public sealed class BskyAppProgressGuide : LexObject
{
    /// <summary>The guide's identifier.</summary>
    [JsonPropertyName("guide")]
    public required string Guide { get; set; }
}

/// <summary>A new-user experience's stored state (<c>app.bsky.actor.defs#nux</c>).</summary>
public sealed class Nux : LexObject
{
    /// <summary>The experience's identifier.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>Whether the owner has completed it.</summary>
    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    /// <summary>Data the experience defines, at most 300 characters.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    /// <summary>When the experience expires and counts as completed.</summary>
    [JsonPropertyName("expiresAt")]
    public AtDatetime? ExpiresAt { get; set; }
}

/// <summary>The labelers the account owner subscribes to (<c>#labelersPref</c>).</summary>
public sealed class LabelersPreference : Preference
{
    /// <summary>The subscribed labelers.</summary>
    [JsonPropertyName("labelers")]
    public required IReadOnlyList<LabelerPreferenceItem> Labelers { get; set; }
}

/// <summary>A subscribed labeler (<c>app.bsky.actor.defs#labelerPrefItem</c>).</summary>
public sealed class LabelerPreferenceItem : LexObject
{
    /// <summary>The labeler's DID.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; set; }
}

/// <summary>
/// The default interaction settings for new posts (<c>#postInteractionSettingsPref</c>). Apps
/// apply them when creating a post; they mirror the threadgate and postgate records.
/// </summary>
public sealed class PostInteractionSettingsPreference : Preference
{
    /// <summary>
    /// Who may reply. An empty list allows no one; <see langword="null"/> allows everyone.
    /// </summary>
    [JsonPropertyName("threadgateAllowRules")]
    public IReadOnlyList<ThreadgateRule>? ThreadgateAllowRules { get; set; }

    /// <summary>Who may quote; empty or <see langword="null"/> applies no rules.</summary>
    [JsonPropertyName("postgateEmbeddingRules")]
    public IReadOnlyList<PostgateEmbeddingRule>? PostgateEmbeddingRules { get; set; }
}

/// <summary>How verified accounts appear (<c>#verificationPrefs</c>).</summary>
public sealed class VerificationPreferences : Preference
{
    /// <summary>Whether the badges of verified accounts and trusted verifiers are hidden.</summary>
    [JsonPropertyName("hideBadges")]
    public bool? HideBadges { get; set; }
}

/// <summary>Live-event feed preferences (<c>#liveEventPreferences</c>).</summary>
public sealed class LiveEventPreferences : Preference
{
    /// <summary>The live-event feeds the owner has hidden.</summary>
    [JsonPropertyName("hiddenFeedIds")]
    public IReadOnlyList<string>? HiddenFeedIds { get; set; }

    /// <summary>Whether every live-event feed is hidden.</summary>
    [JsonPropertyName("hideAllFeeds")]
    public bool? HideAllFeeds { get; set; }
}

// ──────────────────────────────────────────────────────────────
//  Suggestions / Search
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getSuggestions.
/// </summary>
public sealed class GetSuggestionsResponse : ICursorPage<ProfileView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The actors.</summary>
    [JsonPropertyName("actors")]
    public required IReadOnlyList<ProfileView> Actors { get; init; }

    /// <summary>
    /// The recommendation's identifier (a snowflake), for recommendation events.
    /// </summary>
    [JsonPropertyName("recIdStr")]
    public string? RecIdStr { get; init; }

    IReadOnlyList<ProfileView> ICursorPage<ProfileView>.Items => Actors;
}

/// <summary>
/// Response from searchActors.
/// </summary>
public sealed class SearchActorsResponse : ICursorPage<ProfileView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The actors.</summary>
    [JsonPropertyName("actors")]
    public required IReadOnlyList<ProfileView> Actors { get; init; }

    IReadOnlyList<ProfileView> ICursorPage<ProfileView>.Items => Actors;
}

/// <summary>
/// Response from searchActorsTypeahead (autocomplete).
/// </summary>
public sealed class SearchActorsTypeaheadResponse
{
    /// <summary>The actors.</summary>
    [JsonPropertyName("actors")]
    public required IReadOnlyList<ProfileViewBasic> Actors { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Profile record (the actual repo record)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// An actor profile record stored in the repo at app.bsky.actor.profile/self.
/// </summary>
/// <remarks>
/// The properties are settable so that <see cref="BlueskyClients.UpdateProfileAsync"/> can hand the
/// current record to a callback that edits it in place. Setting a property to
/// <see langword="null"/> removes the field.
/// </remarks>
public sealed class ProfileRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection records of this type are stored in (<c>app.bsky.actor.profile</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.actor.profile");

    /// <summary>The Lexicon type discriminator (<c>app.bsky.actor.profile</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

    /// <summary>The human-readable display name (at most 64 graphemes).</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>A free-text description (at most 256 graphemes).</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Free-form pronouns text (at most 20 graphemes).</summary>
    [JsonPropertyName("pronouns")]
    public string? Pronouns { get; set; }

    /// <summary>A website URI shown on the profile.</summary>
    [JsonPropertyName("website")]
    public string? Website { get; set; }

    /// <summary>The avatar image (PNG or JPEG, at most 1,000,000 bytes).</summary>
    [JsonPropertyName("avatar")]
    public BlobRef? Avatar { get; set; }

    /// <summary>The banner image (PNG or JPEG, at most 1,000,000 bytes).</summary>
    [JsonPropertyName("banner")]
    public BlobRef? Banner { get; set; }

    /// <summary>Self-applied labels on the whole account.</summary>
    [JsonPropertyName("labels")]
    public SelfLabels? Labels { get; set; }

    /// <summary>The starter pack the account joined through, if any.</summary>
    [JsonPropertyName("joinedViaStarterPack")]
    public StrongRef? JoinedViaStarterPack { get; set; }

    /// <summary>A reference to the post pinned to the profile.</summary>
    [JsonPropertyName("pinnedPost")]
    public StrongRef? PinnedPost { get; set; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; set; }
}

/// <summary>
/// An account's status, such as being live. Collection: app.bsky.actor.status, record key
/// <c>self</c>. Profile views show it as <see cref="StatusView"/>.
/// </summary>
public sealed class StatusRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection records of this type are stored in (<c>app.bsky.actor.status</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.actor.status");

    /// <summary>The Lexicon type discriminator (<c>app.bsky.actor.status</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

    /// <summary>The status (see <see cref="ActorStatus"/>).</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>An embed for the status, such as an <see cref="ExternalEmbed"/> linking to the stream.</summary>
    [JsonPropertyName("embed")]
    public EmbedBase? Embed { get; init; }

    /// <summary>How long the status lasts, in minutes. Apps may impose limits.</summary>
    [JsonPropertyName("durationMinutes")]
    public int? DurationMinutes { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// Known values of <see cref="StatusRecord.Status"/> and <see cref="StatusView.Status"/>.
/// </summary>
public static class ActorStatus
{
    /// <summary>The account is offering live content.</summary>
    public const string Live = "app.bsky.actor.status#live";
}

/// <summary>
/// An account's choice about appearing in content discovery. Collection:
/// app.bsky.actor.contentVisibilityDeclaration, record key <c>self</c>.
/// </summary>
public sealed class ContentVisibilityDeclarationRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection records of this type are stored in (<c>app.bsky.actor.contentVisibilityDeclaration</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.actor.contentVisibilityDeclaration");

    /// <summary>
    /// The Lexicon type discriminator (<c>app.bsky.actor.contentVisibilityDeclaration</c>).
    /// </summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

    /// <summary>
    /// Whether the account asks that its posts be left out of algorithmic recommendations. An
    /// account without the record counts as <see langword="false"/>.
    /// </summary>
    [JsonPropertyName("hideFromAlgorithmicRecommendations")]
    public required bool HideFromAlgorithmicRecommendations { get; init; }
}
