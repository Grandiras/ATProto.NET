using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Tools.Ozone.Safelink;

/// <summary>
/// A URL safety rule: what the app does with links to a URL or domain
/// (<c>tools.ozone.safelink.defs#urlRule</c>).
/// </summary>
public sealed class UrlRule : LexObject
{
    /// <summary>The URL or domain the rule applies to.</summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>Whether <see cref="Url"/> is a domain or a URL (see <see cref="SafelinkPatternType"/>).</summary>
    [JsonPropertyName("pattern")]
    public required string Pattern { get; init; }

    /// <summary>What to do with matching links (see <see cref="SafelinkActionType"/>).</summary>
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    /// <summary>Why (see <see cref="SafelinkReasonType"/>).</summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary>A comment about the decision.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>The DID of the moderator who added the rule.</summary>
    [JsonPropertyName("createdBy")]
    public required Did CreatedBy { get; init; }

    /// <summary>When the rule was added.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>When the rule last changed.</summary>
    [JsonPropertyName("updatedAt")]
    public required AtDatetime UpdatedAt { get; init; }
}

/// <summary>
/// One change to the URL safety rules, from their audit log (<c>tools.ozone.safelink.defs#event</c>).
/// </summary>
public sealed class SafelinkEvent : LexObject
{
    /// <summary>The event's identifier.</summary>
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    /// <summary>What changed (see <see cref="SafelinkEventType"/>).</summary>
    [JsonPropertyName("eventType")]
    public required string EventType { get; init; }

    /// <summary>The URL or domain the rule applies to.</summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>Whether <see cref="Url"/> is a domain or a URL (see <see cref="SafelinkPatternType"/>).</summary>
    [JsonPropertyName("pattern")]
    public required string Pattern { get; init; }

    /// <summary>What to do with matching links (see <see cref="SafelinkActionType"/>).</summary>
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    /// <summary>Why (see <see cref="SafelinkReasonType"/>).</summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary>The DID of the moderator who made the change.</summary>
    [JsonPropertyName("createdBy")]
    public required Did CreatedBy { get; init; }

    /// <summary>When the change was made.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>A comment about the decision.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

/// <summary>
/// What a URL safety rule matches (<c>tools.ozone.safelink.defs#patternType</c>).
/// </summary>
public static class SafelinkPatternType
{
    /// <summary>Every URL on the domain.</summary>
    public const string Domain = "domain";

    /// <summary>The exact URL.</summary>
    public const string Url = "url";
}

/// <summary>
/// What the app does with links a URL safety rule matches
/// (<c>tools.ozone.safelink.defs#actionType</c>).
/// </summary>
public static class SafelinkActionType
{
    /// <summary>Block the link.</summary>
    public const string Block = "block";

    /// <summary>Warn before following the link.</summary>
    public const string Warn = "warn";

    /// <summary>Allow the link, overriding broader rules.</summary>
    public const string Whitelist = "whitelist";
}

/// <summary>
/// Why a URL safety rule exists (<c>tools.ozone.safelink.defs#reasonType</c>).
/// </summary>
public static class SafelinkReasonType
{
    /// <summary>Child sexual abuse material.</summary>
    public const string Csam = "csam";

    /// <summary>Spam.</summary>
    public const string Spam = "spam";

    /// <summary>Phishing.</summary>
    public const string Phishing = "phishing";

    /// <summary>No particular reason.</summary>
    public const string None = "none";
}

/// <summary>
/// The kinds of change in the URL safety audit log (<c>tools.ozone.safelink.defs#eventType</c>).
/// </summary>
public static class SafelinkEventType
{
    /// <summary>A rule was added.</summary>
    public const string AddRule = "addRule";

    /// <summary>A rule was changed.</summary>
    public const string UpdateRule = "updateRule";

    /// <summary>A rule was removed.</summary>
    public const string RemoveRule = "removeRule";
}

// ─── Request / Response Models ───

/// <summary>
/// Request body for tools.ozone.safelink.addRule and tools.ozone.safelink.updateRule, which take
/// the same fields.
/// </summary>
internal sealed class AddRuleRequest
{
    /// <summary>The URL or domain.</summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>Whether it is a domain or a URL.</summary>
    [JsonPropertyName("pattern")]
    public required string Pattern { get; init; }

    /// <summary>What to do with matching links.</summary>
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    /// <summary>Why.</summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    /// <summary>A comment about the decision.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>The moderator to credit; honored only with admin auth.</summary>
    [JsonPropertyName("createdBy")]
    public Did? CreatedBy { get; init; }
}

/// <summary>
/// Request body for tools.ozone.safelink.removeRule.
/// </summary>
internal sealed class RemoveRuleRequest
{
    /// <summary>The URL or domain.</summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>Whether it is a domain or a URL.</summary>
    [JsonPropertyName("pattern")]
    public required string Pattern { get; init; }

    /// <summary>Why the rule is removed.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>The moderator to credit; honored only with admin auth.</summary>
    [JsonPropertyName("createdBy")]
    public Did? CreatedBy { get; init; }
}

/// <summary>
/// Request body for tools.ozone.safelink.queryEvents.
/// </summary>
internal sealed class QueryEventsRequest
{
    /// <summary>Pagination cursor from a previous response.</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>Maximum number of events.</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    /// <summary>Only events on these URLs or domains.</summary>
    [JsonPropertyName("urls")]
    public IReadOnlyList<string>? Urls { get; init; }

    /// <summary>Only events on rules of this pattern type.</summary>
    [JsonPropertyName("patternType")]
    public string? PatternType { get; init; }

    /// <summary>The sort direction.</summary>
    [JsonPropertyName("sortDirection")]
    public string? SortDirection { get; init; }
}

/// <summary>
/// Response from tools.ozone.safelink.queryEvents.
/// </summary>
public sealed class QueryEventsResponse : ICursorPage<SafelinkEvent>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The events.</summary>
    [JsonPropertyName("events")]
    public required IReadOnlyList<SafelinkEvent> Events { get; init; }

    IReadOnlyList<SafelinkEvent> ICursorPage<SafelinkEvent>.Items => Events;
}

/// <summary>
/// Request body for tools.ozone.safelink.queryRules.
/// </summary>
internal sealed class QueryRulesRequest
{
    /// <summary>Pagination cursor from a previous response.</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>Maximum number of rules.</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    /// <summary>Only rules on these URLs or domains.</summary>
    [JsonPropertyName("urls")]
    public IReadOnlyList<string>? Urls { get; init; }

    /// <summary>Only rules of this pattern type.</summary>
    [JsonPropertyName("patternType")]
    public string? PatternType { get; init; }

    /// <summary>Only rules with these actions.</summary>
    [JsonPropertyName("actions")]
    public IReadOnlyList<string>? Actions { get; init; }

    /// <summary>Only rules with this reason.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>Only rules added by this moderator.</summary>
    [JsonPropertyName("createdBy")]
    public Did? CreatedBy { get; init; }

    /// <summary>The sort direction.</summary>
    [JsonPropertyName("sortDirection")]
    public string? SortDirection { get; init; }
}

/// <summary>
/// Response from tools.ozone.safelink.queryRules.
/// </summary>
public sealed class QueryRulesResponse : ICursorPage<UrlRule>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The rules.</summary>
    [JsonPropertyName("rules")]
    public required IReadOnlyList<UrlRule> Rules { get; init; }

    IReadOnlyList<UrlRule> ICursorPage<UrlRule>.Items => Rules;
}
