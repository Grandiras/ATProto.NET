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
    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.follow</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.follow";

    /// <summary>The DID of the account being followed.</summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// A block record. Collection: app.bsky.graph.block
/// </summary>
public sealed class BlockRecord
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.block</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.block";

    /// <summary>The DID of the account being blocked.</summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// A list record. Collection: app.bsky.graph.list
/// </summary>
public sealed class ListRecord
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.list</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.list";

    /// <summary>List purpose: "app.bsky.graph.defs#modlist" or "app.bsky.graph.defs#curatelist".</summary>
    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    /// <summary>The name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Rich-text facets (mentions, links, tags) applied to the description.</summary>
    [JsonPropertyName("descriptionFacets")]
    public List<Facet>? DescriptionFacets { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public BlobRef? Avatar { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public JsonElement? Labels { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// A list item record. Collection: app.bsky.graph.listitem
/// </summary>
public sealed class ListItemRecord
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.listitem</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.listitem";

    /// <summary>The DID of the account included in the list.</summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>The AT-URI of the list this membership belongs to.</summary>
    [JsonPropertyName("list")]
    public required string List { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// A list block record. Collection: app.bsky.graph.listblock
/// </summary>
public sealed class ListBlockRecord
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.listblock</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.listblock";

    /// <summary>The AT-URI of the list being blocked.</summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
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
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>The account that created this.</summary>
    [JsonPropertyName("creator")]
    public required ProfileView Creator { get; init; }

    /// <summary>The name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The purpose of the list (for example <c>app.bsky.graph.defs#modlist</c>).</summary>
    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Rich-text facets (mentions, links, tags) applied to the description.</summary>
    [JsonPropertyName("descriptionFacets")]
    public List<Facet>? DescriptionFacets { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    /// <summary>The number of items in the list.</summary>
    [JsonPropertyName("listItemCount")]
    public int? ListItemCount { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public ListViewerState? Viewer { get; init; }

    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }
}

/// <summary>
/// Viewer state for a list.
/// </summary>
public sealed class ListViewerState
{
    /// <summary>Whether the viewer has muted this list.</summary>
    [JsonPropertyName("muted")]
    public bool? Muted { get; init; }

    /// <summary>The AT-URI of the viewer's list-block record, if the viewer blocks this list.</summary>
    [JsonPropertyName("blocked")]
    public string? Blocked { get; init; }
}

/// <summary>
/// A basic list view (less detail).
/// </summary>
public sealed class ListViewBasic
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>The name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The purpose of the list (for example <c>app.bsky.graph.defs#modlist</c>).</summary>
    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    /// <summary>The number of items in the list.</summary>
    [JsonPropertyName("listItemCount")]
    public int? ListItemCount { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public ListViewerState? Viewer { get; init; }

    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
    [JsonPropertyName("indexedAt")]
    public string? IndexedAt { get; init; }
}

/// <summary>
/// A list item view (a member of a list).
/// </summary>
public sealed class ListItemView
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The profile of the listed account.</summary>
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
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The profile of the account whose followers these are.</summary>
    [JsonPropertyName("subject")]
    public required ProfileView Subject { get; init; }

    /// <summary>The follower profiles.</summary>
    [JsonPropertyName("followers")]
    public required List<ProfileView> Followers { get; init; }
}

/// <summary>
/// Response from getFollows.
/// </summary>
public sealed class GetFollowsResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The profile of the account whose follows these are.</summary>
    [JsonPropertyName("subject")]
    public required ProfileView Subject { get; init; }

    /// <summary>The profiles this actor follows.</summary>
    [JsonPropertyName("follows")]
    public required List<ProfileView> Follows { get; init; }
}

/// <summary>
/// Response from getBlocks.
/// </summary>
public sealed class GetBlocksResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The blocked profiles.</summary>
    [JsonPropertyName("blocks")]
    public required List<ProfileView> Blocks { get; init; }
}

/// <summary>
/// Response from getLists.
/// </summary>
public sealed class GetListsResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The lists.</summary>
    [JsonPropertyName("lists")]
    public required List<ListView> Lists { get; init; }
}

/// <summary>
/// Response from getList.
/// </summary>
public sealed class GetListResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The list.</summary>
    [JsonPropertyName("list")]
    public required ListView List { get; init; }

    /// <summary>The members of the list.</summary>
    [JsonPropertyName("items")]
    public required List<ListItemView> Items { get; init; }
}

/// <summary>
/// Response from getMutes.
/// </summary>
public sealed class GetMutesResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The muted profiles.</summary>
    [JsonPropertyName("mutes")]
    public required List<ProfileView> Mutes { get; init; }
}

/// <summary>
/// Response from getListMutes.
/// </summary>
public sealed class GetListMutesResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The lists.</summary>
    [JsonPropertyName("lists")]
    public required List<ListView> Lists { get; init; }
}

/// <summary>
/// Response from getListBlocks.
/// </summary>
public sealed class GetListBlocksResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The lists.</summary>
    [JsonPropertyName("lists")]
    public required List<ListView> Lists { get; init; }
}

/// <summary>
/// Response from getSuggestedFollowsByActor.
/// </summary>
public sealed class GetSuggestedFollowsByActorResponse
{
    /// <summary>The suggested profiles.</summary>
    [JsonPropertyName("suggestions")]
    public required List<ProfileView> Suggestions { get; init; }

    /// <summary>
    /// Whether these are generic fallback suggestions rather than personalised ones.
    /// </summary>
    [JsonPropertyName("isFallback")]
    public bool? IsFallback { get; init; }
}

/// <summary>
/// Request body for muteActor / unmuteActor.
/// </summary>
public sealed class MuteActorRequest
{
    /// <summary>The DID or handle of the actor to mute.</summary>
    [JsonPropertyName("actor")]
    public required string Actor { get; init; }
}

