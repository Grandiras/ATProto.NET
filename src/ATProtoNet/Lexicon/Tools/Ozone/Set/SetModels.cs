using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Tools.Ozone.Set;

/// <summary>A named set of values used for moderation rules.</summary>
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

/// <summary>Request to create or update a set.</summary>
public sealed class UpsertSetRequest
{
    /// <summary>The set's name (its identifier).</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>A free-text description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

internal sealed record DeleteSetRequest([property: JsonPropertyName("name")] string Name);

internal sealed record SetValuesRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("values")] IReadOnlyList<string> Values);

/// <summary>Response from querySets.</summary>
public sealed record QuerySetsResponse : CursorPage<OzoneSetView>
{
    /// <summary>This page's sets.</summary>
    [JsonPropertyName("sets")]
    public required IReadOnlyList<OzoneSetView> Sets { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<OzoneSetView> Items => Sets;
}

/// <summary>Response from getValues.</summary>
public sealed record GetValuesResponse : CursorPage<string>
{
    /// <summary>The set the values belong to.</summary>
    [JsonPropertyName("set")]
    public required OzoneSetView Set { get; init; }

    /// <summary>The values in the set.</summary>
    [JsonPropertyName("values")]
    public required IReadOnlyList<string> Values { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<string> Items => Values;
}
