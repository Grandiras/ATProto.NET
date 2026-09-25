using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Models;
using ATProtoNet.Serialization;
using Preference = ATProtoNet.Lexicon.App.Bsky.Actor.Preference;

namespace ATProtoNet.Lexicon.Tools.Ozone.Moderation;

// ─── Moderation Event Types ───

/// <summary>
/// Base moderation event that captures all event types emitted by Ozone (the open
/// <c>tools.ozone.moderation.defs#modEventView.event</c> union). Ozone emits event types this SDK
/// does not model; they read as <see cref="UnknownModEvent"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownModEvent))]
[JsonDerivedType(typeof(ModEventTakedown), "tools.ozone.moderation.defs#modEventTakedown")]
[JsonDerivedType(typeof(ModEventReverseTakedown), "tools.ozone.moderation.defs#modEventReverseTakedown")]
[JsonDerivedType(typeof(ModEventAcknowledge), "tools.ozone.moderation.defs#modEventAcknowledge")]
[JsonDerivedType(typeof(ModEventEscalate), "tools.ozone.moderation.defs#modEventEscalate")]
[JsonDerivedType(typeof(ModEventLabel), "tools.ozone.moderation.defs#modEventLabel")]
[JsonDerivedType(typeof(ModEventComment), "tools.ozone.moderation.defs#modEventComment")]
[JsonDerivedType(typeof(ModEventReport), "tools.ozone.moderation.defs#modEventReport")]
[JsonDerivedType(typeof(ModEventMute), "tools.ozone.moderation.defs#modEventMute")]
[JsonDerivedType(typeof(ModEventUnmute), "tools.ozone.moderation.defs#modEventUnmute")]
[JsonDerivedType(typeof(ModEventMuteReporter), "tools.ozone.moderation.defs#modEventMuteReporter")]
[JsonDerivedType(typeof(ModEventUnmuteReporter), "tools.ozone.moderation.defs#modEventUnmuteReporter")]
[JsonDerivedType(typeof(ModEventEmail), "tools.ozone.moderation.defs#modEventEmail")]
[JsonDerivedType(typeof(ModEventDivert), "tools.ozone.moderation.defs#modEventDivert")]
[JsonDerivedType(typeof(ModEventTag), "tools.ozone.moderation.defs#modEventTag")]
[JsonDerivedType(typeof(ModEventResolveAppeal), "tools.ozone.moderation.defs#modEventResolveAppeal")]
[JsonDerivedType(typeof(ModEventPriorityScore), "tools.ozone.moderation.defs#modEventPriorityScore")]
[JsonDerivedType(typeof(AccountEvent), "tools.ozone.moderation.defs#accountEvent")]
[JsonDerivedType(typeof(IdentityEvent), "tools.ozone.moderation.defs#identityEvent")]
[JsonDerivedType(typeof(RecordEvent), "tools.ozone.moderation.defs#recordEvent")]
[JsonDerivedType(typeof(AgeAssuranceEvent), "tools.ozone.moderation.defs#ageAssuranceEvent")]
[JsonDerivedType(typeof(AgeAssuranceOverrideEvent), "tools.ozone.moderation.defs#ageAssuranceOverrideEvent")]
[JsonDerivedType(typeof(AgeAssurancePurgeEvent), "tools.ozone.moderation.defs#ageAssurancePurgeEvent")]
[JsonDerivedType(typeof(RevokeAccountCredentialsEvent), "tools.ozone.moderation.defs#revokeAccountCredentialsEvent")]
[JsonDerivedType(typeof(ScheduleTakedownEvent), "tools.ozone.moderation.defs#scheduleTakedownEvent")]
[JsonDerivedType(typeof(CancelScheduledTakedownEvent), "tools.ozone.moderation.defs#cancelScheduledTakedownEvent")]
public abstract class ModEventType : LexObject;

/// <summary>
/// A moderation event whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownModEvent : ModEventType, IUnknownUnionVariant
{
    /// <summary>Creates an unknown moderation event from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownModEvent(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Raw = UnknownUnionVariant.RequireObject(raw);
    }

    /// <inheritdoc/>
    public string Type { get; }

    /// <inheritdoc/>
    public JsonElement Raw { get; }
}

/// <summary>A moderation event that takes the subject down.</summary>
public sealed class ModEventTakedown : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>Duration of the action in hours.</summary>
    [JsonPropertyName("durationInHours")]
    public int? DurationInHours { get; init; }

    /// <summary>
    /// Whether to also acknowledge every open report on the account's records, for an account
    /// subject.
    /// </summary>
    [JsonPropertyName("acknowledgeAccountSubjects")]
    public bool? AcknowledgeAccountSubjects { get; init; }

    /// <summary>The moderation policies the action enforces.</summary>
    [JsonPropertyName("policies")]
    public IReadOnlyList<string>? Policies { get; init; }

    /// <summary>The severity level of the violation, such as <c>sev-1</c>.</summary>
    [JsonPropertyName("severityLevel")]
    public string? SeverityLevel { get; init; }

    /// <summary>
    /// The services the takedown applies to: <c>appview</c> and/or <c>pds</c>; <see langword="null"/>
    /// for both.
    /// </summary>
    [JsonPropertyName("targetServices")]
    public IReadOnlyList<string>? TargetServices { get; init; }

    /// <summary>The number of strikes the action gives the account.</summary>
    [JsonPropertyName("strikeCount")]
    public int? StrikeCount { get; init; }

    /// <summary>When the strikes expire; <see langword="null"/> for never.</summary>
    [JsonPropertyName("strikeExpiresAt")]
    public AtDatetime? StrikeExpiresAt { get; init; }
}

/// <summary>A moderation event that restores a taken-down subject.</summary>
public sealed class ModEventReverseTakedown : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>The moderation policies the action enforces.</summary>
    [JsonPropertyName("policies")]
    public IReadOnlyList<string>? Policies { get; init; }

    /// <summary>The severity level of the violation, such as <c>sev-1</c>.</summary>
    [JsonPropertyName("severityLevel")]
    public string? SeverityLevel { get; init; }

    /// <summary>The number of strikes the reversal removes from the account.</summary>
    [JsonPropertyName("strikeCount")]
    public int? StrikeCount { get; init; }
}

/// <summary>A moderation event that acknowledges the subject and closes its review.</summary>
public sealed class ModEventAcknowledge : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>
    /// Whether to also acknowledge every open report on the account's records, for an account
    /// subject.
    /// </summary>
    [JsonPropertyName("acknowledgeAccountSubjects")]
    public bool? AcknowledgeAccountSubjects { get; init; }
}

/// <summary>A moderation event that escalates the subject for further review.</summary>
public sealed class ModEventEscalate : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

/// <summary>A moderation event that applies or removes labels.</summary>
public sealed class ModEventLabel : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>The label values to apply.</summary>
    [JsonPropertyName("createLabelVals")]
    public required IReadOnlyList<string> CreateLabelVals { get; init; }

    /// <summary>The label values to remove.</summary>
    [JsonPropertyName("negateLabelVals")]
    public required IReadOnlyList<string> NegateLabelVals { get; init; }

    /// <summary>How long the change lasts, in hours; <see langword="null"/> for permanently.</summary>
    [JsonPropertyName("durationInHours")]
    public int? DurationInHours { get; init; }
}

/// <summary>A moderation event that records a comment on the subject.</summary>
public sealed class ModEventComment : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public required string Comment { get; init; }

    /// <summary>Whether the comment is pinned to the top of the subject's history.</summary>
    [JsonPropertyName("sticky")]
    public bool? Sticky { get; init; }
}

/// <summary>A moderation event recording a report filed against the subject.</summary>
public sealed class ModEventReport : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>Whether reports from the reporter are currently muted.</summary>
    [JsonPropertyName("isReporterMuted")]
    public bool? IsReporterMuted { get; init; }

    /// <summary>The report's reason type (see <c>ReportReasons</c>).</summary>
    [JsonPropertyName("reportType")]
    public required string ReportType { get; init; }
}

/// <summary>A moderation event that mutes the subject for a period.</summary>
public sealed class ModEventMute : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>Duration of the action in hours.</summary>
    [JsonPropertyName("durationInHours")]
    public required int DurationInHours { get; init; }
}

/// <summary>A moderation event that unmutes the subject.</summary>
public sealed class ModEventUnmute : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

/// <summary>A moderation event that mutes reports from the subject.</summary>
public sealed class ModEventMuteReporter : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>
    /// How long the mute lasts, in hours; <see langword="null"/> (or 0) for a permanent mute.
    /// </summary>
    [JsonPropertyName("durationInHours")]
    public int? DurationInHours { get; init; }
}

