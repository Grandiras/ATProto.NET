using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.Moderation;

// ──────────────────────────────────────────────────────────────
//  com.atproto.moderation.createReport
// ──────────────────────────────────────────────────────────────

/// <summary>
/// What a moderation action is about: an account, a record, a blob, or a chat message or
/// conversation. One union serves reports (<c>com.atproto.moderation.createReport</c>), a PDS's
/// subject status (<c>com.atproto.admin.*SubjectStatus</c>) and Ozone's events and statuses
/// (<c>tools.ozone.moderation.*</c>); each method accepts the variants its Lexicon lists, and a
/// subject type this SDK does not model reads as <see cref="UnknownModerationSubject"/>.
/// </summary>
[AtProtoUnion(typeof(UnknownModerationSubject))]
[JsonDerivedType(typeof(RepoSubject), "com.atproto.admin.defs#repoRef")]
[JsonDerivedType(typeof(RecordSubject), "com.atproto.repo.strongRef")]
[JsonDerivedType(typeof(RepoBlobSubject), "com.atproto.admin.defs#repoBlobRef")]
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

/// <summary>
/// A repository (account) as a moderation subject (<c>com.atproto.admin.defs#repoRef</c>).
/// </summary>
public sealed class RepoSubject : ModerationSubject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }
}

/// <summary>
/// A record version as a moderation subject (<c>com.atproto.repo.strongRef</c>).
/// </summary>
public sealed class RecordSubject : ModerationSubject
{
    /// <summary>The AT-URI of the record (<c>at://did/collection/rkey</c>).</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content identifier) of the record version.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }
}

/// <summary>
/// A blob in an account's repository as a moderation subject
/// (<c>com.atproto.admin.defs#repoBlobRef</c>).
/// </summary>
public sealed class RepoBlobSubject : ModerationSubject
{
    /// <summary>The DID of the account the blob belongs to.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The CID of the blob.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>The AT-URI of the record that references the blob, if known.</summary>
    [JsonPropertyName("recordUri")]
    public AtUri? RecordUri { get; init; }
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

/// <summary>
/// Request body for creating a moderation report.
/// </summary>
internal sealed class CreateReportRequest
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
    public required ModerationSubject Subject { get; init; }

    /// <summary>The tool that filed the report.</summary>
    [JsonPropertyName("modTool")]
    public ModTool? ModTool { get; init; }
}

/// <summary>
/// The tool a report was filed with (<c>com.atproto.moderation.createReport#modTool</c>).
/// </summary>
public sealed class ModTool : LexObject
{
    /// <summary>The tool's name, such as <c>bsky-app/android</c> or <c>bsky-web/chrome</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Additional information about the tool, in a shape the tool defines.</summary>
    [JsonPropertyName("meta")]
    public JsonElement? Meta { get; init; }
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
    public required ModerationSubject Subject { get; init; }

    /// <summary>The DID of the account that filed the report.</summary>
    [JsonPropertyName("reportedBy")]
    public required Did ReportedBy { get; init; }

    /// <summary>Timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }
}

/// <summary>
/// Well-known moderation report reason types (<c>com.atproto.moderation.defs#reasonType</c>).
/// </summary>
/// <remarks>
/// Two sets: the original coarse <c>com.atproto.moderation.defs#reason*</c> reasons (<see cref="Spam"/>
/// to <see cref="Appeal"/>), and the granular <c>tools.ozone.report.defs#reason*</c> reasons,
/// grouped by category, which upstream now prefers. Each coarse reason names its granular
/// replacement. The granular appeal and catch-all reasons are <see cref="OzoneAppeal"/> and
/// <see cref="OzoneOther"/>. Some reasons go only to the app's moderation authority, not to
/// third-party labelers; their summaries say so.
/// </remarks>
public static class ReportReasons
{
    /// <summary>
    /// Spam: frequent unwanted promotion, replies or mentions
    /// (<c>com.atproto.moderation.defs#reasonSpam</c>). Prefer <see cref="MisleadingSpam"/>.
    /// </summary>
    public const string Spam = "com.atproto.moderation.defs#reasonSpam";

