using System.Buffers;
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
    private readonly byte[] _encodedHeader;
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
        _encodedHeader = Jwt.EncodeHeader("JWT", signingKey.Curve);
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

        var payload = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            writer.WriteString("iss"u8, _serviceDid);
            writer.WriteString("aud"u8, audience);
            writer.WriteNumber("exp"u8, now.Add(exp).ToUnixTimeSeconds());
            writer.WriteNumber("iat"u8, now.ToUnixTimeSeconds());
            Jwt.WriteTokenId(writer);

            if (lxm is not null)
                writer.WriteString("lxm"u8, lxm);

            writer.WriteEndObject();
        }

        return Jwt.Sign(_encodedHeader, payload.WrittenSpan, _signingKey);
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
