namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    /// <inheritdoc />
    public IReadOnlyList<TokenVerdict> VerifyServiceAuth(IReadOnlyList<(string Token, DateTimeOffset Now)> steps, ServiceAuthExpectations expectations) => throw Later(6);

    /// <inheritdoc />
    public string? DPoPThumbprint(string jwkJson) => throw Later(6);

    /// <inheritdoc />
    public string DPoPAccessTokenHash(string accessToken) => throw Later(6);

    /// <inheritdoc />
    public string? DPoPHtu(string url) => throw Later(6);

    /// <inheritdoc />
    public string? CreateDPoPProof(string curve, string privateKeyHex, string method, string url, string? nonce, string? accessToken) => throw Later(6);

    /// <inheritdoc />
    public string PkceChallenge(string verifier) => throw Later(6);

    /// <inheritdoc />
    public (string Verifier, string State) GeneratePkce() => throw Later(6);

    /// <inheritdoc />
    public MetadataVerdict ResolveAuthorizationServer(string pdsUrl, string resourceMetadataJson, string serverMetadataJson, bool allowPrivateNetworks) => throw Later(6);
}
