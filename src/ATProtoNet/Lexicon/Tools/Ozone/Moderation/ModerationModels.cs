using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Moderation;

// ─── Moderation Event Types ───

/// <summary>
/// Base moderation event that captures all event types emitted by Ozone.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
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
public abstract class ModEventType { }

public sealed class ModEventTakedown : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("durationInHours")]
    public int? DurationInHours { get; init; }
}

public sealed class ModEventReverseTakedown : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

public sealed class ModEventAcknowledge : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

public sealed class ModEventEscalate : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

public sealed class ModEventLabel : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("createLabelVals")]
    public List<string>? CreateLabelVals { get; init; }

    [JsonPropertyName("negateLabelVals")]
    public List<string>? NegateLabelVals { get; init; }
}

public sealed class ModEventComment : ModEventType
{
    [JsonPropertyName("comment")]
    public required string Comment { get; init; }

    [JsonPropertyName("sticky")]
    public bool? Sticky { get; init; }
}

public sealed class ModEventReport : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("isReporterMuted")]
    public bool? IsReporterMuted { get; init; }

    [JsonPropertyName("reportType")]
    public string? ReportType { get; init; }
}

public sealed class ModEventMute : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("durationInHours")]
    public required int DurationInHours { get; init; }
}

public sealed class ModEventUnmute : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

public sealed class ModEventMuteReporter : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("durationInHours")]
    public required int DurationInHours { get; init; }
}

public sealed class ModEventUnmuteReporter : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

public sealed class ModEventEmail : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("subjectLine")]
    public required string SubjectLine { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

public sealed class ModEventDivert : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

public sealed class ModEventTag : ModEventType
{
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("add")]
    public List<string>? Add { get; init; }

    [JsonPropertyName("remove")]
    public List<string>? Remove { get; init; }
}

// ─── Subject Types ───

/// <summary>
/// A moderation subject — either a repo (account) or a specific record.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RepoSubject), "com.atproto.admin.defs#repoRef")]
[JsonDerivedType(typeof(RecordSubject), "com.atproto.repo.strongRef")]
public abstract class ModerationSubject { }

public sealed class RepoSubject : ModerationSubject
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

public sealed class RecordSubject : ModerationSubject
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public string? Cid { get; init; }
}

// ─── View Models ───

/// <summary>
/// A moderation event record as returned by the API.
/// </summary>
public sealed class ModEventView
{
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    [JsonPropertyName("event")]
    public required ModEventType Event { get; init; }

    [JsonPropertyName("subject")]
    public required ModerationSubject Subject { get; init; }

    [JsonPropertyName("subjectBlobCids")]
    public List<string>? SubjectBlobCids { get; init; }

    [JsonPropertyName("createdBy")]
    public required string CreatedBy { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("creatorHandle")]
    public string? CreatorHandle { get; init; }

    [JsonPropertyName("subjectHandle")]
    public string? SubjectHandle { get; init; }
}

/// <summary>
/// Moderation event detail view with subject/event metadata.
/// </summary>
public sealed class ModEventViewDetail
{
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    [JsonPropertyName("event")]
    public required ModEventType Event { get; init; }

    [JsonPropertyName("subject")]
    public required JsonElement Subject { get; init; }

    [JsonPropertyName("subjectBlobs")]
    public List<JsonElement>? SubjectBlobs { get; init; }

    [JsonPropertyName("createdBy")]
    public required string CreatedBy { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// Subject status view from querySubjects.
/// </summary>
public sealed class SubjectStatusView
{
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    [JsonPropertyName("subject")]
    public required ModerationSubject Subject { get; init; }

    [JsonPropertyName("subjectBlobCids")]
    public List<string>? SubjectBlobCids { get; init; }

    [JsonPropertyName("subjectRepoHandle")]
    public string? SubjectRepoHandle { get; init; }

    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("reviewState")]
    public required string ReviewState { get; init; }

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("muteUntil")]
    public string? MuteUntil { get; init; }

    [JsonPropertyName("muteReportingUntil")]
    public string? MuteReportingUntil { get; init; }

    [JsonPropertyName("lastReviewedBy")]
    public string? LastReviewedBy { get; init; }

    [JsonPropertyName("lastReviewedAt")]
    public string? LastReviewedAt { get; init; }

    [JsonPropertyName("lastReportedAt")]
    public string? LastReportedAt { get; init; }

    [JsonPropertyName("lastAppealedAt")]
    public string? LastAppealedAt { get; init; }

