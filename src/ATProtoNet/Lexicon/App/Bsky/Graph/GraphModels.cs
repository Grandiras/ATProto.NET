using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
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
public sealed class FollowRecord : LexObject
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.follow</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.follow";

    /// <summary>The DID of the account being followed.</summary>
    [JsonPropertyName("subject")]
    public required Did Subject { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// A block record. Collection: app.bsky.graph.block
/// </summary>
public sealed class BlockRecord : LexObject
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.block</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.block";

    /// <summary>The DID of the account being blocked.</summary>
    [JsonPropertyName("subject")]
    public required Did Subject { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// A list record. Collection: app.bsky.graph.list
/// </summary>
public sealed class ListRecord : LexObject
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
    public IReadOnlyList<Facet>? DescriptionFacets { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public BlobRef? Avatar { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public JsonElement? Labels { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// A list item record. Collection: app.bsky.graph.listitem
/// </summary>
public sealed class ListItemRecord : LexObject
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.listitem</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.listitem";

    /// <summary>The DID of the account included in the list.</summary>
    [JsonPropertyName("subject")]
    public required Did Subject { get; init; }

    /// <summary>The AT-URI of the list this membership belongs to.</summary>
    [JsonPropertyName("list")]
    public required AtUri List { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// A list block record. Collection: app.bsky.graph.listblock
/// </summary>
public sealed class ListBlockRecord : LexObject
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.listblock</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.graph.listblock";

    /// <summary>The AT-URI of the list being blocked.</summary>
    [JsonPropertyName("subject")]
    public required AtUri Subject { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
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
public sealed class ListView : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

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
    public IReadOnlyList<Facet>? DescriptionFacets { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    /// <summary>The number of items in the list.</summary>
    [JsonPropertyName("listItemCount")]
    public int? ListItemCount { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public ListViewerState? Viewer { get; init; }

    /// <summary>Timestamp at which the app view indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public required AtDatetime IndexedAt { get; init; }
}

/// <summary>
/// Viewer state for a list.
/// </summary>
public sealed class ListViewerState : LexObject
{
    /// <summary>Whether the viewer has muted this list.</summary>
    [JsonPropertyName("muted")]
    public bool? Muted { get; init; }

    /// <summary>The AT-URI of the viewer's list-block record, if the viewer blocks this list.</summary>
    [JsonPropertyName("blocked")]
    public AtUri? Blocked { get; init; }
}

/// <summary>
/// A basic list view (less detail).
/// </summary>
public sealed class ListViewBasic : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

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
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public ListViewerState? Viewer { get; init; }

    /// <summary>Timestamp at which the app view indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public AtDatetime? IndexedAt { get; init; }
}

/// <summary>
/// A list item view (a member of a list).
/// </summary>
public sealed class ListItemView : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

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
public sealed class GetFollowersResponse : ICursorPage<ProfileView>
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
    public required IReadOnlyList<ProfileView> Followers { get; init; }

    IReadOnlyList<ProfileView> ICursorPage<ProfileView>.Items => Followers;
}

/// <summary>
/// Response from getFollows.
/// </summary>
public sealed class GetFollowsResponse : ICursorPage<ProfileView>
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
    public required IReadOnlyList<ProfileView> Follows { get; init; }

    IReadOnlyList<ProfileView> ICursorPage<ProfileView>.Items => Follows;
}

/// <summary>
/// Response from getBlocks.
/// </summary>
public sealed class GetBlocksResponse : ICursorPage<ProfileView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The blocked profiles.</summary>
    [JsonPropertyName("blocks")]
    public required IReadOnlyList<ProfileView> Blocks { get; init; }

    IReadOnlyList<ProfileView> ICursorPage<ProfileView>.Items => Blocks;
}

/// <summary>
/// Response from getLists.
/// </summary>
public sealed class GetListsResponse : ICursorPage<ListView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The lists.</summary>
    [JsonPropertyName("lists")]
    public required IReadOnlyList<ListView> Lists { get; init; }

    IReadOnlyList<ListView> ICursorPage<ListView>.Items => Lists;
}

/// <summary>
/// Response from getList.
/// </summary>
public sealed class GetListResponse : ICursorPage<ListItemView>
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
    public required IReadOnlyList<ListItemView> Items { get; init; }

    IReadOnlyList<ListItemView> ICursorPage<ListItemView>.Items => Items;
}

/// <summary>
/// Response from getMutes.
/// </summary>
public sealed class GetMutesResponse : ICursorPage<ProfileView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The muted profiles.</summary>
    [JsonPropertyName("mutes")]
    public required IReadOnlyList<ProfileView> Mutes { get; init; }

    IReadOnlyList<ProfileView> ICursorPage<ProfileView>.Items => Mutes;
}

/// <summary>
/// Response from getListMutes.
/// </summary>
public sealed class GetListMutesResponse : ICursorPage<ListView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The lists.</summary>
    [JsonPropertyName("lists")]
    public required IReadOnlyList<ListView> Lists { get; init; }

    IReadOnlyList<ListView> ICursorPage<ListView>.Items => Lists;
}

