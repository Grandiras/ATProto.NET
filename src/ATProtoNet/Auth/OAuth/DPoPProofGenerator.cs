using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using ATProtoNet.Crypto;

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
    private readonly AtProtoKey _key;
    private readonly bool _ownsKey = true;
    private readonly byte[] _encodedHeader;
    private readonly string _thumbprint;

    // The ath of the access token most recently proved for. A session presents the same token on
    // every request until it refreshes, so hashing it once per token rather than per request is
    // enough; the pair is swapped as one reference, so concurrent requests never mix them.
    private CachedAth? _lastAccessToken;
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
        : this(ECDsa.Create(ECCurve.NamedCurves.nistP256))
    {
    }

    /// <summary>
    /// Creates a DPoP proof generator from an existing exported key (for session resumption).
    /// </summary>
    /// <param name="exportedKey">The PKCS#8 private key bytes.</param>
    /// <exception cref="ArgumentException">The key is not a P-256 key.</exception>
    /// <exception cref="CryptographicException">The bytes are not a PKCS#8 private key.</exception>
    public DPoPProofGenerator(byte[] exportedKey)
        : this(ImportP256(exportedKey))
    {
    }

    private DPoPProofGenerator(ECDsa ecdsa)
    {
        var point = ecdsa.ExportParameters(includePrivateParameters: false).Q;
        var jwk = new JsonWebKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = Base64Url.EncodeToString(point.X),
            Y = Base64Url.EncodeToString(point.Y),
        };

        _key = new AtProtoKey(ecdsa, KeyCurve.P256);
        _thumbprint = DPoP.Thumbprint(jwk);
        _encodedHeader = Jwt.EncodeHeader(DPoP.TokenType, KeyCurve.P256, jwk: jwk);
    }

    /// <summary>
    /// A generator that signs with <paramref name="shared"/>'s key object without owning it:
    /// disposing it stops it signing and leaves the key to its owner.
    /// </summary>
    private DPoPProofGenerator(DPoPProofGenerator shared)
    {
        _key = shared._key;
        _ownsKey = false;
        _thumbprint = shared._thumbprint;
        _encodedHeader = shared._encodedHeader;
    }

    /// <summary>
    /// A generator over this one's key that its holder may dispose, as a client disposes the key
    /// of a session it lets go, while this one keeps signing: importing a key costs far more
    /// than signing with it, so a key shared by many short-lived clients is imported once.
    /// </summary>
    /// <exception cref="ObjectDisposedException">This generator is disposed.</exception>
    internal DPoPProofGenerator CreateView()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new DPoPProofGenerator(this);
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
    public byte[] ExportPrivateKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _key.ExportPrivateKey();
    }

    /// <summary>
    /// Generates a DPoP proof JWT for a token request to the Authorization Server.
    /// </summary>
    /// <param name="httpMethod">The HTTP method (e.g., "POST").</param>
    /// <param name="url">The full request URL. Its query, fragment and userinfo are left out of the proof.</param>
    /// <param name="nonce">The server-provided DPoP nonce, or null if not yet known.</param>
    /// <returns>The signed DPoP proof JWT string.</returns>
    /// <exception cref="ArgumentException"><paramref name="url"/> is not an absolute URL naming a host.</exception>
    public string GenerateProof(string httpMethod, string url, string? nonce = null)
    {
        return GenerateProof(httpMethod, url, nonce, accessTokenHash: null);
    }

    /// <summary>
    /// Generates a DPoP proof JWT for an authorized request to the Resource Server (PDS).
    /// Includes the access token hash (<c>ath</c>) field.
    /// </summary>
    /// <param name="httpMethod">The HTTP method (e.g., "GET", "POST").</param>
    /// <param name="url">The full request URL. Its query, fragment and userinfo are left out of the proof.</param>
    /// <param name="nonce">The server-provided DPoP nonce.</param>
    /// <param name="accessToken">The access token to include a hash of.</param>
    /// <returns>The signed DPoP proof JWT string.</returns>
    /// <exception cref="ArgumentException"><paramref name="url"/> is not an absolute URL naming a host.</exception>
    public string GenerateProofWithAccessToken(string httpMethod, string url, string? nonce, string accessToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);

        var cached = _lastAccessToken;
        if (cached is null || !string.Equals(cached.Token, accessToken, StringComparison.Ordinal))
            _lastAccessToken = cached = new CachedAth(accessToken, DPoP.AccessTokenHash(accessToken));

        return GenerateProof(httpMethod, url, nonce, cached.Hash);
    }

    private string GenerateProof(string httpMethod, string url, string? nonce, string? accessTokenHash)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(httpMethod);
        ArgumentNullException.ThrowIfNull(url);

        // A proof naming anything but an absolute URL matches no request, so it is refused here
        // rather than sent to fail at the server.
        var htu = DPoP.NormalizeHtu(url)
            ?? throw new ArgumentException($"'{url}' is not an absolute URL naming a host.", nameof(url));

        var payload = new ArrayBufferWriter<byte>(384);
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            Jwt.WriteTokenId(writer);
            writer.WriteString("htm"u8, httpMethod.ToUpperInvariant());
            writer.WriteString("htu"u8, htu);
            writer.WriteNumber("iat"u8, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            if (nonce is not null)
                writer.WriteString("nonce"u8, nonce);

            if (accessTokenHash is not null)
                writer.WriteString("ath"u8, accessTokenHash);

            writer.WriteEndObject();
        }

        return Jwt.Sign(_encodedHeader, payload.WrittenSpan, _key);
    }

    private static ECDsa ImportP256(byte[] exportedKey)
    {
        ArgumentNullException.ThrowIfNull(exportedKey);

        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportPkcs8PrivateKey(exportedKey, out _);

            // AT Protocol OAuth signs DPoP proofs with ES256 only. A key on another curve (a stored
            // K-256 repo key, say) would otherwise sign proofs whose header claims ES256 and P-256,
            // which no authorization server accepts.
            var curve = ecdsa.ExportParameters(includePrivateParameters: false).Curve.Oid?.Value;
            if (curve != ECCurve.NamedCurves.nistP256.Oid.Value)
            {
                throw new ArgumentException(
                    $"A DPoP key must be a P-256 key for ES256; this key's curve is {curve ?? "(unnamed)"}.",
                    nameof(exportedKey));
            }

            return ecdsa;
        }
        catch
        {
            ecdsa.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_ownsKey)
                _key.Dispose();
        }
    }

    private sealed record CachedAth(string Token, string Hash);
}