/// <summary>A moderation event that unmutes reports from the subject.</summary>
public sealed class ModEventUnmuteReporter : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

/// <summary>A moderation event that sends an email to the subject account.</summary>
public sealed class ModEventEmail : ModEventType
{
    /// <summary>An internal moderator note about the email.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>The subject line of the email.</summary>
    [JsonPropertyName("subjectLine")]
    public required string SubjectLine { get; init; }

    /// <summary>The body of the email.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>The moderation policies the action enforces.</summary>
    [JsonPropertyName("policies")]
    public IReadOnlyList<string>? Policies { get; init; }

    /// <summary>The severity level of the violation, such as <c>sev-1</c>.</summary>
    [JsonPropertyName("severityLevel")]
    public string? SeverityLevel { get; init; }

    /// <summary>The number of strikes the action gives the account.</summary>
    [JsonPropertyName("strikeCount")]
    public int? StrikeCount { get; init; }

    /// <summary>When the strikes expire; <see langword="null"/> for never.</summary>
    [JsonPropertyName("strikeExpiresAt")]
    public AtDatetime? StrikeExpiresAt { get; init; }

    /// <summary>Whether the email was delivered.</summary>
    [JsonPropertyName("isDelivered")]
    public bool? IsDelivered { get; init; }
}

/// <summary>
/// A moderation event that diverts the subject's blobs to a separate review service.
/// </summary>
public sealed class ModEventDivert : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

/// <summary>A moderation event that adds or removes tags on the subject.</summary>
public sealed class ModEventTag : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>The tags to add.</summary>
    [JsonPropertyName("add")]
    public required IReadOnlyList<string> Add { get; init; }

    /// <summary>The tags to remove.</summary>
    [JsonPropertyName("remove")]
    public required IReadOnlyList<string> Remove { get; init; }

    /// <summary>How long the change lasts, in hours; <see langword="null"/> for permanently.</summary>
    [JsonPropertyName("durationInHours")]
    public int? DurationInHours { get; init; }
}

/// <summary>A moderation event that resolves an appeal on the subject.</summary>
public sealed class ModEventResolveAppeal : ModEventType
{
    /// <summary>How the appeal was resolved.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

/// <summary>
/// A moderation event that sets the subject's priority score, which orders the review queue.
/// </summary>
public sealed class ModEventPriorityScore : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>The new priority score.</summary>
    [JsonPropertyName("score")]
    public required int Score { get; init; }
}

/// <summary>
/// An account status change on the account's host, which Ozone records as it arrives from the
/// network.
/// </summary>
public sealed class AccountEvent : ModEventType
{
    /// <summary>A free-text comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>
    /// Whether the account has a repository that can be fetched from the host that emitted the
    /// event.
    /// </summary>
    [JsonPropertyName("active")]
    public required bool Active { get; init; }

    /// <summary>
    /// The account's status when it is not active: <c>unknown</c>, <c>deactivated</c>,
    /// <c>deleted</c>, <c>takendown</c>, <c>suspended</c> or <c>tombstoned</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>When the change happened.</summary>
    [JsonPropertyName("timestamp")]
    public required AtDatetime Timestamp { get; init; }
}

/// <summary>
/// An identity change of the account (handle, PDS or tombstone), which Ozone records as it
/// arrives from the network.
/// </summary>
public sealed class IdentityEvent : ModEventType
{
    /// <summary>A free-text comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>The account's handle after the change.</summary>
    [JsonPropertyName("handle")]
    public Handle? Handle { get; init; }

    /// <summary>The account's PDS after the change.</summary>
    [JsonPropertyName("pdsHost")]
    public string? PdsHost { get; init; }

    /// <summary>Whether the identity was tombstoned.</summary>
    [JsonPropertyName("tombstone")]
    public bool? Tombstone { get; init; }

    /// <summary>When the change happened.</summary>
    [JsonPropertyName("timestamp")]
    public required AtDatetime Timestamp { get; init; }
}

/// <summary>
/// A write to the subject record, which Ozone records as it arrives from the network.
/// </summary>
public sealed class RecordEvent : ModEventType
{
    /// <summary>A free-text comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>The operation: <c>create</c>, <c>update</c> or <c>delete</c>.</summary>
    [JsonPropertyName("op")]
    public required string Op { get; init; }

    /// <summary>The record's CID after the write.</summary>
    [JsonPropertyName("cid")]
    public Cid? Cid { get; init; }

    /// <summary>When the write happened.</summary>
    [JsonPropertyName("timestamp")]
    public required AtDatetime Timestamp { get; init; }
}

/// <summary>A step of the account's age-assurance flow, reported by the app view.</summary>
public sealed class AgeAssuranceEvent : ModEventType
{
    /// <summary>When the step was recorded.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>The identifier (a UUID) of this run of the age-assurance flow.</summary>
    [JsonPropertyName("attemptId")]
    public required string AttemptId { get; init; }

    /// <summary>The flow's status: <c>unknown</c>, <c>pending</c> or <c>assured</c>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// The access level the flow granted: <c>unknown</c>, <c>none</c>, <c>safe</c> or <c>full</c>.
    /// </summary>
    [JsonPropertyName("access")]
    public string? Access { get; init; }

    /// <summary>The ISO 3166-1 alpha-2 country code given when the flow began.</summary>
    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; init; }

    /// <summary>The ISO 3166-2 region code given when the flow began.</summary>
    [JsonPropertyName("regionCode")]
    public string? RegionCode { get; init; }

    /// <summary>The IP address that began the flow.</summary>
    [JsonPropertyName("initIp")]
    public string? InitIp { get; init; }

    /// <summary>The user agent that began the flow.</summary>
    [JsonPropertyName("initUa")]
    public string? InitUa { get; init; }

    /// <summary>The IP address that completed the flow.</summary>
    [JsonPropertyName("completeIp")]
    public string? CompleteIp { get; init; }

    /// <summary>The user agent that completed the flow.</summary>
    [JsonPropertyName("completeUa")]
    public string? CompleteUa { get; init; }
}

/// <summary>A moderation event that overrides the account's age-assurance state.</summary>
public sealed class AgeAssuranceOverrideEvent : ModEventType
{
    /// <summary>
    /// The state to set: <c>assured</c>, <c>reset</c> (back to the original state) or
    /// <c>blocked</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// The access level to grant: <c>unknown</c>, <c>none</c>, <c>safe</c> or <c>full</c>.
    /// </summary>
    [JsonPropertyName("access")]
    public string? Access { get; init; }

    /// <summary>Why the state is overridden.</summary>
    [JsonPropertyName("comment")]
    public required string Comment { get; init; }
}

/// <summary>A moderation event that purges the account's age-assurance data.</summary>
public sealed class AgeAssurancePurgeEvent : ModEventType
{
    /// <summary>Why the data is purged.</summary>
    [JsonPropertyName("comment")]
    public required string Comment { get; init; }
}

/// <summary>A moderation event that revokes the account's credentials (sessions and app passwords).</summary>
public sealed class RevokeAccountCredentialsEvent : ModEventType
{
    /// <summary>Why the credentials are revoked.</summary>
    [JsonPropertyName("comment")]
    public required string Comment { get; init; }
}

/// <summary>
/// A takedown was scheduled for the account (see <see cref="ModerationClient.ScheduleActionAsync"/>).
/// </summary>
public sealed class ScheduleTakedownEvent : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>The exact time the takedown runs.</summary>
    [JsonPropertyName("executeAt")]
    public AtDatetime? ExecuteAt { get; init; }

    /// <summary>The earliest time the takedown runs, for a randomized time.</summary>
    [JsonPropertyName("executeAfter")]
    public AtDatetime? ExecuteAfter { get; init; }

    /// <summary>The latest time the takedown runs, for a randomized time.</summary>
    [JsonPropertyName("executeUntil")]
    public AtDatetime? ExecuteUntil { get; init; }
}

/// <summary>The account's scheduled takedowns were cancelled.</summary>
public sealed class CancelScheduledTakedownEvent : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

// ─── Subject Types ───

/// <summary>
/// A moderation subject — either a repo (account) or a specific record. A subject type this SDK
/// does not model reads as <see cref="UnknownModerationSubject"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownModerationSubject))]
[JsonDerivedType(typeof(RepoSubject), "com.atproto.admin.defs#repoRef")]
[JsonDerivedType(typeof(RecordSubject), "com.atproto.repo.strongRef")]
[JsonDerivedType(typeof(MessageSubject), "chat.bsky.convo.defs#messageRef")]
[JsonDerivedType(typeof(ConvoSubject), "chat.bsky.convo.defs#convoRef")]
public abstract class ModerationSubject : LexObject;

