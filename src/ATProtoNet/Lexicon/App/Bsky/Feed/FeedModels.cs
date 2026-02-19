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
    [JsonPropertyName("root")]
    public required StrongRef Root { get; init; }

    [JsonPropertyName("parent")]
    public required StrongRef Parent { get; init; }
}

/// <summary>
/// Self-applied content labels for a post.
/// </summary>
public sealed class SelfLabels
{
    [JsonPropertyName("$type")]
    public string Type => "com.atproto.label.defs#selfLabels";

    [JsonPropertyName("values")]
    public required List<SelfLabelValue> Values { get; init; }
}

/// <summary>
/// A single self-label value.
/// </summary>
public sealed class SelfLabelValue
{
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
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.like";

    [JsonPropertyName("subject")]
    public required StrongRef Subject { get; init; }

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
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.repost";

    [JsonPropertyName("subject")]
    public required StrongRef Subject { get; init; }

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
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.threadgate";

    [JsonPropertyName("post")]
    public required string Post { get; init; }

    [JsonPropertyName("allow")]
    public List<JsonElement>? Allow { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("hiddenReplies")]
    public List<string>? HiddenReplies { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Feed generator record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A feed generator record. Collection: app.bsky.feed.generator
/// </summary>
public sealed class GeneratorRecord
{
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.feed.generator";

    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("descriptionFacets")]
    public List<Facet>? DescriptionFacets { get; init; }

    [JsonPropertyName("avatar")]
    public BlobRef? Avatar { get; init; }

    [JsonPropertyName("acceptsInteractions")]
    public bool? AcceptsInteractions { get; init; }

    [JsonPropertyName("labels")]
    public SelfLabels? Labels { get; init; }

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
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("author")]
    public required ProfileViewBasic Author { get; init; }

    [JsonPropertyName("record")]
    public required JsonElement Record { get; init; }

    [JsonPropertyName("embed")]
    public EmbedView? Embed { get; init; }

    [JsonPropertyName("replyCount")]
    public int? ReplyCount { get; init; }

    [JsonPropertyName("repostCount")]
    public int? RepostCount { get; init; }

    [JsonPropertyName("likeCount")]
    public int? LikeCount { get; init; }

    [JsonPropertyName("quoteCount")]
    public int? QuoteCount { get; init; }

    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    [JsonPropertyName("viewer")]
    public PostViewerState? Viewer { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

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

    [JsonPropertyName("threadMuted")]
    public bool? ThreadMuted { get; init; }

    [JsonPropertyName("replyDisabled")]
    public bool? ReplyDisabled { get; init; }

    [JsonPropertyName("embeddingDisabled")]
    public bool? EmbeddingDisabled { get; init; }

    [JsonPropertyName("pinned")]
    public bool? Pinned { get; init; }
}

/// <summary>
/// A feed view item wrapping a post with optional reason (repost).
/// </summary>
public sealed class FeedViewPost
{
    [JsonPropertyName("post")]
    public required PostView Post { get; init; }

    [JsonPropertyName("reply")]
    public FeedReplyRef? Reply { get; init; }

    [JsonPropertyName("reason")]
    public JsonElement? Reason { get; init; }

    [JsonPropertyName("feedContext")]
    public string? FeedContext { get; init; }
}

/// <summary>
/// Reply context within a feed view.
/// </summary>
public sealed class FeedReplyRef
{
    [JsonPropertyName("root")]
    public required JsonElement Root { get; init; }

    [JsonPropertyName("parent")]
    public required JsonElement Parent { get; init; }

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
    [JsonPropertyName("post")]
    public required PostView Post { get; init; }

    [JsonPropertyName("parent")]
    public ThreadNode? Parent { get; init; }

    [JsonPropertyName("replies")]
    public List<ThreadNode>? Replies { get; init; }
}

/// <summary>
/// A not-found post placeholder in a thread.
/// </summary>
public sealed class NotFoundPost : ThreadNode
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("notFound")]
    public bool NotFound => true;
}

