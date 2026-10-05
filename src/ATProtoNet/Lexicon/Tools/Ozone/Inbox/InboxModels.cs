using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Inbox;

/// <summary>A subject of the viewer's with moderation actions against it (<c>tools.ozone.inbox.defs#subjectView</c>).</summary>
public sealed class SubjectView : LexObject
{
    /// <summary>The DID of the moderation service that took the actions.</summary>
    [JsonPropertyName("src")]
    public required Did Src { get; init; }

    /// <summary>The subject: a <see cref="RepoSubject"/> or a <see cref="RecordSubject"/>.</summary>
    [JsonPropertyName("subject")]
    public required ModerationSubject Subject { get; init; }

    /// <summary>The subject's current enforcement state.</summary>
    [JsonPropertyName("enforcement")]
    public required EnforcementView Enforcement { get; init; }

    /// <summary>The viewer's appeal against the actions, if there is one.</summary>
    [JsonPropertyName("appeal")]
    public AppealView? Appeal { get; init; }

    /// <summary>What the viewer may do about the actions; <c>appeal</c> is the only known value.</summary>
    [JsonPropertyName("availableActions")]
    public IReadOnlyList<string>? AvailableActions { get; init; }

    /// <summary>The most recent action taken against the subject.</summary>
    [JsonPropertyName("latestAction")]
    public ActionView? LatestAction { get; init; }

    /// <summary>How many actions were taken against the subject.</summary>
    [JsonPropertyName("actionCount")]
    public int? ActionCount { get; init; }

    /// <summary>When the first action was taken.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>When the subject's actions last changed.</summary>
    [JsonPropertyName("updatedAt")]
    public required AtDatetime UpdatedAt { get; init; }
}

/// <summary>The enforcement state of a subject (<c>tools.ozone.inbox.defs#enforcementView</c>).</summary>
public sealed class EnforcementView : LexObject
{
    /// <summary>One of <c>none</c>, <c>labeled</c>, <c>removed</c>, <c>suspended</c> or <c>takendown</c>.</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    /// <summary>How far the enforcement reaches: <c>network</c>, <c>app</c> or <c>labelOnly</c>.</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>When the enforcement ends, if it does.</summary>
    [JsonPropertyName("expiresAt")]
    public AtDatetime? ExpiresAt { get; init; }

    /// <summary>The label values active on the subject, without negated and expired ones.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<string>? Labels { get; init; }
}

/// <summary>The state of the viewer's appeal against a subject's actions (<c>tools.ozone.inbox.defs#appealView</c>).</summary>
public sealed class AppealView : LexObject
{
    /// <summary>One of <c>none</c>, <c>pending</c>, <c>resolved</c>, <c>superseded</c> or <c>expired</c>.</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    /// <summary>When the appeal was filed.</summary>
    [JsonPropertyName("appealedAt")]
    public AtDatetime? AppealedAt { get; init; }

    /// <summary>When the appeal's report was closed.</summary>
    [JsonPropertyName("resolvedAt")]
    public AtDatetime? ResolvedAt { get; init; }

    /// <summary>The moderator's explanation, from the public note of the closing activity; absent when none was written.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; init; }

    /// <summary>Until when the actions can be appealed.</summary>
    [JsonPropertyName("appealableUntil")]
    public AtDatetime? AppealableUntil { get; init; }
}

/// <summary>A moderation action taken against a subject (<c>tools.ozone.inbox.defs#actionView</c>).</summary>
public sealed class ActionView : LexObject
{
    /// <summary>The action's identifier (the moderation event's).</summary>
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    /// <summary>The public action type.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>How far the action reaches: <c>network</c>, <c>app</c> or <c>labelOnly</c>.</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>When the action was taken.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>When the action was reversed, if it was.</summary>
    [JsonPropertyName("reversedAt")]
    public AtDatetime? ReversedAt { get; init; }

    /// <summary>When the action ends, if it does.</summary>
    [JsonPropertyName("expiresAt")]
    public AtDatetime? ExpiresAt { get; init; }

    /// <summary>The label values of a label action.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<string>? Labels { get; init; }

    /// <summary>The policies applied by the action.</summary>
    [JsonPropertyName("policies")]
    public IReadOnlyList<string>? Policies { get; init; }
}

/// <summary>The moderation action an appeal is against.</summary>
/// <remarks>The Lexicon marks this union closed, so an unrecognized <c>$type</c> is an error.</remarks>
[AtProtoUnion(Closed = true)]
[JsonDerivedType(typeof(AppealActionRef), "tools.ozone.inbox.appealActionedSubject#actionRef")]
[JsonDerivedType(typeof(AppealLabelRef), "tools.ozone.inbox.appealActionedSubject#labelRef")]
[JsonDerivedType(typeof(AppealTakedownRef), "tools.ozone.inbox.appealActionedSubject#takedownRef")]
public abstract class AppealAction : LexObject;

/// <summary>Appeals one action, by its identifier from the inbox.</summary>
public sealed class AppealActionRef : AppealAction
{
    /// <summary>The action's identifier (<see cref="ActionView.Id"/>).</summary>
    [JsonPropertyName("id")]
    public required long Id { get; init; }
}

/// <summary>Appeals a label.</summary>
public sealed class AppealLabelRef : AppealAction
{
    /// <summary>The label value.</summary>
    [JsonPropertyName("val")]
    public required string Val { get; init; }
}

/// <summary>Appeals the takedown of the subject.</summary>
public sealed class AppealTakedownRef : AppealAction;

// ─── Request Models ───

internal sealed record AppealActionedSubjectRequest(
    [property: JsonPropertyName("subject")] ModerationSubject Subject,
    [property: JsonPropertyName("action")] AppealAction? Action = null,
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("modTool")] ModTool? ModTool = null);