/// <summary>
/// A moderation subject whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownModerationSubject : ModerationSubject, IUnknownUnionVariant
{
    /// <summary>Creates an unknown moderation subject from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownModerationSubject(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Raw = UnknownUnionVariant.RequireObject(raw);
    }

    /// <inheritdoc/>
    public string Type { get; }

    /// <inheritdoc/>
    public JsonElement Raw { get; }
}

/// <summary>A moderation subject referring to a whole repository (account).</summary>
public sealed class RepoSubject : ModerationSubject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }
}

/// <summary>A moderation subject referring to a single record.</summary>
public sealed class RecordSubject : ModerationSubject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public Cid? Cid { get; init; }
}

/// <summary>
/// A chat message as a moderation subject (<c>chat.bsky.convo.defs#messageRef</c>).
/// </summary>
public sealed class MessageSubject : ModerationSubject
{
    /// <summary>The DID of the message's sender.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }

    /// <summary>The identifier of the message.</summary>
    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }
}

/// <summary>
/// A chat conversation as a moderation subject (<c>chat.bsky.convo.defs#convoRef</c>).
/// </summary>
public sealed class ConvoSubject : ModerationSubject
{
    /// <summary>The DID of the account the conversation is reported for.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The identifier of the conversation.</summary>
    [JsonPropertyName("convoId")]
    public required string ConvoId { get; init; }
}

// ─── View Models ───

/// <summary>
/// A moderation event record as returned by the API.
/// </summary>
public sealed class ModEventView : LexObject
{
    /// <summary>The identifier of the event.</summary>
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    /// <summary>The moderation event that was emitted.</summary>
    [JsonPropertyName("event")]
    public required ModEventType Event { get; init; }

    /// <summary>The subject the event applies to.</summary>
    [JsonPropertyName("subject")]
    public required ModerationSubject Subject { get; init; }

    /// <summary>The CIDs of specific blobs on the subject record the action applies to.</summary>
    [JsonPropertyName("subjectBlobCids")]
    public IReadOnlyList<Cid>? SubjectBlobCids { get; init; }

    /// <summary>The DID of the account that created this.</summary>
    [JsonPropertyName("createdBy")]
    public required Did CreatedBy { get; init; }

    /// <summary>When the event was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>The handle of the account that created the event.</summary>
    [JsonPropertyName("creatorHandle")]
    public Handle? CreatorHandle { get; init; }

    /// <summary>The handle of the subject account at the time of the event.</summary>
    [JsonPropertyName("subjectHandle")]
    public Handle? SubjectHandle { get; init; }

    /// <summary>The tool the event was emitted with.</summary>
    [JsonPropertyName("modTool")]
    public ModTool? ModTool { get; init; }
}

/// <summary>
/// Moderation event detail view with subject/event metadata.
/// </summary>
public sealed class ModEventViewDetail : LexObject
{
    /// <summary>The identifier of the event.</summary>
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    /// <summary>The moderation event that was emitted.</summary>
    [JsonPropertyName("event")]
    public required ModEventType Event { get; init; }

    /// <summary>The subject the event applies to.</summary>
    [JsonPropertyName("subject")]
    public required JsonElement Subject { get; init; }

    /// <summary>The blobs on the subject record the event applies to.</summary>
    [JsonPropertyName("subjectBlobs")]
    public IReadOnlyList<BlobView>? SubjectBlobs { get; init; }

    /// <summary>The DID of the account that created this.</summary>
    [JsonPropertyName("createdBy")]
    public required Did CreatedBy { get; init; }

    /// <summary>When the event was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>The tool the event was emitted with.</summary>
    [JsonPropertyName("modTool")]
    public ModTool? ModTool { get; init; }
}

/// <summary>
/// The tool a moderation event was emitted with (<c>tools.ozone.moderation.defs#modTool</c>).
/// </summary>
public sealed class ModTool : LexObject
{
    /// <summary>The tool's name, such as <c>automod/1.1.3</c> or <c>ozone/workspace</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Additional information about the tool, in a shape the tool defines.</summary>
    [JsonPropertyName("meta")]
    public JsonElement? Meta { get; init; }
}

/// <summary>
/// A subject's moderation status, from queryStatuses.
/// </summary>
public sealed class SubjectStatusView : LexObject
{
    /// <summary>The identifier of the subject status record.</summary>
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    /// <summary>The subject this status describes.</summary>
    [JsonPropertyName("subject")]
    public required ModerationSubject Subject { get; init; }

    /// <summary>The CIDs of specific blobs on the subject record the action applies to.</summary>
    [JsonPropertyName("subjectBlobCids")]
    public IReadOnlyList<Cid>? SubjectBlobCids { get; init; }

    /// <summary>The handle of the subject repository.</summary>
    [JsonPropertyName("subjectRepoHandle")]
    public Handle? SubjectRepoHandle { get; init; }

    /// <summary>When the subject's status last changed.</summary>
    [JsonPropertyName("updatedAt")]
    public required AtDatetime UpdatedAt { get; init; }

    /// <summary>When the subject's status was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>The review state of the subject (open, escalated, or closed).</summary>
    [JsonPropertyName("reviewState")]
    public required string ReviewState { get; init; }

    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>When the subject's mute ends.</summary>
    [JsonPropertyName("muteUntil")]
    public AtDatetime? MuteUntil { get; init; }

    /// <summary>When the mute on reports from this subject ends.</summary>
    [JsonPropertyName("muteReportingUntil")]
    public AtDatetime? MuteReportingUntil { get; init; }

    /// <summary>The DID of the moderator who last reviewed the subject.</summary>
    [JsonPropertyName("lastReviewedBy")]
    public Did? LastReviewedBy { get; init; }

    /// <summary>When the subject was last reviewed.</summary>
    [JsonPropertyName("lastReviewedAt")]
    public AtDatetime? LastReviewedAt { get; init; }

    /// <summary>When the subject was last reported.</summary>
    [JsonPropertyName("lastReportedAt")]
    public AtDatetime? LastReportedAt { get; init; }

    /// <summary>When the subject was last appealed.</summary>
    [JsonPropertyName("lastAppealedAt")]
    public AtDatetime? LastAppealedAt { get; init; }

    /// <summary>Whether the subject has been taken down.</summary>
    [JsonPropertyName("takendown")]
    public bool? Takendown { get; init; }

    /// <summary>Whether the subject status is under appeal.</summary>
    [JsonPropertyName("appealed")]
    public bool? Appealed { get; init; }

    /// <summary>When the subject's suspension ends.</summary>
    [JsonPropertyName("suspendUntil")]
    public AtDatetime? SuspendUntil { get; init; }

    /// <summary>Free-form tags attached to the subject.</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// The subject's hosting status: an <see cref="AccountHosting"/> or a <see cref="RecordHosting"/>.
    /// </summary>
    [JsonPropertyName("hosting")]
    public SubjectHosting? Hosting { get; init; }

    /// <summary>The subject's priority score, which moderators set to order the queue.</summary>
    [JsonPropertyName("priorityScore")]
    public int? PriorityScore { get; init; }

    /// <summary>Statistics about the account, for an account subject.</summary>
    [JsonPropertyName("accountStats")]
    public AccountStats? AccountStats { get; init; }

    /// <summary>Statistics about the account's records.</summary>
    [JsonPropertyName("recordsStats")]
    public RecordsStats? RecordsStats { get; init; }

    /// <summary>The account's strikes.</summary>
    [JsonPropertyName("accountStrike")]
    public AccountStrike? AccountStrike { get; init; }

    /// <summary>
    /// The account's age-assurance state: <c>pending</c>, <c>assured</c>, <c>unknown</c>,
    /// <c>reset</c> or <c>blocked</c>.
    /// </summary>
    [JsonPropertyName("ageAssuranceState")]
    public string? AgeAssuranceState { get; init; }

    /// <summary>Who last changed the age-assurance state: <c>admin</c> or <c>user</c>.</summary>
    [JsonPropertyName("ageAssuranceUpdatedBy")]
    public string? AgeAssuranceUpdatedBy { get; init; }
}

