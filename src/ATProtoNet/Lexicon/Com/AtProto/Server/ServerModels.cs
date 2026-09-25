using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Com.AtProto.Server;

/// <summary>
/// Request body for com.atproto.server.createSession.
/// </summary>
internal sealed class CreateSessionRequest
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

    /// <summary>
    /// Whether a taken-down account may sign in, to a session that can only migrate or export it.
    /// </summary>
    [JsonPropertyName("allowTakendown")]
    public bool? AllowTakendown { get; init; }
}

/// <summary>
/// Response from com.atproto.server.createSession and com.atproto.server.refreshSession.
/// </summary>
public sealed class SessionResponse
{
    /// <summary>The access JWT used to authenticate subsequent requests.</summary>
    [JsonPropertyName("accessJwt")]
    public string AccessJwt { get; init; } = string.Empty;

    /// <summary>The refresh JWT used to obtain a new access token.</summary>
    [JsonPropertyName("refreshJwt")]
    public string RefreshJwt { get; init; } = string.Empty;

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The DID document for the account, as returned by the PDS.</summary>
    [JsonPropertyName("didDoc")]
    public object? DidDoc { get; init; }

    /// <summary>The email address of the account.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>Whether the email address has been confirmed.</summary>
    [JsonPropertyName("emailConfirmed")]
    public bool? EmailConfirmed { get; init; }

    /// <summary>Whether email is enabled as a second authentication factor.</summary>
    [JsonPropertyName("emailAuthFactor")]
    public bool? EmailAuthFactor { get; init; }

    /// <summary>
    /// Whether the account is active (not deactivated, suspended, or taken down).
    /// </summary>
    [JsonPropertyName("active")]
    public bool? Active { get; init; }

    /// <summary>The hosting status of the account, if it is not active.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Response from com.atproto.server.getSession.
/// </summary>
public sealed class GetSessionResponse
{
    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The email address of the account.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>Whether the email address has been confirmed.</summary>
    [JsonPropertyName("emailConfirmed")]
    public bool? EmailConfirmed { get; init; }

    /// <summary>Whether email is enabled as a second authentication factor.</summary>
    [JsonPropertyName("emailAuthFactor")]
    public bool? EmailAuthFactor { get; init; }

    /// <summary>The DID document for the account, as returned by the PDS.</summary>
    [JsonPropertyName("didDoc")]
    public object? DidDoc { get; init; }

    /// <summary>
    /// Whether the account is active (not deactivated, suspended, or taken down).
    /// </summary>
    [JsonPropertyName("active")]
    public bool? Active { get; init; }

    /// <summary>The hosting status of the account, if it is not active.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.createAccount.
/// </summary>
public sealed class CreateAccountRequest
{
    /// <summary>The email address of the account.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public Did? Did { get; init; }

    /// <summary>The invite code to consume when creating the account.</summary>
    [JsonPropertyName("inviteCode")]
    public string? InviteCode { get; init; }

    /// <summary>An email verification code, if the server requires one.</summary>
    [JsonPropertyName("verificationCode")]
    public string? VerificationCode { get; init; }

    /// <summary>A phone number to verify, if the server requires one.</summary>
    [JsonPropertyName("verificationPhone")]
    public string? VerificationPhone { get; init; }

    /// <summary>The account password.</summary>
    [JsonPropertyName("password")]
    public string? Password { get; init; }

    /// <summary>A DID recovery key to add to the account's DID document.</summary>
    [JsonPropertyName("recoveryKey")]
    public string? RecoveryKey { get; init; }

    /// <summary>A signed DID PLC operation to apply when creating the account.</summary>
    [JsonPropertyName("plcOp")]
    public object? PlcOp { get; init; }
}

/// <summary>
/// Response from com.atproto.server.createAccount.
/// </summary>
public sealed class CreateAccountResponse
{
    /// <summary>The access JWT used to authenticate subsequent requests.</summary>
    [JsonPropertyName("accessJwt")]
    public string AccessJwt { get; init; } = string.Empty;

