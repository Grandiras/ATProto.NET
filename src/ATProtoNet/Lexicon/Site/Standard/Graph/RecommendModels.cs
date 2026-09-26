using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Site.Standard.Graph;

// ──────────────────────────────────────────────────────────────
//  Recommend record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Represents a Standard.site recommendation — an account recommending a document.
/// </summary>
public sealed class RecommendRecord : LexObject
{
    /// <summary>The Lexicon type discriminator (<c>site.standard.graph.recommend</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "site.standard.graph.recommend";

    /// <summary>
    /// AT-URI reference to the document record being recommended
    /// (e.g. at://did:plc:abc123/site.standard.document/xyz789).
    /// </summary>
    [JsonPropertyName("document")]
    public required AtUri Document { get; init; }

    /// <summary>When the recommendation was made.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}
