using System.Text.Json.Serialization;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Site.Standard.Publication;

// ──────────────────────────────────────────────────────────────
//  Publication record
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Represents a Standard.site publication — a collection of documents published to the web.
/// </summary>
public sealed class PublicationRecord
{
    [JsonPropertyName("$type")]
    public string Type => "site.standard.publication";

    /// <summary>Base URL for the publication (e.g. https://standard.site). Avoid trailing slashes.</summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>Name of the publication.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Square image to identify the publication. Should be at least 256×256.</summary>
    [JsonPropertyName("icon")]
    public BlobRef? Icon { get; init; }

    /// <summary>Brief description of the publication.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Simplified theme for tools and apps to use when displaying content.</summary>
    [JsonPropertyName("basicTheme")]
    public BasicTheme? BasicTheme { get; init; }

    /// <summary>Platform-specific preferences for the publication.</summary>
    [JsonPropertyName("preferences")]
    public PublicationPreferences? Preferences { get; init; }
}

/// <summary>
/// Platform-specific preferences for a publication.
/// </summary>
public sealed class PublicationPreferences
{
    /// <summary>Whether the publication should appear in discovery feeds.</summary>
    [JsonPropertyName("showInDiscover")]
    public bool? ShowInDiscover { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Theme
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Simplified publication theme with four color roles (site.standard.theme.basic).
/// </summary>
public sealed class BasicTheme
{
    [JsonPropertyName("$type")]
    public string Type => "site.standard.theme.basic";

    /// <summary>Color used for content background.</summary>
    [JsonPropertyName("background")]
    public required ThemeColorRgb Background { get; init; }

    /// <summary>Color used for content text.</summary>
    [JsonPropertyName("foreground")]
    public required ThemeColorRgb Foreground { get; init; }

    /// <summary>Color used for links and button backgrounds.</summary>
    [JsonPropertyName("accent")]
    public required ThemeColorRgb Accent { get; init; }

    /// <summary>Color used for button text.</summary>
    [JsonPropertyName("accentForeground")]
    public required ThemeColorRgb AccentForeground { get; init; }
}

/// <summary>
/// An RGB color (site.standard.theme.color#rgb). Values 0-255.
/// </summary>
public sealed class ThemeColorRgb
{
    [JsonPropertyName("$type")]
    public string Type => "site.standard.theme.color#rgb";

    /// <summary>Red channel (0-255).</summary>
    [JsonPropertyName("r")]
    public required int R { get; init; }

    /// <summary>Green channel (0-255).</summary>
    [JsonPropertyName("g")]
    public required int G { get; init; }

    /// <summary>Blue channel (0-255).</summary>
    [JsonPropertyName("b")]
    public required int B { get; init; }
}

/// <summary>
/// An RGBA color (site.standard.theme.color#rgba). RGB values 0-255, alpha 0-100.
/// </summary>
public sealed class ThemeColorRgba
{
    [JsonPropertyName("$type")]
    public string Type => "site.standard.theme.color#rgba";

    [JsonPropertyName("r")]
    public required int R { get; init; }

    [JsonPropertyName("g")]
    public required int G { get; init; }

    [JsonPropertyName("b")]
    public required int B { get; init; }

    /// <summary>Alpha/opacity (0 = transparent, 100 = opaque).</summary>
    [JsonPropertyName("a")]
    public required int A { get; init; }
}
