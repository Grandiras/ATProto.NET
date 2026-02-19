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
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    [JsonPropertyName("banner")]
    public string? Banner { get; init; }

    [JsonPropertyName("followsCount")]
    public int? FollowsCount { get; init; }

    [JsonPropertyName("followersCount")]
    public int? FollowersCount { get; init; }

    [JsonPropertyName("postsCount")]
    public int? PostsCount { get; init; }

    [JsonPropertyName("associatedChat")]
    public JsonElement? AssociatedChat { get; init; }

    [JsonPropertyName("indexedAt")]
    public string? IndexedAt { get; init; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }

    [JsonPropertyName("viewer")]
    public ViewerState? Viewer { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    [JsonPropertyName("pinnedPost")]
    public StrongRef? PinnedPost { get; init; }
}

/// <summary>
/// Basic profile view (used in actor lists, follows, etc.).
/// </summary>
public sealed class ProfileView
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    [JsonPropertyName("indexedAt")]
    public string? IndexedAt { get; init; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }

    [JsonPropertyName("viewer")]
    public ViewerState? Viewer { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }
}

/// <summary>
/// Minimal profile view (used inline in posts, etc.).
/// </summary>
public sealed class ProfileViewBasic
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    [JsonPropertyName("viewer")]
    public ViewerState? Viewer { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }
}

/// <summary>
/// Viewer relationship state between the authenticated user and a viewed actor.
/// </summary>
public sealed class ViewerState
{
    [JsonPropertyName("muted")]
    public bool? Muted { get; init; }

    [JsonPropertyName("mutedByList")]
    public JsonElement? MutedByList { get; init; }

    [JsonPropertyName("blockedBy")]
    public bool? BlockedBy { get; init; }

    [JsonPropertyName("blocking")]
    public string? Blocking { get; init; }

    [JsonPropertyName("blockingByList")]
    public JsonElement? BlockingByList { get; init; }

    [JsonPropertyName("following")]
    public string? Following { get; init; }

    [JsonPropertyName("followedBy")]
    public string? FollowedBy { get; init; }

    [JsonPropertyName("knownFollowers")]
    public KnownFollowers? KnownFollowers { get; init; }
}

/// <summary>
/// Known followers between the viewer and the subject.
/// </summary>
public sealed class KnownFollowers
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

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
    [JsonPropertyName("preferences")]
    public required List<JsonElement> Preferences { get; init; }
}

/// <summary>
/// Request for putPreferences.
/// </summary>
public sealed class PutPreferencesRequest
{
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
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("actors")]
    public required List<ProfileView> Actors { get; init; }
}

/// <summary>
/// Response from searchActors.
/// </summary>
public sealed class SearchActorsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("actors")]
    public required List<ProfileView> Actors { get; init; }
}

/// <summary>
/// Response from searchActorsTypeahead (autocomplete).
/// </summary>
public sealed class SearchActorsTypeaheadResponse
{
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
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.actor.profile";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("avatar")]
    public BlobRef? Avatar { get; init; }

    [JsonPropertyName("banner")]
    public BlobRef? Banner { get; init; }

    [JsonPropertyName("pinnedPost")]
    public StrongRef? PinnedPost { get; init; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }
}