/// <summary>
/// A subject's hosting status (the open union behind <see cref="SubjectStatusView.Hosting"/>).
/// A status this SDK does not model reads as <see cref="UnknownSubjectHosting"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownSubjectHosting))]
[JsonDerivedType(typeof(AccountHosting), "tools.ozone.moderation.defs#accountHosting")]
[JsonDerivedType(typeof(RecordHosting), "tools.ozone.moderation.defs#recordHosting")]
public abstract class SubjectHosting : LexObject;

/// <summary>
/// A hosting status whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownSubjectHosting : SubjectHosting, IUnknownUnionVariant
{
    /// <summary>Creates an unknown hosting status from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownSubjectHosting(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Raw = UnknownUnionVariant.RequireObject(raw);
    }

    /// <inheritdoc/>
    public string Type { get; }

    /// <inheritdoc/>
    public JsonElement Raw { get; }
}

/// <summary>An account's hosting status.</summary>
public sealed class AccountHosting : SubjectHosting
{
    /// <summary>
    /// The status: <c>takendown</c>, <c>suspended</c>, <c>deleted</c>, <c>deactivated</c> or
    /// <c>unknown</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>When the status last changed.</summary>
    [JsonPropertyName("updatedAt")]
    public AtDatetime? UpdatedAt { get; init; }

    /// <summary>When the account was created.</summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; init; }

    /// <summary>When the account was deleted.</summary>
    [JsonPropertyName("deletedAt")]
    public AtDatetime? DeletedAt { get; init; }

    /// <summary>When the account was deactivated.</summary>
    [JsonPropertyName("deactivatedAt")]
    public AtDatetime? DeactivatedAt { get; init; }

    /// <summary>When the account was reactivated.</summary>
    [JsonPropertyName("reactivatedAt")]
    public AtDatetime? ReactivatedAt { get; init; }
}

/// <summary>A record's hosting status.</summary>
public sealed class RecordHosting : SubjectHosting
{
    /// <summary>The status: <c>deleted</c> or <c>unknown</c>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>When the status last changed.</summary>
    [JsonPropertyName("updatedAt")]
    public AtDatetime? UpdatedAt { get; init; }

    /// <summary>When the record was created.</summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; init; }

    /// <summary>When the record was deleted.</summary>
    [JsonPropertyName("deletedAt")]
    public AtDatetime? DeletedAt { get; init; }
}

/// <summary>
/// Moderation statistics about an account (<c>tools.ozone.moderation.defs#accountStats</c>).
/// </summary>
public sealed class AccountStats : LexObject
{
    /// <summary>The number of reports on the account.</summary>
    [JsonPropertyName("reportCount")]
    public int? ReportCount { get; init; }

    /// <summary>The number of appeals the account made.</summary>
    [JsonPropertyName("appealCount")]
    public int? AppealCount { get; init; }

    /// <summary>The number of times the account was suspended.</summary>
    [JsonPropertyName("suspendCount")]
    public int? SuspendCount { get; init; }

    /// <summary>The number of times the account was escalated.</summary>
    [JsonPropertyName("escalateCount")]
    public int? EscalateCount { get; init; }

    /// <summary>The number of times the account was taken down.</summary>
    [JsonPropertyName("takedownCount")]
    public int? TakedownCount { get; init; }
}

/// <summary>
/// Moderation statistics about an account's records
/// (<c>tools.ozone.moderation.defs#recordsStats</c>).
/// </summary>
public sealed class RecordsStats : LexObject
{
    /// <summary>The number of reports on the account's records.</summary>
    [JsonPropertyName("totalReports")]
    public int? TotalReports { get; init; }

    /// <summary>The number of records that were reported.</summary>
    [JsonPropertyName("reportedCount")]
    public int? ReportedCount { get; init; }

    /// <summary>The number of records that were escalated.</summary>
    [JsonPropertyName("escalatedCount")]
    public int? EscalatedCount { get; init; }

    /// <summary>The number of records that were appealed.</summary>
    [JsonPropertyName("appealedCount")]
    public int? AppealedCount { get; init; }

    /// <summary>The number of the account's records that are moderation subjects.</summary>
    [JsonPropertyName("subjectCount")]
    public int? SubjectCount { get; init; }

    /// <summary>The number of those subjects still waiting for review.</summary>
    [JsonPropertyName("pendingCount")]
    public int? PendingCount { get; init; }

    /// <summary>The number of those subjects that were reviewed.</summary>
    [JsonPropertyName("processedCount")]
    public int? ProcessedCount { get; init; }

    /// <summary>The number of records that were taken down.</summary>
    [JsonPropertyName("takendownCount")]
    public int? TakendownCount { get; init; }
}

/// <summary>
/// An account's strikes (<c>tools.ozone.moderation.defs#accountStrike</c>).
/// </summary>
public sealed class AccountStrike : LexObject
{
    /// <summary>The strikes that have not expired.</summary>
    [JsonPropertyName("activeStrikeCount")]
    public int? ActiveStrikeCount { get; init; }

    /// <summary>Every strike the account has received.</summary>
    [JsonPropertyName("totalStrikeCount")]
    public int? TotalStrikeCount { get; init; }

    /// <summary>When the account received its first strike.</summary>
    [JsonPropertyName("firstStrikeAt")]
    public AtDatetime? FirstStrikeAt { get; init; }

    /// <summary>When the account received its latest strike.</summary>
    [JsonPropertyName("lastStrikeAt")]
    public AtDatetime? LastStrikeAt { get; init; }
}

/// <summary>
/// An account or record as Ozone sees it, or a marker that Ozone does not know it: what
/// <see cref="ModerationClient.GetReposAsync"/> and <see cref="ModerationClient.GetRecordsAsync"/>
/// return one of per requested subject. Where <see cref="ModerationSubject"/> refers to a subject,
/// this is its hydrated view. A view this SDK does not model reads as
/// <see cref="UnknownModerationSubjectView"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownModerationSubjectView))]
[JsonDerivedType(typeof(RepoViewDetail), "tools.ozone.moderation.defs#repoViewDetail")]
[JsonDerivedType(typeof(RepoViewNotFound), "tools.ozone.moderation.defs#repoViewNotFound")]
[JsonDerivedType(typeof(RecordViewDetail), "tools.ozone.moderation.defs#recordViewDetail")]
[JsonDerivedType(typeof(RecordViewNotFound), "tools.ozone.moderation.defs#recordViewNotFound")]
public abstract class ModerationSubjectView : LexObject;

/// <summary>
/// A subject view whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownModerationSubjectView : ModerationSubjectView, IUnknownUnionVariant
{
    /// <summary>Creates an unknown subject view from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownModerationSubjectView(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Raw = UnknownUnionVariant.RequireObject(raw);
    }

    /// <inheritdoc/>
    public string Type { get; }

    /// <inheritdoc/>
    public JsonElement Raw { get; }
}

/// <summary>An account Ozone does not know.</summary>
public sealed class RepoViewNotFound : ModerationSubjectView
{
    /// <summary>The DID that was looked up.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }
}

/// <summary>A record Ozone does not know.</summary>
public sealed class RecordViewNotFound : ModerationSubjectView
{
    /// <summary>The AT URI that was looked up.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }
}

/// <summary>
/// Record view with moderation context.
/// </summary>
public sealed class RecordViewDetail : ModerationSubjectView
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>The record value.</summary>
    [JsonPropertyName("value")]
    public required JsonElement Value { get; init; }

    /// <summary>The blobs the record references.</summary>
    [JsonPropertyName("blobs")]
    public required IReadOnlyList<BlobView> Blobs { get; init; }

    /// <summary>The labels applied to the record.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<Label>? Labels { get; init; }

    /// <summary>When Ozone indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public required AtDatetime IndexedAt { get; init; }

    /// <summary>Moderation state attached to this subject.</summary>
    [JsonPropertyName("moderation")]
    public required ModerationDetail Moderation { get; init; }

    /// <summary>The repository the record belongs to.</summary>
    [JsonPropertyName("repo")]
    public required RepoView Repo { get; init; }
}

/// <summary>
/// A blob with its moderation context (<c>tools.ozone.moderation.defs#blobView</c>).
/// </summary>
public sealed class BlobView : LexObject
{
    /// <summary>The blob's CID.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>The blob's media type.</summary>
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; init; }

    /// <summary>The blob's size in bytes.</summary>
    [JsonPropertyName("size")]
    public required long Size { get; init; }

    /// <summary>When Ozone first saw the blob.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>
    /// Media details: an <see cref="ImageDetails"/> or a <see cref="VideoDetails"/>.
    /// </summary>
    [JsonPropertyName("details")]
    public BlobDetails? Details { get; init; }

    /// <summary>The blob's moderation state.</summary>
    [JsonPropertyName("moderation")]
    public ModerationDetail? Moderation { get; init; }
}

