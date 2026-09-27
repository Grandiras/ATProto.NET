using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.App.Bsky.Bookmark;

// ── Views ────────────────────────────────────────────────────

/// <summary>One of the viewer's bookmarks (<c>app.bsky.bookmark.defs#bookmarkView</c>).</summary>
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

// ── API requests and responses ───────────────────────────────

/// <summary>Request body for createBookmark.</summary>
internal sealed record CreateBookmarkRequest(
    [property: JsonPropertyName("uri")] AtUri Uri,
    [property: JsonPropertyName("cid")] Cid Cid);

/// <summary>Request body for deleteBookmark.</summary>
internal sealed record DeleteBookmarkRequest([property: JsonPropertyName("uri")] AtUri Uri);

/// <summary>Response from getBookmarks.</summary>
public sealed record GetBookmarksResponse : CursorPage<BookmarkView>
{
    /// <summary>The bookmarks, newest first.</summary>
    [JsonPropertyName("bookmarks")]
    public required IReadOnlyList<BookmarkView> Bookmarks { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<BookmarkView> Items => Bookmarks;
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
