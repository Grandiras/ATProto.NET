using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Set;

/// <summary>
/// A named set of values used for moderation rules.
/// </summary>
public sealed class OzoneSetView
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("setSize")]
    public required int SetSize { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; init; }
}

/// <summary>
/// Request to create or update a set.
/// </summary>
public sealed class UpsertSetRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Request to delete a set.
/// </summary>
public sealed class DeleteSetRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>
/// Request to add values to a set.
/// </summary>
public sealed class AddValuesRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("values")]
    public required List<string> Values { get; init; }
}

/// <summary>
/// Request to delete values from a set.
/// </summary>
public sealed class DeleteValuesRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("values")]
    public required List<string> Values { get; init; }
}

/// <summary>
/// Response from querySets.
/// </summary>
public sealed class QuerySetsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("sets")]
    public required List<OzoneSetView> Sets { get; init; }
}

/// <summary>
/// Response from getValues.
/// </summary>
public sealed class GetValuesResponse
{
    [JsonPropertyName("set")]
    public required OzoneSetView Set { get; init; }

    [JsonPropertyName("values")]
    public required List<string> Values { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}
