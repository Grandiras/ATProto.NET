using System.Text.Json;
using System.Text.Json.Serialization;
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
    public required List<EmbedImage> Images { get; init; }
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
    public List<VideoCaption>? Captions { get; init; }
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
    public required List<GalleryItem> Items { get; init; }
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
    public required List<ImageViewItem> Images { get; init; }
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
}

/// <summary>
/// View of a quoted record embed.
/// </summary>
public sealed class RecordView : EmbedView
{
    /// <summary>The embedded record view.</summary>
    [JsonPropertyName("record")]
    public required JsonElement Record { get; init; }
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
    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

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
}

/// <summary>
/// View of a gallery embed.
/// </summary>
public sealed class GalleryView : EmbedView
{
    /// <summary>The media item views.</summary>
    [JsonPropertyName("items")]
    public required List<GalleryViewItem> Items { get; init; }
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
