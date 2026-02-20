using System.Security.Cryptography;
using System.Text;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// Generates PKCE (Proof Key for Code Exchange, RFC 7636) challenge and verifier pairs.
/// The AT Protocol OAuth spec mandates PKCE with the S256 challenge method.
/// </summary>
public static class PkceGenerator
{
    /// <summary>
    /// Generates a new PKCE code verifier (43–128 characters of URL-safe random bytes).
    /// </summary>
    public static string GenerateCodeVerifier()
    {
        // Generate 32 random bytes → 43 base64url characters
        var bytes = RandomNumberGenerator.GetBytes(32);
        return DPoPProofGenerator.Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Computes the S256 code challenge for the given verifier.
    /// <c>challenge = BASE64URL(SHA256(verifier))</c>
    /// </summary>
    /// <param name="codeVerifier">The PKCE code verifier.</param>
    /// <returns>The S256 code challenge.</returns>
    public static string ComputeCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return DPoPProofGenerator.Base64UrlEncode(hash);
    }

    /// <summary>
    /// Generates a random state parameter for OAuth authorization requests.
    /// </summary>
    public static string GenerateState()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return DPoPProofGenerator.Base64UrlEncode(bytes);
    }
}
