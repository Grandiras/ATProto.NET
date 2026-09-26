using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using ATProtoNet.Crypto;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// A key a confidential client authenticates to authorization servers with: it signs the ES256
/// client assertion (<c>private_key_jwt</c>, RFC 7523) sent with every pushed authorization,
/// token, refresh and revocation request.
/// </summary>
/// <remarks>
/// <para>A confidential client lists its keys in <see cref="OAuthOptions.ClientKeys"/> and publishes
/// their public halves in its client metadata, inline as <see cref="OAuthClientMetadata.Jwks"/>
/// (see <see cref="CreateKeySet"/>) or at <see cref="OAuthClientMetadata.JwksUri"/>. The reference
/// authorization server grants such clients far longer sessions than public ones.</para>
/// <para>Each session remembers the key its grant was authenticated with
/// (<see cref="Auth.OAuthSession.ClientKeyId"/>), and the authorization server expects every later
/// request for it to be signed by that same key. To rotate, add the new key first in
/// <see cref="OAuthOptions.ClientKeys"/> and keep publishing and configuring the old one until the
/// sessions issued with it have ended; a session whose key is gone can no longer be refreshed.</para>
/// <para>The key is the caller's: an <see cref="OAuthClient"/> does not dispose it.</para>
/// </remarks>
public sealed class OAuthClientKey : IDisposable
{
    /// <summary>How long a client assertion is valid.</summary>
    internal static readonly TimeSpan AssertionLifetime = TimeSpan.FromSeconds(60);

    /// <summary>The only algorithm AT Protocol client assertions use.</summary>
    internal const string Algorithm = "ES256";

    private readonly AtProtoKey _key;
    private readonly byte[] _encodedHeader;
    private readonly string _x;
    private readonly string _y;

    private OAuthClientKey(string keyId, AtProtoKey key)
    {
        try
        {
            if (key.Curve != KeyCurve.P256)
                throw new ArgumentException("A client key must be a P-256 key: client assertions are ES256.", nameof(key));

            // A public key cannot sign; asking for the private scalar is the only way to tell.
            var parameters = key.ExportParameters(includePrivateParameters: true);
            CryptographicOperations.ZeroMemory(parameters.D);

            KeyId = keyId;
            _key = key;
            _x = Base64Url.EncodeToString(parameters.Q.X);
            _y = Base64Url.EncodeToString(parameters.Q.Y);
            _encodedHeader = EncodeHeader(keyId);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The key id (<c>kid</c>) the key is published under and names in its assertions' headers.
    /// </summary>
    public string KeyId { get; }

    /// <summary>
    /// The public key as a JWK to publish in the client metadata: <c>kty</c> <c>EC</c>,
    /// <c>crv</c> <c>P-256</c>, <c>use</c> <c>sig</c>, <c>alg</c> <c>ES256</c> and the
    /// <see cref="KeyId"/>. A new instance on every read.
    /// </summary>
    public JsonWebKey PublicJwk => new()
    {
        Kty = "EC",
        Crv = "P-256",
        X = _x,
        Y = _y,
        Kid = KeyId,
        Use = "sig",
        Alg = Algorithm,
    };

    /// <summary>Generates a new P-256 client key.</summary>
    /// <param name="keyId">The key id to publish it under.</param>
    /// <returns>The key. Persist it with <see cref="ExportPrivateKey"/>: sessions stay bound to it.</returns>
    /// <exception cref="ArgumentException"><paramref name="keyId"/> is empty.</exception>
    public static OAuthClientKey Generate(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        return new OAuthClientKey(keyId, AtProtoCrypto.GenerateP256Key());
    }

    /// <summary>Imports a P-256 client key from its PKCS#8 private key.</summary>
    /// <param name="keyId">The key id the key is published under.</param>
    /// <param name="pkcs8PrivateKey">The PKCS#8-encoded private key.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="keyId"/> is empty, or the key is not a P-256 key.
    /// </exception>
    /// <exception cref="CryptographicException">The bytes are not a PKCS#8 private key.</exception>
    public static OAuthClientKey Import(string keyId, ReadOnlySpan<byte> pkcs8PrivateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        return new OAuthClientKey(keyId, AtProtoCrypto.ImportPrivateKey(pkcs8PrivateKey, KeyCurve.P256));
    }

    /// <summary>
    /// Builds the JWK set to publish as <see cref="OAuthClientMetadata.Jwks"/>, or to serve at
    /// <see cref="OAuthClientMetadata.JwksUri"/>: the public halves of <paramref name="keys"/>.
    /// </summary>
    /// <param name="keys">The client keys.</param>
    /// <returns>A new key set.</returns>
    public static JsonWebKeySet CreateKeySet(IEnumerable<OAuthClientKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return new JsonWebKeySet { Keys = [.. keys.Select(key => key.PublicJwk)] };
    }

    /// <summary>Exports the private key in PKCS#8 form, to persist a generated key.</summary>
    /// <remarks>
    /// <b>Security:</b> the bytes are unencrypted, and anyone holding them can authenticate as
    /// the client. Store them as you would any server secret.
    /// </remarks>
    public byte[] ExportPrivateKey() => _key.ExportPrivateKey();

    /// <summary>
    /// Signs a client assertion (RFC 7523 section 3, as the AT Protocol profile uses it):
    /// <c>iss</c> and <c>sub</c> the client id, <c>aud</c> the authorization server's issuer, a
    /// fresh <c>jti</c>, and an <c>exp</c> <see cref="AssertionLifetime"/> after <c>iat</c>.
    /// </summary>
    /// <param name="clientId">The client id.</param>
    /// <param name="audience">The issuer of the authorization server the assertion is for.</param>
    /// <param name="now">The time the assertion is issued at.</param>
    internal string CreateAssertion(string clientId, string audience, DateTimeOffset now)
    {
        var issuedAt = now.ToUnixTimeSeconds();
        var payload = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            writer.WriteString("iss"u8, clientId);
            writer.WriteString("sub"u8, clientId);
            writer.WriteString("aud"u8, audience);
            Jwt.WriteTokenId(writer);
            writer.WriteNumber("iat"u8, issuedAt);
            writer.WriteNumber("exp"u8, issuedAt + (long)AssertionLifetime.TotalSeconds);
            writer.WriteEndObject();
        }

        return Jwt.Sign(_encodedHeader, payload.WrittenSpan, _key);
    }

    /// <summary>Whether a published JWK is this key's public half.</summary>
    internal bool Matches(JsonWebKey jwk) =>
        jwk.Kty == "EC" && jwk.Crv == "P-256" &&
        string.Equals(jwk.X, _x, StringComparison.Ordinal) &&
        string.Equals(jwk.Y, _y, StringComparison.Ordinal);

    // The reference client sends only alg and kid: RFC 7523 defines no typ for client assertions.
    private static byte[] EncodeHeader(string keyId)
    {
        var json = new ArrayBufferWriter<byte>(128);
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            writer.WriteString("alg"u8, Algorithm);
            writer.WriteString("kid"u8, keyId);
            writer.WriteEndObject();
        }

        var encoded = new byte[Base64Url.GetEncodedLength(json.WrittenCount)];
        Base64Url.EncodeToUtf8(json.WrittenSpan, encoded);
        return encoded;
    }

    /// <inheritdoc/>
    public void Dispose() => _key.Dispose();
}
