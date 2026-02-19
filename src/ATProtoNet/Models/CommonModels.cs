using System.Text.Json.Serialization;
using ATProtoNet.Identity;

namespace ATProtoNet.Models;

/// <summary>
/// Represents a blob reference in AT Protocol.
/// Blobs are binary data (images, videos, etc.) stored alongside records.
/// </summary>
public sealed class BlobRef
{
    /// <summary>
    /// The type discriminator. Always "blob".
    /// </summary>
    [JsonPropertyName("$type")]
    public string Type { get; init; } = "blob";

    /// <summary>
    /// Content-addressed reference to the blob data.
    /// </summary>
    [JsonPropertyName("ref")]
    public BlobLink? Ref { get; init; }

    /// <summary>
    /// MIME type of the blob.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string MimeType { get; init; } = string.Empty;

    /// <summary>
    /// Size of the blob in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; init; }
}

/// <summary>
/// A CID link within a blob reference.
/// </summary>
public sealed class BlobLink
{
    /// <summary>
    /// The CID link string.
    /// </summary>
    [JsonPropertyName("$link")]
    public string Link { get; init; } = string.Empty;
}

/// <summary>
/// Represents a CID link in AT Protocol JSON data.
/// Used for content-addressed references within records.
/// </summary>
public sealed class CidLink
{
    /// <summary>
    /// The CID link string.
    /// </summary>
    [JsonPropertyName("$link")]
    public string Link { get; init; } = string.Empty;

    /// <summary>
    /// Creates a CidLink from a CID.
    /// </summary>
    public static CidLink FromCid(Cid cid) => new() { Link = cid.Value };
}

/// <summary>
/// A strong reference to a specific record, including both URI and CID.
/// This is used when you need to reference a specific version of a record.
/// </summary>
public sealed class StrongRef
{
    /// <summary>
    /// The AT URI of the record.
    /// </summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>
    /// The CID of the specific version of the record.
    /// </summary>
    [JsonPropertyName("cid")]
    public string Cid { get; init; } = string.Empty;
}

/// <summary>
/// Represents labels applied to content for moderation/classification.
/// </summary>
public sealed class Label
{
    /// <summary>
    /// The version of the label format.
    /// </summary>
    [JsonPropertyName("ver")]
    public int? Version { get; init; }

    /// <summary>
    /// DID of the labeler who created this label.
    /// </summary>
    [JsonPropertyName("src")]
    public string Src { get; init; } = string.Empty;

    /// <summary>
    /// AT URI of the subject being labeled.
    /// </summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>
    /// CID of the version of the subject, if applicable.
    /// </summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; init; }

    /// <summary>
    /// The label value/name (e.g., "nsfw", "spam").
    /// </summary>
    [JsonPropertyName("val")]
    public string Val { get; init; } = string.Empty;

    /// <summary>
    /// Whether this is a negation label (removes a previous label).
    /// </summary>
    [JsonPropertyName("neg")]
    public bool? Neg { get; init; }

    /// <summary>
    /// Timestamp when the label was created.
    /// </summary>
    [JsonPropertyName("cts")]
    public string Cts { get; init; } = string.Empty;

    /// <summary>
    /// Timestamp when the label expires, if applicable.
    /// </summary>
    [JsonPropertyName("exp")]
    public string? Exp { get; init; }

    /// <summary>
    /// Signature of the label, as bytes.
    /// </summary>
    [JsonPropertyName("sig")]
    public byte[]? Sig { get; init; }
}

/// <summary>
/// Represents pagination cursor for XRPC responses.
/// </summary>
public interface ICursoredResponse
{
    /// <summary>
    /// Cursor for the next page of results. Null when no more results.
    /// </summary>
    string? Cursor { get; }
}
