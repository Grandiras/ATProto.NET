using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Server;

namespace ATProtoNet.Lexicon.Com.AtProto.Admin;

/// <summary>Client for com.atproto.admin.* XRPC endpoints. Requires admin/moderator authentication.</summary>
public sealed class AdminClient
{
    private readonly XrpcClient _xrpc;

    internal AdminClient(XrpcClient xrpc) => _xrpc = xrpc;

    /// <summary>Get detailed info about an account by DID.</summary>
    /// <param name="did">The account DID.</param>
    public Task<AccountInfo> GetAccountInfoAsync(
        Did did, CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<AccountInfo>(
            "com.atproto.admin.getAccountInfo", new XrpcParams().Add("did", did), cancellationToken: cancellationToken);

    /// <summary>Get info about multiple accounts by DIDs.</summary>
    /// <param name="dids">The account DIDs.</param>
    public Task<GetAccountInfosResponse> GetAccountInfosAsync(
        IEnumerable<Did> dids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dids);
        return _xrpc.QueryAsync<GetAccountInfosResponse>(
            "com.atproto.admin.getAccountInfos",
            new XrpcParams()
                .AddAll("dids", dids.Select(did => did.Value)),
            cancellationToken: cancellationToken);
    }

    /// <summary>Search one page of the server's accounts, optionally by email address.</summary>
    /// <remarks>
    /// Served by the Bluesky entryway (and through Ozone) and by some PDS implementations, such as
    /// Tranquil; the reference Bluesky PDS does not implement it.
    /// </remarks>
    /// <param name="email">The email address to match.</param>
    /// <param name="limit">Maximum number of results (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<SearchAccountsResponse> SearchAccountsAsync(
        string? email = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<SearchAccountsResponse>(
            "com.atproto.admin.searchAccounts",
            new XrpcParams()
                .Add("email", email)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);

    /// <summary>Get the status of a subject (account, record, or blob).</summary>
    /// <param name="did">The account DID, for an account subject (or a blob's owner).</param>
    /// <param name="uri">The record's AT URI, for a record subject.</param>
    /// <param name="blob">The blob's CID, for a blob subject.</param>
    public Task<GetSubjectStatusResponse> GetSubjectStatusAsync(
        Did? did = null, AtUri? uri = null, Cid? blob = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetSubjectStatusResponse>(
            "com.atproto.admin.getSubjectStatus",
            new XrpcParams().Add("did", did).Add("uri", uri).Add("blob", blob), cancellationToken: cancellationToken);

    /// <summary>Update the status (takedown, etc.) of a subject.</summary>
    public Task<UpdateSubjectStatusResponse> UpdateSubjectStatusAsync(
        UpdateSubjectStatusRequest request, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<UpdateSubjectStatusResponse>(
            "com.atproto.admin.updateSubjectStatus", request, cancellationToken: cancellationToken);

    /// <summary>Send an email to an account.</summary>
    public Task<SendEmailResponse> SendEmailAsync(
        SendEmailRequest request, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<SendEmailResponse>(
            "com.atproto.admin.sendEmail", request, cancellationToken: cancellationToken);

    /// <summary>Delete an account (admin action).</summary>
    /// <param name="did">The account DID.</param>
    public Task DeleteAccountAsync(
        Did did, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "com.atproto.admin.deleteAccount",
            new AdminDeleteAccountRequest(Did: did), cancellationToken: cancellationToken);

    /// <summary>Disable invite code creation for an account.</summary>
    /// <param name="account">The account DID.</param>
    /// <param name="note">An optional note recorded with the action.</param>
    public Task DisableAccountInvitesAsync(
        Did account, string? note = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "com.atproto.admin.disableAccountInvites",
            new AccountInvitesRequest(account, note), cancellationToken: cancellationToken);

    /// <summary>Enable invite code creation for an account.</summary>
    /// <param name="account">The account DID.</param>
    /// <param name="note">An optional note recorded with the action.</param>
    public Task EnableAccountInvitesAsync(
        Did account, string? note = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "com.atproto.admin.enableAccountInvites",
            new AccountInvitesRequest(account, note), cancellationToken: cancellationToken);

    /// <summary>Update an account's email (admin action).</summary>
    /// <param name="account">The account DID or handle.</param>
    /// <param name="email">The new email address.</param>
    public Task UpdateAccountEmailAsync(
        AtIdentifier account, string email,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "com.atproto.admin.updateAccountEmail",
            new UpdateAccountEmailRequest(Account: account, Email: email), cancellationToken: cancellationToken);

    /// <summary>Update an account's handle (admin action).</summary>
    /// <param name="did">The account DID.</param>
    /// <param name="handle">The new handle.</param>
    public Task UpdateAccountHandleAsync(
        Did did, Handle handle,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "com.atproto.admin.updateAccountHandle",
            new UpdateAccountHandleRequest(Did: did, Handle: handle), cancellationToken: cancellationToken);

    /// <summary>Update an account's password (admin action).</summary>
    /// <param name="did">The account DID.</param>
    /// <param name="password">The new password.</param>
    public Task UpdateAccountPasswordAsync(
        Did did, string password,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "com.atproto.admin.updateAccountPassword",
            new UpdateAccountPasswordRequest(Did: did, Password: password), cancellationToken: cancellationToken);

    /// <summary>Replace the repository signing key in an account's DID document (admin action).</summary>
    /// <remarks>
    /// Served by the Bluesky entryway; the reference PDS does not implement it.
    /// </remarks>
    /// <param name="did">The account DID.</param>
    /// <param name="signingKey">The new signing key, as a <c>did:key</c>.</param>
    public Task UpdateAccountSigningKeyAsync(
        Did did, Did signingKey,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "com.atproto.admin.updateAccountSigningKey",
            new UpdateAccountSigningKeyRequest(Did: did, SigningKey: signingKey), cancellationToken: cancellationToken);

    /// <summary>Disable invite codes.</summary>
    /// <param name="codes">The codes to disable.</param>
    /// <param name="accounts">The accounts whose codes to disable.</param>
    public Task DisableInviteCodesAsync(
        IEnumerable<string>? codes = null, IEnumerable<string>? accounts = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync(
            "com.atproto.admin.disableInviteCodes",
            new DisableInviteCodesRequest(Codes: codes?.ToList(), Accounts: accounts?.ToList()),
            cancellationToken: cancellationToken);

    /// <summary>Get one page of the server's invite codes.</summary>
    /// <param name="sort">The order: <c>recent</c> (the default) or <c>usage</c>.</param>
    /// <param name="limit">Maximum number of results (1-500, default 100).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetInviteCodesResponse> GetInviteCodesAsync(
        string? sort = null, int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetInviteCodesResponse>(
            "com.atproto.admin.getInviteCodes",
            new XrpcParams()
                .Add("sort", sort)
                .Add("limit", limit)
                .Add("cursor", cursor),
            cancellationToken: cancellationToken);
}
