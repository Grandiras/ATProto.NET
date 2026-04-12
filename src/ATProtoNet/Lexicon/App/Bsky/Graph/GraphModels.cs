using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.App.Bsky.Graph;

// ──────────────────────────────────────────────────────────────
//  Graph records (stored in repos)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A follow record. Collection: app.bsky.graph.follow
/// </summary>
public sealed class FollowRecord
{
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.follow";

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// A block record. Collection: app.bsky.graph.block
/// </summary>
public sealed class BlockRecord
{
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.block";

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// A list record. Collection: app.bsky.graph.list
/// </summary>
public sealed class ListRecord
{
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.list";

    /// <summary>List purpose: "app.bsky.graph.defs#modlist" or "app.bsky.graph.defs#curatelist".</summary>
    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("descriptionFacets")]
    public List<Facet>? DescriptionFacets { get; init; }

    [JsonPropertyName("avatar")]
    public BlobRef? Avatar { get; init; }

    [JsonPropertyName("labels")]
    public JsonElement? Labels { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// A list item record. Collection: app.bsky.graph.listitem
/// </summary>
public sealed class ListItemRecord
{
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.listitem";

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("list")]
    public required string List { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// A list block record. Collection: app.bsky.graph.listblock
/// </summary>
public sealed class ListBlockRecord
{
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.listblock";

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Well-known list purposes
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Well-known list purpose URIs.
/// </summary>
public static class ListPurpose
{
    /// <summary>A moderation list (muting/blocking).</summary>
    public const string ModList = "app.bsky.graph.defs#modlist";

    /// <summary>A curation list (feed curation).</summary>
    public const string CurateList = "app.bsky.graph.defs#curatelist";

    /// <summary>A reference list (general-purpose list).</summary>
    public const string ReferenceList = "app.bsky.graph.defs#referencelist";
}

// ──────────────────────────────────────────────────────────────
//  View types
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A list view.
/// </summary>
public sealed class ListView
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("creator")]
    public required ProfileView Creator { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("descriptionFacets")]
    public List<Facet>? DescriptionFacets { get; init; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    [JsonPropertyName("listItemCount")]
    public int? ListItemCount { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    [JsonPropertyName("viewer")]
    public ListViewerState? Viewer { get; init; }

    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }
}

/// <summary>
/// Viewer state for a list.
/// </summary>
public sealed class ListViewerState
{
    [JsonPropertyName("muted")]
    public bool? Muted { get; init; }

    [JsonPropertyName("blocked")]
    public string? Blocked { get; init; }
}

/// <summary>
/// A basic list view (less detail).
/// </summary>
public sealed class ListViewBasic
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    [JsonPropertyName("listItemCount")]
    public int? ListItemCount { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    [JsonPropertyName("viewer")]
    public ListViewerState? Viewer { get; init; }

    [JsonPropertyName("indexedAt")]
    public string? IndexedAt { get; init; }
}

/// <summary>
/// A list item view (a member of a list).
/// </summary>
public sealed class ListItemView
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("subject")]
    public required ProfileView Subject { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  API responses
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getFollowers.
/// </summary>
public sealed class GetFollowersResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("subject")]
    public required ProfileView Subject { get; init; }

    [JsonPropertyName("followers")]
    public required List<ProfileView> Followers { get; init; }
}

/// <summary>
/// Response from getFollows.
/// </summary>
public sealed class GetFollowsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("subject")]
    public required ProfileView Subject { get; init; }

    [JsonPropertyName("follows")]
    public required List<ProfileView> Follows { get; init; }
}

/// <summary>
/// Response from getBlocks.
/// </summary>
public sealed class GetBlocksResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("blocks")]
    public required List<ProfileView> Blocks { get; init; }
}

/// <summary>
/// Response from getLists.
/// </summary>
public sealed class GetListsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("lists")]
    public required List<ListView> Lists { get; init; }
}

/// <summary>
/// Response from getList.
/// </summary>
public sealed class GetListResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("list")]
    public required ListView List { get; init; }

    [JsonPropertyName("items")]
    public required List<ListItemView> Items { get; init; }
}

/// <summary>
/// Response from getMutes.
/// </summary>
public sealed class GetMutesResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("mutes")]
    public required List<ProfileView> Mutes { get; init; }
}

/// <summary>
/// Response from getListMutes.
/// </summary>
public sealed class GetListMutesResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("lists")]
    public required List<ListView> Lists { get; init; }
}

/// <summary>
/// Response from getListBlocks.
/// </summary>
public sealed class GetListBlocksResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("lists")]
    public required List<ListView> Lists { get; init; }
}

/// <summary>
/// Response from getSuggestedFollowsByActor.
/// </summary>
public sealed class GetSuggestedFollowsByActorResponse
{
    [JsonPropertyName("suggestions")]
    public required List<ProfileView> Suggestions { get; init; }

