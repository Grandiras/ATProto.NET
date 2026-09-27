using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.App.Bsky.Graph;

// ── Graph records (stored in repos) ──────────────────────────

/// <summary>A follow record. Collection: app.bsky.graph.follow</summary>
public sealed class FollowRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection records of this type are stored in (<c>app.bsky.graph.follow</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.graph.follow");

    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.follow</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

    /// <summary>The DID of the account being followed.</summary>
    [JsonPropertyName("subject")]
    public required Did Subject { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>The record through which the account came to follow, such as a starter pack.</summary>
    [JsonPropertyName("via")]
    public StrongRef? Via { get; init; }
}

/// <summary>A block record. Collection: app.bsky.graph.block</summary>
public sealed class BlockRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection records of this type are stored in (<c>app.bsky.graph.block</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.graph.block");

    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.block</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

    /// <summary>The DID of the account being blocked.</summary>
    [JsonPropertyName("subject")]
    public required Did Subject { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>A list record. Collection: app.bsky.graph.list</summary>
public sealed class ListRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection records of this type are stored in (<c>app.bsky.graph.list</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.graph.list");

    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.list</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

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

    /// <summary>Self-applied labels on the list.</summary>
    [JsonPropertyName("labels")]
    public SelfLabels? Labels { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>A list item record. Collection: app.bsky.graph.listitem</summary>
public sealed class ListItemRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection records of this type are stored in (<c>app.bsky.graph.listitem</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.graph.listitem");

    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.listitem</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

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

/// <summary>A list block record. Collection: app.bsky.graph.listblock</summary>
public sealed class ListBlockRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection records of this type are stored in (<c>app.bsky.graph.listblock</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.graph.listblock");

    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.listblock</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

    /// <summary>The AT-URI of the list being blocked.</summary>
    [JsonPropertyName("subject")]
    public required AtUri Subject { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// A request by its author to be left out of the public presentation of a reference list.
/// Collection: app.bsky.graph.referencelistoptout, record key a TID.
/// </summary>
/// <remarks>
/// The appview honors it only while the list's purpose is <see cref="ListPurpose.ReferenceList"/>,
/// and indexes at most one per author and list.
/// </remarks>
public sealed class ReferenceListOptOutRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection records of this type are stored in (<c>app.bsky.graph.referencelistoptout</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.graph.referencelistoptout");

    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.referencelistoptout</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

    /// <summary>The canonical, DID-based AT-URI of the list to be left out of.</summary>
    [JsonPropertyName("subject")]
    public required AtUri Subject { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// A verification of one account by another. Collection: app.bsky.graph.verification, record key
/// a TID.
/// </summary>
/// <remarks>
/// An app counts a verification only when it trusts its issuer, and only while the subject's
/// current handle and display name still match the ones recorded here.
/// </remarks>
public sealed class VerificationRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection records of this type are stored in (<c>app.bsky.graph.verification</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.graph.verification");

    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.verification</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

    /// <summary>The DID of the verified account.</summary>
    [JsonPropertyName("subject")]
    public required Did Subject { get; init; }

    /// <summary>The verified account's handle when it was verified.</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The verified account's display name when it was verified.</summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>When the verification was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

// ── Well-known list purposes ─────────────────────────────────

/// <summary>Well-known list purpose URIs.</summary>
public static class ListPurpose
{
    /// <summary>A moderation list (muting/blocking).</summary>
    public const string ModList = "app.bsky.graph.defs#modlist";

    /// <summary>A curation list (feed curation).</summary>
    public const string CurateList = "app.bsky.graph.defs#curatelist";

    /// <summary>A reference list (general-purpose list).</summary>
    public const string ReferenceList = "app.bsky.graph.defs#referencelist";
}

// ── View types ───────────────────────────────────────────────

/// <summary>A list view. Also a variant of <see cref="EmbeddedRecordView"/>, for a list embedded in a post.</summary>
public sealed class ListView : EmbeddedRecordView
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

/// <summary>Viewer state for a list.</summary>
public sealed class ListViewerState : LexObject
{
    /// <summary>Whether the viewer has muted this list.</summary>
    [JsonPropertyName("muted")]
    public bool? Muted { get; init; }

    /// <summary>The AT-URI of the viewer's list-block record, if the viewer blocks this list.</summary>
    [JsonPropertyName("blocked")]
    public AtUri? Blocked { get; init; }

    /// <summary>The AT-URI of the viewer's opt-out record, if the viewer opted out of this reference list.</summary>
    [JsonPropertyName("referenceListOptOut")]
    public AtUri? ReferenceListOptOut { get; init; }
}

/// <summary>A basic list view (less detail).</summary>
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

/// <summary>A list item view (a member of a list).</summary>
public sealed class ListItemView : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The profile of the listed account.</summary>
    [JsonPropertyName("subject")]
    public required ProfileView Subject { get; init; }

    /// <summary>
    /// <see langword="true"/> when the listed account opted out of the reference list; absent
    /// otherwise.
    /// </summary>
    [JsonPropertyName("subjectOptedOut")]
    public bool? SubjectOptedOut { get; init; }
}

// ── API responses ────────────────────────────────────────────

/// <summary>Response from getFollowers.</summary>
public sealed record GetFollowersResponse : CursorPage<ProfileView>
{
    /// <summary>The profile of the account whose followers these are.</summary>
    [JsonPropertyName("subject")]
    public required ProfileView Subject { get; init; }

    /// <summary>The follower profiles.</summary>
    [JsonPropertyName("followers")]
    public required IReadOnlyList<ProfileView> Followers { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<ProfileView> Items => Followers;
}

/// <summary>Response from getFollows.</summary>
public sealed record GetFollowsResponse : CursorPage<ProfileView>
{
    /// <summary>The profile of the account whose follows these are.</summary>
    [JsonPropertyName("subject")]
    public required ProfileView Subject { get; init; }

    /// <summary>The profiles this actor follows.</summary>
    [JsonPropertyName("follows")]
    public required IReadOnlyList<ProfileView> Follows { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<ProfileView> Items => Follows;
}

/// <summary>Response from getBlocks.</summary>
public sealed record GetBlocksResponse : CursorPage<ProfileView>
{
    /// <summary>The blocked profiles.</summary>
    [JsonPropertyName("blocks")]
    public required IReadOnlyList<ProfileView> Blocks { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<ProfileView> Items => Blocks;
}

/// <summary>Response from getLists.</summary>
public sealed record GetListsResponse : CursorPage<ListView>
{
    /// <summary>The lists.</summary>
    [JsonPropertyName("lists")]
    public required IReadOnlyList<ListView> Lists { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<ListView> Items => Lists;
}

/// <summary>Response from getList.</summary>
public sealed record GetListResponse : CursorPage<ListItemView>
{
    /// <summary>The list.</summary>
    [JsonPropertyName("list")]
    public required ListView List { get; init; }

    /// <summary>The members of the list.</summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<ListItemView> Members { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<ListItemView> Items => Members;
}

/// <summary>Response from getMutes.</summary>
public sealed record GetMutesResponse : CursorPage<ProfileView>
{
    /// <summary>The muted profiles.</summary>
    [JsonPropertyName("mutes")]
    public required IReadOnlyList<ProfileView> Mutes { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<ProfileView> Items => Mutes;
}

/// <summary>Response from getListMutes.</summary>
public sealed record GetListMutesResponse : CursorPage<ListView>
{
    /// <summary>The lists.</summary>
    [JsonPropertyName("lists")]
    public required IReadOnlyList<ListView> Lists { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<ListView> Items => Lists;
}

/// <summary>Response from getListBlocks.</summary>
public sealed record GetListBlocksResponse : CursorPage<ListView>
{
    /// <summary>The lists.</summary>
    [JsonPropertyName("lists")]
    public required IReadOnlyList<ListView> Lists { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<ListView> Items => Lists;
}

/// <summary>Response from getSuggestedFollowsByActor.</summary>
public sealed class GetSuggestedFollowsByActorResponse
{
    /// <summary>The suggested profiles.</summary>
    [JsonPropertyName("suggestions")]
    public required IReadOnlyList<ProfileView> Suggestions { get; init; }

    /// <summary>The recommendation's identifier (a snowflake), for recommendation events.</summary>
    [JsonPropertyName("recIdStr")]
    public string? RecIdStr { get; init; }

    /// <summary>Whether these were generic fallback suggestions. No longer used.</summary>
    [JsonPropertyName("isFallback")]
    [Obsolete("Deprecated upstream: the appview no longer uses this field.")]
    public bool? IsFallback { get; init; }
}

/// <summary>Request body for muteActor / unmuteActor.</summary>
internal sealed record MuteActorRequest(
    [property: JsonPropertyName("actor")] AtIdentifier Actor,
    [property: JsonPropertyName("onlyReposts")] bool? OnlyReposts = null,
    [property: JsonPropertyName("onlyQuoteposts")] bool? OnlyQuoteposts = null);

/// <summary>Request body for muteActorList / unmuteActorList.</summary>
internal sealed record MuteActorListRequest([property: JsonPropertyName("list")] AtUri List);

// ── Starter pack records & views ─────────────────────────────

/// <summary>A starter pack record. Collection: app.bsky.graph.starterpack</summary>
public sealed class StarterPackRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection records of this type are stored in (<c>app.bsky.graph.starterpack</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.graph.starterpack");

    /// <summary>The Lexicon type discriminator (<c>app.bsky.graph.starterpack</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

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

/// <summary>A feed item reference in a starter pack.</summary>
public sealed class StarterPackFeedItem : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }
}

/// <summary>
/// Basic view of a starter pack. Also a variant of <see cref="EmbeddedRecordView"/>, for a starter
/// pack embedded in a post.
/// </summary>
public sealed class StarterPackViewBasic : EmbeddedRecordView
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

    /// <summary>The number of accounts that joined via this starter pack in the last week.</summary>
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

/// <summary>Full view of a starter pack.</summary>
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
    public IReadOnlyList<GeneratorView>? Feeds { get; init; }

    /// <summary>The number of accounts that joined via this starter pack in the last week.</summary>
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

// ── Relationship types ───────────────────────────────────────

/// <summary>
/// One entry of a <c>getRelationships</c> response: a <see cref="Relationship"/>, or a
/// <see cref="NotFoundActor"/> for an account that could not be found. An entry this SDK does not
/// model reads as <see cref="UnknownRelationshipEntry"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownRelationshipEntry))]
[JsonDerivedType(typeof(Relationship), "app.bsky.graph.defs#relationship")]
[JsonDerivedType(typeof(NotFoundActor), "app.bsky.graph.defs#notFoundActor")]
public abstract class RelationshipEntry : LexObject;

/// <summary>
/// A relationship entry whose <c>$type</c> this SDK version does not model. It keeps the raw
/// object and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
/// <param name="type">The object's <c>$type</c>.</param>
/// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
public sealed class UnknownRelationshipEntry(string type, JsonElement raw) : RelationshipEntry, IUnknownUnionVariant
{
    /// <inheritdoc/>
    public string Type { get; } = UnknownUnionVariant.RequireType(type);

    /// <inheritdoc/>
    public JsonElement Raw { get; } = UnknownUnionVariant.RequireObject(raw);
}

/// <summary>The relationship between the queried actor and another account.</summary>
public sealed class Relationship : RelationshipEntry
{
    /// <summary>The DID (decentralized identifier) of the other account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The AT-URI of the actor's follow record, if the actor follows the other account.</summary>
    [JsonPropertyName("following")]
    public AtUri? Following { get; init; }

    /// <summary>The AT-URI of the other account's follow record, if it follows the actor.</summary>
    [JsonPropertyName("followedBy")]
    public AtUri? FollowedBy { get; init; }

    /// <summary>The AT-URI of the actor's block record, if the actor blocks the other account.</summary>
    [JsonPropertyName("blocking")]
    public AtUri? Blocking { get; init; }

    /// <summary>The AT-URI of the other account's block record, if it blocks the actor.</summary>
    [JsonPropertyName("blockedBy")]
    public AtUri? BlockedBy { get; init; }

    /// <summary>
    /// The AT-URI of the actor's list-block record, if the actor blocks the other account through
    /// a block list.
    /// </summary>
    [JsonPropertyName("blockingByList")]
    public AtUri? BlockingByList { get; init; }

    /// <summary>
    /// The AT-URI of the other account's list-block record, if it blocks the actor through a
    /// block list.
    /// </summary>
    [JsonPropertyName("blockedByList")]
    public AtUri? BlockedByList { get; init; }
}

/// <summary>A "not found" actor placeholder in relationship responses.</summary>
public sealed class NotFoundActor : RelationshipEntry
{
    /// <summary>The DID or handle that could not be resolved.</summary>
    [JsonPropertyName("actor")]
    public required AtIdentifier Actor { get; init; }

    /// <summary>Always <see langword="true"/>; marks the referenced subject as unavailable.</summary>
    [JsonPropertyName("notFound")]
    public required bool NotFound { get; init; }
}

// ── Additional API responses ─────────────────────────────────

/// <summary>Response from getRelationships.</summary>
public sealed class GetRelationshipsResponse
{
    /// <summary>The DID of the actor the relationships are relative to.</summary>
    [JsonPropertyName("actor")]
    public Did? Actor { get; init; }

    /// <summary>The relationships between the actor and each of the requested accounts.</summary>
    [JsonPropertyName("relationships")]
    public required IReadOnlyList<RelationshipEntry> Relationships { get; init; }
}

/// <summary>Response from getKnownFollowers.</summary>
public sealed record GetKnownFollowersResponse : CursorPage<ProfileView>
{
    /// <summary>The profile of the account whose known followers these are.</summary>
    [JsonPropertyName("subject")]
    public required ProfileView Subject { get; init; }

    /// <summary>The follower profiles.</summary>
    [JsonPropertyName("followers")]
    public required IReadOnlyList<ProfileView> Followers { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<ProfileView> Items => Followers;
}

/// <summary>Response from getStarterPack.</summary>
public sealed class GetStarterPackResponse
{
    /// <summary>The starter pack.</summary>
    [JsonPropertyName("starterPack")]
    public required StarterPackView StarterPack { get; init; }
}

/// <summary>Response from getStarterPacks.</summary>
public sealed class GetStarterPacksResponse
{
    /// <summary>The starter packs.</summary>
    [JsonPropertyName("starterPacks")]
    public required IReadOnlyList<StarterPackViewBasic> StarterPacks { get; init; }
}

/// <summary>Response from getActorStarterPacks.</summary>
public sealed record GetActorStarterPacksResponse : CursorPage<StarterPackViewBasic>
{
    /// <summary>The starter packs.</summary>
    [JsonPropertyName("starterPacks")]
    public required IReadOnlyList<StarterPackViewBasic> StarterPacks { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<StarterPackViewBasic> Items => StarterPacks;
}

/// <summary>Response from searchStarterPacks.</summary>
public sealed record SearchStarterPacksResponse : CursorPage<StarterPackViewBasic>
{
    /// <summary>The starter packs.</summary>
    [JsonPropertyName("starterPacks")]
    public required IReadOnlyList<StarterPackViewBasic> StarterPacks { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<StarterPackViewBasic> Items => StarterPacks;
}

/// <summary>Response from searchStarterPacksV2, which returns full starter pack views.</summary>
public sealed record SearchStarterPacksV2Response : CursorPage<StarterPackView>
{
    /// <summary>An estimate of the number of matching starter packs, possibly rounded or truncated.</summary>
    [JsonPropertyName("hitsTotal")]
    public int? HitsTotal { get; init; }

    /// <summary>The starter packs.</summary>
    [JsonPropertyName("starterPacks")]
    public required IReadOnlyList<StarterPackView> StarterPacks { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<StarterPackView> Items => StarterPacks;
}

/// <summary>
/// One of the viewer's lists, and whether an actor is on it
/// (<c>app.bsky.graph.getListsWithMembership#listWithMembership</c>).
/// </summary>
public sealed class ListWithMembership : LexObject
{
    /// <summary>The list.</summary>
    [JsonPropertyName("list")]
    public required ListView List { get; init; }

    /// <summary>The actor's entry on the list; <see langword="null"/> when the actor is not on it.</summary>
    [JsonPropertyName("listItem")]
    public ListItemView? ListItem { get; init; }
}

/// <summary>Response from getListsWithMembership.</summary>
public sealed record GetListsWithMembershipResponse : CursorPage<ListWithMembership>
{
    /// <summary>The viewer's lists, each with the actor's membership.</summary>
    [JsonPropertyName("listsWithMembership")]
    public required IReadOnlyList<ListWithMembership> ListsWithMembership { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<ListWithMembership> Items => ListsWithMembership;
}

/// <summary>
/// One of the viewer's starter packs, and whether an actor is in it
/// (<c>app.bsky.graph.getStarterPacksWithMembership#starterPackWithMembership</c>).
/// </summary>
public sealed class StarterPackWithMembership : LexObject
{
    /// <summary>The starter pack.</summary>
    [JsonPropertyName("starterPack")]
    public required StarterPackView StarterPack { get; init; }

    /// <summary>
    /// The actor's entry on the starter pack's list; <see langword="null"/> when the actor is not
    /// in it.
    /// </summary>
    [JsonPropertyName("listItem")]
    public ListItemView? ListItem { get; init; }
}

/// <summary>Response from getStarterPacksWithMembership.</summary>
public sealed record GetStarterPacksWithMembershipResponse : CursorPage<StarterPackWithMembership>
{
    /// <summary>The viewer's starter packs, each with the actor's membership.</summary>
    [JsonPropertyName("starterPacksWithMembership")]
    public required IReadOnlyList<StarterPackWithMembership> StarterPacksWithMembership { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<StarterPackWithMembership> Items =>
        StarterPacksWithMembership;
}

/// <summary>Request body for muteThread / unmuteThread.</summary>
internal sealed record MuteThreadRequest([property: JsonPropertyName("root")] AtUri Root);
