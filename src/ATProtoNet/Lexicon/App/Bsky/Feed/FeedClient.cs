using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Lexicon.Com.AtProto.Repo;
using ATProtoNet.Models;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.App.Bsky.Feed;

/// <summary>
/// Client for app.bsky.feed.* XRPC endpoints.
/// Handles timelines, feeds, posts, likes, reposts, and search.
/// </summary>
public sealed class FeedClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal FeedClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────
    //  Timeline & Feeds
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get the authenticated user's home timeline.
    /// </summary>
    public Task<FeedResponse> GetTimelineAsync(
        int? limit = null, string? cursor = null, string? algorithm = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
            ["algorithm"] = algorithm,
        };

        return _xrpc.QueryAsync<FeedResponse>(
            "app.bsky.feed.getTimeline", parameters, cancellationToken);
    }

    /// <summary>
    /// Get an author's feed (posts they created).
    /// </summary>
    /// <param name="actor">Handle or DID of the author.</param>
    /// <param name="limit">Max posts per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="filter">Feed filter: "posts_with_replies", "posts_no_replies",
    /// "posts_with_media", "posts_and_author_threads".</param>
    /// <param name="includePins">Whether to include pinned posts (default true).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<FeedResponse> GetAuthorFeedAsync(
        string actor,
        int? limit = null,
        string? cursor = null,
        string? filter = null,
        bool? includePins = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["actor"] = actor,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
            ["filter"] = filter,
            ["includePins"] = includePins?.ToString()?.ToLowerInvariant(),
        };

        return _xrpc.QueryAsync<FeedResponse>(
            "app.bsky.feed.getAuthorFeed", parameters, cancellationToken);
    }

    /// <summary>
    /// Get a custom/algorithmic feed.
    /// </summary>
    /// <param name="feed">The AT-URI of the feed generator.</param>
    /// <param name="limit">Max posts per page.</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<FeedResponse> GetFeedAsync(
        string feed,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["feed"] = feed,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<FeedResponse>(
            "app.bsky.feed.getFeed", parameters, cancellationToken);
    }

    /// <summary>
    /// Get a list feed.
    /// </summary>
    public Task<FeedResponse> GetListFeedAsync(
        string list,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["list"] = list,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<FeedResponse>(
            "app.bsky.feed.getListFeed", parameters, cancellationToken);
    }

    /// <summary>
    /// Get an actor's liked posts.
    /// </summary>
    public Task<FeedResponse> GetActorLikesAsync(
        string actor,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["actor"] = actor,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<FeedResponse>(
            "app.bsky.feed.getActorLikes", parameters, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Posts
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get a post thread (the post, its parents, and replies).
    /// </summary>
    /// <param name="uri">The AT-URI of the post.</param>
    /// <param name="depth">Max reply depth (0-1000, default 6).</param>
    /// <param name="parentHeight">Max parent height (0-1000, default 80).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetPostThreadResponse> GetPostThreadAsync(
        string uri,
        int? depth = null,
        int? parentHeight = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["uri"] = uri,
            ["depth"] = depth?.ToString(),
            ["parentHeight"] = parentHeight?.ToString(),
        };

        return _xrpc.QueryAsync<GetPostThreadResponse>(
            "app.bsky.feed.getPostThread", parameters, cancellationToken);
    }

    /// <summary>
    /// Get multiple posts by AT-URI (max 25).
    /// </summary>
    public Task<GetPostsResponse> GetPostsAsync(
        IEnumerable<string> uris, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["uris"] = string.Join(",", uris),
        };

        return _xrpc.QueryAsync<GetPostsResponse>(
            "app.bsky.feed.getPosts", parameters, cancellationToken);
    }

    /// <summary>
    /// Get accounts that liked a post.
    /// </summary>
    public Task<GetLikesResponse> GetLikesAsync(
        string uri, string? cid = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["uri"] = uri,
            ["cid"] = cid,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetLikesResponse>(
            "app.bsky.feed.getLikes", parameters, cancellationToken);
    }

    /// <summary>
    /// Get accounts that reposted a post.
    /// </summary>
    public Task<GetRepostedByResponse> GetRepostedByAsync(
        string uri, string? cid = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["uri"] = uri,
            ["cid"] = cid,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetRepostedByResponse>(
            "app.bsky.feed.getRepostedBy", parameters, cancellationToken);
    }

    /// <summary>
    /// Get posts that quote a given post.
    /// </summary>
    public Task<GetQuotesResponse> GetQuotesAsync(
        string uri, string? cid = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["uri"] = uri,
            ["cid"] = cid,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetQuotesResponse>(
            "app.bsky.feed.getQuotes", parameters, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Feed Generators
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get info about a feed generator.
    /// </summary>
    public Task<GetFeedGeneratorResponse> GetFeedGeneratorAsync(
        string feed, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?> { ["feed"] = feed };
        return _xrpc.QueryAsync<GetFeedGeneratorResponse>(
            "app.bsky.feed.getFeedGenerator", parameters, cancellationToken);
    }

    /// <summary>
    /// Get info about multiple feed generators.
    /// </summary>
    public Task<GetFeedGeneratorsResponse> GetFeedGeneratorsAsync(
        IEnumerable<string> feeds, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["feeds"] = string.Join(",", feeds),
        };

        return _xrpc.QueryAsync<GetFeedGeneratorsResponse>(
            "app.bsky.feed.getFeedGenerators", parameters, cancellationToken);
    }

    /// <summary>
    /// Get feed generators created by an actor.
    /// </summary>
    public Task<GetActorFeedsResponse> GetActorFeedsAsync(
        string actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["actor"] = actor,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetActorFeedsResponse>(
            "app.bsky.feed.getActorFeeds", parameters, cancellationToken);
    }

    /// <summary>
    /// Get suggested feeds.
    /// </summary>
    public Task<GetSuggestedFeedsResponse> GetSuggestedFeedsAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetSuggestedFeedsResponse>(
            "app.bsky.feed.getSuggestedFeeds", parameters, cancellationToken);
    }

    /// <summary>
    /// Describe the feed generator service.
    /// </summary>
    public Task<DescribeFeedGeneratorResponse> DescribeFeedGeneratorAsync(
        CancellationToken cancellationToken = default)
    {
        return _xrpc.QueryAsync<DescribeFeedGeneratorResponse>(
            "app.bsky.feed.describeFeedGenerator", cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get a feed skeleton (for feed generator implementations).
    /// </summary>
    public Task<GetFeedSkeletonResponse> GetFeedSkeletonAsync(
        string feed, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["feed"] = feed,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<GetFeedSkeletonResponse>(
            "app.bsky.feed.getFeedSkeleton", parameters, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Search
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Search posts.
    /// </summary>
    /// <param name="q">Search query string.</param>
    /// <param name="sort">Sort order: "top" or "latest".</param>
    /// <param name="since">Filter to posts since this date (ISO 8601).</param>
    /// <param name="until">Filter to posts before this date (ISO 8601).</param>
    /// <param name="mentions">Filter to posts mentioning this DID.</param>
    /// <param name="author">Filter to posts by this author (DID or handle).</param>
    /// <param name="lang">Filter by language (BCP-47).</param>
    /// <param name="domain">Filter by domain in post links.</param>
    /// <param name="url">Filter by URL in post links.</param>
    /// <param name="tag">Filter by hashtag (without #).</param>
    /// <param name="limit">Max results per page (1-100, default 25).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SearchPostsResponse> SearchPostsAsync(
        string q,
        string? sort = null,
        string? since = null,
        string? until = null,
        string? mentions = null,
        string? author = null,
        string? lang = null,
        string? domain = null,
        string? url = null,
        string? tag = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["q"] = q,
            ["sort"] = sort,
            ["since"] = since,
            ["until"] = until,
            ["mentions"] = mentions,
            ["author"] = author,
            ["lang"] = lang,
            ["domain"] = domain,
            ["url"] = url,
            ["tag"] = tag,
            ["limit"] = limit?.ToString(),
            ["cursor"] = cursor,
        };

        return _xrpc.QueryAsync<SearchPostsResponse>(
            "app.bsky.feed.searchPosts", parameters, cancellationToken);
    }
}
