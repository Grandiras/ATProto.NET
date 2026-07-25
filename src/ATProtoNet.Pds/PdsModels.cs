using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Pds;

/// <summary>
/// Result from creating a session or account.
/// </summary>
public sealed class PdsSessionResult
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("emailConfirmed")]
    public bool EmailConfirmed { get; init; }

    [JsonPropertyName("accessJwt")]
    public required string AccessJwt { get; init; }

    [JsonPropertyName("refreshJwt")]
    public required string RefreshJwt { get; init; }
}

/// <summary>
/// Session info returned by getSession.
/// </summary>
public sealed class PdsSessionInfo
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("emailConfirmed")]
    public bool EmailConfirmed { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; }
}

/// <summary>
/// Reference to a created/updated record.
/// </summary>
public sealed class PdsRecordRef
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }
}

/// <summary>
/// A record result with its value.
/// </summary>
public sealed class PdsRecordResult
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("value")]
    public required JsonElement Value { get; init; }
}

/// <summary>
/// Server description for describeServer.
/// </summary>
public sealed class PdsDescription
{
    [JsonPropertyName("inviteCodeRequired")]
    public bool InviteCodeRequired { get; init; }

    [JsonPropertyName("availableUserDomains")]
    public required List<string> AvailableUserDomains { get; init; }

    [JsonPropertyName("links")]
    public PdsLinks? Links { get; init; }

    [JsonPropertyName("contact")]
    public PdsContact? Contact { get; init; }
}

/// <summary>
/// Links provided by the PDS.
/// </summary>
public sealed class PdsLinks
{
    [JsonPropertyName("privacyPolicy")]
    public string? PrivacyPolicy { get; init; }

    [JsonPropertyName("termsOfService")]
    public string? TermsOfService { get; init; }
}

/// <summary>
/// Contact information for the PDS operator.
/// </summary>
public sealed class PdsContact
{
    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

/// <summary>
/// The invite codes issued to one account, as returned by
/// <c>com.atproto.server.createInviteCodes</c>.
/// </summary>
public sealed class PdsAccountInviteCodes
{
    /// <summary>The account the codes belong to.</summary>
    [JsonPropertyName("account")]
    public required string Account { get; init; }

    /// <summary>The generated codes.</summary>
    [JsonPropertyName("codes")]
    public required IReadOnlyList<string> Codes { get; init; }
}

/// <summary>
/// Wire representation of an invite code (<c>com.atproto.server.defs#inviteCode</c>).
/// </summary>
public sealed class PdsInviteCodeView
{
    /// <summary>The code itself.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>Total number of uses the code was issued with.</summary>
    [JsonPropertyName("available")]
    public required int Available { get; init; }

    /// <summary>Whether the code has been disabled.</summary>
    [JsonPropertyName("disabled")]
    public required bool Disabled { get; init; }

    /// <summary>The DID the code belongs to, or the creator for admin codes.</summary>
    [JsonPropertyName("forAccount")]
    public required string ForAccount { get; init; }

    /// <summary>Who created the code.</summary>
    [JsonPropertyName("createdBy")]
    public required string CreatedBy { get; init; }

    /// <summary>When the code was created (ISO 8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    /// <summary>Confirmed redemptions of the code.</summary>
    [JsonPropertyName("uses")]
    public required IReadOnlyList<PdsInviteCodeUseView> Uses { get; init; }

    /// <summary>Projects a stored code onto its wire shape.</summary>
    public static PdsInviteCodeView FromCode(PdsInviteCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return new PdsInviteCodeView
        {
            Code = code.Code,
            Available = code.AvailableUses,
            Disabled = code.Disabled,
            ForAccount = code.ForAccount ?? code.CreatedBy,
            CreatedBy = code.CreatedBy,
            CreatedAt = code.CreatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            Uses = [.. code.Uses.Select(u => new PdsInviteCodeUseView
            {
                UsedBy = u.UsedBy,
                UsedAt = u.UsedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            })],
        };
    }
}

/// <summary>
/// Wire representation of a single invite code redemption
/// (<c>com.atproto.server.defs#inviteCodeUse</c>).
/// </summary>
public sealed class PdsInviteCodeUseView
{
    /// <summary>The DID of the account that redeemed the code.</summary>
    [JsonPropertyName("usedBy")]
    public required string UsedBy { get; init; }

    /// <summary>When the code was redeemed (ISO 8601).</summary>
    [JsonPropertyName("usedAt")]
    public required string UsedAt { get; init; }
}

/// <summary>
/// Reference to an uploaded blob.
/// </summary>
public sealed class PdsBlobRef
{
    [JsonPropertyName("cid")]
    public required string Cid { get; init; }

    [JsonPropertyName("mimeType")]
    public required string MimeType { get; init; }

    [JsonPropertyName("size")]
    public required long Size { get; init; }
}
