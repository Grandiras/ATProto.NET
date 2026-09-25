using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Graph;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.App.Bsky.Feed;

// ──────────────────────────────────────────────────────────────
//  Post record (the actual repo record)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A Bluesky post record stored in the repository.
/// Collection: app.bsky.feed.post
/// </summary>
public sealed class PostRecord : LexObject
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.feed.post</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.post";

    /// <summary>The post text content (max 300 graphemes / ~3000 bytes).</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>Rich-text facets (mentions, links, hashtags).</summary>
    [JsonPropertyName("facets")]
    public IReadOnlyList<Facet>? Facets { get; init; }

    /// <summary>Reply reference (parent and root post).</summary>
    [JsonPropertyName("reply")]
    public ReplyRef? Reply { get; init; }

    /// <summary>Embedded content (images, links, quotes, video).</summary>
    [JsonPropertyName("embed")]
    public EmbedBase? Embed { get; init; }

    /// <summary>Language tags for the post (BCP-47).</summary>
    [JsonPropertyName("langs")]
    public IReadOnlyList<string>? Langs { get; init; }

    /// <summary>Self-applied labels for content warnings.</summary>
    [JsonPropertyName("labels")]
    public SelfLabels? Labels { get; init; }

    /// <summary>Additional tags (up to 8, max 640 chars each).</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Timestamp of post creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// Reply reference linking to parent and root posts.
/// </summary>
public sealed class ReplyRef : LexObject
{
    /// <summary>The root post of the thread.</summary>
    [JsonPropertyName("root")]
    public required StrongRef Root { get; init; }

    /// <summary>The direct parent post.</summary>
    [JsonPropertyName("parent")]
    public required StrongRef Parent { get; init; }
}

/// <summary>
/// Self-applied content labels for a post.
/// </summary>
public sealed class SelfLabels : LexObject
{
    /// <summary>
    /// The Lexicon type discriminator (<c>com.atproto.label.defs#selfLabels</c>).
    /// </summary>
    [JsonPropertyName("$type")]
    public string Type => "com.atproto.label.defs#selfLabels";

    /// <summary>The self-applied labels.</summary>
    [JsonPropertyName("values")]
    public required IReadOnlyList<SelfLabelValue> Values { get; init; }
}

