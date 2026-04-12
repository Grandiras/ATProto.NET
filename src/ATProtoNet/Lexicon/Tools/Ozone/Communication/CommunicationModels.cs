using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Communication;

/// <summary>
/// A communication template used for moderation emails.
/// </summary>
public sealed class CommunicationTemplateView
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("contentMarkdown")]
    public required string ContentMarkdown { get; init; }

    [JsonPropertyName("disabled")]
    public required bool Disabled { get; init; }

    [JsonPropertyName("lastUpdatedBy")]
    public required string LastUpdatedBy { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; init; }
}

/// <summary>
/// Request to create a communication template.
/// </summary>
public sealed class CreateTemplateRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("contentMarkdown")]
    public required string ContentMarkdown { get; init; }

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }
}

/// <summary>
/// Request to update a communication template.
/// </summary>
public sealed class UpdateTemplateRequest
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("contentMarkdown")]
    public string? ContentMarkdown { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; init; }

    [JsonPropertyName("disabled")]
    public bool? Disabled { get; init; }
}

/// <summary>
/// Request to delete a communication template.
/// </summary>
public sealed class DeleteTemplateRequest
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
}

/// <summary>
/// Response from listTemplates.
/// </summary>
public sealed class ListTemplatesResponse
{
    [JsonPropertyName("communicationTemplates")]
    public required List<CommunicationTemplateView> CommunicationTemplates { get; init; }
}