    [JsonPropertyName("takendown")]
    public bool? Takendown { get; init; }

    [JsonPropertyName("appealed")]
    public bool? Appealed { get; init; }

    [JsonPropertyName("suspendUntil")]
    public string? SuspendUntil { get; init; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; init; }
}

/// <summary>
/// Record view with moderation context.
/// </summary>
public sealed class RecordViewDetail
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("value")]
    public required JsonElement Value { get; init; }

    [JsonPropertyName("blobCids")]
    public List<string>? BlobCids { get; init; }

    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    [JsonPropertyName("moderation")]
    public required ModerationDetail Moderation { get; init; }

    [JsonPropertyName("repo")]
    public required RepoView Repo { get; init; }
}

/// <summary>
/// Moderation detail attached to a record or repo view.
/// </summary>
public sealed class ModerationDetail
{
    [JsonPropertyName("subjectStatus")]
    public SubjectStatusView? SubjectStatus { get; init; }
}

/// <summary>
/// Repo/account view with moderation context.
/// </summary>
public sealed class RepoView
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("relatedRecords")]
    public List<JsonElement>? RelatedRecords { get; init; }

    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    [JsonPropertyName("moderation")]
    public required ModerationDetail Moderation { get; init; }

    [JsonPropertyName("invitedBy")]
    public JsonElement? InvitedBy { get; init; }

    [JsonPropertyName("invitesDisabled")]
    public bool? InvitesDisabled { get; init; }

    [JsonPropertyName("inviteNote")]
    public string? InviteNote { get; init; }

    [JsonPropertyName("deactivatedAt")]
    public string? DeactivatedAt { get; init; }

    [JsonPropertyName("threatSignatures")]
    public List<JsonElement>? ThreatSignatures { get; init; }
}

/// <summary>
/// Repo view detail with additional fields.
/// </summary>
public sealed class RepoViewDetail
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("relatedRecords")]
    public List<JsonElement>? RelatedRecords { get; init; }

    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    [JsonPropertyName("moderation")]
    public required ModerationDetail Moderation { get; init; }

    [JsonPropertyName("labels")]
    public List<JsonElement>? Labels { get; init; }

    [JsonPropertyName("invitedBy")]
    public JsonElement? InvitedBy { get; init; }

    [JsonPropertyName("invites")]
    public List<JsonElement>? Invites { get; init; }

    [JsonPropertyName("invitesDisabled")]
    public bool? InvitesDisabled { get; init; }

    [JsonPropertyName("inviteNote")]
    public string? InviteNote { get; init; }

    [JsonPropertyName("emailConfirmedAt")]
    public string? EmailConfirmedAt { get; init; }

    [JsonPropertyName("deactivatedAt")]
    public string? DeactivatedAt { get; init; }

    [JsonPropertyName("threatSignatures")]
    public List<JsonElement>? ThreatSignatures { get; init; }
}

// ─── Subject review state constants ───

/// <summary>
/// Review state constants for moderation subjects.
/// </summary>
public static class SubjectReviewState
{
    public const string Open = "tools.ozone.moderation.defs#reviewOpen";
    public const string Escalated = "tools.ozone.moderation.defs#reviewEscalated";
    public const string Closed = "tools.ozone.moderation.defs#reviewClosed";
    public const string None = "tools.ozone.moderation.defs#reviewNone";
}

// ─── Request / Response Models ───

/// <summary>
/// Request body for tools.ozone.moderation.emitEvent.
/// </summary>
public sealed class EmitEventRequest
{
    [JsonPropertyName("event")]
    public required ModEventType Event { get; init; }

    [JsonPropertyName("subject")]
    public required ModerationSubject Subject { get; init; }

    [JsonPropertyName("subjectBlobCids")]
    public List<string>? SubjectBlobCids { get; init; }

    [JsonPropertyName("createdBy")]
    public required string CreatedBy { get; init; }
}

/// <summary>
/// Response from tools.ozone.moderation.queryEvents.
/// </summary>
public sealed class QueryEventsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("events")]
    public required List<ModEventView> Events { get; init; }
}

/// <summary>
/// Response from tools.ozone.moderation.querySubjects (subject queue view).
/// </summary>
public sealed class QuerySubjectsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("subjects")]
    public required List<SubjectStatusView> Subjects { get; init; }
}

/// <summary>
/// Response from tools.ozone.moderation.searchRepos.
/// </summary>
public sealed class SearchReposResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("repos")]
    public required List<RepoView> Repos { get; init; }
}
