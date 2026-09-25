using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.Com.AtProto.Server;

/// <summary>
/// Client for com.atproto.server.* XRPC endpoints.
/// Handles session management, account creation, and server administration.
/// </summary>
/// <remarks>
/// Like every Lexicon sub-client it is stateless: the session calls return the tokens they are
/// given and take the ones they need, but never install or clear the client's session. To sign
/// in or out, use <see cref="AtProtoClient.LoginAsync"/>, <see cref="AtProtoClient.CreateAccountAndLoginAsync"/>
/// and <see cref="AtProtoClient.LogoutAsync"/>, which also keep the session refreshed.
/// </remarks>
public sealed class ServerClient
{
    private readonly XrpcClient _xrpc;

    internal ServerClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Create an authentication session (sign in). The request carries no credentials, and the
    /// tokens returned are not installed on the client; <see cref="AtProtoClient.LoginAsync"/>
    /// does both.
    /// </summary>
    /// <param name="identifier">The account's handle, DID or email address.</param>
    /// <param name="password">The password or app password.</param>
    /// <param name="authFactorToken">The emailed second-factor token, when the account needs one.</param>
    /// <param name="allowTakendown">
    /// Let a taken-down account sign in, to a session that can only migrate or export it. Without
    /// it the service refuses such an account with <c>AccountTakedown</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SessionResponse> CreateSessionAsync(
        string identifier, string password, string? authFactorToken = null, bool allowTakendown = false,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateSessionRequest
        {
            Identifier = identifier,
            Password = password,
            AuthFactorToken = authFactorToken,

            // Sent only when asked for, so the request stays what a server predating the field expects.
            AllowTakendown = allowTakendown ? true : null,
        };

