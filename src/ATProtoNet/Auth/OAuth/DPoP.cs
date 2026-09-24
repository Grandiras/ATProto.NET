using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ATProtoNet.Auth.OAuth;

/// <summary>
/// The parts of DPoP (<see href="https://www.rfc-editor.org/rfc/rfc9449">RFC 9449</see>) that
/// the proof generator and the proof validator must compute identically.
/// </summary>
internal static class DPoP
{
    /// <summary>The <c>typ</c> header every DPoP proof carries.</summary>
    public const string TokenType = "dpop+jwt";

    /// <summary>
    /// Normalizes a request URL to the <c>htu</c> form, <c>scheme://host[:port]/path</c>.
    /// Returns <see langword="null"/> for anything that is not an absolute URL naming a host.
    /// </summary>
    /// <param name="url">The request URL.</param>
    /// <remarks>
    /// <para>RFC 9449 section 4.2 defines <c>htu</c> as the request's target URI without query
    /// and fragment, and section 4.3 compares it against the request URI with both removed. A
    /// proof that named the full URL would not match, and every XRPC query carries a query
    /// string, so one proof covers any query on a given path.</para>
    /// <para>Userinfo is dropped as well: RFC 9110 section 4.2.4 forbids it in an http(s)
    /// target URI, so the URI a verifier compares against never carries it.</para>
    /// <para>The scheme and host are lower-cased and a default port is dropped, the syntax- and
    /// scheme-based normalization section 4.3 asks for, so a proof is not rejected over
    /// casing.</para>
    /// </remarks>
    public static string? NormalizeHtu(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return null;

        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
    }

    /// <summary>
    /// Computes the <c>ath</c> claim: the base64url SHA-256 hash of an access token.
    /// </summary>
    /// <param name="accessToken">The access token the proof accompanies.</param>
    public static string AccessTokenHash(string accessToken)
    {
        // RFC 9449 hashes the token's ASCII encoding; access tokens are ASCII, so UTF-8 is it.
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(accessToken), hash);
        return Base64Url.EncodeToString(hash);
    }

    /// <summary>
    /// Computes a JWK's thumbprint per <see href="https://www.rfc-editor.org/rfc/rfc7638">RFC
    /// 7638</see>: SHA-256 over the canonical JSON of the key's required members, in
    /// lexicographic order, base64url-encoded.
    /// </summary>
    /// <param name="jwk">An EC public key.</param>
    /// <exception cref="ArgumentException">The key is not an EC key with <c>crv</c>, <c>x</c> and <c>y</c>.</exception>
    /// <remarks>
    /// For an EC key the required members are exactly <c>crv</c>, <c>kty</c>, <c>x</c>, and
    /// <c>y</c>, so any other member a key carries (<c>kid</c>, <c>use</c>, <c>alg</c>) is
    /// excluded and cannot be used to make one key present two thumbprints.
    /// </remarks>
    public static string Thumbprint(JsonWebKey jwk)
    {
        ArgumentNullException.ThrowIfNull(jwk);

        if (jwk.Kty != "EC" || jwk.Crv is null || jwk.X is null || jwk.Y is null)
            throw new ArgumentException("An RFC 7638 thumbprint needs an EC key with crv, x and y.", nameof(jwk));

        var canonical = new ArrayBufferWriter<byte>(192);
        using (var writer = new Utf8JsonWriter(canonical))
        {
            writer.WriteStartObject();
            writer.WriteString("crv"u8, jwk.Crv);
            writer.WriteString("kty"u8, jwk.Kty);
            writer.WriteString("x"u8, jwk.X);
            writer.WriteString("y"u8, jwk.Y);
            writer.WriteEndObject();
        }

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(canonical.WrittenSpan, hash);
        return Base64Url.EncodeToString(hash);
    }
}
