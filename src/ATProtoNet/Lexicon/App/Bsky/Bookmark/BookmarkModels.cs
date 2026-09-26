using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.App.Bsky.Bookmark;

// ──────────────────────────────────────────────────────────────
//  Views
// ──────────────────────────────────────────────────────────────

/// <summary>
/// One of the viewer's bookmarks (<c>app.bsky.bookmark.defs#bookmarkView</c>).
/// </summary>
public sealed class BookmarkView : LexObject
{
    /// <summary>A strong reference to the bookmarked record.</summary>
    [JsonPropertyName("subject")]
    public required StrongRef Subject { get; init; }

    /// <summary>When the bookmark was created.</summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; init; }

    /// <summary>
    /// The bookmarked post: a <see cref="PostView"/>, or a <see cref="NotFoundPost"/> or
    /// <see cref="BlockedPost"/> placeholder when it can no longer be shown.
    /// </summary>
    [JsonPropertyName("item")]
    public required PostEntry Item { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  API requests and responses
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for createBookmark.
/// </summary>
internal sealed class CreateBookmarkRequest
{
    /// <summary>The AT-URI of the post to bookmark.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID of the post version to bookmark.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }
}

/// <summary>
/// Request body for deleteBookmark.
/// </summary>
internal sealed class DeleteBookmarkRequest
{
    /// <summary>The AT-URI of the bookmarked post.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }
}

/// <summary>
/// Response from getBookmarks.
/// </summary>
public sealed class GetBookmarksResponse : ICursorPage<BookmarkView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The bookmarks, newest first.</summary>
    [JsonPropertyName("bookmarks")]
    public required IReadOnlyList<BookmarkView> Bookmarks { get; init; }

    IReadOnlyList<BookmarkView> ICursorPage<BookmarkView>.Items => Bookmarks;
}

/// <summary>
/// Error names the <c>app.bsky.bookmark.*</c> methods declare, for matching with
/// <see cref="Http.XrpcException.Is"/>.
/// </summary>
public static class BookmarkErrors
{
    /// <summary>The URI names a collection that cannot be bookmarked; only posts can.</summary>
    public const string UnsupportedCollection = "UnsupportedCollection";
}
