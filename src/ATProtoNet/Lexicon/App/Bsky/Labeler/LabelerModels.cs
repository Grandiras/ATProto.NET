using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.App.Bsky.Labeler;

// ──────────────────────────────────────────────────────────
//  Record types
// ──────────────────────────────────────────────────────────

/// <summary>
/// Record type for <c>app.bsky.labeler.service</c> — declares a labeler service.
/// </summary>
public sealed class LabelerServiceRecord
{
    /// <summary>The Lexicon type discriminator (<c>app.bsky.labeler.service</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.labeler.service";

    /// <summary>The labeler's declared labelling policies.</summary>
    [JsonPropertyName("policies")]
    public required LabelerPolicies Policies { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// Policy configuration for a labeler service.
/// </summary>
public sealed class LabelerPolicies
{
    /// <summary>The label values this labeler may publish.</summary>
    [JsonPropertyName("labelValues")]
    public List<string>? LabelValues { get; init; }

    /// <summary>The labeler's definitions for its custom label values.</summary>
    [JsonPropertyName("labelValueDefinitions")]
    public List<LabelValueDefinition>? LabelValueDefinitions { get; init; }
}

/// <summary>
/// Custom label value definition published by a labeler.
/// </summary>
public sealed class LabelValueDefinition
{
    /// <summary>The label value identifier this definition describes.</summary>
    [JsonPropertyName("identifier")]
    public required string Identifier { get; init; }

    /// <summary>
    /// How prominently the label is surfaced (<c>inform</c>, <c>alert</c>, or <c>none</c>).
    /// </summary>
    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    /// <summary>What the label blurs (<c>content</c>, <c>media</c>, or <c>none</c>).</summary>
    [JsonPropertyName("blurs")]
    public required string Blurs { get; init; }

    /// <summary>
    /// The default viewer setting for the label (<c>ignore</c>, <c>warn</c>, or <c>hide</c>).
    /// </summary>
    [JsonPropertyName("defaultSetting")]
    public string? DefaultSetting { get; init; }

    /// <summary>Whether the label may only be shown to adult accounts.</summary>
    [JsonPropertyName("adultOnly")]
    public bool? AdultOnly { get; init; }

    /// <summary>The localised name and description strings for the label.</summary>
    [JsonPropertyName("locales")]
    public required List<LabelValueDefinitionStrings> Locales { get; init; }
}

/// <summary>
/// Localized name and description for a label value definition.
/// </summary>
public sealed class LabelValueDefinitionStrings
{
    /// <summary>The BCP-47 language tag these strings are in.</summary>
    [JsonPropertyName("lang")]
    public required string Lang { get; init; }

    /// <summary>The localised short name of the label.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The localised description of the label.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

// ──────────────────────────────────────────────────────────
//  View types
// ──────────────────────────────────────────────────────────

/// <summary>
/// Detailed view of a labeler service.
/// </summary>
public sealed class LabelerViewDetailed
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>The account that created this.</summary>
    [JsonPropertyName("creator")]
    public required JsonElement Creator { get; init; }

    /// <summary>The number of likes.</summary>
    [JsonPropertyName("likeCount")]
    public int? LikeCount { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public LabelerViewerState? Viewer { get; init; }

    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    /// <summary>The labeler's declared labelling policies.</summary>
    [JsonPropertyName("policies")]
    public required LabelerPolicies Policies { get; init; }
}

/// <summary>
/// Basic view of a labeler service.
/// </summary>
public sealed class LabelerView
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    /// <summary>The account that created this.</summary>
    [JsonPropertyName("creator")]
    public required JsonElement Creator { get; init; }

    /// <summary>The number of likes.</summary>
    [JsonPropertyName("likeCount")]
    public int? LikeCount { get; init; }

    /// <summary>The requesting account's relationship to this subject.</summary>
    [JsonPropertyName("viewer")]
    public LabelerViewerState? Viewer { get; init; }

    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }
}

/// <summary>
/// Viewer state for a labeler service.
/// </summary>
public sealed class LabelerViewerState
{
    /// <summary>The AT-URI of the viewer's like record, if they have liked this.</summary>
    [JsonPropertyName("like")]
    public string? Like { get; init; }
}

// ──────────────────────────────────────────────────────────
//  Response types
// ──────────────────────────────────────────────────────────

/// <summary>
/// Response from <c>app.bsky.labeler.getServices</c>.
/// </summary>
public sealed class GetLabelerServicesResponse
{
    /// <summary>The labeler service views.</summary>
    [JsonPropertyName("views")]
    public required List<JsonElement> Views { get; init; }
}

// ──────────────────────────────────────────────────────────
//  Label interpretation
// ──────────────────────────────────────────────────────────

/// <summary>
/// Well-known label severity levels.
/// </summary>
public static class LabelSeverity
{
    /// <summary>The <c>inform</c> label severity.</summary>
    public const string Inform = "inform";

    /// <summary>The <c>alert</c> label severity.</summary>
    public const string Alert = "alert";

    /// <summary>The <c>none</c> label severity.</summary>
    public const string None = "none";
}

/// <summary>
/// Well-known label blur behaviors.
/// </summary>
public static class LabelBlurs
{
    /// <summary>Blurs the labelled content itself.</summary>
    public const string Content = "content";

    /// <summary>Blurs only the media attached to the labelled content.</summary>
    public const string Media = "media";

    /// <summary>Applies no blurring.</summary>
    public const string None = "none";
}

/// <summary>
/// Well-known label default settings.
/// </summary>
public static class LabelDefaultSetting
{
    /// <summary>The <c>ignore</c> label default setting.</summary>
    public const string Ignore = "ignore";

    /// <summary>The <c>warn</c> label default setting.</summary>
    public const string Warn = "warn";

    /// <summary>The <c>hide</c> label default setting.</summary>
    public const string Hide = "hide";
}

/// <summary>
/// Well-known standard label values used by Bluesky moderation.
/// </summary>
public static class StandardLabelValues
{
    /// <summary>The <c>porn</c> standard label value.</summary>
    public const string Porn = "porn";

    /// <summary>The <c>sexual</c> standard label value.</summary>
    public const string Sexual = "sexual";

    /// <summary>The <c>nudity</c> standard label value.</summary>
    public const string Nudity = "nudity";

    /// <summary>The <c>graphic-media</c> standard label value.</summary>
    public const string GraphicMedia = "graphic-media";

    /// <summary>The <c>gore</c> standard label value.</summary>
    public const string Gore = "gore";

    /// <summary>The <c>spam</c> standard label value.</summary>
    public const string Spam = "spam";

    /// <summary>The <c>impersonation</c> standard label value.</summary>
    public const string Impersonation = "impersonation";

    /// <summary>Content not available (takedown / DMCA).</summary>
    public const string NotAvailable = "!no-unauthenticated";

    /// <summary>The account's content requires authentication to view.</summary>
    public const string NoUnauthenticated = "!no-unauthenticated";

    /// <summary>Content warning.</summary>
    public const string ContentWarning = "content-warning";

    /// <summary>Misleading content.</summary>
    public const string Misleading = "misleading";
}
