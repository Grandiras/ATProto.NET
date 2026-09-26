using System.Buffers;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;

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
/// <para>Every token carries <c>iss</c> (this service's DID, never with a fragment), <c>aud</c>,
/// <c>lxm</c>, <c>iat</c>, <c>exp</c> and a fresh <c>jti</c>, as the
/// <see href="https://atproto.com/specs/xrpc#inter-service-authentication-jwt">service auth
/// spec</see> requires since its 2026 revision
/// (<see href="https://github.com/bluesky-social/proposals/tree/main/0014-service-auth-revised">proposal
/// 0014</see>).</para>
/// </remarks>
public sealed class ServiceAuthGenerator : IDisposable
{
    private readonly AtProtoKey _signingKey;
    private readonly Did _serviceDid;
    private readonly byte[] _encodedHeader;
    private bool _disposed;

    /// <summary>The DID of the service generating tokens.</summary>
    public Did ServiceDid => _serviceDid;

    /// <summary>
    /// The verification method the tokens name in their <c>kid</c> header, or
    /// <see langword="null"/> when they carry none and so mean <c>#atproto</c>.
    /// </summary>
    public string? KeyId { get; }

    /// <summary>
    /// Creates a new service auth generator.
    /// </summary>
    /// <param name="serviceDid">
    /// The DID of this service (the <c>iss</c> claim). A <see cref="Did"/> never carries a
    /// fragment, which the spec no longer allows in <c>iss</c>; name a key other than
    /// <c>#atproto</c> with <paramref name="keyId"/> instead.
    /// </param>
    /// <param name="signingKey">The signing key. P-256 uses ES256, K-256 uses ES256K.</param>
    /// <param name="keyId">
    /// The verification method in <paramref name="serviceDid"/>'s DID document that
    /// <paramref name="signingKey"/> is published under, sent as the <c>kid</c> header: a fragment
    /// with its <c>#</c> and without the DID, e.g. <c>#atproto_label</c>. Leave it
    /// <see langword="null"/> for <c>#atproto</c>, the default a receiver assumes, which is also
    /// the only key most receivers accept.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="keyId"/> is not a <c>#fragment</c>.</exception>
    public ServiceAuthGenerator(Did serviceDid, AtProtoKey signingKey, string? keyId = null)
    {
        _serviceDid = serviceDid ?? throw new ArgumentNullException(nameof(serviceDid));
        _signingKey = signingKey ?? throw new ArgumentNullException(nameof(signingKey));

        if (keyId is not null && !ServiceAuthSyntax.IsKeyId(keyId))
        {
            throw new ArgumentException(
                $"A service auth key ID is a verification-method fragment such as '#atproto'; got '{keyId}'.",
                nameof(keyId));
        }

        KeyId = keyId;
        _encodedHeader = Jwt.EncodeHeader("JWT", signingKey.Curve, keyId);
    }

    /// <summary>
    /// Creates a service auth token for calling an XRPC method on another AT Protocol service.
    /// </summary>
    /// <param name="audience">
    /// The target service (<c>aud</c>): its DID followed by the fragment naming the service entry
    /// in its DID document, e.g. <c>did:web:feed.example.com#bsky_fg</c> or
    /// <c>did:web:api.bsky.app#bsky_appview</c>. A bare DID is still accepted, since some
    /// receivers only understand that form so far, but it is deprecated: it names no service,
    /// and a receiver may refuse it.
    /// </param>
    /// <param name="lxm">
    /// The XRPC method being called (e.g., <c>com.atproto.repo.getRecord</c>). The token is valid
    /// for that method only.
    /// </param>
    /// <param name="expiresIn">Token lifetime. Defaults to 60 seconds. Maximum is 5 minutes.</param>
    /// <returns>A signed JWT string suitable for the <c>Authorization: Bearer</c> header.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the generator has been disposed.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="audience"/> is not a DID with an optional <c>#</c> service fragment.
    /// </exception>
    public string CreateToken(string audience, Nsid lxm, TimeSpan? expiresIn = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentNullException.ThrowIfNull(lxm);

        if (!ServiceAuthSyntax.IsAudience(audience))
        {
            throw new ArgumentException(
                $"A service auth audience is a DID with an optional service fragment; got '{audience}'.",
                nameof(audience));
        }

        var exp = expiresIn ?? TimeSpan.FromSeconds(60);
        if (exp > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(expiresIn), "Service auth tokens cannot exceed 5 minutes.");

        var now = DateTimeOffset.UtcNow;

        var payload = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            writer.WriteString("iss"u8, _serviceDid.Value);
            writer.WriteString("aud"u8, audience);
            writer.WriteNumber("exp"u8, now.Add(exp).ToUnixTimeSeconds());
            writer.WriteNumber("iat"u8, now.ToUnixTimeSeconds());
            Jwt.WriteTokenId(writer);
            writer.WriteString("lxm"u8, lxm.Value);
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