/// <summary>
/// Media details of a blob (the open union behind <see cref="BlobView.Details"/>). Details this
/// SDK does not model read as <see cref="UnknownBlobDetails"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownBlobDetails))]
[JsonDerivedType(typeof(ImageDetails), "tools.ozone.moderation.defs#imageDetails")]
[JsonDerivedType(typeof(VideoDetails), "tools.ozone.moderation.defs#videoDetails")]
public abstract class BlobDetails : LexObject;

/// <summary>
/// Blob details whose <c>$type</c> this SDK version does not model. They keep the raw object and
/// write it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownBlobDetails : BlobDetails, IUnknownUnionVariant
{
    /// <summary>Creates unknown blob details from their discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownBlobDetails(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Raw = UnknownUnionVariant.RequireObject(raw);
    }

    /// <inheritdoc/>
    public string Type { get; }

    /// <inheritdoc/>
    public JsonElement Raw { get; }
}

/// <summary>The dimensions of an image blob.</summary>
public sealed class ImageDetails : BlobDetails
{
    /// <summary>The width in pixels.</summary>
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    /// <summary>The height in pixels.</summary>
    [JsonPropertyName("height")]
    public required int Height { get; init; }
}

/// <summary>The dimensions and length of a video blob.</summary>
public sealed class VideoDetails : BlobDetails
{
    /// <summary>The width in pixels.</summary>
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    /// <summary>The height in pixels.</summary>
    [JsonPropertyName("height")]
    public required int Height { get; init; }

    /// <summary>The length in seconds.</summary>
    [JsonPropertyName("length")]
    public required int Length { get; init; }
}

/// <summary>
/// Moderation detail attached to a record or repo view.
/// </summary>
public sealed class ModerationDetail : LexObject
{
    /// <summary>The current moderation status of the subject.</summary>
    [JsonPropertyName("subjectStatus")]
    public SubjectStatusView? SubjectStatus { get; init; }
}

/// <summary>
/// Repo/account view with moderation context.
/// </summary>
public sealed class RepoView : LexObject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The email address of the account.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>
    /// Selected records from the repository (such as the profile record) included for convenience.
    /// </summary>
    [JsonPropertyName("relatedRecords")]
    public IReadOnlyList<JsonElement>? RelatedRecords { get; init; }

    /// <summary>When Ozone indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public required AtDatetime IndexedAt { get; init; }

    /// <summary>Moderation state attached to this subject.</summary>
    [JsonPropertyName("moderation")]
    public required ModerationDetail Moderation { get; init; }

    /// <summary>The invite code the account signed up with, if any.</summary>
    [JsonPropertyName("invitedBy")]
    public JsonElement? InvitedBy { get; init; }

    /// <summary>Whether the account is barred from creating invite codes.</summary>
    [JsonPropertyName("invitesDisabled")]
    public bool? InvitesDisabled { get; init; }

    /// <summary>An optional note recorded against the invite.</summary>
    [JsonPropertyName("inviteNote")]
    public string? InviteNote { get; init; }

    /// <summary>When the account was deactivated, if it is deactivated.</summary>
    [JsonPropertyName("deactivatedAt")]
    public AtDatetime? DeactivatedAt { get; init; }

    /// <summary>
    /// Signals correlating this account with others (such as a shared IP or device).
    /// </summary>
    [JsonPropertyName("threatSignatures")]
    public IReadOnlyList<JsonElement>? ThreatSignatures { get; init; }
}

/// <summary>
/// Repo view detail with additional fields.
/// </summary>
public sealed class RepoViewDetail : ModerationSubjectView
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The email address of the account.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>
    /// Selected records from the repository (such as the profile record) included for convenience.
    /// </summary>
    [JsonPropertyName("relatedRecords")]
    public IReadOnlyList<JsonElement>? RelatedRecords { get; init; }

    /// <summary>When Ozone indexed this data.</summary>
    [JsonPropertyName("indexedAt")]
    public required AtDatetime IndexedAt { get; init; }

    /// <summary>Moderation state attached to this subject.</summary>
    [JsonPropertyName("moderation")]
    public required ModerationDetail Moderation { get; init; }

    /// <summary>The labels applied to this subject.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyList<JsonElement>? Labels { get; init; }

    /// <summary>The invite code the account signed up with, if any.</summary>
    [JsonPropertyName("invitedBy")]
    public JsonElement? InvitedBy { get; init; }

    /// <summary>The invite codes created by the account.</summary>
    [JsonPropertyName("invites")]
    public IReadOnlyList<JsonElement>? Invites { get; init; }

    /// <summary>Whether the account is barred from creating invite codes.</summary>
    [JsonPropertyName("invitesDisabled")]
    public bool? InvitesDisabled { get; init; }

    /// <summary>An optional note recorded against the invite.</summary>
    [JsonPropertyName("inviteNote")]
    public string? InviteNote { get; init; }

    /// <summary>When the email address was confirmed, if it has been.</summary>
    [JsonPropertyName("emailConfirmedAt")]
    public AtDatetime? EmailConfirmedAt { get; init; }

    /// <summary>When the account was deactivated, if it is deactivated.</summary>
    [JsonPropertyName("deactivatedAt")]
    public AtDatetime? DeactivatedAt { get; init; }

    /// <summary>
    /// Signals correlating this account with others (such as a shared IP or device).
    /// </summary>
    [JsonPropertyName("threatSignatures")]
    public IReadOnlyList<JsonElement>? ThreatSignatures { get; init; }
}

/// <summary>
/// Everything Ozone knows about one subject, from getSubjects
/// (<c>tools.ozone.moderation.defs#subjectView</c>).
/// </summary>
public sealed class SubjectView : LexObject
{
    /// <summary>The kind of subject: <c>account</c>, <c>record</c> or <c>chat</c>.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>The subject as requested: an account's DID, or a record's AT URI.</summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>The subject's moderation status, if it has one.</summary>
    [JsonPropertyName("status")]
    public SubjectStatusView? Status { get; init; }

    /// <summary>The account, or the record's account.</summary>
    [JsonPropertyName("repo")]
    public RepoViewDetail? Repo { get; init; }

    /// <summary>
    /// The account's profile view, in the shape the app view returns (an open union upstream
    /// declares no variants for).
    /// </summary>
    [JsonPropertyName("profile")]
    public JsonElement? Profile { get; init; }

    /// <summary>The record, for a record subject.</summary>
    [JsonPropertyName("record")]
    public RecordViewDetail? Record { get; init; }
}

/// <summary>
/// How an account's reports turned out (<c>tools.ozone.moderation.defs#reporterStats</c>).
/// </summary>
public sealed class ReporterStats : LexObject
{
    /// <summary>The reporter's DID.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The reports the account filed on accounts.</summary>
    [JsonPropertyName("accountReportCount")]
    public required int AccountReportCount { get; init; }

    /// <summary>The reports the account filed on records.</summary>
    [JsonPropertyName("recordReportCount")]
    public required int RecordReportCount { get; init; }

    /// <summary>The accounts the account reported.</summary>
    [JsonPropertyName("reportedAccountCount")]
    public required int ReportedAccountCount { get; init; }

    /// <summary>The records the account reported.</summary>
    [JsonPropertyName("reportedRecordCount")]
    public required int ReportedRecordCount { get; init; }

    /// <summary>The accounts taken down as a result of the account's reports.</summary>
    [JsonPropertyName("takendownAccountCount")]
    public required int TakendownAccountCount { get; init; }

    /// <summary>The records taken down as a result of the account's reports.</summary>
    [JsonPropertyName("takendownRecordCount")]
    public required int TakendownRecordCount { get; init; }

    /// <summary>The accounts labeled as a result of the account's reports.</summary>
    [JsonPropertyName("labeledAccountCount")]
    public required int LabeledAccountCount { get; init; }

    /// <summary>The records labeled as a result of the account's reports.</summary>
    [JsonPropertyName("labeledRecordCount")]
    public required int LabeledRecordCount { get; init; }
}

/// <summary>
/// One day of an account's history, from getAccountTimeline
/// (<c>tools.ozone.moderation.getAccountTimeline#timelineItem</c>).
/// </summary>
public sealed class TimelineItem : LexObject
{
    /// <summary>The day, as <c>YYYY-MM-DD</c>.</summary>
    [JsonPropertyName("day")]
    public required string Day { get; init; }

