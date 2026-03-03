using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Crypto;

namespace ATProtoNet.Auth;

/// <summary>
/// Generates inter-service authentication JWTs for AT Protocol service-to-service communication.
/// <para>
/// Used by Feed Generators, Labelers, and other services to authenticate requests to PDSes
/// and other AT Protocol services. Tokens are short-lived (60 seconds) and scoped to a
/// specific logical method (<c>lxm</c>).
/// </para>
/// </summary>
/// <remarks>
/// See: https://atproto.com/specs/xrpc#service-auth
/// </remarks>
public sealed class ServiceAuthGenerator : IDisposable
{
    private readonly AtProtoKey _signingKey;
    private readonly string _serviceDid;
    private readonly string _algorithm;
    private bool _disposed;

    /// <summary>The DID of the service generating tokens.</summary>
    public string ServiceDid => _serviceDid;

    /// <summary>
    /// Creates a new service auth generator.
    /// </summary>
    /// <param name="serviceDid">The DID of this service (the <c>iss</c> claim).</param>
    /// <param name="signingKey">The signing key. P-256 uses ES256, K-256 uses ES256K.</param>
    public ServiceAuthGenerator(string serviceDid, AtProtoKey signingKey)
    {
        _serviceDid = serviceDid ?? throw new ArgumentNullException(nameof(serviceDid));
        _signingKey = signingKey ?? throw new ArgumentNullException(nameof(signingKey));
        _algorithm = signingKey.Curve == KeyCurve.P256 ? "ES256" : "ES256K";
    }

    /// <summary>
    /// Creates a service auth token for authenticating to another AT Protocol service.
    /// </summary>
    /// <param name="audience">The DID of the target service (<c>aud</c>).</param>
    /// <param name="lxm">The XRPC method being called (e.g., <c>com.atproto.repo.getRecord</c>).</param>
    /// <param name="expiresIn">Token lifetime. Defaults to 60 seconds. Maximum is 5 minutes.</param>
    /// <returns>A signed JWT string suitable for the <c>Authorization: Bearer</c> header.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the generator has been disposed.</exception>
    public string CreateToken(string audience, string? lxm = null, TimeSpan? expiresIn = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        var exp = expiresIn ?? TimeSpan.FromSeconds(60);
        if (exp > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(expiresIn), "Service auth tokens cannot exceed 5 minutes.");

        var now = DateTimeOffset.UtcNow;

        // JWT Header
        var header = new Dictionary<string, object>
        {
            ["typ"] = "JWT",
            ["alg"] = _algorithm,
        };

        // JWT Payload
        var payload = new Dictionary<string, object>
        {
            ["iss"] = _serviceDid,
            ["aud"] = audience,
            ["exp"] = now.Add(exp).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
        };

        if (lxm is not null)
            payload["lxm"] = lxm;

        return SignJwt(header, payload);
    }

    private string SignJwt(Dictionary<string, object> header, Dictionary<string, object> payload)
    {
        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var signingInput = Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}");
        var signature = _signingKey.Sign(signingInput);
        var signatureB64 = Base64UrlEncode(signature);

        return $"{headerB64}.{payloadB64}.{signatureB64}";
    }

    private static string Base64UrlEncode(byte[] data)
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
            _signingKey.Dispose();
        }
    }
}