        return _xrpc.ProcedureWithTokenAsync<SessionResponse>(
            "com.atproto.server.createSession", request, bearerToken: null, cancellationToken);
    }

    /// <summary>
    /// Exchange a refresh JWT for new session tokens. The refresh JWT is single-use: after this
    /// call only the one returned is valid.
    /// </summary>
    /// <param name="refreshJwt">The session's refresh JWT.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SessionResponse> RefreshSessionAsync(string refreshJwt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshJwt);
        return _xrpc.ProcedureWithTokenAsync<SessionResponse>(
            "com.atproto.server.refreshSession", body: null, refreshJwt, cancellationToken);
    }

    /// <summary>
    /// Get information about the current session.
    /// </summary>
    public Task<GetSessionResponse> GetSessionAsync(CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetSessionResponse>(
            "com.atproto.server.getSession", options: XrpcClient.Direct, cancellationToken: cancellationToken);

    /// <summary>
    /// Delete a session on the server (sign out), invalidating its refresh JWT. The client's own
    /// session is left installed; <see cref="AtProtoClient.LogoutAsync"/> clears it as well.
    /// </summary>
    /// <param name="refreshJwt">The refresh JWT of the session to delete, as the Lexicon requires.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task DeleteSessionAsync(string refreshJwt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshJwt);
        return _xrpc.ProcedureWithTokenAsync(
            "com.atproto.server.deleteSession", body: null, refreshJwt, cancellationToken);
    }

    /// <summary>
    /// Create a new account on the server. The request carries no credentials, and the session
    /// returned is not installed on the client; <see cref="AtProtoClient.CreateAccountAndLoginAsync"/>
    /// does that too.
    /// </summary>
    /// <param name="request">The account to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CreateAccountResponse> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _xrpc.ProcedureWithTokenAsync<CreateAccountResponse>(
            "com.atproto.server.createAccount", request, bearerToken: null, cancellationToken);
    }

    /// <summary>
    /// Delete an account. Requires a confirmation token.
    /// </summary>
    public Task DeleteAccountAsync(DeleteAccountRequest request, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync("com.atproto.server.deleteAccount", request, cancellationToken: cancellationToken);

    /// <summary>
    /// Get a description of the server's configuration and capabilities.
    /// </summary>
    public Task<DescribeServerResponse> DescribeServerAsync(CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<DescribeServerResponse>("com.atproto.server.describeServer", cancellationToken: cancellationToken);

    /// <summary>
    /// Create a new app password for third-party application access.
    /// </summary>
    public Task<AppPassword> CreateAppPasswordAsync(string name, bool? privileged = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<AppPassword>(
            "com.atproto.server.createAppPassword",
            new CreateAppPasswordRequest { Name = name, Privileged = privileged },
            cancellationToken: cancellationToken);

    /// <summary>
    /// List all app passwords for the current account.
    /// </summary>
    public Task<ListAppPasswordsResponse> ListAppPasswordsAsync(CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<ListAppPasswordsResponse>("com.atproto.server.listAppPasswords", cancellationToken: cancellationToken);

    /// <summary>
    /// Revoke an app password by name.
    /// </summary>
    public Task RevokeAppPasswordAsync(string name, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync("com.atproto.server.revokeAppPassword",
            new RevokeAppPasswordRequest { Name = name }, cancellationToken: cancellationToken);

    /// <summary>
    /// Request a password reset email.
    /// </summary>
    public Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync("com.atproto.server.requestPasswordReset",
            new RequestPasswordResetRequest { Email = email }, cancellationToken: cancellationToken);

    /// <summary>
    /// Reset password using a token received via email.
    /// </summary>
    public Task ResetPasswordAsync(string token, string password, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync("com.atproto.server.resetPassword",
            new ResetPasswordRequest { Token = token, Password = password }, cancellationToken: cancellationToken);

    /// <summary>
    /// Confirm an email address with a token.
    /// </summary>
    public Task ConfirmEmailAsync(string email, string token, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync("com.atproto.server.confirmEmail",
            new ConfirmEmailRequest { Email = email, Token = token }, cancellationToken: cancellationToken);

    /// <summary>
    /// Request an email confirmation code.
    /// </summary>
    public Task RequestEmailConfirmationAsync(CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync("com.atproto.server.requestEmailConfirmation", cancellationToken: cancellationToken);

    /// <summary>
    /// Request an email update token.
    /// </summary>
    public Task<RequestEmailUpdateResponse> RequestEmailUpdateAsync(CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<RequestEmailUpdateResponse>("com.atproto.server.requestEmailUpdate", cancellationToken: cancellationToken);

    /// <summary>
    /// Update the email address for the current account.
    /// </summary>
    public Task UpdateEmailAsync(UpdateEmailRequest request, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync("com.atproto.server.updateEmail", request, cancellationToken: cancellationToken);

    /// <summary>
    /// Get a service auth token for inter-service authentication.
    /// </summary>
    /// <param name="aud">
    /// The DID of the service the token is for, optionally with a <c>#serviceId</c> fragment.
    /// </param>
    /// <param name="lxm">The XRPC method to bind the token to, if any.</param>
    /// <param name="exp">When the token expires, in Unix epoch seconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetServiceAuthResponse> GetServiceAuthAsync(string aud, Nsid? lxm = null,
        int? exp = null, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("aud", aud)
            .Add("lxm", lxm)
            .Add("exp", exp);
        return _xrpc.QueryAsync<GetServiceAuthResponse>("com.atproto.server.getServiceAuth", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Create an invite code.
    /// </summary>
    /// <param name="useCount">How many accounts the code may create.</param>
    /// <param name="forAccount">The DID of the account to issue the code to, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CreateInviteCodeResponse> CreateInviteCodeAsync(int useCount, Did? forAccount = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<CreateInviteCodeResponse>(
            "com.atproto.server.createInviteCode",
            new CreateInviteCodeRequest { UseCount = useCount, ForAccount = forAccount },
            cancellationToken: cancellationToken);

    /// <summary>
    /// Create multiple invite codes.
    /// </summary>
    public Task<CreateInviteCodesResponse> CreateInviteCodesAsync(CreateInviteCodesRequest request, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<CreateInviteCodesResponse>(
            "com.atproto.server.createInviteCodes", request, cancellationToken: cancellationToken);

    /// <summary>
    /// Get invite codes for the current account.
    /// </summary>
    public Task<GetAccountInviteCodesResponse> GetAccountInviteCodesAsync(
        bool? includeUsed = null, bool? createAvailable = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("includeUsed", includeUsed)
            .Add("createAvailable", createAvailable);
        return _xrpc.QueryAsync<GetAccountInviteCodesResponse>(
            "com.atproto.server.getAccountInviteCodes", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Request a deletion token for account deletion.
    /// </summary>
    public Task RequestAccountDeleteAsync(CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync("com.atproto.server.requestAccountDelete", cancellationToken: cancellationToken);

    /// <summary>
    /// Reserve a signing key for account creation.
    /// </summary>
    /// <param name="did">The DID to reserve the key for, if it already exists.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ReserveSigningKeyResponse> ReserveSigningKeyAsync(Did? did = null, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<ReserveSigningKeyResponse>(
            "com.atproto.server.reserveSigningKey",
            new ReserveSigningKeyRequest { Did = did },
            cancellationToken: cancellationToken);

    /// <summary>
    /// Activate a deactivated account.
    /// </summary>
    public Task ActivateAccountAsync(CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync("com.atproto.server.activateAccount", cancellationToken: cancellationToken);

    /// <summary>
    /// Deactivate an account.
    /// </summary>
    /// <param name="deleteAfter">
    /// A recommendation to the server of how long to keep the deactivated account before deleting
    /// it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task DeactivateAccountAsync(AtDatetime? deleteAfter = null, CancellationToken cancellationToken = default) =>
        // Always a JSON body, even an empty one: the reference PDS rejects this method without one.
        _xrpc.ProcedureAsync(
            "com.atproto.server.deactivateAccount",
            new DeactivateAccountRequest { DeleteAfter = deleteAfter },
            cancellationToken: cancellationToken);

    /// <summary>
    /// Check account status.
    /// </summary>
    public Task<CheckAccountStatusResponse> CheckAccountStatusAsync(CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<CheckAccountStatusResponse>("com.atproto.server.checkAccountStatus", cancellationToken: cancellationToken);
}
