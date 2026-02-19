using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.Moderation;

// ──────────────────────────────────────────────────────────────
//  com.atproto.moderation.createReport
// ──────────────────────────────────────────────────────────────

/// <summary>
/// The subject of a moderation report – can be a repo (account) or a record.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RepoSubject), "com.atproto.admin.defs#repoRef")]
[JsonDerivedType(typeof(RecordSubject), "com.atproto.repo.strongRef")]
public abstract class ReportSubject { }

/// <summary>
/// A repository (account) subject for moderation reports.
/// </summary>
public sealed class RepoSubject : ReportSubject
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// A record subject for moderation reports.
/// </summary>
public sealed class RecordSubject : ReportSubject
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }
}

/// <summary>
/// Request body for creating a moderation report.
/// </summary>
public sealed class CreateReportRequest
{
    /// <summary>
    /// The reason type for the report. Common values:
    /// "com.atproto.moderation.defs#reasonSpam",
    /// "com.atproto.moderation.defs#reasonViolation",
    /// "com.atproto.moderation.defs#reasonMisleading",
    /// "com.atproto.moderation.defs#reasonSexual",
    /// "com.atproto.moderation.defs#reasonRude",
    /// "com.atproto.moderation.defs#reasonOther",
    /// "com.atproto.moderation.defs#reasonAppeal"
    /// </summary>
    [JsonPropertyName("reasonType")]
    public required string ReasonType { get; init; }

    /// <summary>Optional free-text reason.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>The subject being reported.</summary>
    [JsonPropertyName("subject")]
    public required ReportSubject Subject { get; init; }
}

/// <summary>
/// Response from createReport.
/// </summary>
public sealed class CreateReportResponse
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("reasonType")]
    public required string ReasonType { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("subject")]
    public required JsonElement Subject { get; init; }

    [JsonPropertyName("reportedBy")]
    public required string ReportedBy { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// Well-known moderation report reason types.
/// </summary>
public static class ReportReasons
{
    public const string Spam = "com.atproto.moderation.defs#reasonSpam";
    public const string Violation = "com.atproto.moderation.defs#reasonViolation";
    public const string Misleading = "com.atproto.moderation.defs#reasonMisleading";
    public const string Sexual = "com.atproto.moderation.defs#reasonSexual";
    public const string Rude = "com.atproto.moderation.defs#reasonRude";
    public const string Other = "com.atproto.moderation.defs#reasonOther";
    public const string Appeal = "com.atproto.moderation.defs#reasonAppeal";
}
