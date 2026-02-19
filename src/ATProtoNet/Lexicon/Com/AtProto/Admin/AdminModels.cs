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
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("emailConfirmedAt")]
    public string? EmailConfirmedAt { get; init; }

    [JsonPropertyName("indexedAt")]
    public required string IndexedAt { get; init; }

    [JsonPropertyName("invitedBy")]
    public JsonElement? InvitedBy { get; init; }

    [JsonPropertyName("invites")]
    public List<JsonElement>? Invites { get; init; }

    [JsonPropertyName("invitesDisabled")]
    public bool? InvitesDisabled { get; init; }

    [JsonPropertyName("relatedRecords")]
    public List<JsonElement>? RelatedRecords { get; init; }

    [JsonPropertyName("deactivatedAt")]
    public string? DeactivatedAt { get; init; }

    [JsonPropertyName("threatSignatures")]
    public List<ThreatSignature>? ThreatSignatures { get; init; }
}

/// <summary>
/// A threat signature associated with an account.
/// </summary>
public sealed class ThreatSignature
{
    [JsonPropertyName("property")]
    public required string Property { get; init; }

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
    [JsonPropertyName("subject")]
    public required JsonElement Subject { get; init; }

    [JsonPropertyName("takedown")]
    public SubjectStatusDetail? Takedown { get; init; }

    [JsonPropertyName("deactivated")]
    public SubjectStatusDetail? Deactivated { get; init; }
}

/// <summary>
/// Detailed status information for a subject (takedown, deactivated, etc.).
/// </summary>
public sealed class SubjectStatusDetail
{
    [JsonPropertyName("applied")]
    public bool Applied { get; init; }

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
    [JsonPropertyName("subject")]
    public required JsonElement Subject { get; init; }

    [JsonPropertyName("takedown")]
    public SubjectStatusDetail? Takedown { get; init; }

    [JsonPropertyName("deactivated")]
    public SubjectStatusDetail? Deactivated { get; init; }
}

/// <summary>
/// Response from updateSubjectStatus.
/// </summary>
public sealed class UpdateSubjectStatusResponse
{
    [JsonPropertyName("subject")]
    public required JsonElement Subject { get; init; }

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
    [JsonPropertyName("recipientDid")]
    public required string RecipientDid { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("senderDid")]
    public required string SenderDid { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

/// <summary>
/// Response from sendEmail.
/// </summary>
public sealed class SendEmailResponse
{
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
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>
/// Request body for disableAccountInvites.
/// </summary>
public sealed class DisableAccountInvitesRequest
{
    [JsonPropertyName("account")]
    public required string Account { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

/// <summary>
/// Request body for enableAccountInvites.
/// </summary>
public sealed class EnableAccountInvitesRequest
{
    [JsonPropertyName("account")]
    public required string Account { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

/// <summary>
/// Request body for updateAccountEmail.
/// </summary>
public sealed class UpdateAccountEmailRequest
{
    [JsonPropertyName("account")]
    public required string Account { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }
}

/// <summary>
/// Request body for updateAccountHandle.
/// </summary>
public sealed class UpdateAccountHandleRequest
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }
}

/// <summary>
/// Request body for updateAccountPassword.
/// </summary>
public sealed class UpdateAccountPasswordRequest
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("password")]
    public required string Password { get; init; }
}

/// <summary>
/// Request body for disableInviteCodes.
/// </summary>
public sealed class DisableInviteCodesRequest
{
    [JsonPropertyName("codes")]
    public List<string>? Codes { get; init; }

    [JsonPropertyName("accounts")]
    public List<string>? Accounts { get; init; }
}

/// <summary>
/// Response from getInviteCodes.
/// </summary>
public sealed class GetInviteCodesResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("codes")]
    public required List<JsonElement> Codes { get; init; }
}
