using ATProtoNet.Http;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Lexicon.Com.AtProto.Server;

/// <summary>
/// Client for com.atproto.server.* XRPC endpoints.
/// Handles session management, account creation, and server administration.
/// </summary>
public sealed class ServerClient
{
    private readonly XrpcClient _xrpc;
    private readonly ILogger _logger;

    internal ServerClient(XrpcClient xrpc, ILogger logger)
    {
        _xrpc = xrpc;
        _logger = logger;
    }

    /// <summary>
    /// Create an authentication session (login).
    /// </summary>
    public async Task<SessionResponse> CreateSessionAsync(
        string identifier, string password, string? authFactorToken = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateSessionRequest
        {
            Identifier = identifier,
            Password = password,
            AuthFactorToken = authFactorToken,
        };

        var response = await _xrpc.ProcedureAsync<CreateSessionRequest, SessionResponse>(
            "com.atproto.server.createSession", request, cancellationToken: cancellationToken);

        _xrpc.SetTokens(response.AccessJwt, response.RefreshJwt);
        _logger.LogInformation("Session created for {Handle} ({Did})", response.Handle, response.Did);

        return response;
    }

    /// <summary>
    /// Refresh the current session to get a new access token.
    /// </summary>
    public async Task<SessionResponse> RefreshSessionAsync(CancellationToken cancellationToken = default)
    {
        var response = await _xrpc.ProcedureWithRefreshTokenAsync<SessionResponse>(
            "com.atproto.server.refreshSession", cancellationToken);

        _xrpc.SetTokens(response.AccessJwt, response.RefreshJwt);
        _logger.LogDebug("Session refreshed for {Handle}", response.Handle);

        return response;
    }

    /// <summary>
    /// Get information about the current session.
    /// </summary>
    public Task<GetSessionResponse> GetSessionAsync(CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<GetSessionResponse>("com.atproto.server.getSession", cancellationToken: cancellationToken);

    /// <summary>
    /// Delete the current session (logout).
    /// </summary>
    public async Task DeleteSessionAsync(CancellationToken cancellationToken = default)
    {
        await _xrpc.ProcedureAsync("com.atproto.server.deleteSession", cancellationToken: cancellationToken);
        _xrpc.ClearTokens();
        _logger.LogInformation("Session deleted");
    }

    /// <summary>
    /// Create a new account on the server.
    /// </summary>
    public async Task<CreateAccountResponse> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _xrpc.ProcedureAsync<CreateAccountRequest, CreateAccountResponse>(
            "com.atproto.server.createAccount", request, cancellationToken: cancellationToken);

        _xrpc.SetTokens(response.AccessJwt, response.RefreshJwt);
        _logger.LogInformation("Account created: {Handle} ({Did})", response.Handle, response.Did);

        return response;
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
        _xrpc.ProcedureAsync<CreateAppPasswordRequest, AppPassword>(
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
    public Task<GetServiceAuthResponse> GetServiceAuthAsync(string aud, string? lxm = null,
        int? exp = null, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["aud"] = aud,
            ["lxm"] = lxm,
            ["exp"] = exp?.ToString(),
        };
        return _xrpc.QueryAsync<GetServiceAuthResponse>("com.atproto.server.getServiceAuth", parameters, cancellationToken);
    }

    /// <summary>
    /// Create an invite code.
    /// </summary>
    public Task<CreateInviteCodeResponse> CreateInviteCodeAsync(int useCount, string? forAccount = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<CreateInviteCodeRequest, CreateInviteCodeResponse>(
            "com.atproto.server.createInviteCode",
            new CreateInviteCodeRequest { UseCount = useCount, ForAccount = forAccount },
            cancellationToken: cancellationToken);

    /// <summary>
    /// Create multiple invite codes.
    /// </summary>
    public Task<CreateInviteCodesResponse> CreateInviteCodesAsync(CreateInviteCodesRequest request, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<CreateInviteCodesRequest, CreateInviteCodesResponse>(
            "com.atproto.server.createInviteCodes", request, cancellationToken: cancellationToken);

    /// <summary>
    /// Get invite codes for the current account.
    /// </summary>
    public Task<GetAccountInviteCodesResponse> GetAccountInviteCodesAsync(
        bool? includeUsed = null, bool? createAvailable = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["includeUsed"] = includeUsed?.ToString().ToLowerInvariant(),
            ["createAvailable"] = createAvailable?.ToString().ToLowerInvariant(),
        };
        return _xrpc.QueryAsync<GetAccountInviteCodesResponse>(
            "com.atproto.server.getAccountInviteCodes", parameters, cancellationToken);
    }

    /// <summary>
    /// Request a deletion token for account deletion.
    /// </summary>
    public Task RequestAccountDeleteAsync(CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync("com.atproto.server.requestAccountDelete", cancellationToken: cancellationToken);

    /// <summary>
    /// Reserve a signing key for account creation.
    /// </summary>
    public Task<ReserveSigningKeyResponse> ReserveSigningKeyAsync(string? did = null, CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<ReserveSigningKeyRequest, ReserveSigningKeyResponse>(
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
    public Task DeactivateAccountAsync(CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync("com.atproto.server.deactivateAccount", cancellationToken: cancellationToken);

    /// <summary>
    /// Check account status.
    /// </summary>
    public Task<CheckAccountStatusResponse> CheckAccountStatusAsync(CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<CheckAccountStatusResponse>("com.atproto.server.checkAccountStatus", cancellationToken: cancellationToken);
}
