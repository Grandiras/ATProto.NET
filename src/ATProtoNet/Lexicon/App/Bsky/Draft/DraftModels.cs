using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.App.Bsky.Draft;

// ──────────────────────────────────────────────────────────────
//  Drafts
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A draft of a post or thread (<c>app.bsky.draft.defs#draft</c>). Media are referenced by
/// on-device paths, so a draft's embeds are only usable on the device that made it.
/// </summary>
public sealed class Draft : LexObject
{
    /// <summary>The UUIDv4 identifier of the device that created the draft.</summary>
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; init; }

    /// <summary>The device or platform the draft was created on.</summary>
    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; init; }

    /// <summary>The posts, in thread order (1 to 100).</summary>
    [JsonPropertyName("posts")]
    public required IReadOnlyList<DraftPost> Posts { get; init; }

    /// <summary>The primary languages of the posts' text (BCP-47, at most 3).</summary>
    [JsonPropertyName("langs")]
    public IReadOnlyList<string>? Langs { get; init; }

    /// <summary>The embedding rules of the postgate to create on publishing (at most 5).</summary>
    [JsonPropertyName("postgateEmbeddingRules")]
    public IReadOnlyList<PostgateEmbeddingRule>? PostgateEmbeddingRules { get; init; }

    /// <summary>The reply rules of the threadgate to create on publishing (at most 5).</summary>
    [JsonPropertyName("threadgateAllow")]
    public IReadOnlyList<ThreadgateRule>? ThreadgateAllow { get; init; }
}

/// <summary>
/// One post of a draft (<c>app.bsky.draft.defs#draftPost</c>).
/// </summary>
public sealed class DraftPost : LexObject
{
    /// <summary>
    /// The text (at most 1000 graphemes), which may be longer than a post allows, to be split
    /// or shortened before publishing.
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>Self-applied labels (content warnings).</summary>
    [JsonPropertyName("labels")]
    public SelfLabels? Labels { get; init; }

    /// <summary>Images to embed (at most 4).</summary>
    [JsonPropertyName("embedImages")]
    public IReadOnlyList<DraftEmbedImage>? EmbedImages { get; init; }

    /// <summary>A gallery to embed.</summary>
    [JsonPropertyName("embedGallery")]
    public DraftEmbedGallery? EmbedGallery { get; init; }

    /// <summary>A video to embed (at most 1).</summary>
    [JsonPropertyName("embedVideos")]
    public IReadOnlyList<DraftEmbedVideo>? EmbedVideos { get; init; }

    /// <summary>A link card to embed (at most 1).</summary>
    [JsonPropertyName("embedExternals")]
    public IReadOnlyList<DraftEmbedExternal>? EmbedExternals { get; init; }

    /// <summary>A record to quote (at most 1).</summary>
    [JsonPropertyName("embedRecords")]
    public IReadOnlyList<DraftEmbedRecord>? EmbedRecords { get; init; }
}

/// <summary>
/// A reference to a file on the device that made a draft
/// (<c>app.bsky.draft.defs#draftEmbedLocalRef</c>).
/// </summary>
public sealed class DraftEmbedLocalRef : LexObject
{
    /// <summary>The file's on-device path.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }
}

/// <summary>
/// A caption track of a draft video (<c>app.bsky.draft.defs#draftEmbedCaption</c>).
/// </summary>
public sealed class DraftEmbedCaption : LexObject
{
    /// <summary>The caption language (BCP-47).</summary>
    [JsonPropertyName("lang")]
    public required string Lang { get; init; }