/// <summary>
/// Response from getListBlocks.
/// </summary>
public sealed class GetListBlocksResponse : ICursorPage<ListView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The lists.</summary>
    [JsonPropertyName("lists")]
    public required IReadOnlyList<ListView> Lists { get; init; }

    IReadOnlyList<ListView> ICursorPage<ListView>.Items => Lists;
}

/// <summary>
/// Response from getSuggestedFollowsByActor.
/// </summary>
public sealed class GetSuggestedFollowsByActorResponse
{
    /// <summary>The suggested profiles.</summary>
    [JsonPropertyName("suggestions")]
    public required IReadOnlyList<ProfileView> Suggestions { get; init; }

    /// <summary>
    /// Whether these are generic fallback suggestions rather than personalised ones.
    /// </summary>
    [JsonPropertyName("isFallback")]
    public bool? IsFallback { get; init; }
}

/// <summary>
/// Request body for muteActor / unmuteActor.
/// </summary>
internal sealed class MuteActorRequest
{
    /// <summary>The DID or handle of the actor to mute.</summary>
    [JsonPropertyName("actor")]
    public required AtIdentifier Actor { get; init; }
}

/// <summary>
/// Request body for muteActorList / unmuteActorList.
/// </summary>
internal sealed class MuteActorListRequest
{
    /// <summary>The AT-URI of the list to mute.</summary>
    [JsonPropertyName("list")]
    public required AtUri List { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Starter pack records & views
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A starter pack record. Collection: app.bsky.graph.starterpack
/// </summary>
public sealed class StarterPackRecord : LexObject
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
    public IReadOnlyList<Facet>? DescriptionFacets { get; init; }

    /// <summary>The AT-URI of the list of accounts in the pack.</summary>
    [JsonPropertyName("list")]
    public required AtUri List { get; init; }

    /// <summary>The AT-URIs of feeds included in the pack.</summary>
    [JsonPropertyName("feeds")]
    public IReadOnlyList<StarterPackFeedItem>? Feeds { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// A feed item reference in a starter pack.
/// </summary>
public sealed class StarterPackFeedItem : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }
}

/// <summary>
/// Basic view of a starter pack.
/// </summary>
public sealed class StarterPackViewBasic : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

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
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>Timestamp at which the app view indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public required AtDatetime IndexedAt { get; init; }
}

/// <summary>
/// Full view of a starter pack.
/// </summary>
public sealed class StarterPackView : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

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
    public IReadOnlyList<ListItemView>? ListItemsSample { get; init; }

    /// <summary>The feed generators included in the pack.</summary>
    [JsonPropertyName("feeds")]
    public IReadOnlyList<JsonElement>? Feeds { get; init; }

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
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>Timestamp at which the app view indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public required AtDatetime IndexedAt { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Relationship types
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A relationship between two actors.
/// </summary>
public sealed class Relationship : LexObject
{
    /// <summary>The Lexicon type discriminator for this object.</summary>
    [JsonPropertyName("$type")]
    public string? Type { get; init; }

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

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
}

/// <summary>
/// A "not found" actor placeholder in relationship responses.
/// </summary>
public sealed class NotFoundActor : LexObject
{
    /// <summary>The Lexicon type discriminator for this object.</summary>
    [JsonPropertyName("$type")]
    public string? Type { get; init; }

    /// <summary>The DID or handle that could not be resolved.</summary>
    [JsonPropertyName("actor")]
    public required AtIdentifier Actor { get; init; }

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
    public Did? Actor { get; init; }

    /// <summary>The relationships between the actor and each of the requested accounts.</summary>
    [JsonPropertyName("relationships")]
    public required IReadOnlyList<JsonElement> Relationships { get; init; }
}

/// <summary>
/// Response from getKnownFollowers.
/// </summary>
public sealed class GetKnownFollowersResponse : ICursorPage<ProfileView>
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
    public required IReadOnlyList<ProfileView> Followers { get; init; }

    IReadOnlyList<ProfileView> ICursorPage<ProfileView>.Items => Followers;
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
    public required IReadOnlyList<StarterPackViewBasic> StarterPacks { get; init; }
}

/// <summary>
/// Response from getActorStarterPacks.
/// </summary>
public sealed class GetActorStarterPacksResponse : ICursorPage<StarterPackViewBasic>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The starter packs.</summary>
    [JsonPropertyName("starterPacks")]
    public required IReadOnlyList<StarterPackViewBasic> StarterPacks { get; init; }

    IReadOnlyList<StarterPackViewBasic> ICursorPage<StarterPackViewBasic>.Items => StarterPacks;
}

/// <summary>
/// Response from searchStarterPacks.
/// </summary>
public sealed class SearchStarterPacksResponse : ICursorPage<StarterPackViewBasic>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The starter packs.</summary>
    [JsonPropertyName("starterPacks")]
    public required IReadOnlyList<StarterPackViewBasic> StarterPacks { get; init; }

    IReadOnlyList<StarterPackViewBasic> ICursorPage<StarterPackViewBasic>.Items => StarterPacks;
}

/// <summary>
/// Request body for muteThread / unmuteThread.
/// </summary>
internal sealed class MuteThreadRequest
{
    /// <summary>The AT-URI of the root post of the thread to mute.</summary>
    [JsonPropertyName("root")]
    public required AtUri Root { get; init; }
}
