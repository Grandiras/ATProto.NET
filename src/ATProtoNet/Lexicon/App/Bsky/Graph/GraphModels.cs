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
