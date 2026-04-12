using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Tools.Ozone.Signature;

/// <summary>
/// A signature correlation result.
/// </summary>
public sealed class SigDetail
{
    [JsonPropertyName("property")]
    public required string Property { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

/// <summary>
/// An account with associated signatures.
/// </summary>
public sealed class AccountResult
{
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("similarAccounts")]
    public List<SigDetail>? SimilarAccounts { get; init; }
}

/// <summary>
/// A related account.
/// </summary>
public sealed class RelatedAccount
{
    [JsonPropertyName("account")]
    public required AccountResult Account { get; init; }

    [JsonPropertyName("similarities")]
    public List<SigDetail>? Similarities { get; init; }
}

/// <summary>
/// Response from findCorrelation.
/// </summary>
public sealed class FindCorrelationResponse
{
    [JsonPropertyName("details")]
    public required List<SigDetail> Details { get; init; }
}

/// <summary>
/// Response from searchAccounts.
/// </summary>
public sealed class SearchAccountsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("accounts")]
    public required List<AccountResult> Accounts { get; init; }
}

/// <summary>
/// Response from findRelatedAccounts.
/// </summary>
public sealed class FindRelatedAccountsResponse
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("accounts")]
    public required List<RelatedAccount> Accounts { get; init; }
}
