using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Set;

/// <summary>
/// A named set of values used for moderation rules.
/// </summary>
public sealed class OzoneSetView
{
    /// <summary>The name of the set.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The number of values in the set.</summary>
    [JsonPropertyName("setSize")]
    public required int SetSize { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    /// <summary>Timestamp of the last update (ISO 8601).</summary>
    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; init; }
}

/// <summary>
/// Request to create or update a set.
/// </summary>
public sealed class UpsertSetRequest
{
    /// <summary>The name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Request to delete a set.
/// </summary>
public sealed class DeleteSetRequest
{
    /// <summary>The name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>
/// Request to add values to a set.
/// </summary>
public sealed class AddValuesRequest
{
    /// <summary>The name of the set.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The values to add to the set.</summary>
    [JsonPropertyName("values")]
    public required List<string> Values { get; init; }
}

/// <summary>
/// Request to delete values from a set.
/// </summary>
public sealed class DeleteValuesRequest
{
    /// <summary>The name of the set.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The values to remove from the set.</summary>
    [JsonPropertyName("values")]
    public required List<string> Values { get; init; }
}

/// <summary>
/// Response from querySets.
/// </summary>
public sealed class QuerySetsResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The sets.</summary>
    [JsonPropertyName("sets")]
    public required List<OzoneSetView> Sets { get; init; }
}

/// <summary>
/// Response from getValues.
/// </summary>
public sealed class GetValuesResponse
{
    /// <summary>The set.</summary>
    [JsonPropertyName("set")]
    public required OzoneSetView Set { get; init; }

    /// <summary>The values in the set.</summary>
    [JsonPropertyName("values")]
    public required List<string> Values { get; init; }

    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}
