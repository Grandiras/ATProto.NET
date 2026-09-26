using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.App.Bsky.Draft;

/// <summary>
/// Client for app.bsky.draft.* XRPC endpoints: the authenticated account's post drafts.
/// </summary>
/// <remarks>
/// Drafts are not repository records: the appview keeps them in private storage, visible only to
/// their owner, and may cap how many an account holds.
/// </remarks>
public sealed class DraftClient
{
    private readonly XrpcClient _xrpc;

    internal DraftClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Store a new draft.
    /// </summary>
    /// <param name="draft">The draft.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new draft's identifier.</returns>
    /// <exception cref="XrpcException">
    /// <see cref="DraftErrors.DraftLimitReached"/> when the account has as many drafts as it may.
    /// </exception>
    public async Task<Tid> CreateDraftAsync(Draft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var request = new CreateDraftRequest { Draft = draft };
        var response = await _xrpc.ProcedureAsync<CreateDraftResponse>(
            "app.bsky.draft.createDraft", request, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Id;
    }

    /// <summary>
    /// Replace a stored draft. An identifier that names no draft is silently ignored.
    /// </summary>
    /// <param name="id">The draft's identifier.</param>
    /// <param name="draft">The draft's new content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task UpdateDraftAsync(Tid id, Draft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var request = new UpdateDraftRequest { Draft = new DraftWithId { Id = id, Draft = draft } };
        return _xrpc.ProcedureAsync(
            "app.bsky.draft.updateDraft", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete a draft.
    /// </summary>
    /// <param name="id">The draft's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task DeleteDraftAsync(Tid id, CancellationToken cancellationToken = default)
    {
        var request = new DeleteDraftRequest { Id = id };
        return _xrpc.ProcedureAsync(
            "app.bsky.draft.deleteDraft", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get one page of the authenticated account's drafts.
    /// </summary>
    /// <param name="limit">Max drafts per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetDraftsResponse> GetDraftsAsync(
        int? limit = null, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetDraftsResponse>(
            "app.bsky.draft.getDrafts", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate the authenticated account's drafts, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Drafts per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<DraftView> EnumerateDraftsAsync(
        int? pageSize = null, CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetDraftsResponse, DraftView>(
            (cursor, ct) => GetDraftsAsync(pageSize, cursor, ct),
            cancellationToken);
}