/// <summary>
/// Request body for muteActorList / unmuteActorList.
/// </summary>
public sealed class MuteActorListRequest
{
    /// <summary>The AT-URI of the list to mute.</summary>
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
    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.starterpack</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.starterpack";

    /// <summary>The name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Rich-text facets (mentions, links, tags) applied to the description.</summary>
    [JsonPropertyName("descriptionFacets")]
    public List<Facet>? DescriptionFacets { get; init; }

    /// <summary>The AT-URI of the list of accounts in the pack.</summary>
    [JsonPropertyName("list")]
    public required string List { get; init; }

    /// <summary>The AT-URIs of feeds included in the pack.</summary>
    [JsonPropertyName("feeds")]
    public List<StarterPackFeedItem>? Feeds { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// A feed item reference in a starter pack.
/// </summary>
public sealed class StarterPackFeedItem
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}

/// <summary>
/// Basic view of a starter pack.
/// </summary>
public sealed class StarterPackViewBasic
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>The record value.</summary>
    [JsonPropertyName("record")]
    public required JsonElement Record { get; init; }

    /// <summary>The account that created this.</summary>
    [JsonPropertyName("creator")]
    public required ProfileViewBasic Creator { get; init; }

    /// <summary>The number of items in the list.</summary>
    [JsonPropertyName("listItemCount")]
    public int? ListItemCount { get; init; }

    /// <summary>
    /// The number of accounts that joined via this starter pack in the last week.
    /// </summary>
    [JsonPropertyName("joinedWeekCount")]
    public int? JoinedWeekCount { get; init; }

    /// <summary>The total number of accounts that joined via this starter pack.</summary>
    [JsonPropertyName("joinedAllTimeCount")]
    public int? JoinedAllTimeCount { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }
}

/// <summary>
/// Full view of a starter pack.
/// </summary>
public sealed class StarterPackView
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>The record value.</summary>
    [JsonPropertyName("record")]
    public required JsonElement Record { get; init; }

    /// <summary>The account that created this.</summary>
    [JsonPropertyName("creator")]
    public required ProfileViewBasic Creator { get; init; }

    /// <summary>The list of accounts in the pack.</summary>
    [JsonPropertyName("list")]
    public ListViewBasic? List { get; init; }

    /// <summary>A sample of the list's members.</summary>
    [JsonPropertyName("listItemsSample")]
    public List<ListItemView>? ListItemsSample { get; init; }

    /// <summary>The feed generators included in the pack.</summary>
    [JsonPropertyName("feeds")]
    public List<JsonElement>? Feeds { get; init; }

    /// <summary>
    /// The number of accounts that joined via this starter pack in the last week.
    /// </summary>
    [JsonPropertyName("joinedWeekCount")]
    public int? JoinedWeekCount { get; init; }

    /// <summary>The total number of accounts that joined via this starter pack.</summary>
    [JsonPropertyName("joinedAllTimeCount")]
    public int? JoinedAllTimeCount { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
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
    /// <summary>The Lexicon type discriminator for this object.</summary>
    [JsonPropertyName("$type")]
    public string? Type { get; init; }

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

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
}

/// <summary>
/// A "not found" actor placeholder in relationship responses.
/// </summary>
public sealed class NotFoundActor
{
    /// <summary>The Lexicon type discriminator for this object.</summary>
    [JsonPropertyName("$type")]
    public string? Type { get; init; }

    /// <summary>The DID or handle that could not be resolved.</summary>
    [JsonPropertyName("actor")]
    public required string Actor { get; init; }

    /// <summary>
    /// Always <see langword="true"/>; marks the referenced subject as unavailable.
    /// </summary>
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
    /// <summary>The DID of the actor the relationships are relative to.</summary>
    [JsonPropertyName("actor")]
    public string? Actor { get; init; }

    /// <summary>The relationships between the actor and each of the requested accounts.</summary>
    [JsonPropertyName("relationships")]
    public required List<JsonElement> Relationships { get; init; }
}

/// <summary>
/// Response from getKnownFollowers.
/// </summary>
public sealed class GetKnownFollowersResponse
{
    /// <summary>The profile of the account whose known followers these are.</summary>
    [JsonPropertyName("subject")]
    public required ProfileView Subject { get; init; }

    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The follower profiles.</summary>
    [JsonPropertyName("followers")]
    public required List<ProfileView> Followers { get; init; }
}

/// <summary>
/// Response from getStarterPack.
/// </summary>
public sealed class GetStarterPackResponse
{
    /// <summary>The starter pack.</summary>
    [JsonPropertyName("starterPack")]
    public required StarterPackView StarterPack { get; init; }
}

/// <summary>
/// Response from getStarterPacks.
/// </summary>
public sealed class GetStarterPacksResponse
{
    /// <summary>The starter packs.</summary>
    [JsonPropertyName("starterPacks")]
    public required List<StarterPackViewBasic> StarterPacks { get; init; }
}

/// <summary>
/// Response from getActorStarterPacks.
/// </summary>
public sealed class GetActorStarterPacksResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The starter packs.</summary>
    [JsonPropertyName("starterPacks")]
    public required List<StarterPackViewBasic> StarterPacks { get; init; }
}

/// <summary>
/// Response from searchStarterPacks.
/// </summary>
public sealed class SearchStarterPacksResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The starter packs.</summary>
    [JsonPropertyName("starterPacks")]
    public required List<StarterPackViewBasic> StarterPacks { get; init; }
}

/// <summary>
/// Request body for muteThread / unmuteThread.
/// </summary>
public sealed class MuteThreadRequest
{
    /// <summary>The AT-URI of the root post of the thread to mute.</summary>
    [JsonPropertyName("root")]
    public required string Root { get; init; }
}
