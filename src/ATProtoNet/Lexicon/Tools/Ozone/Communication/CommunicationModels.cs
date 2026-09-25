using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Tools.Ozone.Communication;

/// <summary>
/// A communication template used for moderation emails.
/// </summary>
public sealed class CommunicationTemplateView : LexObject
{
    /// <summary>The identifier of the template.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The name of the template.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The subject line used when the template is sent.</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    /// <summary>The body of the template, in Markdown.</summary>
    [JsonPropertyName("contentMarkdown")]
    public required string ContentMarkdown { get; init; }

    /// <summary>The language of the template (BCP-47).</summary>
    [JsonPropertyName("lang")]
    public string? Lang { get; init; }

    /// <summary>Whether this entry is disabled.</summary>
    [JsonPropertyName("disabled")]
    public required bool Disabled { get; init; }

    /// <summary>The DID of the account that last updated this.</summary>
    [JsonPropertyName("lastUpdatedBy")]
    public required Did LastUpdatedBy { get; init; }

    /// <summary>When the template was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>When the template was last updated.</summary>
    [JsonPropertyName("updatedAt")]
    public required AtDatetime UpdatedAt { get; init; }
}

/// <summary>
/// Request to create a communication template.
/// </summary>
public sealed class CreateTemplateRequest
{
    /// <summary>The name of the template.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The body of the template, in Markdown.</summary>
    [JsonPropertyName("contentMarkdown")]
    public required string ContentMarkdown { get; init; }

    /// <summary>The subject line used when the template is sent.</summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>The language of the template (BCP-47).</summary>
    [JsonPropertyName("lang")]
    public string? Lang { get; init; }

    /// <summary>The DID of the account that created this.</summary>
    [JsonPropertyName("createdBy")]
    public Did? CreatedBy { get; init; }
}

/// <summary>
/// Request to update a communication template.
/// </summary>
public sealed class UpdateTemplateRequest
{
    /// <summary>The identifier of the template to update.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The new name of the template.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The body of the template, in Markdown.</summary>
    [JsonPropertyName("contentMarkdown")]
    public string? ContentMarkdown { get; init; }

    /// <summary>The subject line used when the template is sent.</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    /// <summary>The language of the template (BCP-47).</summary>
    [JsonPropertyName("lang")]
    public string? Lang { get; init; }

    /// <summary>The DID of the account performing the update.</summary>
    [JsonPropertyName("updatedBy")]
    public Did? UpdatedBy { get; init; }

    /// <summary>Whether this entry is disabled.</summary>
    [JsonPropertyName("disabled")]
    public bool? Disabled { get; init; }
}

/// <summary>
/// Request to delete a communication template.
/// </summary>
internal sealed class DeleteTemplateRequest
{
    /// <summary>The identifier of the template to delete.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }
}

/// <summary>
/// Response from listTemplates.
/// </summary>
public sealed class ListTemplatesResponse
{
    /// <summary>The communication templates.</summary>
    [JsonPropertyName("communicationTemplates")]
    public required IReadOnlyList<CommunicationTemplateView> CommunicationTemplates { get; init; }
}