    /// <summary>
    /// A direct violation of server rules, laws or terms of service
    /// (<c>com.atproto.moderation.defs#reasonViolation</c>). Prefer <see cref="RuleOther"/>.
    /// </summary>
    public const string Violation = "com.atproto.moderation.defs#reasonViolation";

    /// <summary>
    /// Misleading identity, affiliation or content
    /// (<c>com.atproto.moderation.defs#reasonMisleading</c>). Prefer <see cref="MisleadingOther"/>.
    /// </summary>
    public const string Misleading = "com.atproto.moderation.defs#reasonMisleading";

    /// <summary>
    /// Unwanted or mislabeled sexual content (<c>com.atproto.moderation.defs#reasonSexual</c>).
    /// Prefer <see cref="SexualUnlabeled"/>.
    /// </summary>
    public const string Sexual = "com.atproto.moderation.defs#reasonSexual";

    /// <summary>
    /// Rude, harassing, explicit or otherwise unwelcoming behavior
    /// (<c>com.atproto.moderation.defs#reasonRude</c>). Prefer <see cref="HarassmentOther"/>.
    /// </summary>
    public const string Rude = "com.atproto.moderation.defs#reasonRude";

    /// <summary>
    /// A report that fits no other category (<c>com.atproto.moderation.defs#reasonOther</c>).
    /// Prefer <see cref="OzoneOther"/>.
    /// </summary>
    public const string Other = "com.atproto.moderation.defs#reasonOther";

    /// <summary>
    /// An appeal of a moderation action (<c>com.atproto.moderation.defs#reasonAppeal</c>).
    /// </summary>
    public const string Appeal = "com.atproto.moderation.defs#reasonAppeal";

    /// <summary>
    /// An appeal of a moderation action, in the granular set
    /// (<c>tools.ozone.report.defs#reasonAppeal</c>).
    /// </summary>
    public const string OzoneAppeal = "tools.ozone.report.defs#reasonAppeal";

    /// <summary>
    /// An issue none of the granular reasons covers (<c>tools.ozone.report.defs#reasonOther</c>).
    /// </summary>
    public const string OzoneOther = "tools.ozone.report.defs#reasonOther";

    // ─── Violence ───

    /// <summary>Animal welfare violations (<c>tools.ozone.report.defs#reasonViolenceAnimal</c>).</summary>
    public const string ViolenceAnimal = "tools.ozone.report.defs#reasonViolenceAnimal";

    /// <summary>Threats or incitement (<c>tools.ozone.report.defs#reasonViolenceThreats</c>).</summary>
    public const string ViolenceThreats = "tools.ozone.report.defs#reasonViolenceThreats";

    /// <summary>Graphic violent content (<c>tools.ozone.report.defs#reasonViolenceGraphicContent</c>).</summary>
    public const string ViolenceGraphicContent = "tools.ozone.report.defs#reasonViolenceGraphicContent";

    /// <summary>Glorification of violence (<c>tools.ozone.report.defs#reasonViolenceGlorification</c>).</summary>
    public const string ViolenceGlorification = "tools.ozone.report.defs#reasonViolenceGlorification";

    /// <summary>
    /// Extremist content (<c>tools.ozone.report.defs#reasonViolenceExtremistContent</c>). Goes
    /// only to the app's moderation authority.
    /// </summary>
    public const string ViolenceExtremistContent = "tools.ozone.report.defs#reasonViolenceExtremistContent";

    /// <summary>Human trafficking (<c>tools.ozone.report.defs#reasonViolenceTrafficking</c>).</summary>
    public const string ViolenceTrafficking = "tools.ozone.report.defs#reasonViolenceTrafficking";

