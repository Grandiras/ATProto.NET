using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Signature;

/// <summary>
/// A signature correlation result.
/// </summary>
public sealed class SigDetail
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
public sealed class AccountResult
{
    /// <summary>The DID (decentralized identifier) of the account.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>The handle of the account (e.g. <c>alice.bsky.social</c>).</summary>
    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    /// <summary>Accounts sharing one or more signature values with this one.</summary>
    [JsonPropertyName("similarAccounts")]
    public List<SigDetail>? SimilarAccounts { get; init; }
}

/// <summary>
/// A related account.
/// </summary>
public sealed class RelatedAccount
{
    /// <summary>The related account.</summary>
    [JsonPropertyName("account")]
    public required AccountResult Account { get; init; }

    /// <summary>The signature values shared with the queried account.</summary>
    [JsonPropertyName("similarities")]
    public List<SigDetail>? Similarities { get; init; }
}

/// <summary>
/// Response from findCorrelation.
/// </summary>
public sealed class FindCorrelationResponse
{
    /// <summary>The correlated signature values.</summary>
    [JsonPropertyName("details")]
    public required List<SigDetail> Details { get; init; }
}

/// <summary>
/// Response from searchAccounts.
/// </summary>
public sealed class SearchAccountsResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The accounts.</summary>
    [JsonPropertyName("accounts")]
    public required List<AccountResult> Accounts { get; init; }
}

/// <summary>
/// Response from findRelatedAccounts.
/// </summary>
public sealed class FindRelatedAccountsResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The accounts.</summary>
    [JsonPropertyName("accounts")]
    public required List<RelatedAccount> Accounts { get; init; }
}
