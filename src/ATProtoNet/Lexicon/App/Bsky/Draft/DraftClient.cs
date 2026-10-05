using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.App.Bsky.Draft;

/// <summary>Client for app.bsky.draft.* XRPC endpoints: the authenticated account's post drafts.</summary>
/// <remarks>
/// Drafts are not repository records: the appview keeps them in private storage, visible only to
/// their owner, and may cap how many an account holds.
/// </remarks>
public sealed class DraftClient
{
    private readonly XrpcClient _xrpc;

    internal DraftClient(XrpcClient xrpc) => _xrpc = xrpc;

    /// <summary>Store a new draft.</summary>
    /// <returns>The new draft's identifier.</returns>
    /// <exception cref="XrpcException">
    /// <see cref="DraftErrors.DraftLimitReached"/> when the account has as many drafts as it may.
    /// </exception>
    public async Task<Tid> CreateDraftAsync(Draft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var response = await _xrpc.ProcedureAsync<CreateDraftResponse>(
            "app.bsky.draft.createDraft",
            new CreateDraftRequest(Draft: draft),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Id;
    }

    /// <summary>Replace a stored draft. An identifier that names no draft is silently ignored.</summary>
    /// <param name="id">The draft's identifier.</param>
    /// <param name="draft">The draft's new content.</param>
    public Task UpdateDraftAsync(Tid id, Draft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return _xrpc.ProcedureAsync(
            "app.bsky.draft.updateDraft",
            new UpdateDraftRequest(Draft: new DraftWithId { Id = id, Draft = draft }),
            cancellationToken: cancellationToken);
    }

    /// <summary>Delete a draft.</summary>
    /// <param name="id">The draft's identifier.</param>
    public Task DeleteDraftAsync(Tid id, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "app.bsky.draft.deleteDraft", new DeleteDraftRequest(Id: id), cancellationToken: cancellationToken);

    /// <summary>Get one page of the authenticated account's drafts.</summary>
    /// <param name="limit">Max drafts per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetDraftsResponse> GetDraftsAsync(
        int? limit = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetDraftsResponse>(
            "app.bsky.draft.getDrafts",
            new XrpcParams().Add("limit", limit).Add("cursor", cursor), cancellationToken: cancellationToken);
}
