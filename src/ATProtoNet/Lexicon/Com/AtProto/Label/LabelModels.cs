using System.Text.Json.Serialization;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Com.AtProto.Label;

// ──────────────────────────────────────────────────────────────
//  com.atproto.label.queryLabels
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from queryLabels.
/// </summary>
public sealed class QueryLabelsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("labels")]
    public required List<Models.Label> Labels { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.label.subscribeLabels (event stream)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A labels event from the label subscription stream.
/// </summary>
public sealed class LabelsEvent
{
    [JsonPropertyName("seq")]
    public long Seq { get; init; }

    [JsonPropertyName("labels")]
    public required List<Models.Label> Labels { get; init; }
}
