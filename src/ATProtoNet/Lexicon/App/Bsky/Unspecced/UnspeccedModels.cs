using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.App.Bsky.Unspecced;

// ──────────────────────────────────────────────────────────────
//  getPostThreadV2 / getPostThreadOtherV2
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getPostThreadV2.
/// </summary>
public sealed class GetPostThreadV2Response
{
    /// <summary>
    /// The thread as a flat list, in display order. Each item's <see cref="ThreadItem.Depth"/>
    /// places it: 0 is the anchor, parents are negative, replies positive.
    /// </summary>
    [JsonPropertyName("thread")]
    public required IReadOnlyList<ThreadItem> Thread { get; init; }

    /// <summary>The threadgate of the thread's root post, if it has one.</summary>
    [JsonPropertyName("threadgate")]
    public ThreadgateView? Threadgate { get; init; }

    /// <summary>
    /// Whether the thread has further replies, such as ones hidden by the threadgate, that
    /// <see cref="UnspeccedClient.GetPostThreadOtherV2Async"/> returns.
    /// </summary>
    [JsonPropertyName("hasOtherReplies")]
    public required bool HasOtherReplies { get; init; }
}

/// <summary>
/// Response from getPostThreadOtherV2.
/// </summary>
public sealed class GetPostThreadOtherV2Response
{
    /// <summary>The further replies as a flat list; each item's value is a <see cref="ThreadItemPost"/>.</summary>
    [JsonPropertyName("thread")]
    public required IReadOnlyList<ThreadItem> Thread { get; init; }
}

/// <summary>
/// One item of a flat thread (<c>app.bsky.unspecced.getPostThreadV2#threadItem</c>, and the
/// same shape in getPostThreadOtherV2).
/// </summary>
public sealed class ThreadItem : LexObject
{
    /// <summary>The AT-URI of the post.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The nesting level: 0 is the anchor, parents are negative, replies positive.</summary>
    [JsonPropertyName("depth")]
    public required int Depth { get; init; }

    /// <summary>
    /// The post, or why it is not shown: a <see cref="ThreadItemPost"/>,
    /// <see cref="ThreadItemNoUnauthenticated"/>, <see cref="ThreadItemNotFound"/> or
    /// <see cref="ThreadItemBlocked"/>.
    /// </summary>
    [JsonPropertyName("value")]
    public required ThreadItemValue Value { get; init; }
}

/// <summary>
/// The content of a <see cref="ThreadItem"/> (the open union behind
/// <see cref="ThreadItem.Value"/>). A variant this SDK does not model reads as
/// <see cref="UnknownThreadItemValue"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownThreadItemValue))]
[JsonDerivedType(typeof(ThreadItemPost), "app.bsky.unspecced.defs#threadItemPost")]
[JsonDerivedType(typeof(ThreadItemNoUnauthenticated), "app.bsky.unspecced.defs#threadItemNoUnauthenticated")]
[JsonDerivedType(typeof(ThreadItemNotFound), "app.bsky.unspecced.defs#threadItemNotFound")]
[JsonDerivedType(typeof(ThreadItemBlocked), "app.bsky.unspecced.defs#threadItemBlocked")]
public abstract class ThreadItemValue : LexObject;

/// <summary>
/// A thread item value whose <c>$type</c> this SDK version does not model. It keeps the raw
/// object and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownThreadItemValue : ThreadItemValue, IUnknownUnionVariant
{
    /// <summary>Creates an unknown thread item value from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownThreadItemValue(string type, JsonElement raw)
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
/// A post in a flat thread (<c>app.bsky.unspecced.defs#threadItemPost</c>).
/// </summary>
public sealed class ThreadItemPost : ThreadItemValue
{
    /// <summary>The post.</summary>
    [JsonPropertyName("post")]
    public required PostView Post { get; init; }

    /// <summary>Whether the post has parents the response left out.</summary>
    [JsonPropertyName("moreParents")]
    public required bool MoreParents { get; init; }

    /// <summary>A best-effort count of the post's replies the response left out.</summary>
    [JsonPropertyName("moreReplies")]
    public required int MoreReplies { get; init; }

    /// <summary>
    /// Whether the post is part of the original poster's contiguous thread from the root.
    /// </summary>
    [JsonPropertyName("opThread")]
    public required bool OpThread { get; init; }

    /// <summary>The post's 1-based position in that thread, when it is part of one.</summary>
    [JsonPropertyName("opThreadPostIndex")]
    public int? OpThreadPostIndex { get; init; }

    /// <summary>The number of posts in that thread, when the post is part of one.</summary>
    [JsonPropertyName("opThreadPostCount")]
    public int? OpThreadPostCount { get; init; }

    /// <summary>Whether the thread author's threadgate hides the post for everyone.</summary>
    [JsonPropertyName("hiddenByThreadgate")]
    public required bool HiddenByThreadgate { get; init; }

    /// <summary>Whether the post is by an account the viewer muted.</summary>
    [JsonPropertyName("mutedByViewer")]
    public required bool MutedByViewer { get; init; }
}

/// <summary>
/// A post shown only to signed-in viewers, whose author asked that logged-out visitors not see
/// it (<c>app.bsky.unspecced.defs#threadItemNoUnauthenticated</c>).
/// </summary>
public sealed class ThreadItemNoUnauthenticated : ThreadItemValue;

/// <summary>
/// A post that could not be found (<c>app.bsky.unspecced.defs#threadItemNotFound</c>).
/// </summary>
public sealed class ThreadItemNotFound : ThreadItemValue;

/// <summary>
/// A post hidden by a block (<c>app.bsky.unspecced.defs#threadItemBlocked</c>).
/// </summary>
public sealed class ThreadItemBlocked : ThreadItemValue
{
    /// <summary>The post's author, and the viewer's relationship to them.</summary>
    [JsonPropertyName("author")]
    public required BlockedAuthor Author { get; init; }
}

/// <summary>
/// Known values of the <c>sort</c> parameter of getPostThreadV2.
/// </summary>
public static class PostThreadSort
{
    /// <summary>Newest replies first.</summary>
    public const string Newest = "newest";

    /// <summary>Oldest replies first (the default).</summary>
    public const string Oldest = "oldest";

    /// <summary>The most engaged-with replies first.</summary>
    public const string Top = "top";
}
