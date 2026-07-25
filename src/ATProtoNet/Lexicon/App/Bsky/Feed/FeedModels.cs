using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.App.Bsky.Feed;

// ──────────────────────────────────────────────────────────────
//  Post record (the actual repo record)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A Bluesky post record stored in the repository.
/// Collection: app.bsky.feed.post
/// </summary>
public sealed class PostRecord
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.feed.post</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.post";

    /// <summary>The post text content (max 300 graphemes / ~3000 bytes).</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>Rich-text facets (mentions, links, hashtags).</summary>
    [JsonPropertyName("facets")]
    public List<Facet>? Facets { get; init; }

    /// <summary>Reply reference (parent and root post).</summary>
    [JsonPropertyName("reply")]
    public ReplyRef? Reply { get; init; }

    /// <summary>Embedded content (images, links, quotes, video).</summary>
    [JsonPropertyName("embed")]
    public EmbedBase? Embed { get; init; }

    /// <summary>Language tags for the post (BCP-47).</summary>
    [JsonPropertyName("langs")]
    public List<string>? Langs { get; init; }

    /// <summary>Self-applied labels for content warnings.</summary>
    [JsonPropertyName("labels")]
    public SelfLabels? Labels { get; init; }

    /// <summary>Additional tags (up to 8, max 640 chars each).</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; init; }

    /// <summary>Timestamp of post creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// Reply reference linking to parent and root posts.
/// </summary>
public sealed class ReplyRef
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
public sealed class SelfLabels
{
    /// <summary>
    /// The Lexicon type discriminator (<c>com.atproto.label.defs#selfLabels</c>).
    /// </summary>
    [JsonPropertyName("$type")]
    public string Type => "com.atproto.label.defs#selfLabels";

    /// <summary>The self-applied labels.</summary>
    [JsonPropertyName("values")]
    public required List<SelfLabelValue> Values { get; init; }
}