    /// <summary>The refresh JWT used to obtain a new access token.</summary>
    [JsonPropertyName("refreshJwt")]
    public string RefreshJwt { get; init; } = string.Empty;

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The DID document for the account, as returned by the PDS.</summary>
    [JsonPropertyName("didDoc")]
    public object? DidDoc { get; init; }
}

/// <summary>
/// Request body for deactivateAccount.
/// </summary>
internal sealed class DeactivateAccountRequest
{
    /// <summary>How long the server should keep the deactivated account before deleting it.</summary>
    [JsonPropertyName("deleteAfter")]
    public AtDatetime? DeleteAfter { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.deleteAccount.
/// </summary>
public sealed class DeleteAccountRequest
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The account password.</summary>
    [JsonPropertyName("password")]
    public required string Password { get; init; }

    /// <summary>The deletion token emailed to the account holder.</summary>
    [JsonPropertyName("token")]
    public required string Token { get; init; }
}

/// <summary>
/// Response from com.atproto.server.describeServer.
/// </summary>
public sealed class DescribeServerResponse
{
    /// <summary>Whether an invite code is required to create an account.</summary>
    [JsonPropertyName("inviteCodeRequired")]
    public bool? InviteCodeRequired { get; init; }

    /// <summary>Whether phone verification is required to create an account.</summary>
    [JsonPropertyName("phoneVerificationRequired")]
    public bool? PhoneVerificationRequired { get; init; }

    /// <summary>The handle domains this server will issue handles under.</summary>
    [JsonPropertyName("availableUserDomains")]
    public IReadOnlyList<string> AvailableUserDomains { get; init; } = [];

    /// <summary>Links to the server's policy documents.</summary>
    [JsonPropertyName("links")]
    public ServerLinks? Links { get; init; }

    /// <summary>Contact details for the server operator.</summary>
    [JsonPropertyName("contact")]
    public ServerContact? Contact { get; init; }

    /// <summary>The largest blob the server accepts, in bytes, if it states one.</summary>
    [JsonPropertyName("blobUploadLimit")]
    public long? BlobUploadLimit { get; init; }

    /// <summary>The DID of the server.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }
}

/// <summary>Links to a server's policy documents.</summary>
public sealed class ServerLinks : LexObject
{
    /// <summary>URL of the server's privacy policy.</summary>
    [JsonPropertyName("privacyPolicy")]
    public string? PrivacyPolicy { get; init; }

