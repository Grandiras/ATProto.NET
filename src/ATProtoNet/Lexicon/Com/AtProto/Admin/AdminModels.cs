using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.Admin;

// ──────────────────────────────────────────────────────────────
//  com.atproto.admin.getAccountInfo
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Detailed account information returned by admin endpoints.
/// </summary>
public sealed class AccountInfo
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    /// <summary>The email address of the account.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>
    /// Timestamp at which the email address was confirmed (ISO 8601), if it has been.
    /// </summary>
    [JsonPropertyName("emailConfirmedAt")]
    public string? EmailConfirmedAt { get; init; }

    /// <summary>Timestamp at which the app view indexed this data (ISO 8601).</summary>
    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    /// <summary>The invite code the account signed up with, if any.</summary>
    [JsonPropertyName("invitedBy")]
    public JsonElement? InvitedBy { get; init; }

    /// <summary>The invite codes created by the account.</summary>
    [JsonPropertyName("invites")]
    public List<JsonElement>? Invites { get; init; }

    /// <summary>Whether the account is barred from creating invite codes.</summary>
    [JsonPropertyName("invitesDisabled")]
    public bool? InvitesDisabled { get; init; }

    /// <summary>
    /// Selected records from the repository (such as the profile record) included for convenience.
    /// </summary>
    [JsonPropertyName("relatedRecords")]
    public List<JsonElement>? RelatedRecords { get; init; }

    /// <summary>
    /// Timestamp at which the account was deactivated (ISO 8601), if it is deactivated.
    /// </summary>
    [JsonPropertyName("deactivatedAt")]
    public string? DeactivatedAt { get; init; }

    /// <summary>
    /// Signals correlating this account with others (such as a shared IP or device).
    /// </summary>
    [JsonPropertyName("threatSignatures")]
    public List<ThreatSignature>? ThreatSignatures { get; init; }
}

/// <summary>
/// A threat signature associated with an account.
/// </summary>
public sealed class ThreatSignature
{
    /// <summary>The name of the account property the signature was derived from.</summary>
    [JsonPropertyName("property")]
    public required string Property { get; init; }

    /// <summary>The record value.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.admin.getAccountInfos
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getAccountInfos (batch account lookup).
/// </summary>
public sealed class GetAccountInfosResponse
{
    /// <summary>The account information records.</summary>
    [JsonPropertyName("infos")]
    public required List<AccountInfo> Infos { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.admin.getSubjectStatus
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Response from getSubjectStatus.
/// </summary>
public sealed class GetSubjectStatusResponse
{
    /// <summary>The subject the status applies to.</summary>
    [JsonPropertyName("subject")]
    public required JsonElement Subject { get; init; }

    /// <summary>Takedown status of the subject, if taken down.</summary>
    [JsonPropertyName("takedown")]
    public SubjectStatusDetail? Takedown { get; init; }

    /// <summary>Deactivation status of the subject, if deactivated.</summary>
    [JsonPropertyName("deactivated")]
    public SubjectStatusDetail? Deactivated { get; init; }
}

/// <summary>
/// Detailed status information for a subject (takedown, deactivated, etc.).
/// </summary>
public sealed class SubjectStatusDetail
{
    /// <summary>Whether the status is currently applied.</summary>
    [JsonPropertyName("applied")]
    public bool Applied { get; init; }

    /// <summary>A reference to the source of this status, if any.</summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.admin.updateSubjectStatus
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for updateSubjectStatus.
/// </summary>
public sealed class UpdateSubjectStatusRequest
{
    /// <summary>The subject to update.</summary>
    [JsonPropertyName("subject")]
    public required JsonElement Subject { get; init; }

    /// <summary>Takedown status of the subject, if taken down.</summary>
    [JsonPropertyName("takedown")]
    public SubjectStatusDetail? Takedown { get; init; }

    /// <summary>Deactivation status of the subject, if deactivated.</summary>
    [JsonPropertyName("deactivated")]
    public SubjectStatusDetail? Deactivated { get; init; }
}

/// <summary>
/// Response from updateSubjectStatus.
/// </summary>
public sealed class UpdateSubjectStatusResponse
{
    /// <summary>The subject that was updated.</summary>
    [JsonPropertyName("subject")]
    public required JsonElement Subject { get; init; }

    /// <summary>Takedown status of the subject, if taken down.</summary>
    [JsonPropertyName("takedown")]
    public SubjectStatusDetail? Takedown { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.admin.sendEmail
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for sendEmail.
/// </summary>
public sealed class SendEmailRequest
{
    /// <summary>The DID of the account receiving the email.</summary>
    [JsonPropertyName("recipientDid")]
    public required string RecipientDid { get; init; }

    /// <summary>The body of the email.</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>The DID of the moderator sending the email.</summary>
    [JsonPropertyName("senderDid")]
    public required string SenderDid { get; init; }

    /// <summary>The subject line of the email.</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    /// <summary>A free-text moderator comment.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

/// <summary>
/// Response from sendEmail.
/// </summary>
public sealed class SendEmailResponse
{
    /// <summary>Whether the email was sent.</summary>
    [JsonPropertyName("sent")]
    public bool Sent { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.admin account management
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for admin deleteAccount.
/// </summary>
public sealed class AdminDeleteAccountRequest
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// Request body for disableAccountInvites.
/// </summary>
public sealed class DisableAccountInvitesRequest
{
    /// <summary>The DID or handle of the account.</summary>
    [JsonPropertyName("account")]
    public required string Account { get; init; }

    /// <summary>An optional free-text note recorded with the action.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

/// <summary>
/// Request body for enableAccountInvites.
/// </summary>
public sealed class EnableAccountInvitesRequest
{
    /// <summary>The DID or handle of the account.</summary>
    [JsonPropertyName("account")]
    public required string Account { get; init; }

    /// <summary>An optional free-text note recorded with the action.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

/// <summary>
/// Request body for updateAccountEmail.
/// </summary>
public sealed class UpdateAccountEmailRequest
{
    /// <summary>The DID or handle of the account.</summary>
    [JsonPropertyName("account")]
    public required string Account { get; init; }

    /// <summary>The email address of the account.</summary>
    [JsonPropertyName("email")]
    public required string Email { get; init; }
}

/// <summary>
/// Request body for updateAccountHandle.
/// </summary>
public sealed class UpdateAccountHandleRequest
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required string Handle { get; init; }
}

/// <summary>
/// Request body for updateAccountPassword.
/// </summary>
public sealed class UpdateAccountPasswordRequest
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The account password.</summary>
    [JsonPropertyName("password")]
    public required string Password { get; init; }
}

/// <summary>
/// Request body for disableInviteCodes.
/// </summary>
public sealed class DisableInviteCodesRequest
{
    /// <summary>The invite codes.</summary>
    [JsonPropertyName("codes")]
    public List<string>? Codes { get; init; }

    /// <summary>The accounts.</summary>
    [JsonPropertyName("accounts")]
    public List<string>? Accounts { get; init; }
}

/// <summary>
/// Response from getInviteCodes.
/// </summary>
public sealed class GetInviteCodesResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The invite codes.</summary>
    [JsonPropertyName("codes")]
    public required List<JsonElement> Codes { get; init; }
}