/// <summary>
/// A single self-label value.
/// </summary>
public sealed class SelfLabelValue
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
public sealed class LikeRecord
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.feed.like</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.like";

    /// <summary>A strong reference to the post being liked.</summary>
    [JsonPropertyName("subject")]
    public required StrongRef Subject { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Repost record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A repost record. Collection: app.bsky.feed.repost
/// </summary>
public sealed class RepostRecord
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.feed.repost</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.repost";

    /// <summary>A strong reference to the post being reposted.</summary>
    [JsonPropertyName("subject")]
    public required StrongRef Subject { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Threadgate record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A threadgate record that controls who can reply to a thread.
/// Collection: app.bsky.feed.threadgate
/// </summary>
public sealed class ThreadgateRecord
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.feed.threadgate</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.threadgate";

    /// <summary>The AT-URI of the post this threadgate applies to.</summary>
    [JsonPropertyName("post")]
    public required string Post { get; init; }

    /// <summary>
    /// The rules controlling who may reply. An empty list disables replies entirely; <see
    /// langword="null"/> allows everyone.
    /// </summary>
    [JsonPropertyName("allow")]
    public List<JsonElement>? Allow { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    /// <summary>The AT-URIs of replies hidden by the thread author.</summary>
    [JsonPropertyName("hiddenReplies")]
    public List<string>? HiddenReplies { get; init; }
}

/// <summary>
/// A postgate record that controls embedding/quoting of a post.
/// Collection: app.bsky.feed.postgate
/// </summary>
public sealed class PostgateRecord
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.feed.postgate</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.postgate";

    /// <summary>The AT-URI of the post this postgate applies to.</summary>
    [JsonPropertyName("post")]
    public required string Post { get; init; }

    /// <summary>The AT-URIs of quote posts the author has detached.</summary>
    [JsonPropertyName("detachedEmbeddingUris")]
    public List<string>? DetachedEmbeddingUris { get; init; }

    /// <summary>The rules controlling who may quote this post.</summary>
    [JsonPropertyName("embeddingRules")]
    public List<JsonElement>? EmbeddingRules { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Feed generator record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A feed generator record. Collection: app.bsky.feed.generator
/// </summary>
public sealed class GeneratorRecord
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.feed.generator</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.generator";

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Rich-text facets (mentions, links, tags) applied to the description.</summary>
    [JsonPropertyName("descriptionFacets")]
    public List<Facet>? DescriptionFacets { get; init; }

    /// <summary>The avatar image.</summary>
    [JsonPropertyName("avatar")]
    public BlobRef? Avatar { get; init; }

    /// <summary>Whether the feed generator accepts interaction events.</summary>
    [JsonPropertyName("acceptsInteractions")]
    public bool? AcceptsInteractions { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public SelfLabels? Labels { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Post view types (returned from API)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A full post view as returned by feed endpoints.
/// </summary>
public sealed class PostView
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>The account that authored the post.</summary>
    [JsonPropertyName("author")]
    public required ProfileViewBasic Author { get; init; }

    /// <summary>The post record.</summary>
    [JsonPropertyName("record")]
    public required JsonElement Record { get; init; }

    /// <summary>Embedded content attached to the post.</summary>
    [JsonPropertyName("embed")]
    public EmbedView? Embed { get; init; }

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

    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public PostViewerState? Viewer { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    /// <summary>The threadgate record controlling who may reply.</summary>
    [JsonPropertyName("threadgate")]
    public JsonElement? Threadgate { get; init; }
}

/// <summary>
/// Viewer state for a post (like/repost status).
/// </summary>
public sealed class PostViewerState
{
    /// <summary>AT-URI of the viewer's like record, if liked.</summary>
    [JsonPropertyName("like")]
    public string? Like { get; init; }

    /// <summary>AT-URI of the viewer's repost record, if reposted.</summary>
    [JsonPropertyName("repost")]
    public string? Repost { get; init; }

    /// <summary>Whether the viewer has muted this thread.</summary>
    [JsonPropertyName("threadMuted")]
    public bool? ThreadMuted { get; init; }

    /// <summary>Whether the viewer is prevented from replying by a threadgate.</summary>
    [JsonPropertyName("replyDisabled")]
    public bool? ReplyDisabled { get; init; }

    /// <summary>Whether the viewer is prevented from quoting this post by a postgate.</summary>
    [JsonPropertyName("embeddingDisabled")]
    public bool? EmbeddingDisabled { get; init; }

    /// <summary>Whether the post is pinned to the author's profile.</summary>
    [JsonPropertyName("pinned")]
    public bool? Pinned { get; init; }
}

/// <summary>
/// A feed view item wrapping a post with optional reason (repost).
/// </summary>
public sealed class FeedViewPost
{
    /// <summary>The post.</summary>
    [JsonPropertyName("post")]
    public required PostView Post { get; init; }

    /// <summary>Reply information for the post, if it is a reply.</summary>
    [JsonPropertyName("reply")]
    public FeedReplyRef? Reply { get; init; }

    /// <summary>The reason this item appears in the feed (for example a repost).</summary>
    [JsonPropertyName("reason")]
    public JsonElement? Reason { get; init; }

    /// <summary>
    /// An opaque context string the feed generator may pass back in interaction events.
    /// </summary>
    [JsonPropertyName("feedContext")]
    public string? FeedContext { get; init; }
}

/// <summary>
/// Reply context within a feed view.
/// </summary>
public sealed class FeedReplyRef
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
/// A thread view node.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ThreadViewPost), "app.bsky.feed.defs#threadViewPost")]
[JsonDerivedType(typeof(NotFoundPost), "app.bsky.feed.defs#notFoundPost")]
[JsonDerivedType(typeof(BlockedPost), "app.bsky.feed.defs#blockedPost")]
public abstract class ThreadNode { }

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
    public List<ThreadNode>? Replies { get; init; }
}

/// <summary>
/// A not-found post placeholder in a thread.
/// </summary>
public sealed class NotFoundPost : ThreadNode
{
    /// <summary>The AT-URI of the post that could not be found.</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

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
    public required string Uri { get; init; }

    /// <summary>Whether the subject is blocked.</summary>
    [JsonPropertyName("blocked")]
    public bool Blocked => true;

    /// <summary>The account that authored the post.</summary>
    [JsonPropertyName("author")]
    public JsonElement? Author { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Feed generator view
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A feed generator view.
/// </summary>
public sealed class GeneratorView
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

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
    public List<Facet>? DescriptionFacets { get; init; }

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
    public List<Label>? Labels { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public GeneratorViewerState? Viewer { get; init; }

    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }
}

/// <summary>
/// Viewer state for a feed generator.
/// </summary>
public sealed class GeneratorViewerState
{
    /// <summary>The AT-URI of the viewer's like record, if they have liked this.</summary>
    [JsonPropertyName("like")]
    public string? Like { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  API response types
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getTimeline / getAuthorFeed / getFeed / getListFeed.
/// </summary>
public sealed class FeedResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The feed items.</summary>
    [JsonPropertyName("feed")]
    public required List<FeedViewPost> Feed { get; init; }
}

/// <summary>
/// Response from getPostThread.
/// </summary>
public sealed class GetPostThreadResponse
{
    /// <summary>The thread rooted at the requested post.</summary>
    [JsonPropertyName("thread")]
    public required ThreadNode Thread { get; init; }

    /// <summary>The threadgate record controlling who may reply.</summary>
    [JsonPropertyName("threadgate")]
    public JsonElement? Threadgate { get; init; }
}

/// <summary>
/// Response from getPosts.
/// </summary>
public sealed class GetPostsResponse
{
    /// <summary>The posts.</summary>
    [JsonPropertyName("posts")]
    public required List<PostView> Posts { get; init; }
}

/// <summary>
/// Response from getLikes.
/// </summary>
public sealed class GetLikesResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    /// <summary>The likes.</summary>
    [JsonPropertyName("likes")]
    public required List<LikeInfo> Likes { get; init; }
}

/// <summary>
/// A single like info entry.
/// </summary>
public sealed class LikeInfo
{
    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    /// <summary>The account that liked the subject.</summary>
    [JsonPropertyName("actor")]
    public required ProfileView Actor { get; init; }
}

/// <summary>
/// Response from getRepostedBy.
/// </summary>
public sealed class GetRepostedByResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    /// <summary>The profiles that reposted the post.</summary>
    [JsonPropertyName("repostedBy")]
    public required List<ProfileView> RepostedBy { get; init; }
}

/// <summary>
/// Response from getQuotes.
/// </summary>
public sealed class GetQuotesResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    /// <summary>The posts.</summary>
    [JsonPropertyName("posts")]
    public required List<PostView> Posts { get; init; }
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
    public required List<GeneratorView> Feeds { get; init; }
}

/// <summary>
/// Response from getActorFeeds.
/// </summary>
public sealed class GetActorFeedsResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The feed generators.</summary>
    [JsonPropertyName("feeds")]
    public required List<GeneratorView> Feeds { get; init; }
}

/// <summary>
/// Response from getSuggestedFeeds.
/// </summary>
public sealed class GetSuggestedFeedsResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The feed generators.</summary>
    [JsonPropertyName("feeds")]
    public required List<GeneratorView> Feeds { get; init; }
}

/// <summary>
/// Response from searchPosts.
/// </summary>
public sealed class SearchPostsResponse
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
    public required List<PostView> Posts { get; init; }
}

