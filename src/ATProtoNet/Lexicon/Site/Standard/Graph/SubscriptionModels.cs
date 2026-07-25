using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Site.Standard.Graph;

// ──────────────────────────────────────────────────────────────
//  Subscription record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Represents a Standard.site subscription — a follow relationship to a publication.
/// </summary>
public sealed class SubscriptionRecord
{
    /// <summary>The Lexicon type discriminator (<c>site.standard.graph.subscription</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "site.standard.graph.subscription";

    /// <summary>
    /// AT-URI reference to the publication record being subscribed to
    /// (e.g. at://did:plc:abc123/site.standard.publication/xyz789).
    /// </summary>
    [JsonPropertyName("publication")]
    public required string Publication { get; init; }
}
