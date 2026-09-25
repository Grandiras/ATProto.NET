using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Site.Standard.Document;

// ──────────────────────────────────────────────────────────────
//  Document record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Represents a Standard.site document — an individual published document or blog post.
/// </summary>
public sealed class DocumentRecord : LexObject
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
    public required AtDatetime PublishedAt { get; init; }

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
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Timestamp of the document's last edit.</summary>
    [JsonPropertyName("updatedAt")]
    public AtDatetime? UpdatedAt { get; init; }

    /// <summary>The people who contributed to the document.</summary>
    [JsonPropertyName("contributors")]
    public IReadOnlyList<DocumentContributor>? Contributors { get; init; }

    /// <summary>
    /// Open union describing how the document relates to external resources. Each entry must
    /// specify a <c>$type</c>.
    /// </summary>
    [JsonPropertyName("links")]
    public JsonElement? Links { get; init; }

    /// <summary>Self-applied labels on the document.</summary>
    [JsonPropertyName("labels")]
    public SelfLabels? Labels { get; init; }
}

/// <summary>
/// A contributor to a document (<c>site.standard.document#contributor</c>).
/// </summary>
public sealed class DocumentContributor : LexObject
{
    /// <summary>The contributor's DID.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The contributor's role, such as author or editor (at most 100 graphemes).</summary>
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    /// <summary>The name to show for the contributor (at most 100 graphemes).</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }
}
