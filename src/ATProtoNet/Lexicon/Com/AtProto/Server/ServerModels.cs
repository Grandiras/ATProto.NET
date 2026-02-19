using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.Server;

/// <summary>
/// Request body for com.atproto.server.createSession.
/// </summary>
public sealed class CreateSessionRequest
{
    /// <summary>
    /// Handle or other identifier supported by the server for the authenticating user.
    /// </summary>
    [JsonPropertyName("identifier")]
    public required string Identifier { get; init; }

    /// <summary>
    /// The password for the account.
    /// </summary>
    [JsonPropertyName("password")]
    public required string Password { get; init; }

    /// <summary>
    /// Email auth factor token, if email authentication is enabled.
    /// </summary>
    [JsonPropertyName("authFactorToken")]
    public string? AuthFactorToken { get; init; }
}

/// <summary>
/// Response from com.atproto.server.createSession and com.atproto.server.refreshSession.
/// </summary>
public sealed class SessionResponse
{
    [JsonPropertyName("accessJwt")]
    public string AccessJwt { get; init; } = string.Empty;

    [JsonPropertyName("refreshJwt")]
    public string RefreshJwt { get; init; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; init; } = string.Empty;

    [JsonPropertyName("did")]
    public string Did { get; init; } = string.Empty;

    [JsonPropertyName("didDoc")]
    public object? DidDoc { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("emailConfirmed")]
    public bool? EmailConfirmed { get; init; }

    [JsonPropertyName("emailAuthFactor")]
    public bool? EmailAuthFactor { get; init; }

    [JsonPropertyName("active")]
    public bool? Active { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Response from com.atproto.server.getSession.
/// </summary>
public sealed class GetSessionResponse
{
    [JsonPropertyName("handle")]
    public string Handle { get; init; } = string.Empty;

    [JsonPropertyName("did")]
    public string Did { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("emailConfirmed")]
    public bool? EmailConfirmed { get; init; }

    [JsonPropertyName("emailAuthFactor")]
    public bool? EmailAuthFactor { get; init; }

    [JsonPropertyName("didDoc")]
    public object? DidDoc { get; init; }

    [JsonPropertyName("active")]
    public bool? Active { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.createAccount.
/// </summary>
public sealed class CreateAccountRequest
{
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("did")]
    public string? Did { get; init; }

    [JsonPropertyName("inviteCode")]
    public string? InviteCode { get; init; }

    [JsonPropertyName("verificationCode")]
    public string? VerificationCode { get; init; }

    [JsonPropertyName("verificationPhone")]
    public string? VerificationPhone { get; init; }

    [JsonPropertyName("password")]
    public string? Password { get; init; }

    [JsonPropertyName("recoveryKey")]
    public string? RecoveryKey { get; init; }

    [JsonPropertyName("plcOp")]
    public object? PlcOp { get; init; }
}

/// <summary>
/// Response from com.atproto.server.createAccount.
/// </summary>
public sealed class CreateAccountResponse
{
    [JsonPropertyName("accessJwt")]
    public string AccessJwt { get; init; } = string.Empty;

    [JsonPropertyName("refreshJwt")]
    public string RefreshJwt { get; init; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; init; } = string.Empty;

    [JsonPropertyName("did")]
    public string Did { get; init; } = string.Empty;

    [JsonPropertyName("didDoc")]
    public object? DidDoc { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.deleteAccount.
/// </summary>
public sealed class DeleteAccountRequest
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("password")]
    public required string Password { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }
}

/// <summary>
/// Response from com.atproto.server.describeServer.
/// </summary>
public sealed class DescribeServerResponse
{
    [JsonPropertyName("inviteCodeRequired")]
    public bool? InviteCodeRequired { get; init; }

    [JsonPropertyName("phoneVerificationRequired")]
    public bool? PhoneVerificationRequired { get; init; }

    [JsonPropertyName("availableUserDomains")]
    public List<string> AvailableUserDomains { get; init; } = [];

    [JsonPropertyName("links")]
    public ServerLinks? Links { get; init; }

    [JsonPropertyName("contact")]
    public ServerContact? Contact { get; init; }

    [JsonPropertyName("did")]
    public string Did { get; init; } = string.Empty;
}

public sealed class ServerLinks
{
    [JsonPropertyName("privacyPolicy")]
    public string? PrivacyPolicy { get; init; }

    [JsonPropertyName("termsOfService")]
    public string? TermsOfService { get; init; }
}

public sealed class ServerContact
{
    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.createAppPassword.
/// </summary>
public sealed class CreateAppPasswordRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("privileged")]
    public bool? Privileged { get; init; }
}

/// <summary>
/// Response from com.atproto.server.createAppPassword.
/// </summary>
public sealed class AppPassword
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("privileged")]
    public bool? Privileged { get; init; }
}

