using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Communication;

/// <summary>
/// A communication template used for moderation emails.
/// </summary>
public sealed class CommunicationTemplateView
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

    /// <summary>Whether this entry is disabled.</summary>
    [JsonPropertyName("disabled")]
    public required bool Disabled { get; init; }

    /// <summary>The DID of the account that last updated this.</summary>
    [JsonPropertyName("lastUpdatedBy")]
    public required string LastUpdatedBy { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    /// <summary>Timestamp of the last update (ISO 8601).</summary>
    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; init; }
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

    /// <summary>The DID of the account that created this.</summary>
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }
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

    /// <summary>The DID of the account performing the update.</summary>
    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; init; }

    /// <summary>Whether this entry is disabled.</summary>
    [JsonPropertyName("disabled")]
    public bool? Disabled { get; init; }
}

/// <summary>
/// Request to delete a communication template.
/// </summary>
public sealed class DeleteTemplateRequest
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
    public required List<CommunicationTemplateView> CommunicationTemplates { get; init; }
}
