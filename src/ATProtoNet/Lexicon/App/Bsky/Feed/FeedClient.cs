using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;

namespace ATProtoNet.Lexicon.App.Bsky.Feed;

/// <summary>
/// Client for app.bsky.feed.* XRPC endpoints.
/// Handles timelines, feeds, posts, likes, reposts, and search.
/// </summary>
public sealed class FeedClient
{
    private readonly XrpcClient _xrpc;

    internal FeedClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    // ──────────────────────────────────────────────────────────
    //  Timeline & Feeds
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get one page of the authenticated user's home timeline.
    /// </summary>
    /// <param name="algorithm">Variant of the timeline algorithm; the server's default when omitted.</param>
    /// <param name="limit">Max posts per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<FeedResponse> GetTimelineAsync(
        string? algorithm = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("algorithm", algorithm)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<FeedResponse>(
            "app.bsky.feed.getTimeline", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the authenticated user's home timeline, fetching pages as needed.
    /// </summary>
    /// <param name="algorithm">Variant of the timeline algorithm; the server's default when omitted.</param>
    /// <param name="pageSize">Posts per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<FeedViewPost> EnumerateTimelineAsync(
        string? algorithm = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<FeedResponse, FeedViewPost>(
            (cursor, ct) => GetTimelineAsync(algorithm, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get one page of an author's feed (posts and reposts by the actor).
    /// </summary>
    /// <param name="actor">Handle or DID of the author.</param>
    /// <param name="filter">Feed filter: "posts_with_replies", "posts_no_replies",
    /// "posts_with_media", "posts_and_author_threads".</param>
    /// <param name="includePins">Whether to include pinned posts (default true).</param>
    /// <param name="limit">Max posts per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<FeedResponse> GetAuthorFeedAsync(
        AtIdentifier actor,
        string? filter = null,
        bool? includePins = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("actor", actor)
            .Add("filter", filter)
            .Add("includePins", includePins)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<FeedResponse>(
            "app.bsky.feed.getAuthorFeed", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate an author's feed, fetching pages as needed.
    /// </summary>
    /// <param name="actor">Handle or DID of the author.</param>
    /// <param name="filter">Feed filter: "posts_with_replies", "posts_no_replies",
    /// "posts_with_media", "posts_and_author_threads".</param>
    /// <param name="includePins">Whether to include pinned posts (default true).</param>
    /// <param name="pageSize">Posts per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<FeedViewPost> EnumerateAuthorFeedAsync(
        AtIdentifier actor,
        string? filter = null,
        bool? includePins = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<FeedResponse, FeedViewPost>(
            (cursor, ct) => GetAuthorFeedAsync(actor, filter, includePins, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get one page of a custom (algorithmic) feed.
    /// </summary>
    /// <param name="feed">The AT-URI of the feed generator record.</param>
    /// <param name="limit">Max posts per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<FeedResponse> GetFeedAsync(
        AtUri feed,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("feed", feed)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<FeedResponse>(
            "app.bsky.feed.getFeed", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate a custom (algorithmic) feed, fetching pages as needed.
    /// </summary>
    /// <param name="feed">The AT-URI of the feed generator record.</param>
    /// <param name="pageSize">Posts per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<FeedViewPost> EnumerateFeedAsync(
        AtUri feed,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<FeedResponse, FeedViewPost>(
            (cursor, ct) => GetFeedAsync(feed, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get one page of a list feed (recent posts by the list's members).
    /// </summary>
    /// <param name="list">The AT-URI of the list.</param>
    /// <param name="limit">Max posts per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<FeedResponse> GetListFeedAsync(
        AtUri list,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("list", list)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<FeedResponse>(
            "app.bsky.feed.getListFeed", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate a list feed, fetching pages as needed.
    /// </summary>
    /// <param name="list">The AT-URI of the list.</param>
    /// <param name="pageSize">Posts per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<FeedViewPost> EnumerateListFeedAsync(
        AtUri list,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<FeedResponse, FeedViewPost>(
            (cursor, ct) => GetListFeedAsync(list, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get one page of the posts an actor has liked.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="limit">Max posts per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<FeedResponse> GetActorLikesAsync(
        AtIdentifier actor,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("actor", actor)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<FeedResponse>(
            "app.bsky.feed.getActorLikes", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the posts an actor has liked, fetching pages as needed.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="pageSize">Posts per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<FeedViewPost> EnumerateActorLikesAsync(
        AtIdentifier actor,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<FeedResponse, FeedViewPost>(
            (cursor, ct) => GetActorLikesAsync(actor, pageSize, cursor, ct),
            cancellationToken);

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
        AtUri uri,
        int? depth = null,
        int? parentHeight = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("uri", uri)
            .Add("depth", depth)
            .Add("parentHeight", parentHeight);

        return _xrpc.QueryAsync<GetPostThreadResponse>(
            "app.bsky.feed.getPostThread", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get multiple posts by AT-URI (max 25).
    /// </summary>
    /// <param name="uris">The AT-URIs of the posts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetPostsResponse> GetPostsAsync(
        IEnumerable<AtUri> uris, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .AddAll("uris", uris.Select(uri => uri.Value));

        return _xrpc.QueryAsync<GetPostsResponse>(
            "app.bsky.feed.getPosts", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get one page of the likes on a post (or other subject).
    /// </summary>
    /// <param name="uri">The AT-URI of the liked subject.</param>
    /// <param name="cid">Optional CID of a specific version of the subject.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetLikesResponse> GetLikesAsync(
        AtUri uri, Cid? cid = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("uri", uri)
            .Add("cid", cid)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetLikesResponse>(
            "app.bsky.feed.getLikes", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the likes on a post (or other subject), fetching pages as needed.
    /// </summary>
    /// <param name="uri">The AT-URI of the liked subject.</param>
    /// <param name="cid">Optional CID of a specific version of the subject.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<LikeInfo> EnumerateLikesAsync(
        AtUri uri, Cid? cid = null, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetLikesResponse, LikeInfo>(
            (cursor, ct) => GetLikesAsync(uri, cid, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get one page of the accounts that reposted a post.
    /// </summary>
    /// <param name="uri">The AT-URI of the post.</param>
    /// <param name="cid">Optional CID of a specific version of the post.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetRepostedByResponse> GetRepostedByAsync(
        AtUri uri, Cid? cid = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("uri", uri)
            .Add("cid", cid)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetRepostedByResponse>(
            "app.bsky.feed.getRepostedBy", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the accounts that reposted a post, fetching pages as needed.
    /// </summary>
    /// <param name="uri">The AT-URI of the post.</param>
    /// <param name="cid">Optional CID of a specific version of the post.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<ProfileView> EnumerateRepostedByAsync(
        AtUri uri, Cid? cid = null, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetRepostedByResponse, ProfileView>(
            (cursor, ct) => GetRepostedByAsync(uri, cid, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get one page of the posts that quote a given post.
    /// </summary>
    /// <param name="uri">The AT-URI of the quoted post.</param>
    /// <param name="cid">Optional CID of a specific version of the post.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetQuotesResponse> GetQuotesAsync(
        AtUri uri, Cid? cid = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("uri", uri)
            .Add("cid", cid)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetQuotesResponse>(
            "app.bsky.feed.getQuotes", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the posts that quote a given post, fetching pages as needed.
    /// </summary>
    /// <param name="uri">The AT-URI of the quoted post.</param>
    /// <param name="cid">Optional CID of a specific version of the post.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<PostView> EnumerateQuotesAsync(
        AtUri uri, Cid? cid = null, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetQuotesResponse, PostView>(
            (cursor, ct) => GetQuotesAsync(uri, cid, pageSize, cursor, ct),
            cancellationToken);

    // ──────────────────────────────────────────────────────────
    //  Feed Generators
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get info about a feed generator.
    /// </summary>
    /// <param name="feed">The AT-URI of the feed generator record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetFeedGeneratorResponse> GetFeedGeneratorAsync(
        AtUri feed, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("feed", feed);
        return _xrpc.QueryAsync<GetFeedGeneratorResponse>(
            "app.bsky.feed.getFeedGenerator", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get info about multiple feed generators.
    /// </summary>
    /// <param name="feeds">The AT-URIs of the feed generator records.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetFeedGeneratorsResponse> GetFeedGeneratorsAsync(
        IEnumerable<AtUri> feeds, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .AddAll("feeds", feeds.Select(feed => feed.Value));

        return _xrpc.QueryAsync<GetFeedGeneratorsResponse>(
            "app.bsky.feed.getFeedGenerators", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get one page of the feed generators an actor created.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetActorFeedsResponse> GetActorFeedsAsync(
        AtIdentifier actor, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("actor", actor)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetActorFeedsResponse>(
            "app.bsky.feed.getActorFeeds", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the feed generators an actor created, fetching pages as needed.
    /// </summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<GeneratorView> EnumerateActorFeedsAsync(
        AtIdentifier actor, int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetActorFeedsResponse, GeneratorView>(
            (cursor, ct) => GetActorFeedsAsync(actor, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Get one page of suggested feeds.
    /// </summary>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetSuggestedFeedsResponse> GetSuggestedFeedsAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetSuggestedFeedsResponse>(
            "app.bsky.feed.getSuggestedFeeds", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every suggested feed, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<GeneratorView> EnumerateSuggestedFeedsAsync(
        int? pageSize = null, CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetSuggestedFeedsResponse, GeneratorView>(
            (cursor, ct) => GetSuggestedFeedsAsync(pageSize, cursor, ct),
            cancellationToken);

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
    /// <param name="feed">The AT-URI of the feed generator record.</param>
    /// <param name="limit">Max posts per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetFeedSkeletonResponse> GetFeedSkeletonAsync(
        AtUri feed, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("feed", feed)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetFeedSkeletonResponse>(
            "app.bsky.feed.getFeedSkeleton", parameters, cancellationToken: cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Search
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Search posts, one page at a time.
    /// </summary>
    /// <param name="q">Search query string.</param>
    /// <param name="sort">Sort order: "top" or "latest".</param>
    /// <param name="since">Filter to posts at or after this time: a datetime, or just an ISO date
    /// (<c>YYYY-MM-DD</c>).</param>
    /// <param name="until">Filter to posts before this time: a datetime, or just an ISO date
    /// (<c>YYYY-MM-DD</c>).</param>
    /// <param name="mentions">Filter to posts mentioning this account.</param>
    /// <param name="author">Filter to posts by this account.</param>
    /// <param name="lang">Filter by language (BCP-47).</param>
    /// <param name="domain">Filter by domain in post links.</param>
    /// <param name="url">Filter by URL in post links.</param>
    /// <param name="tags">Only posts with all of these hashtags (without <c>#</c>).</param>
    /// <param name="limit">Max results per page (1-100, default 25).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SearchPostsResponse> SearchPostsAsync(
        string q,
        string? sort = null,
        string? since = null,
        string? until = null,
        AtIdentifier? mentions = null,
        AtIdentifier? author = null,
        string? lang = null,
        string? domain = null,
        string? url = null,
        IEnumerable<string>? tags = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("q", q)
            .Add("sort", sort)
            .Add("since", since)
            .Add("until", until)
            .Add("mentions", mentions)
            .Add("author", author)
            .Add("lang", lang)
            .Add("domain", domain)
            .Add("url", url)
            .AddAll("tag", tags)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<SearchPostsResponse>(
            "app.bsky.feed.searchPosts", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every post matching a search, fetching pages as needed.
    /// </summary>
    /// <param name="q">Search query string.</param>
    /// <param name="sort">Sort order: "top" or "latest".</param>
    /// <param name="since">Filter to posts at or after this time: a datetime, or just an ISO date
    /// (<c>YYYY-MM-DD</c>).</param>
    /// <param name="until">Filter to posts before this time: a datetime, or just an ISO date
    /// (<c>YYYY-MM-DD</c>).</param>
    /// <param name="mentions">Filter to posts mentioning this account.</param>
    /// <param name="author">Filter to posts by this account.</param>
    /// <param name="lang">Filter by language (BCP-47).</param>
    /// <param name="domain">Filter by domain in post links.</param>
    /// <param name="url">Filter by URL in post links.</param>
    /// <param name="tags">Only posts with all of these hashtags (without <c>#</c>).</param>
    /// <param name="pageSize">Results per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<PostView> EnumerateSearchPostsAsync(
        string q,
        string? sort = null,
        string? since = null,
        string? until = null,
        AtIdentifier? mentions = null,
        AtIdentifier? author = null,
        string? lang = null,
        string? domain = null,
        string? url = null,
        IEnumerable<string>? tags = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<SearchPostsResponse, PostView>(
            (cursor, ct) => SearchPostsAsync(
                q, sort, since, until, mentions, author, lang, domain, url, tags, pageSize, cursor, ct),
            cancellationToken);
}