/// <summary>
/// A blocked post placeholder in a thread.
/// </summary>
public sealed class BlockedPost : ThreadNode
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("blocked")]
    public bool Blocked => true;

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
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("creator")]
    public required ProfileView Creator { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("descriptionFacets")]
    public List<Facet>? DescriptionFacets { get; init; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; init; }

    [JsonPropertyName("likeCount")]
    public int? LikeCount { get; init; }

    [JsonPropertyName("acceptsInteractions")]
    public bool? AcceptsInteractions { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    [JsonPropertyName("viewer")]
    public GeneratorViewerState? Viewer { get; init; }

    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }
}

/// <summary>
/// Viewer state for a feed generator.
/// </summary>
public sealed class GeneratorViewerState
{
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
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("feed")]
    public required List<FeedViewPost> Feed { get; init; }
}

/// <summary>
/// Response from getPostThread.
/// </summary>
public sealed class GetPostThreadResponse
{
    [JsonPropertyName("thread")]
    public required ThreadNode Thread { get; init; }

    [JsonPropertyName("threadgate")]
    public JsonElement? Threadgate { get; init; }
}

/// <summary>
/// Response from getPosts.
/// </summary>
public sealed class GetPostsResponse
{
    [JsonPropertyName("posts")]
    public required List<PostView> Posts { get; init; }
}

/// <summary>
/// Response from getLikes.
/// </summary>
public sealed class GetLikesResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    [JsonPropertyName("likes")]
    public required List<LikeInfo> Likes { get; init; }
}

/// <summary>
/// A single like info entry.
/// </summary>
public sealed class LikeInfo
{
    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("actor")]
    public required ProfileView Actor { get; init; }
}

/// <summary>
/// Response from getRepostedBy.
/// </summary>
public sealed class GetRepostedByResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    [JsonPropertyName("repostedBy")]
    public required List<ProfileView> RepostedBy { get; init; }
}

/// <summary>
/// Response from getQuotes.
/// </summary>
public sealed class GetQuotesResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    [JsonPropertyName("posts")]
    public required List<PostView> Posts { get; init; }
}

/// <summary>
/// Response from getFeedGenerator.
/// </summary>
public sealed class GetFeedGeneratorResponse
{
    [JsonPropertyName("view")]
    public required GeneratorView View { get; init; }

    [JsonPropertyName("isOnline")]
    public bool IsOnline { get; init; }

    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }
}

/// <summary>
/// Response from getFeedGenerators.
/// </summary>
public sealed class GetFeedGeneratorsResponse
{
    [JsonPropertyName("feeds")]
    public required List<GeneratorView> Feeds { get; init; }
}

/// <summary>
/// Response from getActorFeeds.
/// </summary>
public sealed class GetActorFeedsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("feeds")]
    public required List<GeneratorView> Feeds { get; init; }
}

/// <summary>
/// Response from getSuggestedFeeds.
/// </summary>
public sealed class GetSuggestedFeedsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("feeds")]
    public required List<GeneratorView> Feeds { get; init; }
}

/// <summary>
/// Response from searchPosts.
/// </summary>
public sealed class SearchPostsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("hitsTotal")]
    public int? HitsTotal { get; init; }

    [JsonPropertyName("posts")]
    public required List<PostView> Posts { get; init; }
}

/// <summary>
/// Response from describeFeedGenerator.
/// </summary>
public sealed class DescribeFeedGeneratorResponse
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("feeds")]
    public required List<DescribeFeedGeneratorFeed> Feeds { get; init; }

    [JsonPropertyName("links")]
    public JsonElement? Links { get; init; }
}

/// <summary>
/// Feed description within describeFeedGenerator.
/// </summary>
public sealed class DescribeFeedGeneratorFeed
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}

/// <summary>
/// Response from getFeedSkeleton (for feed generators).
/// </summary>
public sealed class GetFeedSkeletonResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("feed")]
    public required List<SkeletonFeedPost> Feed { get; init; }
}

/// <summary>
/// A skeleton feed post (just a URI reference, used by feed generators).
/// </summary>
public sealed class SkeletonFeedPost
{
    [JsonPropertyName("post")]
    public required string Post { get; init; }

    [JsonPropertyName("reason")]
    public JsonElement? Reason { get; init; }

    [JsonPropertyName("feedContext")]
    public string? FeedContext { get; init; }
}
