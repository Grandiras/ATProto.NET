using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ATProtoNet.Pds;

/// <summary>
/// Service for issuing and validating JWT session tokens for PDS authentication.
/// Uses HMAC-SHA256 for token signing.
/// </summary>
public sealed class PdsSessionService
{
    private readonly byte[] _signingKey;
    private readonly PdsOptions _options;

    /// <summary>
    /// Creates a new session service.
    /// </summary>
    /// <param name="options">PDS configuration options.</param>
    /// <param name="signingKey">
    /// Optional HMAC signing key bytes. If null, a random 32-byte key is generated.
    /// </param>
    public PdsSessionService(PdsOptions options, byte[]? signingKey = null)
    {
        _options = options;
        _signingKey = signingKey ?? RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>
    /// Issue an access token for the given DID and handle.
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="handle">The account handle.</param>
    /// <param name="expiration">Token lifetime. Default: 2 hours.</param>
    public string IssueAccessToken(string did, string handle, TimeSpan? expiration = null)
    {
        var lifetime = expiration ?? TimeSpan.FromHours(2);
        var payload = new TokenPayload
        {
            Sub = did,
            Handle = handle,
            Scope = "atproto",
            Iss = _options.ResolvedPublicUrl,
            Exp = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds(),
            Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        return CreateToken(payload);
    }

    /// <summary>
    /// Issue a refresh token for the given DID.
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="expiration">Token lifetime. Default: 90 days.</param>
    public string IssueRefreshToken(string did, TimeSpan? expiration = null)
    {
        var lifetime = expiration ?? TimeSpan.FromDays(90);
        var payload = new TokenPayload
        {
            Sub = did,
            Scope = "com.atproto.refresh",
            Iss = _options.ResolvedPublicUrl,
            Exp = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds(),
            Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        return CreateToken(payload);
    }

    /// <summary>
    /// Validate a token and return the parsed payload, or null if invalid.
    /// </summary>
    public TokenValidationResult? ValidateToken(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return null;

        // Verify signature
        var signatureInput = $"{parts[0]}.{parts[1]}";
        var expectedSig = ComputeSignature(Encoding.UTF8.GetBytes(signatureInput));
        var actualSig = parts[2];

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSig), Encoding.UTF8.GetBytes(actualSig)))
            return null;

        // Decode payload
        var payloadJson = Base64UrlDecode(parts[1]);
        var payload = JsonSerializer.Deserialize<TokenPayload>(payloadJson);
        if (payload is null) return null;

        // Check expiration
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > payload.Exp)
            return null;

        return new TokenValidationResult
        {
            Did = payload.Sub ?? "",
            Handle = payload.Handle,
            Scope = payload.Scope ?? "",
            IsValid = true,
        };
    }

    private string CreateToken(TokenPayload payload)
    {
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}"""u8);
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload);
        var payloadB64 = Base64UrlEncode(payloadJson);
        var signature = ComputeSignature(Encoding.UTF8.GetBytes($"{header}.{payloadB64}"));
        return $"{header}.{payloadB64}.{signature}";
    }

    private string ComputeSignature(byte[] input)
    {
        var hash = HMACSHA256.HashData(_signingKey, input);
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }

    internal sealed class TokenPayload
    {
        public string? Sub { get; set; }
        public string? Handle { get; set; }
        public string? Scope { get; set; }
        public string? Iss { get; set; }
        public long Exp { get; set; }
        public long Iat { get; set; }
    }
}

/// <summary>
/// Result from token validation.
/// </summary>
public sealed class TokenValidationResult
{
    /// <summary>The DID from the token.</summary>
    public required string Did { get; init; }

    /// <summary>The handle from the token (access tokens only).</summary>
    public string? Handle { get; init; }

    /// <summary>The scope of the token.</summary>
    public required string Scope { get; init; }

    /// <summary>Whether the token is valid.</summary>
    public required bool IsValid { get; init; }
}
