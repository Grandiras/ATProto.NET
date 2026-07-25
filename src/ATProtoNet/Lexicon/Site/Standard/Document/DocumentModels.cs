using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Site.Standard.Document;

// ──────────────────────────────────────────────────────────────
//  Document record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Represents a Standard.site document — an individual published document or blog post.
/// </summary>
public sealed class DocumentRecord
{
    /// <summary>The Lexicon type discriminator (<c>site.standard.document</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "site.standard.document";

    /// <summary>
    /// Points to a publication record (at://) or a publication URL (https://) for loose documents.
    /// Avoid trailing slashes.
    /// </summary>
    [JsonPropertyName("site")]
    public required string Site { get; init; }

    /// <summary>Title of the document.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Timestamp of the document's publish time.</summary>
    [JsonPropertyName("publishedAt")]
    public required string PublishedAt { get; init; }

    /// <summary>
    /// Combine with site or publication URL to construct a canonical URL.
    /// Should include a leading slash.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>A brief description or excerpt from the document.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Image used for thumbnail or cover image. Less than 1 MB.</summary>
    [JsonPropertyName("coverImage")]
    public BlobRef? CoverImage { get; init; }

    /// <summary>
    /// Open union used to define the record's content. Each entry must specify a $type.
    /// </summary>
    [JsonPropertyName("content")]
    public JsonElement? Content { get; init; }

    /// <summary>
    /// Plaintext representation of the document's contents.
    /// Should not contain markdown or other formatting.
    /// </summary>
    [JsonPropertyName("textContent")]
    public string? TextContent { get; init; }

    /// <summary>Strong reference to a Bluesky post for off-platform comments.</summary>
    [JsonPropertyName("bskyPostRef")]
    public StrongRef? BskyPostRef { get; init; }

    /// <summary>Tags to categorize the document. Avoid prepending with hashtags.</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; init; }

    /// <summary>Timestamp of the document's last edit.</summary>
    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; init; }
}
