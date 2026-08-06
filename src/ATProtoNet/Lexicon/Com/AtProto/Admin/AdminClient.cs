using System.Text.Json;
using ATProtoNet.Http;

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
    public Task<AccountInfo> GetAccountInfoAsync(
        string did, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("did", did);
        return _xrpc.QueryAsync<AccountInfo>(
            "com.atproto.admin.getAccountInfo", parameters, cancellationToken);
    }

    /// <summary>
    /// Get info about multiple accounts by DIDs.
    /// </summary>
    public Task<GetAccountInfosResponse> GetAccountInfosAsync(
        IEnumerable<string> dids, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().AddAll("dids", dids);
        return _xrpc.QueryAsync<GetAccountInfosResponse>(
            "com.atproto.admin.getAccountInfos", parameters, cancellationToken);
    }

    /// <summary>
    /// Get the status of a subject (account, record, or blob).
    /// </summary>
    public Task<GetSubjectStatusResponse> GetSubjectStatusAsync(
        string? did = null, string? uri = null, string? blob = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("did", did)
            .Add("uri", uri)
            .Add("blob", blob);

        return _xrpc.QueryAsync<GetSubjectStatusResponse>(
            "com.atproto.admin.getSubjectStatus", parameters, cancellationToken);
    }

    /// <summary>
    /// Update the status (takedown, etc.) of a subject.
    /// </summary>
    public Task<UpdateSubjectStatusResponse> UpdateSubjectStatusAsync(
        UpdateSubjectStatusRequest request, CancellationToken cancellationToken = default)
    {
        return _xrpc.ProcedureAsync<UpdateSubjectStatusRequest, UpdateSubjectStatusResponse>(
            "com.atproto.admin.updateSubjectStatus", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Send an email to an account.
    /// </summary>
    public Task<SendEmailResponse> SendEmailAsync(
        SendEmailRequest request, CancellationToken cancellationToken = default)
    {
        return _xrpc.ProcedureAsync<SendEmailRequest, SendEmailResponse>(
            "com.atproto.admin.sendEmail", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete an account (admin action).
    /// </summary>
    public async Task DeleteAccountAsync(
        string did, CancellationToken cancellationToken = default)
    {
        var request = new AdminDeleteAccountRequest { Did = did };
        await _xrpc.ProcedureAsync<AdminDeleteAccountRequest>(
            "com.atproto.admin.deleteAccount", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Disable invite code creation for an account.
    /// </summary>
    public async Task DisableAccountInvitesAsync(
        string account, string? note = null,
        CancellationToken cancellationToken = default)
    {
        var request = new DisableAccountInvitesRequest { Account = account, Note = note };
        await _xrpc.ProcedureAsync<DisableAccountInvitesRequest>(
            "com.atproto.admin.disableAccountInvites", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enable invite code creation for an account.
    /// </summary>
    public async Task EnableAccountInvitesAsync(
        string account, string? note = null,
        CancellationToken cancellationToken = default)
    {
        var request = new EnableAccountInvitesRequest { Account = account, Note = note };
        await _xrpc.ProcedureAsync<EnableAccountInvitesRequest>(
            "com.atproto.admin.enableAccountInvites", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update an account's email (admin action).
    /// </summary>
    public async Task UpdateAccountEmailAsync(
        string account, string email,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateAccountEmailRequest { Account = account, Email = email };
        await _xrpc.ProcedureAsync<UpdateAccountEmailRequest>(
            "com.atproto.admin.updateAccountEmail", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update an account's handle (admin action).
    /// </summary>
    public async Task UpdateAccountHandleAsync(
        string did, string handle,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateAccountHandleRequest { Did = did, Handle = handle };
        await _xrpc.ProcedureAsync<UpdateAccountHandleRequest>(
            "com.atproto.admin.updateAccountHandle", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update an account's password (admin action).
    /// </summary>
    public async Task UpdateAccountPasswordAsync(
        string did, string password,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateAccountPasswordRequest { Did = did, Password = password };
        await _xrpc.ProcedureAsync<UpdateAccountPasswordRequest>(
            "com.atproto.admin.updateAccountPassword", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Disable invite codes.
    /// </summary>
    public async Task DisableInviteCodesAsync(
        List<string>? codes = null, List<string>? accounts = null,
        CancellationToken cancellationToken = default)
    {
        var request = new DisableInviteCodesRequest { Codes = codes, Accounts = accounts };
        await _xrpc.ProcedureAsync<DisableInviteCodesRequest>(
            "com.atproto.admin.disableInviteCodes", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get invite codes.
    /// </summary>
    public Task<GetInviteCodesResponse> GetInviteCodesAsync(
        string? sort = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("sort", sort)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetInviteCodesResponse>(
            "com.atproto.admin.getInviteCodes", parameters, cancellationToken);
    }
}