    /// <summary>Other violent content (<c>tools.ozone.report.defs#reasonViolenceOther</c>).</summary>
    public const string ViolenceOther = "tools.ozone.report.defs#reasonViolenceOther";

    // ─── Sexual content ───

    /// <summary>Adult sexual abuse content (<c>tools.ozone.report.defs#reasonSexualAbuseContent</c>).</summary>
    public const string SexualAbuseContent = "tools.ozone.report.defs#reasonSexualAbuseContent";

    /// <summary>Non-consensual intimate imagery (<c>tools.ozone.report.defs#reasonSexualNCII</c>).</summary>
    public const string SexualNcii = "tools.ozone.report.defs#reasonSexualNCII";

    /// <summary>Deepfake adult content (<c>tools.ozone.report.defs#reasonSexualDeepfake</c>).</summary>
    public const string SexualDeepfake = "tools.ozone.report.defs#reasonSexualDeepfake";

    /// <summary>Animal sexual abuse (<c>tools.ozone.report.defs#reasonSexualAnimal</c>).</summary>
    public const string SexualAnimal = "tools.ozone.report.defs#reasonSexualAnimal";

    /// <summary>Unlabeled adult content (<c>tools.ozone.report.defs#reasonSexualUnlabeled</c>).</summary>
    public const string SexualUnlabeled = "tools.ozone.report.defs#reasonSexualUnlabeled";

    /// <summary>Other sexual violence content (<c>tools.ozone.report.defs#reasonSexualOther</c>).</summary>
    public const string SexualOther = "tools.ozone.report.defs#reasonSexualOther";

    // ─── Child safety ───

    /// <summary>
    /// Child sexual abuse material (<c>tools.ozone.report.defs#reasonChildSafetyCSAM</c>). Goes
    /// only to the app's moderation authority.
    /// </summary>
    public const string ChildSafetyCsam = "tools.ozone.report.defs#reasonChildSafetyCSAM";

    /// <summary>
    /// Grooming or predatory behavior (<c>tools.ozone.report.defs#reasonChildSafetyGroom</c>).
    /// Goes only to the app's moderation authority.
    /// </summary>
    public const string ChildSafetyGroom = "tools.ozone.report.defs#reasonChildSafetyGroom";

    /// <summary>
    /// A privacy violation involving a minor (<c>tools.ozone.report.defs#reasonChildSafetyPrivacy</c>).
    /// </summary>
    public const string ChildSafetyPrivacy = "tools.ozone.report.defs#reasonChildSafetyPrivacy";

    /// <summary>
    /// Harassment or bullying of minors (<c>tools.ozone.report.defs#reasonChildSafetyHarassment</c>).
    /// </summary>
    public const string ChildSafetyHarassment = "tools.ozone.report.defs#reasonChildSafetyHarassment";

    /// <summary>
    /// Other child safety issues (<c>tools.ozone.report.defs#reasonChildSafetyOther</c>). Goes
    /// only to the app's moderation authority.
    /// </summary>
    public const string ChildSafetyOther = "tools.ozone.report.defs#reasonChildSafetyOther";

    // ─── Harassment ───

    /// <summary>Trolling (<c>tools.ozone.report.defs#reasonHarassmentTroll</c>).</summary>
    public const string HarassmentTroll = "tools.ozone.report.defs#reasonHarassmentTroll";

    /// <summary>Targeted harassment (<c>tools.ozone.report.defs#reasonHarassmentTargeted</c>).</summary>
    public const string HarassmentTargeted = "tools.ozone.report.defs#reasonHarassmentTargeted";

    /// <summary>Hate speech (<c>tools.ozone.report.defs#reasonHarassmentHateSpeech</c>).</summary>
    public const string HarassmentHateSpeech = "tools.ozone.report.defs#reasonHarassmentHateSpeech";

    /// <summary>Doxxing (<c>tools.ozone.report.defs#reasonHarassmentDoxxing</c>).</summary>
    public const string HarassmentDoxxing = "tools.ozone.report.defs#reasonHarassmentDoxxing";

