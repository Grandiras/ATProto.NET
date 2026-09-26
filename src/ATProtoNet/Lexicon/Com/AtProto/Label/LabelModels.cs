using System.Text.Json.Serialization;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Com.AtProto.Label;

// ──────────────────────────────────────────────────────────────
//  com.atproto.label.queryLabels
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from queryLabels.
/// </summary>
public sealed class QueryLabelsResponse : ICursorPage<Models.Label>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public required IReadOnlyList<Models.Label> Labels { get; init; }

    IReadOnlyList<Models.Label> ICursorPage<Models.Label>.Items => Labels;
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.label.subscribeLabels (event stream)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A message of the <c>com.atproto.label.subscribeLabels</c> event stream: a sequenced
/// <see cref="LabelsEvent"/>, or a <see cref="LabelInfoEvent"/>.
/// </summary>
/// <remarks>
/// The event-stream frame header names the variant (<c>#labels</c>, <c>#info</c>); the body
/// carries no <c>$type</c>. <see cref="Streaming.FirehoseClient.SubscribeLabelsAsync"/> and
/// <see cref="Streaming.LabelStreamConsumer"/> read both.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(LabelsEvent), "#labels")]
[JsonDerivedType(typeof(LabelInfoEvent), "#info")]
public abstract class LabelStreamMessage : LexObject;

/// <summary>
/// Labels (and negations) a labeler emitted: the <c>#labels</c> message of the label stream.
/// </summary>
public sealed class LabelsEvent : LabelStreamMessage
{
    /// <summary>The stream sequence number of this event, and the cursor to resume after it.</summary>
    [JsonPropertyName("seq")]
    public long Seq { get; init; }

    /// <summary>The labels emitted in this event.</summary>
    [JsonPropertyName("labels")]
    public required IReadOnlyList<Models.Label> Labels { get; init; }
}

/// <summary>
/// An informational message from the labeler, such as <c>OutdatedCursor</c> when the requested
/// cursor predates its retention window. It is not sequenced and does not move the cursor.
/// </summary>
public sealed class LabelInfoEvent : LabelStreamMessage
{
    /// <summary>The notice's name, such as <c>OutdatedCursor</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>A human-readable description, if the labeler sent one.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
