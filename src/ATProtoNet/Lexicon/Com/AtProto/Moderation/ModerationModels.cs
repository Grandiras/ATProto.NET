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
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// A record subject for moderation reports.
/// </summary>
public sealed class RecordSubject : ReportSubject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
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
    /// <summary>The identifier of the report.</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary>The reason type the report was filed under.</summary>
    [JsonPropertyName("reasonType")]
    public required string ReasonType { get; init; }

    /// <summary>The free-text reason given by the reporter.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>The subject the report was filed against.</summary>
    [JsonPropertyName("subject")]
    public required JsonElement Subject { get; init; }

    /// <summary>The DID of the account that filed the report.</summary>
    [JsonPropertyName("reportedBy")]
    public required string ReportedBy { get; init; }

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// Well-known moderation report reason types.
/// </summary>
public static class ReportReasons
{
    /// <summary>The <c>com.atproto.moderation.defs#reasonSpam</c> report reason.</summary>
    public const string Spam = "com.atproto.moderation.defs#reasonSpam";

    /// <summary>The <c>com.atproto.moderation.defs#reasonViolation</c> report reason.</summary>
    public const string Violation = "com.atproto.moderation.defs#reasonViolation";

    /// <summary>The <c>com.atproto.moderation.defs#reasonMisleading</c> report reason.</summary>
    public const string Misleading = "com.atproto.moderation.defs#reasonMisleading";

    /// <summary>The <c>com.atproto.moderation.defs#reasonSexual</c> report reason.</summary>
    public const string Sexual = "com.atproto.moderation.defs#reasonSexual";

    /// <summary>The <c>com.atproto.moderation.defs#reasonRude</c> report reason.</summary>
    public const string Rude = "com.atproto.moderation.defs#reasonRude";

    /// <summary>The <c>com.atproto.moderation.defs#reasonOther</c> report reason.</summary>
    public const string Other = "com.atproto.moderation.defs#reasonOther";

    /// <summary>The <c>com.atproto.moderation.defs#reasonAppeal</c> report reason.</summary>
    public const string Appeal = "com.atproto.moderation.defs#reasonAppeal";
}