    [JsonPropertyName("isFallback")]
    public bool? IsFallback { get; init; }
}

/// <summary>
/// Request body for muteActor / unmuteActor.
/// </summary>
public sealed class MuteActorRequest
{
    [JsonPropertyName("actor")]
    public required string Actor { get; init; }
}

/// <summary>
/// Request body for muteActorList / unmuteActorList.
/// </summary>
public sealed class MuteActorListRequest
{
    [JsonPropertyName("list")]
    public required string List { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Starter pack records & views
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A starter pack record. Collection: app.bsky.graph.starterpack
/// </summary>
public sealed class StarterPackRecord
{
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.starterpack";

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("descriptionFacets")]
    public List<Facet>? DescriptionFacets { get; init; }

    [JsonPropertyName("list")]
    public required string List { get; init; }

    [JsonPropertyName("feeds")]
    public List<StarterPackFeedItem>? Feeds { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// A feed item reference in a starter pack.
/// </summary>
public sealed class StarterPackFeedItem
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}

/// <summary>
/// Basic view of a starter pack.
/// </summary>
public sealed class StarterPackViewBasic
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("record")]
    public required JsonElement Record { get; init; }

    [JsonPropertyName("creator")]
    public required ProfileViewBasic Creator { get; init; }

    [JsonPropertyName("listItemCount")]
    public int? ListItemCount { get; init; }

    [JsonPropertyName("joinedWeekCount")]
    public int? JoinedWeekCount { get; init; }

    [JsonPropertyName("joinedAllTimeCount")]
    public int? JoinedAllTimeCount { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }
}

/// <summary>
/// Full view of a starter pack.
/// </summary>
public sealed class StarterPackView
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("record")]
    public required JsonElement Record { get; init; }

    [JsonPropertyName("creator")]
    public required ProfileViewBasic Creator { get; init; }

    [JsonPropertyName("list")]
    public ListViewBasic? List { get; init; }

    [JsonPropertyName("listItemsSample")]
    public List<ListItemView>? ListItemsSample { get; init; }

    [JsonPropertyName("feeds")]
    public List<JsonElement>? Feeds { get; init; }

    [JsonPropertyName("joinedWeekCount")]
    public int? JoinedWeekCount { get; init; }

    [JsonPropertyName("joinedAllTimeCount")]
    public int? JoinedAllTimeCount { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Relationship types
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A relationship between two actors.
/// </summary>
public sealed class Relationship
{
    [JsonPropertyName("$type")]
    public string? Type { get; init; }

    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("following")]
    public string? Following { get; init; }

    [JsonPropertyName("followedBy")]
    public string? FollowedBy { get; init; }
}

/// <summary>
/// A "not found" actor placeholder in relationship responses.
/// </summary>
public sealed class NotFoundActor
{
    [JsonPropertyName("$type")]
    public string? Type { get; init; }

    [JsonPropertyName("actor")]
    public required string Actor { get; init; }

    [JsonPropertyName("notFound")]
    public required bool NotFound { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Additional API responses
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getRelationships.
/// </summary>
public sealed class GetRelationshipsResponse
{
    [JsonPropertyName("actor")]
    public string? Actor { get; init; }

    [JsonPropertyName("relationships")]
    public required List<JsonElement> Relationships { get; init; }
}

/// <summary>
/// Response from getKnownFollowers.
/// </summary>
public sealed class GetKnownFollowersResponse
{
    [JsonPropertyName("subject")]
    public required ProfileView Subject { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("followers")]
    public required List<ProfileView> Followers { get; init; }
}

/// <summary>
/// Response from getStarterPack.
/// </summary>
public sealed class GetStarterPackResponse
{
    [JsonPropertyName("starterPack")]
    public required StarterPackView StarterPack { get; init; }
}

/// <summary>
/// Response from getStarterPacks.
/// </summary>
public sealed class GetStarterPacksResponse
{
    [JsonPropertyName("starterPacks")]
    public required List<StarterPackViewBasic> StarterPacks { get; init; }
}

/// <summary>
/// Response from getActorStarterPacks.
/// </summary>
public sealed class GetActorStarterPacksResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("starterPacks")]
    public required List<StarterPackViewBasic> StarterPacks { get; init; }
}

/// <summary>
/// Response from searchStarterPacks.
/// </summary>
public sealed class SearchStarterPacksResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("starterPacks")]
    public required List<StarterPackViewBasic> StarterPacks { get; init; }
}

/// <summary>
/// Request body for muteThread / unmuteThread.
/// </summary>
public sealed class MuteThreadRequest
{
    [JsonPropertyName("root")]
    public required string Root { get; init; }
}