    /// <summary>How many events of each type happened that day.</summary>
    [JsonPropertyName("summary")]
    public required IReadOnlyList<TimelineItemSummary> Summary { get; init; }
}

/// <summary>
/// How many events of one type an account had on one day
/// (<c>tools.ozone.moderation.getAccountTimeline#timelineItemSummary</c>).
/// </summary>
public sealed class TimelineItemSummary : LexObject
{
    /// <summary>What the events were about: <c>account</c>, <c>record</c> or <c>chat</c>.</summary>
    [JsonPropertyName("eventSubjectType")]
    public required string EventSubjectType { get; init; }

    /// <summary>
    /// The event type: a moderation event (<c>tools.ozone.moderation.defs#modEvent…</c> and the
    /// other event defs), a PLC operation (<c>tools.ozone.moderation.defs#timelineEventPlc…</c>)
    /// or an account history event (<c>tools.ozone.hosting.getAccountHistory#…</c>).
    /// </summary>
    [JsonPropertyName("eventType")]
    public required string EventType { get; init; }

    /// <summary>The number of events.</summary>
    [JsonPropertyName("count")]
    public required int Count { get; init; }
}

/// <summary>
/// A moderation action scheduled to run later
/// (<c>tools.ozone.moderation.defs#scheduledActionView</c>).
/// </summary>
public sealed class ScheduledActionView : LexObject
{
    /// <summary>The scheduled action's identifier.</summary>
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    /// <summary>The action to run: <c>takedown</c>.</summary>
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    /// <summary>The event the action will emit when it runs, as Ozone stored it.</summary>
    [JsonPropertyName("eventData")]
    public JsonElement? EventData { get; init; }

    /// <summary>The account the action applies to.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The exact time the action runs.</summary>
    [JsonPropertyName("executeAt")]
    public AtDatetime? ExecuteAt { get; init; }

    /// <summary>The earliest time the action runs, for a randomized time.</summary>
    [JsonPropertyName("executeAfter")]
    public AtDatetime? ExecuteAfter { get; init; }

    /// <summary>The latest time the action runs, for a randomized time.</summary>
    [JsonPropertyName("executeUntil")]
    public AtDatetime? ExecuteUntil { get; init; }

    /// <summary>
    /// Whether the time is picked at random between <see cref="ExecuteAfter"/> and
    /// <see cref="ExecuteUntil"/>.
    /// </summary>
    [JsonPropertyName("randomizeExecution")]
    public bool? RandomizeExecution { get; init; }

    /// <summary>The moderator who scheduled the action.</summary>
    [JsonPropertyName("createdBy")]
    public required Did CreatedBy { get; init; }

    /// <summary>When the action was scheduled.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>When the scheduled action last changed.</summary>
    [JsonPropertyName("updatedAt")]
    public AtDatetime? UpdatedAt { get; init; }

    /// <summary>The action's status (see <see cref="ScheduledActionStatus"/>).</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>When Ozone last tried to run the action.</summary>
    [JsonPropertyName("lastExecutedAt")]
    public AtDatetime? LastExecutedAt { get; init; }

    /// <summary>Why the last attempt to run the action failed.</summary>
    [JsonPropertyName("lastFailureReason")]
    public string? LastFailureReason { get; init; }

    /// <summary>The moderation event the action emitted when it ran.</summary>
    [JsonPropertyName("executionEventId")]
    public long? ExecutionEventId { get; init; }
}

/// <summary>
/// The statuses of a scheduled action (<see cref="ScheduledActionView.Status"/>), for
/// <see cref="ModerationClient.ListScheduledActionsAsync"/>.
/// </summary>
public static class ScheduledActionStatus
{
    /// <summary>Waiting to run.</summary>
    public const string Pending = "pending";

    /// <summary>Ran.</summary>
    public const string Executed = "executed";

    /// <summary>Cancelled before it ran.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Ran and failed.</summary>
    public const string Failed = "failed";
}

/// <summary>
/// An action to schedule with <see cref="ModerationClient.ScheduleActionAsync"/> (the open
/// <c>tools.ozone.moderation.scheduleAction#input.action</c> union). An action this SDK does not
/// model reads as <see cref="UnknownScheduledAction"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownScheduledAction))]
[JsonDerivedType(typeof(ScheduledTakedown), "tools.ozone.moderation.scheduleAction#takedown")]
public abstract class ScheduledAction : LexObject;

/// <summary>
/// A scheduled action whose <c>$type</c> this SDK version does not model. It keeps the raw object
/// and writes it back unchanged; see <see cref="IUnknownUnionVariant"/>.
/// </summary>
public sealed class UnknownScheduledAction : ScheduledAction, IUnknownUnionVariant
{
    /// <summary>Creates an unknown scheduled action from its discriminator and raw object.</summary>
    /// <param name="type">The object's <c>$type</c>.</param>
    /// <param name="raw">The complete JSON object, including <c>$type</c>.</param>
    public UnknownScheduledAction(string type, JsonElement raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Raw = UnknownUnionVariant.RequireObject(raw);
    }

    /// <inheritdoc/>
    public string Type { get; }

    /// <inheritdoc/>
    public JsonElement Raw { get; }
}

/// <summary>A takedown to run later.</summary>
public sealed class ScheduledTakedown : ScheduledAction
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>How long the takedown lasts, in hours; <see langword="null"/> for permanently.</summary>
    [JsonPropertyName("durationInHours")]
    public int? DurationInHours { get; init; }

    /// <summary>Whether to also acknowledge every open report on the account's content.</summary>
    [JsonPropertyName("acknowledgeAccountSubjects")]
    public bool? AcknowledgeAccountSubjects { get; init; }

    /// <summary>The moderation policies the action enforces (at most 5).</summary>
    [JsonPropertyName("policies")]
    public IReadOnlyList<string>? Policies { get; init; }

    /// <summary>The severity level of the violation, such as <c>sev-1</c>.</summary>
    [JsonPropertyName("severityLevel")]
    public string? SeverityLevel { get; init; }

    /// <summary>The number of strikes the takedown gives the account.</summary>
    [JsonPropertyName("strikeCount")]
    public int? StrikeCount { get; init; }

    /// <summary>When the strikes expire; <see langword="null"/> for never.</summary>
    [JsonPropertyName("strikeExpiresAt")]
    public AtDatetime? StrikeExpiresAt { get; init; }

    /// <summary>The body of the email sent to the account when the takedown runs.</summary>
    [JsonPropertyName("emailContent")]
    public string? EmailContent { get; init; }

    /// <summary>The subject line of that email.</summary>
    [JsonPropertyName("emailSubject")]
    public string? EmailSubject { get; init; }
}

/// <summary>
/// When a scheduled action runs (<c>tools.ozone.moderation.scheduleAction#schedulingConfig</c>):
/// at <see cref="ExecuteAt"/>, or at a random time between <see cref="ExecuteAfter"/> and
/// <see cref="ExecuteUntil"/>.
/// </summary>
public sealed class SchedulingConfig : LexObject
{
    /// <summary>The exact time to run the action.</summary>
    [JsonPropertyName("executeAt")]
    public AtDatetime? ExecuteAt { get; init; }

    /// <summary>The earliest time to run the action, for a randomized time.</summary>
    [JsonPropertyName("executeAfter")]
    public AtDatetime? ExecuteAfter { get; init; }

    /// <summary>The latest time to run the action, for a randomized time.</summary>
    [JsonPropertyName("executeUntil")]
    public AtDatetime? ExecuteUntil { get; init; }
}

/// <summary>
/// Which accounts an action was scheduled for
/// (<c>tools.ozone.moderation.scheduleAction#scheduledActionResults</c>).
/// </summary>
public sealed class ScheduledActionResults : LexObject
{
    /// <summary>The accounts the action was scheduled for.</summary>
    [JsonPropertyName("succeeded")]
    public required IReadOnlyList<Did> Succeeded { get; init; }

    /// <summary>The accounts it could not be scheduled for, and why.</summary>
    [JsonPropertyName("failed")]
    public required IReadOnlyList<FailedScheduling> Failed { get; init; }
}

/// <summary>
/// An account an action could not be scheduled for
/// (<c>tools.ozone.moderation.scheduleAction#failedScheduling</c>).
/// </summary>
public sealed class FailedScheduling : LexObject
{
    /// <summary>The account.</summary>
    [JsonPropertyName("subject")]
    public required Did Subject { get; init; }

