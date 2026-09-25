using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Tools.Ozone.Verification;

/// <summary>
/// A verification the Ozone service issued, with its subject and issuer
/// (<c>tools.ozone.verification.defs#verificationView</c>).
/// </summary>
public sealed class VerificationView : LexObject
{
    /// <summary>The DID of the account that issued the verification.</summary>
    [JsonPropertyName("issuer")]
    public required Did Issuer { get; init; }

    /// <summary>The AT URI of the verification record.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The DID of the verified account.</summary>
    [JsonPropertyName("subject")]
    public required Did Subject { get; init; }

    /// <summary>
    /// The verified account's handle when it was verified. The verification holds only while the
    /// account's current handle still matches it.
    /// </summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>
    /// The verified account's display name when it was verified. The verification holds only
    /// while the current display name still matches it.
    /// </summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>When the verification was created.</summary>
    [JsonPropertyName("createdAt")]
    public required AtDatetime CreatedAt { get; init; }

    /// <summary>Why the verification was revoked; set only on a revoked one.</summary>
    [JsonPropertyName("revokeReason")]
    public string? RevokeReason { get; init; }

    /// <summary>When the verification was revoked.</summary>
    [JsonPropertyName("revokedAt")]
    public AtDatetime? RevokedAt { get; init; }

    /// <summary>The DID of the account that revoked the verification.</summary>
    [JsonPropertyName("revokedBy")]
    public Did? RevokedBy { get; init; }

    /// <summary>
    /// The verified account's profile view, in the shape the app view returns (an open union
    /// upstream declares no variants for).
    /// </summary>
    [JsonPropertyName("subjectProfile")]
    public JsonElement? SubjectProfile { get; init; }

    /// <summary>The issuer's profile view, in the shape the app view returns.</summary>
    [JsonPropertyName("issuerProfile")]
    public JsonElement? IssuerProfile { get; init; }

    /// <summary>
    /// The verified account as Ozone sees it: a <see cref="RepoViewDetail"/>, or a
    /// <see cref="RepoViewNotFound"/>.
    /// </summary>
    [JsonPropertyName("subjectRepo")]
    public ModerationSubjectView? SubjectRepo { get; init; }

    /// <summary>
    /// The issuer as Ozone sees it: a <see cref="RepoViewDetail"/>, or a
    /// <see cref="RepoViewNotFound"/>.
    /// </summary>
    [JsonPropertyName("issuerRepo")]
    public ModerationSubjectView? IssuerRepo { get; init; }
}

/// <summary>
/// An account to verify, for <see cref="VerificationClient.GrantVerificationsAsync"/>
/// (<c>tools.ozone.verification.grantVerifications#verificationInput</c>).
/// </summary>
public sealed class VerificationInput : LexObject
{
    /// <summary>The DID of the account to verify.</summary>
    [JsonPropertyName("subject")]
    public required Did Subject { get; init; }

    /// <summary>The account's current handle, which the verification is bound to.</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>The account's current display name, which the verification is bound to.</summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>The verification record's timestamp; the current time when unset.</summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; init; }
}

/// <summary>
/// An account that could not be verified
/// (<c>tools.ozone.verification.grantVerifications#grantError</c>).
/// </summary>
public sealed class GrantError : LexObject
{
    /// <summary>Why the verification failed.</summary>
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    /// <summary>The DID of the account.</summary>
    [JsonPropertyName("subject")]
    public required Did Subject { get; init; }
}

/// <summary>
/// A verification that could not be revoked
/// (<c>tools.ozone.verification.revokeVerifications#revokeError</c>).
/// </summary>
public sealed class RevokeError : LexObject
{
    /// <summary>The AT URI of the verification record.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>Why the revocation failed.</summary>
    [JsonPropertyName("error")]
    public required string Error { get; init; }
}

// ─── Request / Response Models ───

/// <summary>
/// Request body for tools.ozone.verification.grantVerifications.
/// </summary>
internal sealed class GrantVerificationsRequest
{
    /// <summary>The accounts to verify.</summary>
    [JsonPropertyName("verifications")]
    public required IReadOnlyList<VerificationInput> Verifications { get; init; }
}

/// <summary>
/// Response from tools.ozone.verification.grantVerifications.
/// </summary>
public sealed class GrantVerificationsResponse
{
    /// <summary>The verifications created.</summary>
    [JsonPropertyName("verifications")]
    public required IReadOnlyList<VerificationView> Verifications { get; init; }

    /// <summary>The accounts that could not be verified, and why.</summary>
    [JsonPropertyName("failedVerifications")]
    public required IReadOnlyList<GrantError> FailedVerifications { get; init; }
}

/// <summary>
/// Response from tools.ozone.verification.listVerifications.
/// </summary>
public sealed class ListVerificationsResponse : ICursorPage<VerificationView>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The verifications.</summary>
    [JsonPropertyName("verifications")]
    public required IReadOnlyList<VerificationView> Verifications { get; init; }

    IReadOnlyList<VerificationView> ICursorPage<VerificationView>.Items => Verifications;
}

/// <summary>
/// Request body for tools.ozone.verification.revokeVerifications.
/// </summary>
internal sealed class RevokeVerificationsRequest
{
    /// <summary>The verification records to revoke.</summary>
    [JsonPropertyName("uris")]
    public required IReadOnlyList<AtUri> Uris { get; init; }

    /// <summary>Why they are revoked.</summary>
    [JsonPropertyName("revokeReason")]
    public string? RevokeReason { get; init; }
}

/// <summary>
/// Response from tools.ozone.verification.revokeVerifications.
/// </summary>
public sealed class RevokeVerificationsResponse
{
    /// <summary>The verification records revoked.</summary>
    [JsonPropertyName("revokedVerifications")]
    public required IReadOnlyList<AtUri> RevokedVerifications { get; init; }

    /// <summary>The verifications that could not be revoked, and why.</summary>
    [JsonPropertyName("failedRevocations")]
    public required IReadOnlyList<RevokeError> FailedRevocations { get; init; }
}
