using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.App.Bsky.RichText;

/// <summary>
/// A rich-text facet annotation applied to a range of bytes in text.
/// </summary>
public sealed class Facet
{
    /// <summary>The byte range this facet annotates (UTF-8 byte offsets).</summary>
    [JsonPropertyName("index")]
    public required FacetIndex Index { get; init; }

    /// <summary>The features (annotations) applied to this range.</summary>
    [JsonPropertyName("features")]
    public required List<FacetFeature> Features { get; init; }
}

/// <summary>
/// Byte range within UTF-8 encoded text.
/// </summary>
public sealed class FacetIndex
{
    /// <summary>Start byte offset (inclusive).</summary>
    [JsonPropertyName("byteStart")]
    public int ByteStart { get; init; }

    /// <summary>End byte offset (exclusive).</summary>
    [JsonPropertyName("byteEnd")]
    public int ByteEnd { get; init; }
}

/// <summary>
/// Base type for facet features.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(MentionFeature), "app.bsky.richtext.facet#mention")]
[JsonDerivedType(typeof(LinkFeature), "app.bsky.richtext.facet#link")]
[JsonDerivedType(typeof(TagFeature), "app.bsky.richtext.facet#tag")]
public abstract class FacetFeature { }

/// <summary>
/// A mention of another user.
/// </summary>
public sealed class MentionFeature : FacetFeature
{
    /// <summary>The DID of the mentioned user.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// A hyperlink to an external URL.
/// </summary>
public sealed class LinkFeature : FacetFeature
{
    /// <summary>The URL being linked to.</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}

/// <summary>
/// A hashtag reference.
/// </summary>
public sealed class TagFeature : FacetFeature
{
    /// <summary>The tag text (without the # prefix).</summary>
    [JsonPropertyName("tag")]
    public required string Tag { get; init; }
}
