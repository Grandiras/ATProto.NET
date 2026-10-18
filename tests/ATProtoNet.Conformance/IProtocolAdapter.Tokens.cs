namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>
    /// Verifies inter-service auth tokens in order with one verifier and one replay store, each at
    /// its own time, as a receiver configured by <paramref name="expectations"/> does.
    /// </summary>
    IReadOnlyList<TokenVerdict> VerifyServiceAuth(IReadOnlyList<(string Token, DateTimeOffset Now)> steps, ServiceAuthExpectations expectations);

    /// <summary>The RFC 7638 thumbprint of a JWK, or <see langword="null"/> when it is not a complete EC key.</summary>
    string? DPoPThumbprint(string jwkJson);

    /// <summary>The DPoP <c>ath</c> of an access token.</summary>
    string DPoPAccessTokenHash(string accessToken);

    /// <summary>The DPoP <c>htu</c> for a request URL, or <see langword="null"/> when it is not an absolute URL with a host.</summary>
    string? DPoPHtu(string url);

    /// <summary>
    /// A DPoP proof for a request, signed with the given key (<paramref name="curve"/> <c>P-256</c>
    /// or <c>K-256</c>, the private scalar in hex), or <see langword="null"/> when it is refused.
    /// </summary>
    string? CreateDPoPProof(string curve, string privateKeyHex, string method, string url, string? nonce, string? accessToken);

    /// <summary>The PKCE S256 code challenge of a verifier.</summary>
    string PkceChallenge(string verifier);

    /// <summary>A fresh PKCE code verifier and OAuth state value.</summary>
    (string Verifier, string State) GeneratePkce();

    /// <summary>
    /// Validates the protected-resource metadata a PDS publishes and the authorization-server
    /// metadata it names, as the OAuth client does when it discovers the PDS's authorization server.
    /// </summary>
    MetadataVerdict ResolveAuthorizationServer(string pdsUrl, string resourceMetadataJson, string serverMetadataJson, bool allowPrivateNetworks);
}

/// <summary>What a service-auth receiver accepts.</summary>
/// <param name="Audiences">The accepted <c>aud</c> values.</param>
/// <param name="Method">The method the token must name in <c>lxm</c>, or <see langword="null"/> for none to bind.</param>
/// <param name="AllowedKeyIds">The verification-method fragments a <c>kid</c> may name.</param>
/// <param name="MaxLifetime">The longest lifetime accepted.</param>
/// <param name="ClockSkew">The clock skew allowed.</param>
/// <param name="Documents">The DID documents issuers resolve to (JSON), by DID.</param>
/// <param name="FreshDocuments">The documents a refetch returns, where they differ.</param>
public sealed record ServiceAuthExpectations(
    IReadOnlyList<string> Audiences,
    string? Method,
    IReadOnlyList<string> AllowedKeyIds,
    TimeSpan MaxLifetime,
    TimeSpan ClockSkew,
    IReadOnlyDictionary<string, string> Documents,
    IReadOnlyDictionary<string, string> FreshDocuments);

/// <summary>A token's verdict: the reference error name when refused, its claims when accepted.</summary>
public sealed record TokenVerdict(
    bool Accepted,
    string? Error = null,
    string? Issuer = null,
    string? Audience = null,
    string? Method = null,
    string? KeyId = null,
    string? TokenId = null,
    long? ExpiresAt = null);

/// <summary>The outcome of authorization-server discovery: the issuer, or the OAuth error.</summary>
public sealed record MetadataVerdict(bool Accepted, string? Issuer = null, string? Error = null);
