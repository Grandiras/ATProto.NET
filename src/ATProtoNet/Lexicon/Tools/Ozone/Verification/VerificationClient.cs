using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Tools.Ozone.Verification;

/// <summary>
/// Client for tools.ozone.verification.* endpoints: the verifications (<c>app.bsky.graph.verification</c>
/// records) the Ozone service issues as a trusted verifier.
/// </summary>
public sealed class VerificationClient
{
    private readonly XrpcClient _xrpc;

    internal VerificationClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Verify several accounts at once.
    /// </summary>
    /// <param name="verifications">The accounts to verify (at most 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verifications created, and the accounts that failed.</returns>
    public Task<GrantVerificationsResponse> GrantVerificationsAsync(
        IEnumerable<VerificationInput> verifications,
        CancellationToken cancellationToken = default)
    {
        var request = new GrantVerificationsRequest { Verifications = [.. verifications] };
        return _xrpc.ProcedureAsync<GrantVerificationsResponse>(
            "tools.ozone.verification.grantVerifications", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Revoke several verifications at once.
    /// </summary>
    /// <param name="uris">The verification records to revoke (at most 100).</param>
    /// <param name="revokeReason">Why they are revoked (at most 1,000 characters).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verifications revoked, and those that failed.</returns>
    public Task<RevokeVerificationsResponse> RevokeVerificationsAsync(
        IEnumerable<AtUri> uris,
        string? revokeReason = null,
        CancellationToken cancellationToken = default)
    {
        var request = new RevokeVerificationsRequest { Uris = [.. uris], RevokeReason = revokeReason };
        return _xrpc.ProcedureAsync<RevokeVerificationsResponse>(
            "tools.ozone.verification.revokeVerifications", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List one page of verifications.
    /// </summary>
    /// <param name="subjects">Only verifications of these accounts (at most 100).</param>
    /// <param name="issuers">Only verifications from these issuers (at most 100).</param>
    /// <param name="createdAfter">Only verifications created after this time.</param>
    /// <param name="createdBefore">Only verifications created before this time.</param>
    /// <param name="isRevoked">Only revoked (or only unrevoked) verifications; <see langword="null"/> for both.</param>
    /// <param name="sortDirection">The sort direction by creation time: <c>asc</c> or <c>desc</c> (the default).</param>
    /// <param name="limit">Maximum number of verifications (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ListVerificationsResponse> ListVerificationsAsync(
        IEnumerable<Did>? subjects = null,
        IEnumerable<Did>? issuers = null,
        AtDatetime? createdAfter = null,
        AtDatetime? createdBefore = null,
        bool? isRevoked = null,
        string? sortDirection = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("cursor", cursor)
            .Add("limit", limit)
            .Add("createdAfter", createdAfter?.ToString())
            .Add("createdBefore", createdBefore?.ToString())
            .AddAll("issuers", issuers?.Select(did => did.Value))
            .AddAll("subjects", subjects?.Select(did => did.Value))
            .Add("sortDirection", sortDirection)
            .Add("isRevoked", isRevoked);
        return _xrpc.QueryAsync<ListVerificationsResponse>(
            "tools.ozone.verification.listVerifications", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every verification matching the filters, fetching pages as needed.
    /// </summary>
    /// <param name="subjects">Only verifications of these accounts (at most 100).</param>
    /// <param name="issuers">Only verifications from these issuers (at most 100).</param>
    /// <param name="createdAfter">Only verifications created after this time.</param>
    /// <param name="createdBefore">Only verifications created before this time.</param>
    /// <param name="isRevoked">Only revoked (or only unrevoked) verifications; <see langword="null"/> for both.</param>
    /// <param name="sortDirection">The sort direction by creation time: <c>asc</c> or <c>desc</c> (the default).</param>
    /// <param name="pageSize">Verifications per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<VerificationView> EnumerateVerificationsAsync(
        IEnumerable<Did>? subjects = null,
        IEnumerable<Did>? issuers = null,
        AtDatetime? createdAfter = null,
        AtDatetime? createdBefore = null,
        bool? isRevoked = null,
        string? sortDirection = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<ListVerificationsResponse, VerificationView>(
            (cursor, ct) => ListVerificationsAsync(
                subjects, issuers, createdAfter, createdBefore, isRevoked, sortDirection, pageSize, cursor, ct),
            cancellationToken);
}
