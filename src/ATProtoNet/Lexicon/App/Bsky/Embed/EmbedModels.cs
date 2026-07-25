using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.App.Bsky.Embed;

// ──────────────────────────────────────────────────────────────
//  Embed types (used as post embeds when creating records)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Base type for embed objects attached to posts.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ImagesEmbed), "app.bsky.embed.images")]
[JsonDerivedType(typeof(ExternalEmbed), "app.bsky.embed.external")]
[JsonDerivedType(typeof(RecordEmbed), "app.bsky.embed.record")]
[JsonDerivedType(typeof(RecordWithMediaEmbed), "app.bsky.embed.recordWithMedia")]
[JsonDerivedType(typeof(VideoEmbed), "app.bsky.embed.video")]
public abstract class EmbedBase { }

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
public sealed class EmbedImage
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
public sealed class AspectRatio
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
public sealed class ExternalInfo
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
/// A record embed combined with media (images or external link).
/// </summary>
public sealed class RecordWithMediaEmbed : EmbedBase
{
    /// <summary>The embedded record.</summary>
    [JsonPropertyName("record")]
    public required RecordEmbed Record { get; init; }

    /// <summary>The media embed (images or external). Must be ImagesEmbed or ExternalEmbed.</summary>
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
public sealed class VideoCaption
{
    /// <summary>The BCP-47 language tag of the caption track.</summary>
    [JsonPropertyName("lang")]
    public required string Lang { get; init; }

    /// <summary>The uploaded caption file (WebVTT).</summary>
    [JsonPropertyName("file")]
    public required BlobRef File { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Embed view types (returned when reading posts)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Base type for embedded content views (returned from server).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ImagesView), "app.bsky.embed.images#view")]
[JsonDerivedType(typeof(ExternalView), "app.bsky.embed.external#view")]
[JsonDerivedType(typeof(RecordView), "app.bsky.embed.record#view")]
[JsonDerivedType(typeof(RecordWithMediaView), "app.bsky.embed.recordWithMedia#view")]
[JsonDerivedType(typeof(VideoView), "app.bsky.embed.video#view")]
public abstract class EmbedView { }

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
public sealed class ImageViewItem
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
public sealed class ExternalViewInfo
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
