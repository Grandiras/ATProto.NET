using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

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
}

/// <summary>A moderation event that restores a taken-down subject.</summary>
public sealed class ModEventReverseTakedown : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

/// <summary>A moderation event that acknowledges the subject and closes its review.</summary>
public sealed class ModEventAcknowledge : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
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
    public IReadOnlyList<string>? CreateLabelVals { get; init; }

    /// <summary>The label values to remove.</summary>
    [JsonPropertyName("negateLabelVals")]
    public IReadOnlyList<string>? NegateLabelVals { get; init; }
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

    /// <summary>The report type, for report events.</summary>
    [JsonPropertyName("reportType")]
    public string? ReportType { get; init; }
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

/// <summary>A moderation event that mutes reports from the subject for a period.</summary>
public sealed class ModEventMuteReporter : ModEventType
{
    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>Duration of the action in hours.</summary>
    [JsonPropertyName("durationInHours")]
    public required int DurationInHours { get; init; }
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
    public IReadOnlyList<string>? Add { get; init; }

    /// <summary>The tags to remove.</summary>
    [JsonPropertyName("remove")]
    public IReadOnlyList<string>? Remove { get; init; }
}

// ─── Subject Types ───

/// <summary>
/// A moderation subject — either a repo (account) or a specific record. A subject type this SDK
/// does not model reads as <see cref="UnknownModerationSubject"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownModerationSubject))]
[JsonDerivedType(typeof(RepoSubject), "com.atproto.admin.defs#repoRef")]
[JsonDerivedType(typeof(RecordSubject), "com.atproto.repo.strongRef")]
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
    public IReadOnlyList<JsonElement>? SubjectBlobs { get; init; }

    /// <summary>The DID of the account that created this.</summary>
    [JsonPropertyName("createdBy")]
    public required Did CreatedBy { get; init; }

    /// <summary>When the event was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// Subject status view from querySubjects.
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
}

/// <summary>
/// Record view with moderation context.
/// </summary>
public sealed class RecordViewDetail : LexObject
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

    /// <summary>The CIDs of blobs referenced by the record.</summary>
    [JsonPropertyName("blobCids")]
    public IReadOnlyList<Cid>? BlobCids { get; init; }

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
public sealed class RepoViewDetail : LexObject
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
/// Response from tools.ozone.moderation.querySubjects (subject queue view).
/// </summary>
public sealed class QuerySubjectsResponse : ICursorPage<SubjectStatusView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The subject status records.</summary>
    [JsonPropertyName("subjects")]
    public required IReadOnlyList<SubjectStatusView> Subjects { get; init; }

    IReadOnlyList<SubjectStatusView> ICursorPage<SubjectStatusView>.Items => Subjects;
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