    /// <summary>The caption content.</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

/// <summary>
/// A gallery in a draft post (<c>app.bsky.draft.defs#draftEmbedGallery</c>).
/// </summary>
public sealed class DraftEmbedGallery : LexObject
{
    /// <summary>The gallery's items (at most 20; clients offer 10).</summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<DraftGalleryItem> Items { get; init; }
}

/// <summary>
/// An item of a draft gallery (the open union behind <see cref="DraftEmbedGallery.Items"/>). An
/// item type this SDK does not model reads as <see cref="UnknownDraftGalleryItem"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownDraftGalleryItem))]
[JsonDerivedType(typeof(DraftEmbedImage), "app.bsky.draft.defs#draftEmbedImage")]
public abstract class DraftGalleryItem : LexObject;

/// <summary>
/// A draft gallery item whose <c>$type</c> this SDK version does not model. It keeps the raw
/// object and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownDraftGalleryItem : DraftGalleryItem, IUnknownUnionVariant
{
    /// <summary>Creates an unknown gallery item from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownDraftGalleryItem(string type, JsonElement raw)
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
/// An image in a draft post or gallery (<c>app.bsky.draft.defs#draftEmbedImage</c>).
/// </summary>
public sealed class DraftEmbedImage : DraftGalleryItem
{
    /// <summary>The image file on the device.</summary>
    [JsonPropertyName("localRef")]
    public required DraftEmbedLocalRef LocalRef { get; init; }

    /// <summary>The alt text.</summary>
    [JsonPropertyName("alt")]
    public string? Alt { get; init; }
}

/// <summary>
/// A video in a draft post (<c>app.bsky.draft.defs#draftEmbedVideo</c>).
/// </summary>
public sealed class DraftEmbedVideo : LexObject
{
    /// <summary>The video file on the device.</summary>
    [JsonPropertyName("localRef")]
    public required DraftEmbedLocalRef LocalRef { get; init; }

    /// <summary>The alt text.</summary>
    [JsonPropertyName("alt")]
    public string? Alt { get; init; }

    /// <summary>Caption tracks (at most 20).</summary>
    [JsonPropertyName("captions")]
    public IReadOnlyList<DraftEmbedCaption>? Captions { get; init; }
}

/// <summary>
/// A link card in a draft post (<c>app.bsky.draft.defs#draftEmbedExternal</c>).
/// </summary>
public sealed class DraftEmbedExternal : LexObject
{
    /// <summary>The linked URL.</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}

/// <summary>
/// A quoted record in a draft post (<c>app.bsky.draft.defs#draftEmbedRecord</c>).
/// </summary>
public sealed class DraftEmbedRecord : LexObject
{
    /// <summary>A strong reference to the quoted record.</summary>
    [JsonPropertyName("record")]
    public required StrongRef Record { get; init; }
}

/// <summary>
/// A stored draft (<c>app.bsky.draft.defs#draftView</c>).
/// </summary>
public sealed class DraftView : LexObject
{
    /// <summary>The draft's identifier.</summary>
    [JsonPropertyName("id")]
    public required Tid Id { get; init; }

    /// <summary>The draft.</summary>
    [JsonPropertyName("draft")]
    public required Draft Draft { get; init; }

    /// <summary>When the draft was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>When the draft was last updated.</summary>
    [JsonPropertyName("updatedAt")]
    public required AtDatetime UpdatedAt { get; init; }
}

/// <summary>
/// A draft with its identifier (<c>app.bsky.draft.defs#draftWithId</c>).
/// </summary>
internal sealed class DraftWithId
{
    /// <summary>The draft's identifier.</summary>
    [JsonPropertyName("id")]
    public required Tid Id { get; init; }

    /// <summary>The draft.</summary>
    [JsonPropertyName("draft")]
    public required Draft Draft { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  API requests and responses
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for createDraft.
/// </summary>
internal sealed class CreateDraftRequest
{
    /// <summary>The draft to store.</summary>
    [JsonPropertyName("draft")]
    public required Draft Draft { get; init; }
}

/// <summary>
/// Response from createDraft.
/// </summary>
internal sealed class CreateDraftResponse
{
    /// <summary>The new draft's identifier.</summary>
    [JsonPropertyName("id")]
    public required Tid Id { get; init; }
}

/// <summary>
/// Request body for updateDraft.
/// </summary>
internal sealed class UpdateDraftRequest
{
    /// <summary>The draft and the identifier it is stored under.</summary>
    [JsonPropertyName("draft")]
    public required DraftWithId Draft { get; init; }
}

/// <summary>
/// Request body for deleteDraft.
/// </summary>
internal sealed class DeleteDraftRequest
{
    /// <summary>The identifier of the draft to delete.</summary>
    [JsonPropertyName("id")]
    public required Tid Id { get; init; }
}

/// <summary>
/// Response from getDrafts.
/// </summary>
public sealed class GetDraftsResponse : ICursorPage<DraftView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The drafts.</summary>
    [JsonPropertyName("drafts")]
    public required IReadOnlyList<DraftView> Drafts { get; init; }

    IReadOnlyList<DraftView> ICursorPage<DraftView>.Items => Drafts;
}

/// <summary>
/// Error names the <c>app.bsky.draft.*</c> methods declare, for matching with
/// <see cref="Http.XrpcException.Is"/>.
/// </summary>
public static class DraftErrors
{
    /// <summary>The account already has as many drafts as it may.</summary>
    public const string DraftLimitReached = "DraftLimitReached";
}
