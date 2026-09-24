using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Tools.Ozone.Set;

/// <summary>
/// A named set of values used for moderation rules.
/// </summary>
public sealed class OzoneSetView : LexObject
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

    /// <summary>When the set was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>When the set was last updated.</summary>
    [JsonPropertyName("updatedAt")]
    public required AtDatetime UpdatedAt { get; init; }
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
internal sealed class DeleteSetRequest
{
    /// <summary>The name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>
/// Request to add values to a set.
/// </summary>
internal sealed class AddValuesRequest
{
    /// <summary>The name of the set.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The values to add to the set.</summary>
    [JsonPropertyName("values")]
    public required IReadOnlyList<string> Values { get; init; }
}

/// <summary>
/// Request to delete values from a set.
/// </summary>
internal sealed class DeleteValuesRequest
{
    /// <summary>The name of the set.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The values to remove from the set.</summary>
    [JsonPropertyName("values")]
    public required IReadOnlyList<string> Values { get; init; }
}

/// <summary>
/// Response from querySets.
/// </summary>
public sealed class QuerySetsResponse : ICursorPage<OzoneSetView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The sets.</summary>
    [JsonPropertyName("sets")]
    public required IReadOnlyList<OzoneSetView> Sets { get; init; }

    IReadOnlyList<OzoneSetView> ICursorPage<OzoneSetView>.Items => Sets;
}

/// <summary>
/// Response from getValues.
/// </summary>
public sealed class GetValuesResponse : ICursorPage<string>
{
    /// <summary>The set.</summary>
    [JsonPropertyName("set")]
    public required OzoneSetView Set { get; init; }

    /// <summary>The values in the set.</summary>
    [JsonPropertyName("values")]
    public required IReadOnlyList<string> Values { get; init; }

    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    IReadOnlyList<string> ICursorPage<string>.Items => Values;
}
