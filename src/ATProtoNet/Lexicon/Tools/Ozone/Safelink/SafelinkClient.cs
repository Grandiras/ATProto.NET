using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Tools.Ozone.Safelink;

/// <summary>
/// Client for tools.ozone.safelink.* endpoints: URL safety rules that block, warn on or allow
/// links, and their audit log.
/// </summary>
public sealed class SafelinkClient
{
    private readonly XrpcClient _xrpc;

    internal SafelinkClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Add a URL safety rule.
    /// </summary>
    /// <param name="url">The URL or domain.</param>
    /// <param name="pattern">Whether <paramref name="url"/> is a domain or a URL (see <see cref="SafelinkPatternType"/>).</param>
    /// <param name="action">What to do with matching links (see <see cref="SafelinkActionType"/>).</param>
    /// <param name="reason">Why (see <see cref="SafelinkReasonType"/>).</param>
    /// <param name="comment">A comment about the decision.</param>
    /// <param name="createdBy">The moderator to credit; honored only with admin auth.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The audit event the change recorded.</returns>
    /// <exception cref="XrpcException"><c>InvalidUrl</c> or <c>RuleAlreadyExists</c>.</exception>
    public Task<SafelinkEvent> AddRuleAsync(
        string url,
        string pattern,
        string action,
        string reason,
        string? comment = null,
        Did? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var request = new AddRuleRequest
        {
            Url = url,
            Pattern = pattern,
            Action = action,
            Reason = reason,
            Comment = comment,
            CreatedBy = createdBy,
        };
        return _xrpc.ProcedureAsync<SafelinkEvent>(
            "tools.ozone.safelink.addRule", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Change a URL safety rule's action or reason.
    /// </summary>
    /// <param name="url">The rule's URL or domain.</param>
    /// <param name="pattern">The rule's pattern type (see <see cref="SafelinkPatternType"/>).</param>
    /// <param name="action">What to do with matching links (see <see cref="SafelinkActionType"/>).</param>
    /// <param name="reason">Why (see <see cref="SafelinkReasonType"/>).</param>
    /// <param name="comment">A comment about the change.</param>
    /// <param name="createdBy">The moderator to credit; honored only with admin auth.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The audit event the change recorded.</returns>
    /// <exception cref="XrpcException"><c>RuleNotFound</c>.</exception>
    public Task<SafelinkEvent> UpdateRuleAsync(
        string url,
        string pattern,
        string action,
        string reason,
        string? comment = null,
        Did? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var request = new AddRuleRequest
        {
            Url = url,
            Pattern = pattern,
            Action = action,
            Reason = reason,
            Comment = comment,
            CreatedBy = createdBy,
        };
        return _xrpc.ProcedureAsync<SafelinkEvent>(
            "tools.ozone.safelink.updateRule", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Remove a URL safety rule.
    /// </summary>
    /// <param name="url">The rule's URL or domain.</param>
    /// <param name="pattern">The rule's pattern type (see <see cref="SafelinkPatternType"/>).</param>
    /// <param name="comment">Why the rule is removed.</param>
    /// <param name="createdBy">The moderator to credit; honored only with admin auth.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The audit event the change recorded.</returns>
    /// <exception cref="XrpcException"><c>RuleNotFound</c>.</exception>
    public Task<SafelinkEvent> RemoveRuleAsync(
        string url,
        string pattern,
        string? comment = null,
        Did? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        var request = new RemoveRuleRequest
        {
            Url = url,
            Pattern = pattern,
            Comment = comment,
            CreatedBy = createdBy,
        };
        return _xrpc.ProcedureAsync<SafelinkEvent>(
            "tools.ozone.safelink.removeRule", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Query one page of the URL safety rules.
    /// </summary>
    /// <param name="urls">Only rules on these URLs or domains.</param>
    /// <param name="patternType">Only rules of this pattern type (see <see cref="SafelinkPatternType"/>).</param>
    /// <param name="actions">Only rules with these actions (see <see cref="SafelinkActionType"/>).</param>
    /// <param name="reason">Only rules with this reason (see <see cref="SafelinkReasonType"/>).</param>
    /// <param name="createdBy">Only rules added by this moderator.</param>
    /// <param name="sortDirection">The sort direction: <c>asc</c> or <c>desc</c> (the default).</param>
    /// <param name="limit">Maximum number of rules (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<QueryRulesResponse> QueryRulesAsync(
        IEnumerable<string>? urls = null,
        string? patternType = null,
        IEnumerable<string>? actions = null,
        string? reason = null,
        Did? createdBy = null,
        string? sortDirection = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var request = new QueryRulesRequest
        {
            Urls = urls is null ? null : [.. urls],
            PatternType = patternType,
            Actions = actions is null ? null : [.. actions],
            Reason = reason,
            CreatedBy = createdBy,
            SortDirection = sortDirection,
            Limit = limit,
            Cursor = cursor,
        };
        return _xrpc.ProcedureAsync<QueryRulesResponse>(
            "tools.ozone.safelink.queryRules", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every URL safety rule matching the filters, fetching pages as needed.
    /// </summary>
    /// <param name="urls">Only rules on these URLs or domains.</param>
    /// <param name="patternType">Only rules of this pattern type (see <see cref="SafelinkPatternType"/>).</param>
    /// <param name="actions">Only rules with these actions (see <see cref="SafelinkActionType"/>).</param>
    /// <param name="reason">Only rules with this reason (see <see cref="SafelinkReasonType"/>).</param>
    /// <param name="createdBy">Only rules added by this moderator.</param>
    /// <param name="sortDirection">The sort direction: <c>asc</c> or <c>desc</c> (the default).</param>
    /// <param name="pageSize">Rules per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<UrlRule> EnumerateRulesAsync(
        IEnumerable<string>? urls = null,
        string? patternType = null,
        IEnumerable<string>? actions = null,
        string? reason = null,
        Did? createdBy = null,
        string? sortDirection = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<QueryRulesResponse, UrlRule>(
            (cursor, ct) => QueryRulesAsync(urls, patternType, actions, reason, createdBy, sortDirection, pageSize, cursor, ct),
            cancellationToken);

    /// <summary>
    /// Query one page of the URL safety audit log.
    /// </summary>
    /// <param name="urls">Only events on these URLs or domains.</param>
    /// <param name="patternType">Only events on rules of this pattern type (see <see cref="SafelinkPatternType"/>).</param>
    /// <param name="sortDirection">The sort direction: <c>asc</c> or <c>desc</c> (the default).</param>
    /// <param name="limit">Maximum number of events (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<QueryEventsResponse> QueryEventsAsync(
        IEnumerable<string>? urls = null,
        string? patternType = null,
        string? sortDirection = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var request = new QueryEventsRequest
        {
            Urls = urls is null ? null : [.. urls],
            PatternType = patternType,
            SortDirection = sortDirection,
            Limit = limit,
            Cursor = cursor,
        };
        return _xrpc.ProcedureAsync<QueryEventsResponse>(
            "tools.ozone.safelink.queryEvents", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every URL safety audit event matching the filters, fetching pages as needed.
    /// </summary>
    /// <param name="urls">Only events on these URLs or domains.</param>
    /// <param name="patternType">Only events on rules of this pattern type (see <see cref="SafelinkPatternType"/>).</param>
    /// <param name="sortDirection">The sort direction: <c>asc</c> or <c>desc</c> (the default).</param>
    /// <param name="pageSize">Events per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<SafelinkEvent> EnumerateEventsAsync(
        IEnumerable<string>? urls = null,
        string? patternType = null,
        string? sortDirection = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<QueryEventsResponse, SafelinkEvent>(
            (cursor, ct) => QueryEventsAsync(urls, patternType, sortDirection, pageSize, cursor, ct),
            cancellationToken);
}