/// <summary>
/// Response from describeFeedGenerator.
/// </summary>
public sealed class DescribeFeedGeneratorResponse
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The feed generators.</summary>
    [JsonPropertyName("feeds")]
    public required List<DescribeFeedGeneratorFeed> Feeds { get; init; }

    /// <summary>Links to the server's policy documents.</summary>
    [JsonPropertyName("links")]
    public JsonElement? Links { get; init; }
}

/// <summary>
/// Feed description within describeFeedGenerator.
/// </summary>
public sealed class DescribeFeedGeneratorFeed
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}

/// <summary>
/// Response from getFeedSkeleton (for feed generators).
/// </summary>
public sealed class GetFeedSkeletonResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The feed items.</summary>
    [JsonPropertyName("feed")]
    public required List<SkeletonFeedPost> Feed { get; init; }
}

/// <summary>
/// A skeleton feed post (just a URI reference, used by feed generators).
/// </summary>
public sealed class SkeletonFeedPost
{
    /// <summary>The AT-URI of the post.</summary>
    [JsonPropertyName("post")]
    public required string Post { get; init; }

    /// <summary>The reason this item appears in the feed (for example a repost).</summary>
    [JsonPropertyName("reason")]
    public JsonElement? Reason { get; init; }

    /// <summary>
    /// An opaque context string the feed generator may pass back in interaction events.
    /// </summary>
    [JsonPropertyName("feedContext")]
    public string? FeedContext { get; init; }
}