    /// <summary>What went wrong.</summary>
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    /// <summary>A machine-readable error code.</summary>
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Which accounts' scheduled actions were cancelled
/// (<c>tools.ozone.moderation.cancelScheduledActions#cancellationResults</c>).
/// </summary>
public sealed class CancellationResults : LexObject
{
    /// <summary>The accounts whose pending actions were all cancelled.</summary>
    [JsonPropertyName("succeeded")]
    public required IReadOnlyList<Did> Succeeded { get; init; }

    /// <summary>The accounts whose actions could not be cancelled, and why.</summary>
    [JsonPropertyName("failed")]
    public required IReadOnlyList<FailedCancellation> Failed { get; init; }
}

/// <summary>
/// An account whose scheduled actions could not be cancelled
/// (<c>tools.ozone.moderation.cancelScheduledActions#failedCancellation</c>).
/// </summary>
public sealed class FailedCancellation : LexObject
{
    /// <summary>The account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>What went wrong.</summary>
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    /// <summary>A machine-readable error code.</summary>
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }
}

// ─── Subject review state constants ───

/// <summary>
/// Review state constants for moderation subjects.
/// </summary>
public static class SubjectReviewState
{
    /// <summary>The <c>tools.ozone.moderation.defs#reviewOpen</c> subject review state.</summary>
    public const string Open = "tools.ozone.moderation.defs#reviewOpen";

    /// <summary>
    /// The <c>tools.ozone.moderation.defs#reviewEscalated</c> subject review state.
    /// </summary>
    public const string Escalated = "tools.ozone.moderation.defs#reviewEscalated";

    /// <summary>The <c>tools.ozone.moderation.defs#reviewClosed</c> subject review state.</summary>
    public const string Closed = "tools.ozone.moderation.defs#reviewClosed";

    /// <summary>The <c>tools.ozone.moderation.defs#reviewNone</c> subject review state.</summary>
    public const string None = "tools.ozone.moderation.defs#reviewNone";
}

// ─── Request / Response Models ───

/// <summary>
/// Request body for tools.ozone.moderation.emitEvent.
/// </summary>
public sealed class EmitEventRequest
{
    /// <summary>The moderation event to emit.</summary>
    [JsonPropertyName("event")]
    public required ModEventType Event { get; init; }

    /// <summary>The subject the event applies to.</summary>
    [JsonPropertyName("subject")]
    public required ModerationSubject Subject { get; init; }

    /// <summary>The CIDs of specific blobs on the subject record the action applies to.</summary>
    [JsonPropertyName("subjectBlobCids")]
    public IReadOnlyList<Cid>? SubjectBlobCids { get; init; }

    /// <summary>The DID of the account that created this.</summary>
    [JsonPropertyName("createdBy")]
    public required Did CreatedBy { get; init; }

    /// <summary>The tool emitting the event.</summary>
    [JsonPropertyName("modTool")]
    public ModTool? ModTool { get; init; }

    /// <summary>
    /// An identifier the caller chooses to make the call idempotent: a second event with the
    /// same one fails with <c>DuplicateExternalId</c>.
    /// </summary>
    [JsonPropertyName("externalId")]
    public string? ExternalId { get; init; }

    /// <summary>What to do with the subject's reports as the event is emitted.</summary>
    [JsonPropertyName("reportAction")]
    public ReportAction? ReportAction { get; init; }
}

/// <summary>
/// What an emitted event does to the subject's reports
/// (<c>tools.ozone.moderation.emitEvent#reportAction</c>).
/// </summary>
public sealed class ReportAction : LexObject
{
    /// <summary>The reports to act on, by identifier.</summary>
    [JsonPropertyName("ids")]
    public IReadOnlyList<long>? Ids { get; init; }

    /// <summary>The report types to act on.</summary>
    [JsonPropertyName("types")]
    public IReadOnlyList<string>? Types { get; init; }

    /// <summary>Whether to act on every report on the subject.</summary>
    [JsonPropertyName("all")]
    public bool? All { get; init; }

    /// <summary>A note to record with the action.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

/// <summary>
/// Response from tools.ozone.moderation.queryEvents.
/// </summary>
public sealed class QueryEventsResponse : ICursorPage<ModEventView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The moderation events.</summary>
    [JsonPropertyName("events")]
    public required IReadOnlyList<ModEventView> Events { get; init; }

    IReadOnlyList<ModEventView> ICursorPage<ModEventView>.Items => Events;
}

/// <summary>
/// Response from tools.ozone.moderation.queryStatuses (the review queue).
/// </summary>
public sealed class QueryStatusesResponse : ICursorPage<SubjectStatusView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The subjects' moderation statuses.</summary>
    [JsonPropertyName("subjectStatuses")]
    public required IReadOnlyList<SubjectStatusView> SubjectStatuses { get; init; }

    IReadOnlyList<SubjectStatusView> ICursorPage<SubjectStatusView>.Items => SubjectStatuses;
}

/// <summary>
/// Which subject statuses <see cref="ModerationClient.QueryStatusesAsync"/> returns, and in what
/// order. Every filter is optional; set only the ones you need.
/// </summary>
public sealed class SubjectStatusFilter
{
    internal static SubjectStatusFilter None { get; } = new();

    /// <summary>Only this subject: an account's DID, or a record's AT URI.</summary>
    public string? Subject { get; init; }

    /// <summary>
    /// With an account <see cref="Subject"/>, also the statuses of the account's records.
    /// </summary>
    public bool? IncludeAllUserRecords { get; init; }

    /// <summary>Only subjects of this kind: <c>account</c>, <c>record</c> or <c>conversation</c>.</summary>
    public string? SubjectType { get; init; }

    /// <summary>Only records in these collections.</summary>
    public IReadOnlyList<Nsid>? Collections { get; init; }

    /// <summary>Leave out these subjects: account DIDs or record AT URIs.</summary>
    public IReadOnlyList<string>? IgnoreSubjects { get; init; }

    /// <summary>Only subjects in this review state (see <see cref="SubjectReviewState"/>).</summary>
    public string? ReviewState { get; init; }

    /// <summary>Only subjects whose status comment contains this keyword.</summary>
    public string? Comment { get; init; }

    /// <summary>Only subjects reported after this time.</summary>
    public AtDatetime? ReportedAfter { get; init; }

    /// <summary>Only subjects reported before this time.</summary>
    public AtDatetime? ReportedBefore { get; init; }

    /// <summary>Only subjects reviewed after this time.</summary>
    public AtDatetime? ReviewedAfter { get; init; }

    /// <summary>Only subjects reviewed before this time.</summary>
    public AtDatetime? ReviewedBefore { get; init; }

    /// <summary>Only subjects last reviewed by this moderator.</summary>
    public Did? LastReviewedBy { get; init; }

    /// <summary>Only subjects with these hosting statuses (such as <c>deleted</c>).</summary>
    public IReadOnlyList<string>? HostingStatuses { get; init; }

    /// <summary>Only subjects deleted from their host after this time.</summary>
    public AtDatetime? HostingDeletedAfter { get; init; }

    /// <summary>Only subjects deleted from their host before this time.</summary>
    public AtDatetime? HostingDeletedBefore { get; init; }

    /// <summary>Only subjects whose hosting status changed after this time.</summary>
    public AtDatetime? HostingUpdatedAfter { get; init; }

    /// <summary>Only subjects whose hosting status changed before this time.</summary>
    public AtDatetime? HostingUpdatedBefore { get; init; }

    /// <summary>Whether to include muted subjects.</summary>
    public bool? IncludeMuted { get; init; }

    /// <summary>Whether to return only muted subjects.</summary>
    public bool? OnlyMuted { get; init; }

    /// <summary>Only subjects that are, or are not, taken down.</summary>
    public bool? Takendown { get; init; }

    /// <summary>Only subjects that do, or do not, have an unresolved appeal.</summary>
    public bool? Appealed { get; init; }

    /// <summary>Only subjects with these tags.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Leave out subjects with any of these tags.</summary>
    public IReadOnlyList<string>? ExcludeTags { get; init; }

    /// <summary>Only accounts suspended at least this many times.</summary>
    public int? MinAccountSuspendCount { get; init; }

    /// <summary>Only accounts with at least this many reported records.</summary>
    public int? MinReportedRecordsCount { get; init; }

    /// <summary>Only accounts with at least this many taken-down records.</summary>
    public int? MinTakendownRecordsCount { get; init; }

    /// <summary>Only subjects with at least this priority score.</summary>
    public int? MinPriorityScore { get; init; }

