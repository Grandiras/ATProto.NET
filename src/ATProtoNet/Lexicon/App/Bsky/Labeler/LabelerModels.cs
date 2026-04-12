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
    [JsonPropertyName("$type")]
    public string Type => "app.bsky.labeler.service";

    [JsonPropertyName("policies")]
    public required LabelerPolicies Policies { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// Policy configuration for a labeler service.
/// </summary>
public sealed class LabelerPolicies
{
    [JsonPropertyName("labelValues")]
    public List<string>? LabelValues { get; init; }

    [JsonPropertyName("labelValueDefinitions")]
    public List<LabelValueDefinition>? LabelValueDefinitions { get; init; }
}

/// <summary>
/// Custom label value definition published by a labeler.
/// </summary>
public sealed class LabelValueDefinition
{
    [JsonPropertyName("identifier")]
    public required string Identifier { get; init; }

    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    [JsonPropertyName("blurs")]
    public required string Blurs { get; init; }

    [JsonPropertyName("defaultSetting")]
    public string? DefaultSetting { get; init; }

    [JsonPropertyName("adultOnly")]
    public bool? AdultOnly { get; init; }

    [JsonPropertyName("locales")]
    public required List<LabelValueDefinitionStrings> Locales { get; init; }
}

/// <summary>
/// Localized name and description for a label value definition.
/// </summary>
public sealed class LabelValueDefinitionStrings
{
    [JsonPropertyName("lang")]
    public required string Lang { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

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
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("creator")]
    public required JsonElement Creator { get; init; }

    [JsonPropertyName("likeCount")]
    public int? LikeCount { get; init; }

    [JsonPropertyName("viewer")]
    public LabelerViewerState? Viewer { get; init; }

    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }

    [JsonPropertyName("policies")]
    public required LabelerPolicies Policies { get; init; }
}

/// <summary>
/// Basic view of a labeler service.
/// </summary>
public sealed class LabelerView
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("creator")]
    public required JsonElement Creator { get; init; }

    [JsonPropertyName("likeCount")]
    public int? LikeCount { get; init; }

    [JsonPropertyName("viewer")]
    public LabelerViewerState? Viewer { get; init; }

    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; init; }
}

/// <summary>
/// Viewer state for a labeler service.
/// </summary>
public sealed class LabelerViewerState
{
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
    public const string Inform = "inform";
    public const string Alert = "alert";
    public const string None = "none";
}

/// <summary>
/// Well-known label blur behaviors.
/// </summary>
public static class LabelBlurs
{
    public const string Content = "content";
    public const string Media = "media";
    public const string None = "none";
}

/// <summary>
/// Well-known label default settings.
/// </summary>
public static class LabelDefaultSetting
{
    public const string Ignore = "ignore";
    public const string Warn = "warn";
    public const string Hide = "hide";
}

/// <summary>
/// Well-known standard label values used by Bluesky moderation.
/// </summary>
public static class StandardLabelValues
{
    public const string Porn = "porn";
    public const string Sexual = "sexual";
    public const string Nudity = "nudity";
    public const string GraphicMedia = "graphic-media";
    public const string Gore = "gore";
    public const string Spam = "spam";
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