/// <summary>
/// Response from com.atproto.server.listAppPasswords.
/// </summary>
public sealed class ListAppPasswordsResponse
{
    [JsonPropertyName("passwords")]
    public List<AppPasswordInfo> Passwords { get; init; } = [];
}

public sealed class AppPasswordInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("privileged")]
    public bool? Privileged { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.requestPasswordReset.
/// </summary>
public sealed class RequestPasswordResetRequest
{
    [JsonPropertyName("email")]
    public required string Email { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.resetPassword.
/// </summary>
public sealed class ResetPasswordRequest
{
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("password")]
    public required string Password { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.confirmEmail.
/// </summary>
public sealed class ConfirmEmailRequest
{
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.updateEmail.
/// </summary>
public sealed class UpdateEmailRequest
{
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("emailAuthFactor")]
    public bool? EmailAuthFactor { get; init; }

    [JsonPropertyName("token")]
    public string? Token { get; init; }
}

/// <summary>
/// Response from com.atproto.server.requestEmailUpdate.
/// </summary>
public sealed class RequestEmailUpdateResponse
{
    [JsonPropertyName("tokenRequired")]
    public bool TokenRequired { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.getServiceAuth.
/// </summary>
public sealed class GetServiceAuthResponse
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;
}

/// <summary>
/// Response from com.atproto.server.createInviteCode.
/// </summary>
public sealed class CreateInviteCodeRequest
{
    [JsonPropertyName("useCount")]
    public required int UseCount { get; init; }

    [JsonPropertyName("forAccount")]
    public string? ForAccount { get; init; }
}

public sealed class CreateInviteCodeResponse
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;
}

/// <summary>
/// Request body for com.atproto.server.createInviteCodes.
/// </summary>
public sealed class CreateInviteCodesRequest
{
    [JsonPropertyName("codeCount")]
    public required int CodeCount { get; init; }

    [JsonPropertyName("useCount")]
    public required int UseCount { get; init; }

    [JsonPropertyName("forAccounts")]
    public List<string>? ForAccounts { get; init; }
}

public sealed class CreateInviteCodesResponse
{
    [JsonPropertyName("codes")]
    public List<AccountCodes> Codes { get; init; } = [];
}

public sealed class AccountCodes
{
    [JsonPropertyName("account")]
    public string Account { get; init; } = string.Empty;

    [JsonPropertyName("codes")]
    public List<string> Codes { get; init; } = [];
}

/// <summary>
/// Response from com.atproto.server.getAccountInviteCodes.
/// </summary>
public sealed class GetAccountInviteCodesResponse
{
    [JsonPropertyName("codes")]
    public List<InviteCode> Codes { get; init; } = [];
}

public sealed class InviteCode
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("available")]
    public int Available { get; init; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; init; }

    [JsonPropertyName("forAccount")]
    public string ForAccount { get; init; } = string.Empty;

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("uses")]
    public List<InviteCodeUse> Uses { get; init; } = [];
}

public sealed class InviteCodeUse
{
    [JsonPropertyName("usedBy")]
    public string UsedBy { get; init; } = string.Empty;

    [JsonPropertyName("usedAt")]
    public string UsedAt { get; init; } = string.Empty;
}

/// <summary>
/// Request body for com.atproto.server.revokeAppPassword.
/// </summary>
public sealed class RevokeAppPasswordRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.reserveSigningKey.
/// </summary>
public sealed class ReserveSigningKeyRequest
{
    [JsonPropertyName("did")]
    public string? Did { get; init; }
}

public sealed class ReserveSigningKeyResponse
{
    [JsonPropertyName("signingKey")]
    public string SigningKey { get; init; } = string.Empty;
}

/// <summary>
/// Response from com.atproto.server.checkAccountStatus.
/// </summary>
public sealed class CheckAccountStatusResponse
{
    [JsonPropertyName("activated")]
    public bool Activated { get; init; }

    [JsonPropertyName("validDid")]
    public bool ValidDid { get; init; }

    [JsonPropertyName("repoCommit")]
    public string RepoCommit { get; init; } = string.Empty;

    [JsonPropertyName("repoRev")]
    public string RepoRev { get; init; } = string.Empty;

    [JsonPropertyName("repoBlocks")]
    public int RepoBlocks { get; init; }

    [JsonPropertyName("indexedRecords")]
    public int IndexedRecords { get; init; }

    [JsonPropertyName("privateStateValues")]
    public int PrivateStateValues { get; init; }

    [JsonPropertyName("expectedBlobs")]
    public int ExpectedBlobs { get; init; }

    [JsonPropertyName("importedBlobs")]
    public int ImportedBlobs { get; init; }
}

/// <summary>
/// Server definition types.
/// </summary>
public static class ServerDefs
{
    public const string InviteCodeTypeAdmin = "admin";
}