/// <summary>
/// A single self-label value.
/// </summary>
public sealed class SelfLabelValue : LexObject
{
    /// <summary>The label value.</summary>
    [JsonPropertyName("val")]
    public required string Val { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Like record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A like record. Collection: app.bsky.feed.like
/// </summary>
public sealed class LikeRecord : LexObject
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.feed.like</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.like";

    /// <summary>A strong reference to the post being liked.</summary>
    [JsonPropertyName("subject")]
    public required StrongRef Subject { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>
    /// The repost through which the account came to the post, when it liked a repost.
    /// </summary>
    [JsonPropertyName("via")]
    public StrongRef? Via { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Repost record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A repost record. Collection: app.bsky.feed.repost
/// </summary>
public sealed class RepostRecord : LexObject
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.feed.repost</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.repost";

    /// <summary>A strong reference to the post being reposted.</summary>
    [JsonPropertyName("subject")]
    public required StrongRef Subject { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>
    /// The repost through which the account came to the post, when it reposted a repost.
    /// </summary>
    [JsonPropertyName("via")]
    public StrongRef? Via { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Threadgate record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A threadgate record that controls who can reply to a thread.
/// Collection: app.bsky.feed.threadgate
/// </summary>
public sealed class ThreadgateRecord : LexObject
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.feed.threadgate</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.threadgate";

    /// <summary>The AT-URI of the post this threadgate applies to.</summary>
    [JsonPropertyName("post")]
    public required AtUri Post { get; init; }

    /// <summary>
    /// The rules controlling who may reply. An empty list disables replies entirely; <see
    /// langword="null"/> allows everyone.
    /// </summary>
    [JsonPropertyName("allow")]
    public IReadOnlyList<ThreadgateRule>? Allow { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>The AT-URIs of replies hidden by the thread author.</summary>
    [JsonPropertyName("hiddenReplies")]
    public IReadOnlyList<AtUri>? HiddenReplies { get; init; }
}

/// <summary>
/// A postgate record that controls embedding/quoting of a post.
/// Collection: app.bsky.feed.postgate
/// </summary>
public sealed class PostgateRecord : LexObject
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.feed.postgate</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.postgate";

    /// <summary>The AT-URI of the post this postgate applies to.</summary>
    [JsonPropertyName("post")]
    public required AtUri Post { get; init; }

    /// <summary>The AT-URIs of quote posts the author has detached.</summary>
    [JsonPropertyName("detachedEmbeddingUris")]
    public IReadOnlyList<AtUri>? DetachedEmbeddingUris { get; init; }

    /// <summary>The rules controlling who may quote this post.</summary>
    [JsonPropertyName("embeddingRules")]
    public IReadOnlyList<PostgateEmbeddingRule>? EmbeddingRules { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// A rule that allows some accounts to reply (the open union behind
/// <see cref="ThreadgateRecord.Allow"/>). A rule this SDK does not model reads as
/// <see cref="UnknownThreadgateRule"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownThreadgateRule))]
[JsonDerivedType(typeof(ThreadgateMentionRule), "app.bsky.feed.threadgate#mentionRule")]
[JsonDerivedType(typeof(ThreadgateFollowerRule), "app.bsky.feed.threadgate#followerRule")]
[JsonDerivedType(typeof(ThreadgateFollowingRule), "app.bsky.feed.threadgate#followingRule")]
[JsonDerivedType(typeof(ThreadgateListRule), "app.bsky.feed.threadgate#listRule")]
public abstract class ThreadgateRule : LexObject;

/// <summary>Allows replies from accounts mentioned in the post.</summary>
public sealed class ThreadgateMentionRule : ThreadgateRule;

/// <summary>Allows replies from accounts that follow the post's author.</summary>
public sealed class ThreadgateFollowerRule : ThreadgateRule;

/// <summary>Allows replies from accounts the post's author follows.</summary>
public sealed class ThreadgateFollowingRule : ThreadgateRule;

/// <summary>Allows replies from the members of a list.</summary>
public sealed class ThreadgateListRule : ThreadgateRule
{
    /// <summary>The AT-URI of the list.</summary>
    [JsonPropertyName("list")]
    public required AtUri List { get; init; }
}

/// <summary>
/// A threadgate rule whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownThreadgateRule : ThreadgateRule, IUnknownUnionVariant
{
    /// <summary>Creates an unknown threadgate rule from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownThreadgateRule(string type, JsonElement raw)
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

/// <summary>
/// A rule about who may quote a post (the open union behind
/// <see cref="PostgateRecord.EmbeddingRules"/>). A rule this SDK does not model reads as
/// <see cref="UnknownPostgateEmbeddingRule"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownPostgateEmbeddingRule))]
[JsonDerivedType(typeof(PostgateDisableRule), "app.bsky.feed.postgate#disableRule")]
public abstract class PostgateEmbeddingRule : LexObject;

/// <summary>Disables quoting the post.</summary>
public sealed class PostgateDisableRule : PostgateEmbeddingRule;

/// <summary>
/// A postgate rule whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownPostgateEmbeddingRule : PostgateEmbeddingRule, IUnknownUnionVariant
{
    /// <summary>Creates an unknown postgate rule from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownPostgateEmbeddingRule(string type, JsonElement raw)
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

// ──────────────────────────────────────────────────────────────
//  Feed generator record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A feed generator record. Collection: app.bsky.feed.generator
/// </summary>
public sealed class GeneratorRecord : LexObject
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.feed.generator</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.generator";

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Rich-text facets (mentions, links, tags) applied to the description.</summary>
    [JsonPropertyName("descriptionFacets")]
    public IReadOnlyList<Facet>? DescriptionFacets { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public BlobRef? Avatar { get; init; }

    /// <summary>Whether the feed generator accepts interaction events.</summary>
    [JsonPropertyName("acceptsInteractions")]
    public bool? AcceptsInteractions { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public SelfLabels? Labels { get; init; }

    /// <summary>What the feed shows, which apps may use to present it (see <see cref="FeedContentMode"/>).</summary>
    [JsonPropertyName("contentMode")]
    public string? ContentMode { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Post view types (returned from API)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A full post view as returned by feed endpoints.
/// </summary>
public sealed class PostView : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>The account that authored the post.</summary>
    [JsonPropertyName("author")]
    public required ProfileViewBasic Author { get; init; }

    /// <summary>The post record.</summary>
    [JsonPropertyName("record")]
    public required JsonElement Record { get; init; }

    /// <summary>Embedded content attached to the post.</summary>
    [JsonPropertyName("embed")]
    public EmbedView? Embed { get; init; }

    /// <summary>The number of bookmarks.</summary>
    [JsonPropertyName("bookmarkCount")]
    public int? BookmarkCount { get; init; }

    /// <summary>The number of replies to the post.</summary>
    [JsonPropertyName("replyCount")]
    public int? ReplyCount { get; init; }

    /// <summary>The number of reposts.</summary>
    [JsonPropertyName("repostCount")]
    public int? RepostCount { get; init; }

    /// <summary>The number of likes.</summary>
    [JsonPropertyName("likeCount")]
    public int? LikeCount { get; init; }

    /// <summary>The number of quote posts.</summary>
    [JsonPropertyName("quoteCount")]
    public int? QuoteCount { get; init; }

    /// <summary>Timestamp at which the app view indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public required AtDatetime IndexedAt { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public PostViewerState? Viewer { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>The threadgate controlling who may reply.</summary>
    [JsonPropertyName("threadgate")]
    public ThreadgateView? Threadgate { get; init; }

    /// <summary>Debug information the appview attaches for internal development; its shape is not specified.</summary>
    [JsonPropertyName("debug")]
    public JsonElement? Debug { get; init; }
}

/// <summary>
/// A post's threadgate as the appview renders it (<c>app.bsky.feed.defs#threadgateView</c>).
/// </summary>
public sealed class ThreadgateView : LexObject
{
    /// <summary>The AT-URI of the threadgate record.</summary>
    [JsonPropertyName("uri")]
    public AtUri? Uri { get; init; }

    /// <summary>The CID of the threadgate record.</summary>
    [JsonPropertyName("cid")]
    public Cid? Cid { get; init; }

    /// <summary>The threadgate record (an <c>app.bsky.feed.threadgate</c>).</summary>
    [JsonPropertyName("record")]
    public JsonElement? Record { get; init; }

    /// <summary>The lists the threadgate's list rules name.</summary>
    [JsonPropertyName("lists")]
    public IReadOnlyList<ListViewBasic>? Lists { get; init; }
}

/// <summary>
/// Viewer state for a post (like/repost status).
/// </summary>
public sealed class PostViewerState : LexObject
{
    /// <summary>AT-URI of the viewer's like record, if liked.</summary>
    [JsonPropertyName("like")]
    public AtUri? Like { get; init; }

    /// <summary>AT-URI of the viewer's repost record, if reposted.</summary>
    [JsonPropertyName("repost")]
    public AtUri? Repost { get; init; }

    /// <summary>Whether the viewer has muted this thread.</summary>
    [JsonPropertyName("threadMuted")]
    public bool? ThreadMuted { get; init; }

    /// <summary>Whether the viewer is prevented from replying by a threadgate.</summary>
    [JsonPropertyName("replyDisabled")]
    public bool? ReplyDisabled { get; init; }

    /// <summary>Whether the viewer is prevented from quoting this post by a postgate.</summary>
    [JsonPropertyName("embeddingDisabled")]
    public bool? EmbeddingDisabled { get; init; }

    /// <summary>Whether the viewer has bookmarked the post.</summary>
    [JsonPropertyName("bookmarked")]
    public bool? Bookmarked { get; init; }

    /// <summary>Whether the post is pinned to the author's profile.</summary>
    [JsonPropertyName("pinned")]
    public bool? Pinned { get; init; }

    /// <summary>A sample of the accounts the viewer follows that liked the post.</summary>
    [JsonPropertyName("knownLikers")]
    public KnownLikers? KnownLikers { get; init; }
}

/// <summary>
/// Accounts the viewer follows that liked a post (<c>app.bsky.feed.defs#knownLikers</c>).
/// </summary>
public sealed class KnownLikers : LexObject
{
    /// <summary>How many accounts the viewer follows liked the post.</summary>
    [JsonPropertyName("count")]
    public required int Count { get; init; }

    /// <summary>Up to five of them.</summary>
    [JsonPropertyName("actors")]
    public required IReadOnlyList<ProfileViewBasic> Actors { get; init; }
}

/// <summary>
/// A feed view item wrapping a post with optional reason (repost).
/// </summary>
public sealed class FeedViewPost : LexObject
{
    /// <summary>The post.</summary>
    [JsonPropertyName("post")]
    public required PostView Post { get; init; }

    /// <summary>Reply information for the post, if it is a reply.</summary>
    [JsonPropertyName("reply")]
    public FeedReplyRef? Reply { get; init; }

    /// <summary>
    /// Why the post is in the feed when it is not there on its own: a <see cref="ReasonRepost"/>
    /// or a <see cref="ReasonPin"/>.
    /// </summary>
    [JsonPropertyName("reason")]
    public FeedReason? Reason { get; init; }

    /// <summary>
    /// An opaque context string the feed generator may pass back in interaction events.
    /// </summary>
    [JsonPropertyName("feedContext")]
    public string? FeedContext { get; init; }

    /// <summary>
    /// The identifier of the request that produced the item, which the feed generator may ask
    /// for back in interaction events.
    /// </summary>
    [JsonPropertyName("reqId")]
    public string? ReqId { get; init; }
}

/// <summary>
/// Why a post appears in a feed (the open union behind <see cref="FeedViewPost.Reason"/>). A
/// reason this SDK does not model reads as <see cref="UnknownFeedReason"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownFeedReason))]
[JsonDerivedType(typeof(ReasonRepost), "app.bsky.feed.defs#reasonRepost")]
[JsonDerivedType(typeof(ReasonPin), "app.bsky.feed.defs#reasonPin")]
public abstract class FeedReason : LexObject;

/// <summary>The post is in the feed because an account reposted it.</summary>
public sealed class ReasonRepost : FeedReason
{
    /// <summary>The account that reposted it.</summary>
    [JsonPropertyName("by")]
    public required ProfileViewBasic By { get; init; }

    /// <summary>The AT-URI of the repost record.</summary>
    [JsonPropertyName("uri")]
    public AtUri? Uri { get; init; }

    /// <summary>The CID of the repost record.</summary>
    [JsonPropertyName("cid")]
    public Cid? Cid { get; init; }

    /// <summary>Timestamp at which the app view indexed the repost.</summary>
    [JsonPropertyName("indexedAt")]
    public required AtDatetime IndexedAt { get; init; }
}

/// <summary>The post is in the feed because the author pinned it.</summary>
public sealed class ReasonPin : FeedReason;

/// <summary>
/// A feed reason whose <c>$type</c> this SDK version does not model. It keeps the raw object and
/// writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownFeedReason : FeedReason, IUnknownUnionVariant
{
    /// <summary>Creates an unknown feed reason from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownFeedReason(string type, JsonElement raw)
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

/// <summary>
/// Reply context within a feed view.
/// </summary>
public sealed class FeedReplyRef : LexObject
{
    /// <summary>The root post of the thread.</summary>
    [JsonPropertyName("root")]
    public required JsonElement Root { get; init; }

    /// <summary>The direct parent post.</summary>
    [JsonPropertyName("parent")]
    public required JsonElement Parent { get; init; }

    /// <summary>The author of the parent post's parent, when available.</summary>
    [JsonPropertyName("grandparentAuthor")]
    public ProfileViewBasic? GrandparentAuthor { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Thread view
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A thread view node (the open union behind <c>app.bsky.feed.getPostThread#thread</c> and a
/// thread post's <c>parent</c> and <c>replies</c>). A node type this SDK does not model reads as
/// <see cref="UnknownThreadNode"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownThreadNode))]
[JsonDerivedType(typeof(ThreadViewPost), "app.bsky.feed.defs#threadViewPost")]
[JsonDerivedType(typeof(NotFoundPost), "app.bsky.feed.defs#notFoundPost")]
[JsonDerivedType(typeof(BlockedPost), "app.bsky.feed.defs#blockedPost")]
public abstract class ThreadNode : LexObject;

/// <summary>
/// A thread node whose <c>$type</c> this SDK version does not model. It keeps the raw object and
/// writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownThreadNode : ThreadNode, IUnknownUnionVariant
{
    /// <summary>Creates an unknown thread node from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownThreadNode(string type, JsonElement raw)
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

/// <summary>
/// A post in a thread tree.
/// </summary>
public sealed class ThreadViewPost : ThreadNode
{
    /// <summary>The post at this node of the thread.</summary>
    [JsonPropertyName("post")]
    public required PostView Post { get; init; }

    /// <summary>The direct parent post.</summary>
    [JsonPropertyName("parent")]
    public ThreadNode? Parent { get; init; }

    /// <summary>The replies to this post.</summary>
    [JsonPropertyName("replies")]
    public IReadOnlyList<ThreadNode>? Replies { get; init; }

    /// <summary>Context about the post's place in the thread.</summary>
    [JsonPropertyName("threadContext")]
    public ThreadContext? ThreadContext { get; init; }
}

/// <summary>
/// Context about a post's place in its thread (<c>app.bsky.feed.defs#threadContext</c>).
/// </summary>
public sealed class ThreadContext : LexObject
{
    /// <summary>The AT-URI of the thread root author's like of the post, if they liked it.</summary>
    [JsonPropertyName("rootAuthorLike")]
    public AtUri? RootAuthorLike { get; init; }
}

/// <summary>
/// A not-found post placeholder in a thread.
/// </summary>
public sealed class NotFoundPost : ThreadNode
{
    /// <summary>The AT-URI of the post that could not be found.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>
    /// Always <see langword="true"/>; marks the referenced subject as unavailable.
    /// </summary>
    [JsonPropertyName("notFound")]
    public bool NotFound => true;
}

/// <summary>
/// A blocked post placeholder in a thread.
/// </summary>
public sealed class BlockedPost : ThreadNode
{
    /// <summary>The AT-URI of the blocked post.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>Whether the subject is blocked.</summary>
    [JsonPropertyName("blocked")]
    public bool Blocked => true;

    /// <summary>The account that authored the post.</summary>
    [JsonPropertyName("author")]
    public required BlockedAuthor Author { get; init; }
}

/// <summary>
/// The author of a blocked post (<c>app.bsky.feed.defs#blockedAuthor</c>).
/// </summary>
public sealed class BlockedAuthor : LexObject
{
    /// <summary>The DID of the author.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The viewer's relationship to the author, including the block.</summary>
    [JsonPropertyName("viewer")]
    public ViewerState? Viewer { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Feed generator view
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A feed generator view. Also a variant of <see cref="EmbeddedRecordView"/>, for a feed embedded
/// in a post.
/// </summary>
public sealed class GeneratorView : EmbeddedRecordView
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The account that created this.</summary>
    [JsonPropertyName("creator")]
    public required ProfileView Creator { get; init; }

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Rich-text facets (mentions, links, tags) applied to the description.</summary>
    [JsonPropertyName("descriptionFacets")]
    public IReadOnlyList<Facet>? DescriptionFacets { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    /// <summary>The number of likes.</summary>
    [JsonPropertyName("likeCount")]
    public int? LikeCount { get; init; }

    /// <summary>Whether the feed generator accepts interaction events.</summary>
    [JsonPropertyName("acceptsInteractions")]
    public bool? AcceptsInteractions { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public GeneratorViewerState? Viewer { get; init; }

    /// <summary>What the feed shows, which apps may use to present it (see <see cref="FeedContentMode"/>).</summary>
    [JsonPropertyName("contentMode")]
    public string? ContentMode { get; init; }

    /// <summary>Timestamp at which the app view indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public required AtDatetime IndexedAt { get; init; }
}

/// <summary>
/// Known values of <see cref="GeneratorView.ContentMode"/> and <see cref="GeneratorRecord.ContentMode"/>.
/// </summary>
public static class FeedContentMode
{
    /// <summary>The feed declares no particular content.</summary>
    public const string Unspecified = "app.bsky.feed.defs#contentModeUnspecified";

    /// <summary>The feed shows video posts, and apps may present it as a video feed.</summary>
    public const string Video = "app.bsky.feed.defs#contentModeVideo";
}

/// <summary>
/// Viewer state for a feed generator.
/// </summary>
public sealed class GeneratorViewerState : LexObject
{
    /// <summary>The AT-URI of the viewer's like record, if they have liked this.</summary>
    [JsonPropertyName("like")]
    public AtUri? Like { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  API response types
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getTimeline / getAuthorFeed / getFeed / getListFeed.
/// </summary>
public sealed class FeedResponse : ICursorPage<FeedViewPost>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The feed items.</summary>
    [JsonPropertyName("feed")]
    public required IReadOnlyList<FeedViewPost> Feed { get; init; }

    IReadOnlyList<FeedViewPost> ICursorPage<FeedViewPost>.Items => Feed;
}

/// <summary>
/// Response from getPostThread.
/// </summary>
public sealed class GetPostThreadResponse
{
    /// <summary>The thread rooted at the requested post.</summary>
    [JsonPropertyName("thread")]
    public required ThreadNode Thread { get; init; }

    /// <summary>The threadgate controlling who may reply.</summary>
    [JsonPropertyName("threadgate")]
    public ThreadgateView? Threadgate { get; init; }
}

/// <summary>
/// Response from getPosts.
/// </summary>
public sealed class GetPostsResponse
{
    /// <summary>The posts.</summary>
    [JsonPropertyName("posts")]
    public required IReadOnlyList<PostView> Posts { get; init; }
}

/// <summary>
/// Response from getLikes.
/// </summary>
public sealed class GetLikesResponse : ICursorPage<LikeInfo>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public Cid? Cid { get; init; }

    /// <summary>The likes.</summary>
    [JsonPropertyName("likes")]
    public required IReadOnlyList<LikeInfo> Likes { get; init; }

    IReadOnlyList<LikeInfo> ICursorPage<LikeInfo>.Items => Likes;
}

/// <summary>
/// A single like info entry.
/// </summary>
public sealed class LikeInfo : LexObject
{
    /// <summary>Timestamp at which the app view indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public required AtDatetime IndexedAt { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>The account that liked the subject.</summary>
    [JsonPropertyName("actor")]
    public required ProfileView Actor { get; init; }
}

/// <summary>
/// Response from getRepostedBy.
/// </summary>
public sealed class GetRepostedByResponse : ICursorPage<ProfileView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public Cid? Cid { get; init; }

    /// <summary>The profiles that reposted the post.</summary>
    [JsonPropertyName("repostedBy")]
    public required IReadOnlyList<ProfileView> RepostedBy { get; init; }

    IReadOnlyList<ProfileView> ICursorPage<ProfileView>.Items => RepostedBy;
}

/// <summary>
/// Response from getQuotes.
/// </summary>
public sealed class GetQuotesResponse : ICursorPage<PostView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public Cid? Cid { get; init; }

    /// <summary>The posts.</summary>
    [JsonPropertyName("posts")]
    public required IReadOnlyList<PostView> Posts { get; init; }

    IReadOnlyList<PostView> ICursorPage<PostView>.Items => Posts;
}

/// <summary>
/// Response from getFeedGenerator.
/// </summary>
public sealed class GetFeedGeneratorResponse
{
    /// <summary>The feed generator view.</summary>
    [JsonPropertyName("view")]
    public required GeneratorView View { get; init; }

    /// <summary>Whether the feed generator service is currently reachable.</summary>
    [JsonPropertyName("isOnline")]
    public bool IsOnline { get; init; }

    /// <summary>Whether the feed generator service is correctly configured.</summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }
}

/// <summary>
/// Response from getFeedGenerators.
/// </summary>
public sealed class GetFeedGeneratorsResponse
{
    /// <summary>The feed generators.</summary>
    [JsonPropertyName("feeds")]
    public required IReadOnlyList<GeneratorView> Feeds { get; init; }
}

/// <summary>
/// Response from getActorFeeds.
/// </summary>
public sealed class GetActorFeedsResponse : ICursorPage<GeneratorView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The feed generators.</summary>
    [JsonPropertyName("feeds")]
    public required IReadOnlyList<GeneratorView> Feeds { get; init; }

    IReadOnlyList<GeneratorView> ICursorPage<GeneratorView>.Items => Feeds;
}

/// <summary>
/// Response from getSuggestedFeeds.
/// </summary>
public sealed class GetSuggestedFeedsResponse : ICursorPage<GeneratorView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The feed generators.</summary>
    [JsonPropertyName("feeds")]
    public required IReadOnlyList<GeneratorView> Feeds { get; init; }

    IReadOnlyList<GeneratorView> ICursorPage<GeneratorView>.Items => Feeds;
}

/// <summary>
/// Response from searchPosts.
/// </summary>
public sealed class SearchPostsResponse : ICursorPage<PostView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The total number of matching results, when the server reports it.</summary>
    [JsonPropertyName("hitsTotal")]
    public int? HitsTotal { get; init; }

    /// <summary>The posts.</summary>
    [JsonPropertyName("posts")]
    public required IReadOnlyList<PostView> Posts { get; init; }

    IReadOnlyList<PostView> ICursorPage<PostView>.Items => Posts;
}

/// <summary>
/// Response from describeFeedGenerator.
/// </summary>
public sealed class DescribeFeedGeneratorResponse
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The feed generators.</summary>
    [JsonPropertyName("feeds")]
    public required IReadOnlyList<DescribeFeedGeneratorFeed> Feeds { get; init; }

    /// <summary>Links to the server's policy documents.</summary>
    [JsonPropertyName("links")]
    public JsonElement? Links { get; init; }
}

/// <summary>
/// Feed description within describeFeedGenerator.
/// </summary>
public sealed class DescribeFeedGeneratorFeed : LexObject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }
}

/// <summary>
/// Response from getFeedSkeleton (for feed generators).
/// </summary>
public sealed class GetFeedSkeletonResponse : ICursorPage<SkeletonFeedPost>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The feed items.</summary>
    [JsonPropertyName("feed")]
    public required IReadOnlyList<SkeletonFeedPost> Feed { get; init; }

    /// <summary>
    /// An identifier for the request, which the feed generator may ask for back in interaction
    /// events.
    /// </summary>
    [JsonPropertyName("reqId")]
    public string? ReqId { get; init; }

    IReadOnlyList<SkeletonFeedPost> ICursorPage<SkeletonFeedPost>.Items => Feed;
}

/// <summary>
/// A skeleton feed post (just a URI reference, used by feed generators).
/// </summary>
public sealed class SkeletonFeedPost : LexObject
{
    /// <summary>The AT-URI of the post.</summary>
    [JsonPropertyName("post")]
    public required AtUri Post { get; init; }

    /// <summary>
    /// Why the post is in the feed when it is not there on its own: a
    /// <see cref="SkeletonReasonRepost"/> or a <see cref="SkeletonReasonPin"/>.
    /// </summary>
    [JsonPropertyName("reason")]
    public SkeletonReason? Reason { get; init; }

    /// <summary>
    /// An opaque context string the feed generator may pass back in interaction events.
    /// </summary>
    [JsonPropertyName("feedContext")]
    public string? FeedContext { get; init; }
}

/// <summary>
/// Why a post appears in a feed skeleton (the open union behind
/// <see cref="SkeletonFeedPost.Reason"/>). A reason this SDK does not model reads as
/// <see cref="UnknownSkeletonReason"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownSkeletonReason))]
[JsonDerivedType(typeof(SkeletonReasonRepost), "app.bsky.feed.defs#skeletonReasonRepost")]
[JsonDerivedType(typeof(SkeletonReasonPin), "app.bsky.feed.defs#skeletonReasonPin")]
public abstract class SkeletonReason : LexObject;

/// <summary>The post is in the skeleton because of a repost.</summary>
public sealed class SkeletonReasonRepost : SkeletonReason
{
    /// <summary>The AT-URI of the repost record.</summary>
    [JsonPropertyName("repost")]
    public required AtUri Repost { get; init; }
}

/// <summary>The post is in the skeleton because the author pinned it.</summary>
public sealed class SkeletonReasonPin : SkeletonReason;

/// <summary>
/// A skeleton reason whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownSkeletonReason : SkeletonReason, IUnknownUnionVariant
{
    /// <summary>Creates an unknown skeleton reason from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownSkeletonReason(string type, JsonElement raw)
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
