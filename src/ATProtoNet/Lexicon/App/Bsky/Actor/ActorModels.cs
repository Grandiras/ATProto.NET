using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.App.Bsky.Actor;

// ──────────────────────────────────────────────────────────────
//  Profile types
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Detailed profile view (returned by getProfile).
/// </summary>
public sealed class ProfileViewDetailed
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

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

    /// <summary>The actor's chat availability declaration.</summary>
    [JsonPropertyName("associatedChat")]
    public JsonElement? AssociatedChat { get; init; }

    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
    [JsonPropertyName("indexedAt")]
    public string? IndexedAt { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public ViewerState? Viewer { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    /// <summary>A reference to the post pinned to the profile.</summary>
    [JsonPropertyName("pinnedPost")]
    public StrongRef? PinnedPost { get; init; }
}

/// <summary>
/// Basic profile view (used in actor lists, follows, etc.).
/// </summary>
public sealed class ProfileView
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
    [JsonPropertyName("indexedAt")]
    public string? IndexedAt { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public ViewerState? Viewer { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }
}

/// <summary>
/// Minimal profile view (used inline in posts, etc.).
/// </summary>
public sealed class ProfileViewBasic
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public ViewerState? Viewer { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }
}

/// <summary>
/// Viewer relationship state between the authenticated user and a viewed actor.
/// </summary>
public sealed class ViewerState
{
    /// <summary>Whether the viewer has muted this subject.</summary>
    [JsonPropertyName("muted")]
    public bool? Muted { get; init; }

    /// <summary>The mute list responsible for muting this actor, if muted by a list.</summary>
    [JsonPropertyName("mutedByList")]
    public JsonElement? MutedByList { get; init; }

    /// <summary>Whether the subject blocks the viewer.</summary>
    [JsonPropertyName("blockedBy")]
    public bool? BlockedBy { get; init; }

    /// <summary>The AT-URI of the viewer's block record, if the viewer blocks this actor.</summary>
    [JsonPropertyName("blocking")]
    public string? Blocking { get; init; }

    /// <summary>The block list responsible for blocking this actor, if blocked by a list.</summary>
    [JsonPropertyName("blockingByList")]
    public JsonElement? BlockingByList { get; init; }

    /// <summary>
    /// The AT-URI of the viewer's follow record, if the viewer follows this actor.
    /// </summary>
    [JsonPropertyName("following")]
    public string? Following { get; init; }

    /// <summary>
    /// The AT-URI of the subject's follow record, if the subject follows the viewer.
    /// </summary>
    [JsonPropertyName("followedBy")]
    public string? FollowedBy { get; init; }

    /// <summary>A sample of followers the viewer also follows.</summary>
    [JsonPropertyName("knownFollowers")]
    public KnownFollowers? KnownFollowers { get; init; }
}

/// <summary>
/// Known followers between the viewer and the subject.
/// </summary>
public sealed class KnownFollowers
{
    /// <summary>The total number of known followers.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>The follower profiles.</summary>
    [JsonPropertyName("followers")]
    public required List<ProfileViewBasic> Followers { get; init; }
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
    public required List<ProfileViewDetailed> Profiles { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Preferences
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getPreferences.
/// </summary>
public sealed class GetPreferencesResponse
{
    /// <summary>The actor preference objects.</summary>
    [JsonPropertyName("preferences")]
    public required List<JsonElement> Preferences { get; init; }
}

/// <summary>
/// Request for putPreferences.
/// </summary>
public sealed class PutPreferencesRequest
{
    /// <summary>The actor preference objects.</summary>
    [JsonPropertyName("preferences")]
    public required List<JsonElement> Preferences { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Suggestions / Search
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getSuggestions.
/// </summary>
public sealed class GetSuggestionsResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The actors.</summary>
    [JsonPropertyName("actors")]
    public required List<ProfileView> Actors { get; init; }
}

/// <summary>
/// Response from searchActors.
/// </summary>
public sealed class SearchActorsResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The actors.</summary>
    [JsonPropertyName("actors")]
    public required List<ProfileView> Actors { get; init; }
}

/// <summary>
/// Response from searchActorsTypeahead (autocomplete).
/// </summary>
public sealed class SearchActorsTypeaheadResponse
{
    /// <summary>The actors.</summary>
    [JsonPropertyName("actors")]
    public required List<ProfileViewBasic> Actors { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Profile record (the actual repo record)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// An actor profile record stored in the repo at app.bsky.actor.profile/self.
/// </summary>
public sealed class ProfileRecord
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.actor.profile</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.actor.profile";

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public BlobRef? Avatar { get; init; }

    /// <summary>The banner image.</summary>
    [JsonPropertyName("banner")]
    public BlobRef? Banner { get; init; }

    /// <summary>A reference to the post pinned to the profile.</summary>
    [JsonPropertyName("pinnedPost")]
    public StrongRef? PinnedPost { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }
}