    /// <summary>
    /// Other harassing or hateful content (<c>tools.ozone.report.defs#reasonHarassmentOther</c>).
    /// </summary>
    public const string HarassmentOther = "tools.ozone.report.defs#reasonHarassmentOther";

    // ─── Misleading ───

    /// <summary>A fake account or bot (<c>tools.ozone.report.defs#reasonMisleadingBot</c>).</summary>
    public const string MisleadingBot = "tools.ozone.report.defs#reasonMisleadingBot";

    /// <summary>Impersonation (<c>tools.ozone.report.defs#reasonMisleadingImpersonation</c>).</summary>
    public const string MisleadingImpersonation = "tools.ozone.report.defs#reasonMisleadingImpersonation";

    /// <summary>Spam (<c>tools.ozone.report.defs#reasonMisleadingSpam</c>).</summary>
    public const string MisleadingSpam = "tools.ozone.report.defs#reasonMisleadingSpam";

    /// <summary>A scam (<c>tools.ozone.report.defs#reasonMisleadingScam</c>).</summary>
    public const string MisleadingScam = "tools.ozone.report.defs#reasonMisleadingScam";

    /// <summary>
    /// False information about elections (<c>tools.ozone.report.defs#reasonMisleadingElections</c>).
    /// </summary>
    public const string MisleadingElections = "tools.ozone.report.defs#reasonMisleadingElections";

    /// <summary>Other misleading content (<c>tools.ozone.report.defs#reasonMisleadingOther</c>).</summary>
    public const string MisleadingOther = "tools.ozone.report.defs#reasonMisleadingOther";

    // ─── Rule violations ───

    /// <summary>Hacking or system attacks (<c>tools.ozone.report.defs#reasonRuleSiteSecurity</c>).</summary>
    public const string RuleSiteSecurity = "tools.ozone.report.defs#reasonRuleSiteSecurity";

    /// <summary>
    /// Promoting or selling prohibited items or services
    /// (<c>tools.ozone.report.defs#reasonRuleProhibitedSales</c>).
    /// </summary>
    public const string RuleProhibitedSales = "tools.ozone.report.defs#reasonRuleProhibitedSales";

    /// <summary>A banned user returning (<c>tools.ozone.report.defs#reasonRuleBanEvasion</c>).</summary>
    public const string RuleBanEvasion = "tools.ozone.report.defs#reasonRuleBanEvasion";

    /// <summary>Other rule violations (<c>tools.ozone.report.defs#reasonRuleOther</c>).</summary>
    public const string RuleOther = "tools.ozone.report.defs#reasonRuleOther";

    // ─── Self-harm ───

    /// <summary>
    /// Content promoting or depicting self-harm (<c>tools.ozone.report.defs#reasonSelfHarmContent</c>).
    /// </summary>
    public const string SelfHarmContent = "tools.ozone.report.defs#reasonSelfHarmContent";

    /// <summary>Eating disorders (<c>tools.ozone.report.defs#reasonSelfHarmED</c>).</summary>
    public const string SelfHarmED = "tools.ozone.report.defs#reasonSelfHarmED";

    /// <summary>
    /// Dangerous challenges or activities (<c>tools.ozone.report.defs#reasonSelfHarmStunts</c>).
    /// </summary>
    public const string SelfHarmStunts = "tools.ozone.report.defs#reasonSelfHarmStunts";

    /// <summary>
    /// Dangerous substances or drug abuse (<c>tools.ozone.report.defs#reasonSelfHarmSubstances</c>).
    /// </summary>
    public const string SelfHarmSubstances = "tools.ozone.report.defs#reasonSelfHarmSubstances";

    /// <summary>Other dangerous content (<c>tools.ozone.report.defs#reasonSelfHarmOther</c>).</summary>
    public const string SelfHarmOther = "tools.ozone.report.defs#reasonSelfHarmOther";
}
