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
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The labels applied to this subject.</summary>
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
    /// <summary>The sequence number of the event on the firehose.</summary>
    [JsonPropertyName("seq")]
    public long Seq { get; init; }

    /// <summary>The labels emitted in this event.</summary>
    [JsonPropertyName("labels")]
    public required List<Models.Label> Labels { get; init; }
}
