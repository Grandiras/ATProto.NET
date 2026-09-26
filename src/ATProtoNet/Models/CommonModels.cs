using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;

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
    public CidLink? Ref { get; init; }

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
/// Represents a CID link in AT Protocol JSON data (<c>{"$link": "…"}</c>).
/// Used for content-addressed references within records, such as a blob's <see cref="BlobRef.Ref"/>.
/// </summary>
public sealed class CidLink
{
    /// <summary>
    /// The linked CID.
    /// </summary>
    [JsonPropertyName("$link")]
    public required Cid Link { get; init; }

    /// <summary>
    /// Creates a CidLink from a CID.
    /// </summary>
    public static CidLink FromCid(Cid cid) => new() { Link = cid };
}

/// <summary>
/// A strong reference to a specific record, including both URI and CID.
/// This is used when you need to reference a specific version of a record.
/// </summary>
public sealed class StrongRef : LexObject
{
    /// <summary>
    /// The AT URI of the record.
    /// </summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>
    /// The CID of the specific version of the record.
    /// </summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }
}

/// <summary>
/// Represents labels applied to content for moderation/classification.
/// </summary>
public sealed class Label : LexObject
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
    public required Did Src { get; init; }

    /// <summary>
    /// The subject being labeled: an AT URI for a record, or a DID for an account.
    /// </summary>
    /// <remarks>
    /// The Lexicon format is the generic <c>uri</c>, not <c>at-uri</c>, because an account
    /// label's subject is a bare DID; the property stays a <see cref="string"/>.
    /// </remarks>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>
    /// CID of the version of the subject, if applicable.
    /// </summary>
    [JsonPropertyName("cid")]
    public Cid? Cid { get; init; }

    /// <summary>
    /// The label value/name (e.g., "nsfw", "spam").
    /// </summary>
    [JsonPropertyName("val")]
    public required string Val { get; init; }

    /// <summary>
    /// Whether this is a negation label (removes a previous label).
    /// </summary>
    [JsonPropertyName("neg")]
    public bool? Neg { get; init; }

    /// <summary>
    /// Timestamp when the label was created.
    /// </summary>
    [JsonPropertyName("cts")]
    public required AtDatetime Cts { get; init; }

    /// <summary>
    /// Timestamp when the label expires, if applicable.
    /// </summary>
    [JsonPropertyName("exp")]
    public AtDatetime? Exp { get; init; }

    /// <summary>
    /// Signature of the label, as bytes. On the wire it is a Lexicon <c>bytes</c> value,
    /// <c>{"$bytes": "…"}</c>.
    /// </summary>
    [JsonPropertyName("sig")]
    [JsonConverter(typeof(LexBytesJsonConverter))]
    public byte[]? Sig { get; init; }
}

/// <summary>
/// One page of a cursor-paginated XRPC response.
/// </summary>
/// <typeparam name="T">The type of the page's items.</typeparam>
/// <remarks>
/// Every cursored response model implements this, with <see cref="Items"/> implemented
/// explicitly over its Lexicon-named list (<c>records</c>, <c>repos</c>, <c>cids</c>, …) so the
/// wire shape is unchanged. The <c>Enumerate*</c> methods walk the pages for you.
/// </remarks>
public interface ICursorPage<out T>
{
    /// <summary>
    /// The items on this page.
    /// </summary>
    IReadOnlyList<T> Items { get; }

    /// <summary>
    /// The cursor for the next page, or <see langword="null"/> when this is the last page.
    /// </summary>
    string? Cursor { get; }
}
