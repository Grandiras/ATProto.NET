using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Generates DPoP (Demonstrating Proof-of-Possession) proof JWTs as required by
/// the AT Protocol OAuth spec (RFC 9449). Each proof is a self-signed JWT using ES256 (P-256).
/// </summary>
/// <remarks>
/// A new DPoP keypair is generated per OAuth session and must not be shared across
/// sessions or devices. The same keypair is used for all requests within one session.
/// </remarks>
public sealed class DPoPProofGenerator : IDisposable
{
    private readonly ECDsa _key;
    private readonly string _publicJwkJson;
    private readonly string _thumbprint;
    private bool _disposed;

    /// <summary>
    /// The JWK thumbprint (base64url-encoded SHA-256 hash) of the DPoP public key.
    /// Used to bind tokens to this specific key.
    /// </summary>
    public string KeyThumbprint => _thumbprint;

    /// <summary>
    /// Creates a new DPoP proof generator with a freshly generated ES256 (P-256) keypair.
    /// </summary>
    public DPoPProofGenerator()
    {
        _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        _publicJwkJson = BuildPublicJwkJson(parameters);
        _thumbprint = ComputeJwkThumbprint(parameters);
    }

    /// <summary>
    /// Creates a DPoP proof generator from an existing exported key (for session resumption).
    /// </summary>
    /// <param name="exportedKey">The PKCS#8 private key bytes.</param>
    public DPoPProofGenerator(byte[] exportedKey)
    {
        _key = ECDsa.Create();
        _key.ImportPkcs8PrivateKey(exportedKey, out _);
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        _publicJwkJson = BuildPublicJwkJson(parameters);
        _thumbprint = ComputeJwkThumbprint(parameters);
    }

    /// <summary>
    /// Exports the private key in PKCS#8 format for persistence.
    /// </summary>
    /// <remarks>
    /// <b>Security warning:</b> The exported key bytes are unencrypted. Store them in a
    /// secure location (e.g. OS keychain, encrypted database, DPAPI-protected storage).
    /// Never log, transmit over unencrypted channels, or store in plain text.
    /// Compromise of this key allows an attacker to use the DPoP-bound tokens.
    /// </remarks>
    public byte[] ExportPrivateKey() => _key.ExportPkcs8PrivateKey();

    /// <summary>
    /// Generates a DPoP proof JWT for a token request to the Authorization Server.
    /// </summary>
    /// <param name="httpMethod">The HTTP method (e.g., "POST").</param>
    /// <param name="url">The full request URL.</param>
    /// <param name="nonce">The server-provided DPoP nonce, or null if not yet known.</param>
    /// <returns>The signed DPoP proof JWT string.</returns>
    public string GenerateProof(string httpMethod, string url, string? nonce = null)
    {
        return GenerateProof(httpMethod, url, nonce, accessTokenHash: null);
    }

    /// <summary>
    /// Generates a DPoP proof JWT for an authorized request to the Resource Server (PDS).
    /// Includes the access token hash (<c>ath</c>) field.
    /// </summary>
    /// <param name="httpMethod">The HTTP method (e.g., "GET", "POST").</param>
    /// <param name="url">The full request URL.</param>
    /// <param name="nonce">The server-provided DPoP nonce.</param>
    /// <param name="accessToken">The access token to include a hash of.</param>
    /// <returns>The signed DPoP proof JWT string.</returns>
    public string GenerateProofWithAccessToken(string httpMethod, string url, string? nonce, string accessToken)
    {
        var ath = ComputeS256Hash(accessToken);
        return GenerateProof(httpMethod, url, nonce, ath);
    }

    private string GenerateProof(string httpMethod, string url, string? nonce, string? accessTokenHash)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // JWT Header
        var header = new Dictionary<string, object>
        {
            ["typ"] = "dpop+jwt",
            ["alg"] = "ES256",
            ["jwk"] = JsonSerializer.Deserialize<Dictionary<string, object>>(_publicJwkJson)!,
        };

        // JWT Payload
        var payload = new Dictionary<string, object>
        {
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["htm"] = httpMethod.ToUpperInvariant(),
            ["htu"] = url,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        if (nonce is not null)
            payload["nonce"] = nonce;

        if (accessTokenHash is not null)
            payload["ath"] = accessTokenHash;

        return SignJwt(header, payload);
    }

    private string SignJwt(Dictionary<string, object> header, Dictionary<string, object> payload)
    {
        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var signingInput = $"{headerB64}.{payloadB64}";
        var signatureBytes = _key.SignData(
            Encoding.UTF8.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var signatureB64 = Base64UrlEncode(signatureBytes);

        return $"{headerB64}.{payloadB64}.{signatureB64}";
    }

    private static string BuildPublicJwkJson(ECParameters parameters)
    {
        var jwk = new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64UrlEncode(parameters.Q.X!),
            ["y"] = Base64UrlEncode(parameters.Q.Y!),
        };
        return JsonSerializer.Serialize(jwk);
    }

    /// <summary>
    /// Computes the JWK Thumbprint per RFC 7638 using SHA-256.
    /// For EC keys, the thumbprint input is: {"crv":"P-256","kty":"EC","x":"...","y":"..."}
    /// (members sorted lexicographically).
    /// </summary>
    private static string ComputeJwkThumbprint(ECParameters parameters)
    {
        // RFC 7638: members must be sorted lexicographically
        var thumbprintInput = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["crv"] = "P-256",
            ["kty"] = "EC",
            ["x"] = Base64UrlEncode(parameters.Q.X!),
            ["y"] = Base64UrlEncode(parameters.Q.Y!),
        });

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(thumbprintInput));
        return Base64UrlEncode(hash);
    }

    /// <summary>
    /// Computes the S256 hash of a string (used for PKCE and access token hashing).
    /// </summary>
    internal static string ComputeS256Hash(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Base64UrlEncode(hash);
    }

    /// <summary>
    /// Base64url-encodes a byte array (no padding).
    /// </summary>
    internal static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _key.Dispose();
        }
    }
}
