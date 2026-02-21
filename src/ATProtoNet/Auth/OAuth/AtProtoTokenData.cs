namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Serializable data representing a user's AT Protocol OAuth session,
/// including DPoP-bound tokens and the private key needed to generate proofs.
/// </summary>
/// <remarks>
/// <para>Stored via <see cref="IAtProtoTokenStore"/> and used by
/// <c>IAtProtoClientFactory</c> to reconstruct authenticated <see cref="AtProtoClient"/>
/// instances for backend API calls.</para>
/// <para><b>Security:</b> This data contains secrets (access token, refresh token,
/// DPoP private key). Store it encrypted at rest. Never log or transmit it
/// over unencrypted channels.</para>
/// </remarks>
public sealed class AtProtoTokenData
{
    /// <summary>The user's DID (decentralized identifier).</summary>
    public required string Did { get; init; }

    /// <summary>The user's AT Protocol handle.</summary>
    public required string Handle { get; init; }

    /// <summary>The DPoP-bound OAuth access token.</summary>
    public required string AccessToken { get; set; }

    /// <summary>The OAuth refresh token.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>The user's PDS (Personal Data Server) URL.</summary>
    public required string PdsUrl { get; init; }

    /// <summary>The Authorization Server issuer URL.</summary>
    public required string Issuer { get; init; }

    /// <summary>The token endpoint URL (for refresh).</summary>
    public required string TokenEndpoint { get; init; }

    /// <summary>
    /// The DPoP private key in PKCS#8 format.
    /// Used to reconstruct a <see cref="DPoPProofGenerator"/> for generating proof JWTs.
    /// </summary>
    /// <remarks>
    /// <b>Security warning:</b> This is an unencrypted private key.
    /// Implementations of <see cref="IAtProtoTokenStore"/> should encrypt this at rest.
    /// </remarks>
    public required byte[] DPoPPrivateKey { get; init; }

    /// <summary>The current DPoP nonce for the Authorization Server.</summary>
    public string? AuthServerDpopNonce { get; set; }

    /// <summary>The current DPoP nonce for the Resource Server (PDS).</summary>
    public string? ResourceServerDpopNonce { get; set; }

    /// <summary>When the access token was obtained.</summary>
    public DateTimeOffset TokenObtainedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Token expiration in seconds from <see cref="TokenObtainedAt"/>.</summary>
    public int? ExpiresIn { get; init; }

    /// <summary>The granted OAuth scopes.</summary>
    public string? Scope { get; init; }
}
