using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Server;

namespace ATProtoNet.Lexicon.Com.AtProto.Admin;

/// <summary>
/// Client for com.atproto.admin.* XRPC endpoints.
/// Requires admin/moderator authentication.
/// </summary>
public sealed class AdminClient
{
    private readonly XrpcClient _xrpc;

    internal AdminClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Get detailed info about an account by DID.
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<AccountInfo> GetAccountInfoAsync(
        Did did, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("did", did);
        return _xrpc.QueryAsync<AccountInfo>(
            "com.atproto.admin.getAccountInfo", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get info about multiple accounts by DIDs.
    /// </summary>
    /// <param name="dids">The account DIDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetAccountInfosResponse> GetAccountInfosAsync(
        IEnumerable<Did> dids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dids);
        var parameters = new XrpcParams().AddAll("dids", dids.Select(did => did.Value));
        return _xrpc.QueryAsync<GetAccountInfosResponse>(
            "com.atproto.admin.getAccountInfos", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the status of a subject (account, record, or blob).
    /// </summary>
    /// <param name="did">The account DID, for an account subject (or a blob's owner).</param>
    /// <param name="uri">The record's AT URI, for a record subject.</param>
    /// <param name="blob">The blob's CID, for a blob subject.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetSubjectStatusResponse> GetSubjectStatusAsync(
        Did? did = null, AtUri? uri = null, Cid? blob = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("did", did)
            .Add("uri", uri)
            .Add("blob", blob);

        return _xrpc.QueryAsync<GetSubjectStatusResponse>(
            "com.atproto.admin.getSubjectStatus", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update the status (takedown, etc.) of a subject.
    /// </summary>
    public Task<UpdateSubjectStatusResponse> UpdateSubjectStatusAsync(
        UpdateSubjectStatusRequest request, CancellationToken cancellationToken = default)
    {
        return _xrpc.ProcedureAsync<UpdateSubjectStatusResponse>(
            "com.atproto.admin.updateSubjectStatus", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Send an email to an account.
    /// </summary>
    public Task<SendEmailResponse> SendEmailAsync(
        SendEmailRequest request, CancellationToken cancellationToken = default)
    {
        return _xrpc.ProcedureAsync<SendEmailResponse>(
            "com.atproto.admin.sendEmail", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete an account (admin action).
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteAccountAsync(
        Did did, CancellationToken cancellationToken = default)
    {
        var request = new AdminDeleteAccountRequest { Did = did };
        await _xrpc.ProcedureAsync(
            "com.atproto.admin.deleteAccount", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Disable invite code creation for an account.
    /// </summary>
    /// <param name="account">The account DID.</param>
    /// <param name="note">An optional note recorded with the action.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DisableAccountInvitesAsync(
        Did account, string? note = null,
        CancellationToken cancellationToken = default)
    {
        var request = new DisableAccountInvitesRequest { Account = account, Note = note };
        await _xrpc.ProcedureAsync(
            "com.atproto.admin.disableAccountInvites", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enable invite code creation for an account.
    /// </summary>
    /// <param name="account">The account DID.</param>
    /// <param name="note">An optional note recorded with the action.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task EnableAccountInvitesAsync(
        Did account, string? note = null,
        CancellationToken cancellationToken = default)
    {
        var request = new EnableAccountInvitesRequest { Account = account, Note = note };
        await _xrpc.ProcedureAsync(
            "com.atproto.admin.enableAccountInvites", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update an account's email (admin action).
    /// </summary>
    /// <param name="account">The account DID or handle.</param>
    /// <param name="email">The new email address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UpdateAccountEmailAsync(
        AtIdentifier account, string email,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateAccountEmailRequest { Account = account, Email = email };
        await _xrpc.ProcedureAsync(
            "com.atproto.admin.updateAccountEmail", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update an account's handle (admin action).
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="handle">The new handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UpdateAccountHandleAsync(
        Did did, Handle handle,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateAccountHandleRequest { Did = did, Handle = handle };
        await _xrpc.ProcedureAsync(
            "com.atproto.admin.updateAccountHandle", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update an account's password (admin action).
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="password">The new password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UpdateAccountPasswordAsync(
        Did did, string password,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateAccountPasswordRequest { Did = did, Password = password };
        await _xrpc.ProcedureAsync(
            "com.atproto.admin.updateAccountPassword", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Disable invite codes.
    /// </summary>
    /// <param name="codes">The codes to disable.</param>
    /// <param name="accounts">The accounts whose codes to disable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DisableInviteCodesAsync(
        IEnumerable<string>? codes = null, IEnumerable<string>? accounts = null,
        CancellationToken cancellationToken = default)
    {
        var request = new DisableInviteCodesRequest { Codes = codes?.ToList(), Accounts = accounts?.ToList() };
        await _xrpc.ProcedureAsync(
            "com.atproto.admin.disableInviteCodes", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get one page of the server's invite codes.
    /// </summary>
    /// <param name="sort">The order: <c>recent</c> (the default) or <c>usage</c>.</param>
    /// <param name="limit">Maximum number of results (1-500, default 100).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetInviteCodesResponse> GetInviteCodesAsync(
        string? sort = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("sort", sort)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetInviteCodesResponse>(
            "com.atproto.admin.getInviteCodes", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerate every invite code on the server, fetching pages as needed.
    /// </summary>
    /// <param name="sort">The order: <c>recent</c> (the default) or <c>usage</c>.</param>
    /// <param name="pageSize">Codes per request (1-500); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<InviteCode> EnumerateInviteCodesAsync(
        string? sort = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<GetInviteCodesResponse, InviteCode>(
            (cursor, ct) => GetInviteCodesAsync(sort, pageSize, cursor, ct),
            cancellationToken);
}