    /// <summary>Only accounts with at least this many active strikes.</summary>
    public int? MinStrikeCount { get; init; }

    /// <summary>
    /// Only accounts in this age-assurance state: <c>pending</c>, <c>assured</c>, <c>unknown</c>,
    /// <c>reset</c> or <c>blocked</c>.
    /// </summary>
    public string? AgeAssuranceState { get; init; }

    /// <summary>
    /// The field to sort by: <c>lastReportedAt</c> (the default), <c>lastReviewedAt</c>,
    /// <c>reportedRecordsCount</c>, <c>takendownRecordsCount</c> or <c>priorityScore</c>.
    /// </summary>
    public string? SortField { get; init; }

    /// <summary>The sort direction: <c>asc</c> or <c>desc</c> (the default).</summary>
    public string? SortDirection { get; init; }

    /// <summary>
    /// Split the queue into this many parts, so moderators can each work one; use with
    /// <see cref="QueueIndex"/>.
    /// </summary>
    public int? QueueCount { get; init; }

    /// <summary>Which part of a split queue to return, from 0.</summary>
    public int? QueueIndex { get; init; }

    /// <summary>A seed that keeps the split of the queue stable across requests.</summary>
    public string? QueueSeed { get; init; }

    internal XrpcParams ToParams() => new XrpcParams()
        .Add("subject", Subject)
        .Add("includeAllUserRecords", IncludeAllUserRecords)
        .Add("subjectType", SubjectType)
        .AddAll("collections", Collections?.Select(collection => collection.Value))
        .AddAll("ignoreSubjects", IgnoreSubjects)
        .Add("reviewState", ReviewState)
        .Add("comment", Comment)
        .Add("reportedAfter", ReportedAfter?.ToString())
        .Add("reportedBefore", ReportedBefore?.ToString())
        .Add("reviewedAfter", ReviewedAfter?.ToString())
        .Add("reviewedBefore", ReviewedBefore?.ToString())
        .Add("lastReviewedBy", LastReviewedBy)
        .AddAll("hostingStatuses", HostingStatuses)
        .Add("hostingDeletedAfter", HostingDeletedAfter?.ToString())
        .Add("hostingDeletedBefore", HostingDeletedBefore?.ToString())
        .Add("hostingUpdatedAfter", HostingUpdatedAfter?.ToString())
        .Add("hostingUpdatedBefore", HostingUpdatedBefore?.ToString())
        .Add("includeMuted", IncludeMuted)
        .Add("onlyMuted", OnlyMuted)
        .Add("takendown", Takendown)
        .Add("appealed", Appealed)
        .AddAll("tags", Tags)
        .AddAll("excludeTags", ExcludeTags)
        .Add("minAccountSuspendCount", MinAccountSuspendCount)
        .Add("minReportedRecordsCount", MinReportedRecordsCount)
        .Add("minTakendownRecordsCount", MinTakendownRecordsCount)
        .Add("minPriorityScore", MinPriorityScore)
        .Add("minStrikeCount", MinStrikeCount)
        .Add("ageAssuranceState", AgeAssuranceState)
        .Add("sortField", SortField)
        .Add("sortDirection", SortDirection)
        .Add("queueCount", QueueCount)
        .Add("queueIndex", QueueIndex)
        .Add("queueSeed", QueueSeed);
}

/// <summary>
/// Response from tools.ozone.moderation.searchRepos.
/// </summary>
public sealed class SearchReposResponse : ICursorPage<RepoView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The repositories.</summary>
    [JsonPropertyName("repos")]
    public required IReadOnlyList<RepoView> Repos { get; init; }

    IReadOnlyList<RepoView> ICursorPage<RepoView>.Items => Repos;
}

/// <summary>
/// Response from tools.ozone.moderation.getAccountPreferences.
/// </summary>
public sealed class GetAccountPreferencesResponse
{
    /// <summary>The account's private app preferences, one object per kind.</summary>
    [JsonPropertyName("preferences")]
    public required IReadOnlyList<Preference> Preferences { get; init; }
}

/// <summary>
/// Response from tools.ozone.moderation.getRepos.
/// </summary>
public sealed class GetReposResponse
{
    /// <summary>
    /// One entry per requested DID: a <see cref="RepoViewDetail"/>, or a
    /// <see cref="RepoViewNotFound"/> for an account Ozone does not know.
    /// </summary>
    [JsonPropertyName("repos")]
    public required IReadOnlyList<ModerationSubjectView> Repos { get; init; }
}

/// <summary>
/// Response from tools.ozone.moderation.getRecords.
/// </summary>
public sealed class GetRecordsResponse
{
    /// <summary>
    /// One entry per requested AT URI: a <see cref="RecordViewDetail"/>, or a
    /// <see cref="RecordViewNotFound"/> for a record Ozone does not know.
    /// </summary>
    [JsonPropertyName("records")]
    public required IReadOnlyList<ModerationSubjectView> Records { get; init; }
}

/// <summary>
/// Response from tools.ozone.moderation.getSubjects.
/// </summary>
public sealed class GetSubjectsResponse
{
    /// <summary>The subjects.</summary>
    [JsonPropertyName("subjects")]
    public required IReadOnlyList<SubjectView> Subjects { get; init; }
}

/// <summary>
/// Response from tools.ozone.moderation.getAccountTimeline.
/// </summary>
public sealed class GetAccountTimelineResponse
{
    /// <summary>The account's history, one entry per day.</summary>
    [JsonPropertyName("timeline")]
    public required IReadOnlyList<TimelineItem> Timeline { get; init; }
}

/// <summary>
/// Response from tools.ozone.moderation.getReporterStats.
/// </summary>
public sealed class GetReporterStatsResponse
{
    /// <summary>The statistics, one entry per reporter.</summary>
    [JsonPropertyName("stats")]
    public required IReadOnlyList<ReporterStats> Stats { get; init; }
}

/// <summary>
/// Request body for tools.ozone.moderation.scheduleAction.
/// </summary>
internal sealed class ScheduleActionRequest
{
    /// <summary>The action to schedule.</summary>
    [JsonPropertyName("action")]
    public required ScheduledAction Action { get; init; }

    /// <summary>The accounts to schedule it for.</summary>
    [JsonPropertyName("subjects")]
    public required IReadOnlyList<Did> Subjects { get; init; }

    /// <summary>The moderator scheduling it.</summary>
    [JsonPropertyName("createdBy")]
    public required Did CreatedBy { get; init; }

    /// <summary>When it runs.</summary>
    [JsonPropertyName("scheduling")]
    public required SchedulingConfig Scheduling { get; init; }

    /// <summary>The tool scheduling it, passed on to the event it emits.</summary>
    [JsonPropertyName("modTool")]
    public ModTool? ModTool { get; init; }
}

/// <summary>
/// Request body for tools.ozone.moderation.listScheduledActions.
/// </summary>
internal sealed class ListScheduledActionsRequest
{
    /// <summary>Only actions scheduled to run after this time.</summary>
    [JsonPropertyName("startsAfter")]
    public AtDatetime? StartsAfter { get; init; }

    /// <summary>Only actions scheduled to run before this time.</summary>
    [JsonPropertyName("endsBefore")]
    public AtDatetime? EndsBefore { get; init; }

    /// <summary>Only actions for these accounts.</summary>
    [JsonPropertyName("subjects")]
    public IReadOnlyList<Did>? Subjects { get; init; }

    /// <summary>Only actions in these statuses.</summary>
    [JsonPropertyName("statuses")]
    public required IReadOnlyList<string> Statuses { get; init; }

    /// <summary>Maximum number of actions.</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    /// <summary>Pagination cursor from a previous response.</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

/// <summary>
/// Response from tools.ozone.moderation.listScheduledActions.
/// </summary>
public sealed class ListScheduledActionsResponse : ICursorPage<ScheduledActionView>
{
    /// <summary>The scheduled actions.</summary>
    [JsonPropertyName("actions")]
    public required IReadOnlyList<ScheduledActionView> Actions { get; init; }

    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    IReadOnlyList<ScheduledActionView> ICursorPage<ScheduledActionView>.Items => Actions;
}

/// <summary>
/// Request body for tools.ozone.moderation.cancelScheduledActions.
/// </summary>
internal sealed class CancelScheduledActionsRequest
{
    /// <summary>The accounts whose pending actions to cancel.</summary>
    [JsonPropertyName("subjects")]
    public required IReadOnlyList<Did> Subjects { get; init; }

    /// <summary>Why they are cancelled.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}
