using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.App.Bsky.Bookmark;

/// <summary>
/// Client for app.bsky.bookmark.* XRPC endpoints: the authenticated account's private
/// bookmarks.
/// </summary>
/// <remarks>
/// Bookmarks are not repository records: the appview keeps them in private storage, so they are
/// visible only to their owner. Only posts can be bookmarked.
/// </remarks>
public sealed class BookmarkClient
{
    private readonly XrpcClient _xrpc;

    internal BookmarkClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Bookmark a post.
    /// </summary>
    /// <param name="uri">The AT-URI of the post.</param>
    /// <param name="cid">The CID of the post version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">
    /// <see cref="BookmarkErrors.UnsupportedCollection"/> when <paramref name="uri"/> is not a post.
    /// </exception>
    public Task CreateBookmarkAsync(AtUri uri, Cid cid, CancellationToken cancellationToken = default)
    {
        var request = new CreateBookmarkRequest { Uri = uri, Cid = cid };
        return _xrpc.ProcedureAsync(
            "app.bsky.bookmark.createBookmark", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Remove the bookmark of a post.
    /// </summary>
    /// <param name="uri">The AT-URI of the bookmarked post.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task DeleteBookmarkAsync(AtUri uri, CancellationToken cancellationToken = default)
    {
        var request = new DeleteBookmarkRequest { Uri = uri };
        return _xrpc.ProcedureAsync(
            "app.bsky.bookmark.deleteBookmark", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get one page of the authenticated account's bookmarks.
    /// </summary>
    /// <param name="limit">Max bookmarks per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetBookmarksResponse> GetBookmarksAsync(
        int? limit = null, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetBookmarksResponse>(
            "app.bsky.bookmark.getBookmarks", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the authenticated account's bookmarks, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Bookmarks per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<BookmarkView> EnumerateBookmarksAsync(
        int? pageSize = null, CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetBookmarksResponse, BookmarkView>(
            (cursor, ct) => GetBookmarksAsync(pageSize, cursor, ct),
            cancellationToken);
}
