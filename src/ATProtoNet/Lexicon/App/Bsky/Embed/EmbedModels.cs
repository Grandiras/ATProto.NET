using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.App.Bsky.Graph;
using ATProtoNet.Lexicon.App.Bsky.Labeler;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.App.Bsky.Embed;

// ──────────────────────────────────────────────────────────────
//  Embed types (used as post embeds when creating records)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Base type for embed objects attached to posts (the open <c>app.bsky.feed.post#embed</c> union).
/// An embed type this SDK does not model reads as <see cref="UnknownEmbed"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownEmbed))]
[JsonDerivedType(typeof(ImagesEmbed), "app.bsky.embed.images")]
[JsonDerivedType(typeof(ExternalEmbed), "app.bsky.embed.external")]
[JsonDerivedType(typeof(RecordEmbed), "app.bsky.embed.record")]
[JsonDerivedType(typeof(RecordWithMediaEmbed), "app.bsky.embed.recordWithMedia")]
[JsonDerivedType(typeof(VideoEmbed), "app.bsky.embed.video")]
[JsonDerivedType(typeof(GalleryEmbed), "app.bsky.embed.gallery")]
public abstract class EmbedBase : LexObject;

/// <summary>
/// An embed whose <c>$type</c> this SDK version does not model. It keeps the raw object and writes
/// it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownEmbed : EmbedBase, IUnknownUnionVariant
{
    /// <summary>Creates an unknown embed from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownEmbed(string type, JsonElement raw)
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
//  app.bsky.embed.images
// ──────────────────────────────────────────────────────────────

/// <summary>
/// An images embed containing up to 4 images.
/// </summary>
public sealed class ImagesEmbed : EmbedBase
{
    /// <summary>The images to embed (up to four).</summary>
    [JsonPropertyName("images")]
    public required IReadOnlyList<EmbedImage> Images { get; init; }
}

/// <summary>
/// A single image within an images embed.
/// </summary>
public sealed class EmbedImage : LexObject
{
    /// <summary>The uploaded blob reference for the image.</summary>
    [JsonPropertyName("image")]
    public required BlobRef Image { get; init; }

    /// <summary>Alt text / accessibility description.</summary>
    [JsonPropertyName("alt")]
    public required string Alt { get; init; }

    /// <summary>Optional aspect ratio for display.</summary>
    [JsonPropertyName("aspectRatio")]
    public AspectRatio? AspectRatio { get; init; }
}

/// <summary>
/// Aspect ratio hint for image display.
/// </summary>
public sealed class AspectRatio : LexObject
{
    /// <summary>The width in pixels.</summary>
    [JsonPropertyName("width")]
    public int Width { get; init; }

    /// <summary>The height in pixels.</summary>
    [JsonPropertyName("height")]
    public int Height { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  app.bsky.embed.external
// ──────────────────────────────────────────────────────────────

/// <summary>
/// An external link embed (link card / Open Graph preview).
/// </summary>
public sealed class ExternalEmbed : EmbedBase
{
    /// <summary>The external link preview.</summary>
    [JsonPropertyName("external")]
    public required ExternalInfo External { get; init; }
}

/// <summary>
/// External link metadata.
/// </summary>
public sealed class ExternalInfo : LexObject
{
    /// <summary>URL of the linked page.</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The title of the linked page.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Description of the linked page.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Optional thumbnail blob.</summary>
    [JsonPropertyName("thumb")]
    public BlobRef? Thumb { get; init; }

    /// <summary>
    /// Records the linked page is associated with, such as the <c>site.standard.document</c> it
    /// publishes.
    /// </summary>
    [JsonPropertyName("associatedRefs")]
    public IReadOnlyList<StrongRef>? AssociatedRefs { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  app.bsky.embed.record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A quote / embedded record reference.
/// </summary>
public sealed class RecordEmbed : EmbedBase
{
    /// <summary>A reference to the embedded record.</summary>
    [JsonPropertyName("record")]
    public required StrongRef Record { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  app.bsky.embed.recordWithMedia
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A record embed combined with media.
/// </summary>
public sealed class RecordWithMediaEmbed : EmbedBase
{
    /// <summary>The embedded record.</summary>
    [JsonPropertyName("record")]
    public required RecordEmbed Record { get; init; }

    /// <summary>
    /// The media: an <see cref="ImagesEmbed"/>, <see cref="VideoEmbed"/>, <see cref="ExternalEmbed"/>
    /// or <see cref="GalleryEmbed"/>.
    /// </summary>
    [JsonPropertyName("media")]
    public required EmbedBase Media { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  app.bsky.embed.video
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A video embed.
/// </summary>
public sealed class VideoEmbed : EmbedBase
{
    /// <summary>The uploaded video blob.</summary>
    [JsonPropertyName("video")]
    public required BlobRef Video { get; init; }

    /// <summary>Alt text describing the media for accessibility.</summary>
    [JsonPropertyName("alt")]
    public string? Alt { get; init; }

    /// <summary>
    /// The intrinsic aspect ratio of the media, used to lay out the placeholder before it loads.
    /// </summary>
    [JsonPropertyName("aspectRatio")]
    public AspectRatio? AspectRatio { get; init; }

    /// <summary>The caption tracks for the video.</summary>
    [JsonPropertyName("captions")]
    public IReadOnlyList<VideoCaption>? Captions { get; init; }

    /// <summary>
    /// How the video is presented: <c>default</c>, or <c>gif</c> for a looping, muted clip (see
    /// <see cref="VideoPresentation"/>).
    /// </summary>
    [JsonPropertyName("presentation")]
    public string? Presentation { get; init; }
}

/// <summary>
/// Known values of <see cref="VideoEmbed.Presentation"/> and <see cref="VideoView.Presentation"/>.
/// </summary>
public static class VideoPresentation
{
    /// <summary>A regular video.</summary>
    public const string Default = "default";

    /// <summary>A short looping clip without sound, shown like an animated GIF.</summary>
    public const string Gif = "gif";
}

/// <summary>
/// A video caption file reference.
/// </summary>
public sealed class VideoCaption : LexObject
{
    /// <summary>The BCP-47 language tag of the caption track.</summary>
    [JsonPropertyName("lang")]
    public required string Lang { get; init; }

    /// <summary>The uploaded caption file (WebVTT).</summary>
    [JsonPropertyName("file")]
    public required BlobRef File { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  app.bsky.embed.gallery
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A gallery embed: an assortment of media items. The Lexicon allows up to 20 items; clients should
/// currently limit authoring to 10.
/// </summary>
public sealed class GalleryEmbed : EmbedBase
{
    /// <summary>The media items, each of which may be of a different type.</summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<GalleryItem> Items { get; init; }
}

/// <summary>
/// One media item in a <see cref="GalleryEmbed"/> (the open <c>app.bsky.embed.gallery#main.items</c>
/// union). An item type this SDK does not model reads as <see cref="UnknownGalleryItem"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownGalleryItem))]
[JsonDerivedType(typeof(GalleryImage), "app.bsky.embed.gallery#image")]
public abstract class GalleryItem : LexObject;

/// <summary>
/// An image in a gallery embed. Unlike <see cref="EmbedImage"/>, alt text and aspect ratio are required.
/// </summary>
public sealed class GalleryImage : GalleryItem
{
    /// <summary>The uploaded image blob (<c>image/*</c>, at most 2,000,000 bytes).</summary>
    [JsonPropertyName("image")]
    public required BlobRef Image { get; init; }

    /// <summary>Alt text describing the image, for accessibility.</summary>
    [JsonPropertyName("alt")]
    public required string Alt { get; init; }

    /// <summary>The image's aspect ratio.</summary>
    [JsonPropertyName("aspectRatio")]
    public required AspectRatio AspectRatio { get; init; }
}

/// <summary>
/// A gallery item whose <c>$type</c> this SDK version does not model. It keeps the raw object and
/// writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownGalleryItem : GalleryItem, IUnknownUnionVariant
{
    /// <summary>Creates an unknown gallery item from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownGalleryItem(string type, JsonElement raw)
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
//  Embed view types (returned when reading posts)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Base type for embedded content views returned by the appview (the open
/// <c>app.bsky.feed.defs#postView.embed</c> union). An embed view this SDK does not model reads as
/// <see cref="UnknownEmbedView"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownEmbedView))]
[JsonDerivedType(typeof(ImagesView), "app.bsky.embed.images#view")]
[JsonDerivedType(typeof(ExternalView), "app.bsky.embed.external#view")]
[JsonDerivedType(typeof(RecordView), "app.bsky.embed.record#view")]
[JsonDerivedType(typeof(RecordWithMediaView), "app.bsky.embed.recordWithMedia#view")]
[JsonDerivedType(typeof(VideoView), "app.bsky.embed.video#view")]
[JsonDerivedType(typeof(GalleryView), "app.bsky.embed.gallery#view")]
public abstract class EmbedView : LexObject;

/// <summary>
/// An embed view whose <c>$type</c> this SDK version does not model. It keeps the raw object and
/// writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownEmbedView : EmbedView, IUnknownUnionVariant
{
    /// <summary>Creates an unknown embed view from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownEmbedView(string type, JsonElement raw)
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
/// View of an images embed.
/// </summary>
public sealed class ImagesView : EmbedView
{
    /// <summary>The embedded image views.</summary>
    [JsonPropertyName("images")]
    public required IReadOnlyList<ImageViewItem> Images { get; init; }
}

/// <summary>
/// A viewed image with thumbnails.
/// </summary>
public sealed class ImageViewItem : LexObject
{
    /// <summary>URL of the thumbnail image.</summary>
    [JsonPropertyName("thumb")]
    public required string Thumb { get; init; }

    /// <summary>URL of the full-size image.</summary>
    [JsonPropertyName("fullsize")]
    public required string Fullsize { get; init; }

    /// <summary>Alt text describing the media for accessibility.</summary>
    [JsonPropertyName("alt")]
    public required string Alt { get; init; }

    /// <summary>
    /// The intrinsic aspect ratio of the media, used to lay out the placeholder before it loads.
    /// </summary>
    [JsonPropertyName("aspectRatio")]
    public AspectRatio? AspectRatio { get; init; }
}

/// <summary>
/// View of an external link embed.
/// </summary>
public sealed class ExternalView : EmbedView
{
    /// <summary>The external link preview.</summary>
    [JsonPropertyName("external")]
    public required ExternalViewInfo External { get; init; }
}

/// <summary>
/// External link view metadata.
/// </summary>
public sealed class ExternalViewInfo : LexObject
{
    /// <summary>URL of the linked page.</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The title of the linked page.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Description of the linked page.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>URL of the thumbnail image.</summary>
    [JsonPropertyName("thumb")]
    public string? Thumb { get; init; }

    /// <summary>When the linked content was created, if known.</summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; init; }

    /// <summary>When the linked content was last updated, if known.</summary>
    [JsonPropertyName("updatedAt")]
    public AtDatetime? UpdatedAt { get; init; }

    /// <summary>The estimated reading time of the linked content, in minutes.</summary>
    [JsonPropertyName("readingTime")]
    public int? ReadingTime { get; init; }

    /// <summary>The labels applied to the linked content.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>The publication or site the linked content comes from.</summary>
    [JsonPropertyName("source")]
    public ExternalViewSource? Source { get; init; }

    /// <summary>Records the linked content is associated with.</summary>
    [JsonPropertyName("associatedRefs")]
    public IReadOnlyList<StrongRef>? AssociatedRefs { get; init; }

    /// <summary>Accounts associated with the linked content, such as its authors.</summary>
    [JsonPropertyName("associatedProfiles")]
    public IReadOnlyList<ProfileViewBasic>? AssociatedProfiles { get; init; }
}

/// <summary>
/// The publication or site linked content comes from
/// (<c>app.bsky.embed.external#viewExternalSource</c>).
/// </summary>
public sealed class ExternalViewSource : LexObject
{
    /// <summary>URL of the source.</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>URL of the source's icon.</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    /// <summary>The source's name.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>A description of the source.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The source's colors.</summary>
    [JsonPropertyName("theme")]
    public ExternalViewSourceTheme? Theme { get; init; }
}

/// <summary>
/// The colors of a linked content's source
/// (<c>app.bsky.embed.external#viewExternalSourceTheme</c>).
/// </summary>
public sealed class ExternalViewSourceTheme : LexObject
{
    /// <summary>The background color.</summary>
    [JsonPropertyName("backgroundRGB")]
    public ColorRgb? BackgroundRgb { get; init; }

    /// <summary>The text color.</summary>
    [JsonPropertyName("foregroundRGB")]
    public ColorRgb? ForegroundRgb { get; init; }

    /// <summary>The accent color, for links and buttons.</summary>
    [JsonPropertyName("accentRGB")]
    public ColorRgb? AccentRgb { get; init; }

    /// <summary>The color of text on the accent color.</summary>
    [JsonPropertyName("accentForegroundRGB")]
    public ColorRgb? AccentForegroundRgb { get; init; }
}

/// <summary>An RGB color (<c>app.bsky.embed.external#colorRGB</c>).</summary>
public sealed class ColorRgb : LexObject
{
    /// <summary>The red component, 0 to 255.</summary>
    [JsonPropertyName("r")]
    public required int R { get; init; }

    /// <summary>The green component, 0 to 255.</summary>
    [JsonPropertyName("g")]
    public required int G { get; init; }

    /// <summary>The blue component, 0 to 255.</summary>
    [JsonPropertyName("b")]
    public required int B { get; init; }
}

/// <summary>
/// View of a quoted record embed.
/// </summary>
public sealed class RecordView : EmbedView
{
    /// <summary>
    /// The embedded record: an <see cref="EmbeddedRecord"/> for a post, a placeholder when it
    /// cannot be shown, or the view of a feed generator, list, labeler or starter pack.
    /// </summary>
    [JsonPropertyName("record")]
    public required EmbeddedRecordView Record { get; init; }
}

/// <summary>
/// The record a record embed shows (the open union behind <see cref="RecordView.Record"/>). A
/// view this SDK does not model reads as <see cref="UnknownEmbeddedRecordView"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownEmbeddedRecordView))]
[JsonDerivedType(typeof(EmbeddedRecord), "app.bsky.embed.record#viewRecord")]
[JsonDerivedType(typeof(EmbeddedRecordNotFound), "app.bsky.embed.record#viewNotFound")]
[JsonDerivedType(typeof(EmbeddedRecordBlocked), "app.bsky.embed.record#viewBlocked")]
[JsonDerivedType(typeof(EmbeddedRecordDetached), "app.bsky.embed.record#viewDetached")]
[JsonDerivedType(typeof(GeneratorView), "app.bsky.feed.defs#generatorView")]
[JsonDerivedType(typeof(ListView), "app.bsky.graph.defs#listView")]
[JsonDerivedType(typeof(LabelerView), "app.bsky.labeler.defs#labelerView")]
[JsonDerivedType(typeof(StarterPackViewBasic), "app.bsky.graph.defs#starterPackViewBasic")]
public abstract class EmbeddedRecordView : LexObject;

/// <summary>
/// An embedded record view whose <c>$type</c> this SDK version does not model. It keeps the raw
/// object and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownEmbeddedRecordView : EmbeddedRecordView, IUnknownUnionVariant
{
    /// <summary>Creates an unknown embedded record view from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownEmbeddedRecordView(string type, JsonElement raw)
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
/// An embedded record, such as a quoted post (<c>app.bsky.embed.record#viewRecord</c>).
/// </summary>
public sealed class EmbeddedRecord : EmbeddedRecordView
{
    /// <summary>The AT-URI of the record.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID of the record version.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>The record's author.</summary>
    [JsonPropertyName("author")]
    public required ProfileViewBasic Author { get; init; }

    /// <summary>The record itself.</summary>
    [JsonPropertyName("value")]
    public required JsonElement Value { get; init; }

    /// <summary>The labels applied to the record.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>The number of replies.</summary>
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

    /// <summary>The views of the record's own embeds.</summary>
    [JsonPropertyName("embeds")]
    public IReadOnlyList<EmbedView>? Embeds { get; init; }

    /// <summary>Timestamp at which the app view indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public required AtDatetime IndexedAt { get; init; }
}

/// <summary>
/// An embedded record that was not found (<c>app.bsky.embed.record#viewNotFound</c>).
/// </summary>
public sealed class EmbeddedRecordNotFound : EmbeddedRecordView
{
    /// <summary>The AT-URI of the record.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>Always <see langword="true"/>.</summary>
    [JsonPropertyName("notFound")]
    public bool NotFound => true;
}

/// <summary>
/// An embedded record whose author blocks, or is blocked by, the viewer
/// (<c>app.bsky.embed.record#viewBlocked</c>).
/// </summary>
public sealed class EmbeddedRecordBlocked : EmbeddedRecordView
{
    /// <summary>The AT-URI of the record.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>Always <see langword="true"/>.</summary>
    [JsonPropertyName("blocked")]
    public bool Blocked => true;

    /// <summary>The record's author.</summary>
    [JsonPropertyName("author")]
    public required BlockedAuthor Author { get; init; }
}

/// <summary>
/// An embedded post its author detached from the quoting post
/// (<c>app.bsky.embed.record#viewDetached</c>).
/// </summary>
public sealed class EmbeddedRecordDetached : EmbeddedRecordView
{
    /// <summary>The AT-URI of the record.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>Always <see langword="true"/>.</summary>
    [JsonPropertyName("detached")]
    public bool Detached => true;
}

/// <summary>
/// View of a record-with-media embed.
/// </summary>
public sealed class RecordWithMediaView : EmbedView
{
    /// <summary>The embedded record view.</summary>
    [JsonPropertyName("record")]
    public required RecordView Record { get; init; }

    /// <summary>The media embedded alongside the record.</summary>
    [JsonPropertyName("media")]
    public required EmbedView Media { get; init; }
}

/// <summary>
/// View of a video embed.
/// </summary>
public sealed class VideoView : EmbedView
{
    /// <summary>The CID of the video blob.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>URL of the HLS playlist for the video.</summary>
    [JsonPropertyName("playlist")]
    public required string Playlist { get; init; }

    /// <summary>URL of the video thumbnail image.</summary>
    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; init; }

    /// <summary>Alt text describing the media for accessibility.</summary>
    [JsonPropertyName("alt")]
    public string? Alt { get; init; }

    /// <summary>
    /// The intrinsic aspect ratio of the media, used to lay out the placeholder before it loads.
    /// </summary>
    [JsonPropertyName("aspectRatio")]
    public AspectRatio? AspectRatio { get; init; }

    /// <summary>
    /// How the video is presented: <c>default</c>, or <c>gif</c> for a looping, muted clip (see
    /// <see cref="VideoPresentation"/>).
    /// </summary>
    [JsonPropertyName("presentation")]
    public string? Presentation { get; init; }
}

/// <summary>
/// View of a gallery embed.
/// </summary>
public sealed class GalleryView : EmbedView
{
    /// <summary>The media item views.</summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<GalleryViewItem> Items { get; init; }
}

/// <summary>
/// One media item in a <see cref="GalleryView"/> (the open <c>app.bsky.embed.gallery#view.items</c>
/// union). An item view this SDK does not model reads as <see cref="UnknownGalleryViewItem"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownGalleryViewItem))]
[JsonDerivedType(typeof(GalleryViewImage), "app.bsky.embed.gallery#viewImage")]
public abstract class GalleryViewItem : LexObject;

/// <summary>
/// A viewed image in a gallery embed.
/// </summary>
public sealed class GalleryViewImage : GalleryViewItem
{
    /// <summary>URL of a thumbnail of the image, typically on the appview's CDN.</summary>
    [JsonPropertyName("thumbnail")]
    public required string Thumbnail { get; init; }

    /// <summary>URL of a large version of the image. May or may not be the exact original blob.</summary>
    [JsonPropertyName("fullsize")]
    public required string Fullsize { get; init; }

    /// <summary>Alt text describing the image, for accessibility.</summary>
    [JsonPropertyName("alt")]
    public required string Alt { get; init; }

    /// <summary>The image's aspect ratio.</summary>
    [JsonPropertyName("aspectRatio")]
    public required AspectRatio AspectRatio { get; init; }
}

/// <summary>
/// A gallery item view whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownGalleryViewItem : GalleryViewItem, IUnknownUnionVariant
{
    /// <summary>Creates an unknown gallery item view from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownGalleryViewItem(string type, JsonElement raw)
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
//  getEmbedExternalView
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getEmbedExternalView. Every property is <see langword="null"/> when no record
/// resolved, or the records do not back the URL; render an ordinary link card then, and leave
/// <see cref="ExternalInfo.AssociatedRefs"/> unset.
/// </summary>
public sealed class GetEmbedExternalViewResponse
{
    /// <summary>The hydrated external embed view, its <c>uri</c> the requested URL.</summary>
    [JsonPropertyName("view")]
    public ExternalView? View { get; init; }

    /// <summary>
    /// Strong references to the records behind the view, for the post's
    /// <see cref="ExternalInfo.AssociatedRefs"/>.
    /// </summary>
    [JsonPropertyName("associatedRefs")]
    public IReadOnlyList<StrongRef>? AssociatedRefs { get; init; }

    /// <summary>
    /// The records behind the view, such as a <c>site.standard.document</c> and its
    /// publication, so that they need not be fetched again.
    /// </summary>
    [JsonPropertyName("associatedRecords")]
    public IReadOnlyList<JsonElement>? AssociatedRecords { get; init; }
}