    /// <summary>URL of the server's terms of service.</summary>
    [JsonPropertyName("termsOfService")]
    public string? TermsOfService { get; init; }
}

/// <summary>Contact details for a server operator.</summary>
public sealed class ServerContact : LexObject
{
    /// <summary>The email address of the account.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.createAppPassword.
/// </summary>
internal sealed class CreateAppPasswordRequest
{
    /// <summary>A name identifying what the app password is used for.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Whether the app password is privileged (may access chat and other restricted endpoints).
    /// </summary>
    [JsonPropertyName("privileged")]
    public bool? Privileged { get; init; }
}

/// <summary>
/// Response from com.atproto.server.createAppPassword.
/// </summary>
public sealed class AppPassword : LexObject
{
    /// <summary>The name of the app password.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>The generated app password. This is the only time it is returned.</summary>
    [JsonPropertyName("password")]
    public string Password { get; init; } = string.Empty;

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>
    /// Whether the app password is privileged (may access chat and other restricted endpoints).
    /// </summary>
    [JsonPropertyName("privileged")]
    public bool? Privileged { get; init; }
}

/// <summary>
/// Response from com.atproto.server.listAppPasswords.
/// </summary>
public sealed class ListAppPasswordsResponse
{
    /// <summary>The app passwords on the account.</summary>
    [JsonPropertyName("passwords")]
    public IReadOnlyList<AppPasswordInfo> Passwords { get; init; } = [];
}

/// <summary>Metadata about an app password, without the password itself.</summary>
public sealed class AppPasswordInfo : LexObject
{
    /// <summary>The name of the app password.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>
    /// Whether the app password is privileged (may access chat and other restricted endpoints).
    /// </summary>
    [JsonPropertyName("privileged")]
    public bool? Privileged { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.requestPasswordReset.
/// </summary>
internal sealed class RequestPasswordResetRequest
{
    /// <summary>The email address of the account.</summary>
    [JsonPropertyName("email")]
    public required string Email { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.resetPassword.
/// </summary>
internal sealed class ResetPasswordRequest
{
    /// <summary>The reset token emailed to the account holder.</summary>
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    /// <summary>The new password.</summary>
    [JsonPropertyName("password")]
    public required string Password { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.confirmEmail.
/// </summary>
internal sealed class ConfirmEmailRequest
{
    /// <summary>The email address of the account.</summary>
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    /// <summary>The confirmation token emailed to the account holder.</summary>
    [JsonPropertyName("token")]
    public required string Token { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.updateEmail.
/// </summary>
public sealed class UpdateEmailRequest
{
    /// <summary>The email address of the account.</summary>
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    /// <summary>Whether email is enabled as a second authentication factor.</summary>
    [JsonPropertyName("emailAuthFactor")]
    public bool? EmailAuthFactor { get; init; }

    /// <summary>The one-time confirmation token emailed to the account holder.</summary>
    [JsonPropertyName("token")]
    public string? Token { get; init; }
}

/// <summary>
/// Response from com.atproto.server.requestEmailUpdate.
/// </summary>
public sealed class RequestEmailUpdateResponse
{
    /// <summary>Whether a confirmation token is required to complete the update.</summary>
    [JsonPropertyName("tokenRequired")]
    public bool TokenRequired { get; init; }
}

/// <summary>
/// Response from com.atproto.server.getServiceAuth.
/// </summary>
public sealed class GetServiceAuthResponse
{
    /// <summary>The signed service auth JWT.</summary>
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;
}

/// <summary>
/// Request body for com.atproto.server.createInviteCode.
/// </summary>
internal sealed class CreateInviteCodeRequest
{
    /// <summary>The number of times each code may be used.</summary>
    [JsonPropertyName("useCount")]
    public required int UseCount { get; init; }

    /// <summary>The DID of the account the code is issued to.</summary>
    [JsonPropertyName("forAccount")]
    public Did? ForAccount { get; init; }
}

/// <summary>The response from creating a single invite code.</summary>
public sealed class CreateInviteCodeResponse
{
    /// <summary>The invite code.</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;
}

/// <summary>
/// Request body for com.atproto.server.createInviteCodes.
/// </summary>
public sealed class CreateInviteCodesRequest
{
    /// <summary>The number of codes to create per account.</summary>
    [JsonPropertyName("codeCount")]
    public required int CodeCount { get; init; }

    /// <summary>The number of times each code may be used.</summary>
    [JsonPropertyName("useCount")]
    public required int UseCount { get; init; }

    /// <summary>The DIDs of the accounts the codes are issued to.</summary>
    [JsonPropertyName("forAccounts")]
    public IReadOnlyList<Did>? ForAccounts { get; init; }
}

/// <summary>The response from creating invite codes in bulk.</summary>
public sealed class CreateInviteCodesResponse
{
    /// <summary>The invite codes.</summary>
    [JsonPropertyName("codes")]
    public IReadOnlyList<AccountCodes> Codes { get; init; } = [];
}

/// <summary>The invite codes issued to one account.</summary>
public sealed class AccountCodes : LexObject
{
    /// <summary>
    /// The DID of the account the codes belong to, or <c>admin</c> for codes minted by the
    /// server administrator.
    /// </summary>
    [JsonPropertyName("account")]
    public string Account { get; init; } = string.Empty;

    /// <summary>The invite codes.</summary>
    [JsonPropertyName("codes")]
    public IReadOnlyList<string> Codes { get; init; } = [];
}

/// <summary>
/// Response from com.atproto.server.getAccountInviteCodes.
/// </summary>
public sealed class GetAccountInviteCodesResponse
{
    /// <summary>The invite codes.</summary>
    [JsonPropertyName("codes")]
    public IReadOnlyList<InviteCode> Codes { get; init; } = [];
}

/// <summary>An invite code and its usage history.</summary>
public sealed class InviteCode : LexObject
{
    /// <summary>The invite code string.</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    /// <summary>The number of remaining uses on the code.</summary>
    [JsonPropertyName("available")]
    public int Available { get; init; }

    /// <summary>Whether the code has been disabled.</summary>
    [JsonPropertyName("disabled")]
    public bool Disabled { get; init; }

    /// <summary>
    /// The DID of the account the code is issued to, or <c>admin</c> for a code minted by the
    /// server administrator.
    /// </summary>
    [JsonPropertyName("forAccount")]
    public string ForAccount { get; init; } = string.Empty;

    /// <summary>
    /// The DID of the account that created the code, or <c>admin</c> for the server
    /// administrator.
    /// </summary>
    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary>Timestamp of creation (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>The recorded uses of the code.</summary>
    [JsonPropertyName("uses")]
    public IReadOnlyList<InviteCodeUse> Uses { get; init; } = [];
}

/// <summary>A single use of an invite code.</summary>
public sealed class InviteCodeUse : LexObject
{
    /// <summary>The DID of the account that used the code.</summary>
    [JsonPropertyName("usedBy")]
    public required Did UsedBy { get; init; }

    /// <summary>Timestamp at which the code was used.</summary>
    [JsonPropertyName("usedAt")]
    public required AtDatetime UsedAt { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.revokeAppPassword.
/// </summary>
internal sealed class RevokeAppPasswordRequest
{
    /// <summary>The name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

/// <summary>
/// Request body for com.atproto.server.reserveSigningKey.
/// </summary>
internal sealed class ReserveSigningKeyRequest
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public Did? Did { get; init; }
}

/// <summary>The response from reserving a repository signing key.</summary>
public sealed class ReserveSigningKeyResponse
{
    /// <summary>The reserved signing key, as a <c>did:key</c> string.</summary>
    [JsonPropertyName("signingKey")]
    public string SigningKey { get; init; } = string.Empty;
}

/// <summary>
/// Response from com.atproto.server.checkAccountStatus.
/// </summary>
public sealed class CheckAccountStatusResponse
{
    /// <summary>Whether the account has been activated.</summary>
    [JsonPropertyName("activated")]
    public bool Activated { get; init; }

    /// <summary>Whether the account's DID document is valid and resolvable.</summary>
    [JsonPropertyName("validDid")]
    public bool ValidDid { get; init; }

    /// <summary>The CID of the current repository commit.</summary>
    [JsonPropertyName("repoCommit")]
    public required Cid RepoCommit { get; init; }

    /// <summary>The current repository revision.</summary>
    [JsonPropertyName("repoRev")]
    public required Tid RepoRev { get; init; }

    /// <summary>The number of blocks in the repository.</summary>
    [JsonPropertyName("repoBlocks")]
    public int RepoBlocks { get; init; }

    /// <summary>The number of records indexed for the account.</summary>
    [JsonPropertyName("indexedRecords")]
    public int IndexedRecords { get; init; }

    /// <summary>The number of private state values stored for the account.</summary>
    [JsonPropertyName("privateStateValues")]
    public int PrivateStateValues { get; init; }

    /// <summary>The number of blobs the repository references.</summary>
    [JsonPropertyName("expectedBlobs")]
    public int ExpectedBlobs { get; init; }

    /// <summary>The number of referenced blobs that have been imported.</summary>
    [JsonPropertyName("importedBlobs")]
    public int ImportedBlobs { get; init; }
}

/// <summary>
/// Server definition types.
/// </summary>
public static class ServerDefs
{
    /// <summary>The <c>admin</c> invite code type.</summary>
    public const string InviteCodeTypeAdmin = "admin";
}
