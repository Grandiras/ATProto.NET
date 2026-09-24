using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Lexicon.Tools.Ozone.Signature;

/// <summary>
/// A signature correlation result.
/// </summary>
public sealed class SigDetail : LexObject
{
    /// <summary>The name of the account property the signature was derived from.</summary>
    [JsonPropertyName("property")]
    public required string Property { get; init; }

    /// <summary>The record value.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

/// <summary>
/// An account with associated signatures.
/// </summary>
public sealed class AccountResult : LexObject
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required Did Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required Handle Handle { get; init; }

    /// <summary>Accounts sharing one or more signature values with this one.</summary>
    [JsonPropertyName("similarAccounts")]
    public IReadOnlyList<SigDetail>? SimilarAccounts { get; init; }
}

/// <summary>
/// A related account.
/// </summary>
public sealed class RelatedAccount : LexObject
{
    /// <summary>The related account.</summary>
    [JsonPropertyName("account")]
    public required AccountResult Account { get; init; }

    /// <summary>The signature values shared with the queried account.</summary>
    [JsonPropertyName("similarities")]
    public IReadOnlyList<SigDetail>? Similarities { get; init; }
}

/// <summary>
/// Response from findCorrelation.
/// </summary>
public sealed class FindCorrelationResponse
{
    /// <summary>The correlated signature values.</summary>
    [JsonPropertyName("details")]
    public required IReadOnlyList<SigDetail> Details { get; init; }
}

/// <summary>
/// Response from searchAccounts.
/// </summary>
public sealed class SearchAccountsResponse : ICursorPage<AccountResult>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The accounts.</summary>
    [JsonPropertyName("accounts")]
    public required IReadOnlyList<AccountResult> Accounts { get; init; }

    IReadOnlyList<AccountResult> ICursorPage<AccountResult>.Items => Accounts;
}

/// <summary>
/// Response from findRelatedAccounts.
/// </summary>
public sealed class FindRelatedAccountsResponse : ICursorPage<RelatedAccount>
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The accounts.</summary>
    [JsonPropertyName("accounts")]
    public required IReadOnlyList<RelatedAccount> Accounts { get; init; }

    IReadOnlyList<RelatedAccount> ICursorPage<RelatedAccount>.Items => Accounts;
}
