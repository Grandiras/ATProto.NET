using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

using ATProtoNet.Data;

namespace ATProtoNet.Crypto.Jose;

/// <summary>Compact JWS (RFC 7515) with the two atproto algorithms, <c>ES256</c> and <c>ES256K</c>; claim-agnostic.</summary>
/// <remarks>
/// Which claims a token carries, its clock rules and its <c>jti</c> belong to each token type; only the encoding and
/// the signature live here. See <see href="https://www.rfc-editor.org/rfc/rfc7515"/> and <see href="https://www.rfc-editor.org/rfc/rfc8812"/>.
/// </remarks>
public static class Jws
{
    /// <summary>The longest token <see cref="TryDecode"/> reads, in characters.</summary>
    public const int MaxTokenLength = 16 * 1024;


    /// <summary>The JWS algorithm a key on <paramref name="curve"/> signs with.</summary>
    /// <param name="curve">The curve.</param>
    /// <returns><c>ES256</c> for P-256, <c>ES256K</c> for K-256.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="curve"/> is not a defined curve.</exception>
    public static string AlgorithmFor(KeyCurve curve) => Curve.Of(curve).JwsAlgorithm;

    /// <summary>Signs a compact JWS: <c>base64url(header).base64url(payload).base64url(signature)</c>, unpadded.</summary>
    /// <param name="header">The header; its algorithm must be the key's, and an embedded <c>jwk</c> is written public.</param>
    /// <param name="payloadJson">The payload, UTF-8 JSON.</param>
    /// <param name="key">The signing key.</param>
    /// <returns>The token.</returns>
    /// <exception cref="ArgumentException">The header's algorithm is not <see cref="AlgorithmFor"/> the key's curve.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="key"/> is disposed.</exception>
    public static string Sign(JwsHeader header, ReadOnlySpan<byte> payloadJson, SigningKey key)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(key);
        if (header.Algorithm != AlgorithmFor(key.Curve))
            throw new ArgumentException($"A {key.Curve} key signs {AlgorithmFor(key.Curve)}, not {header.Algorithm}.", nameof(header));
        var headerJson = JsonSerializer.SerializeToUtf8Bytes(header with { Jwk = header.Jwk?.ToPublic() }, JoseJsonContext.Default.JwsHeader);
        var signingLength = Base64Url.GetEncodedLength(headerJson.Length) + 1 + Base64Url.GetEncodedLength(payloadJson.Length);
        var token = new byte[signingLength + 1 + Base64Url.GetEncodedLength(VerificationKey.SignatureLength)];
        var at = Base64Url.EncodeToUtf8(headerJson, token);
        token[at++] = (byte)'.';
        Base64Url.EncodeToUtf8(payloadJson, token.AsSpan(at));
        Span<byte> signature = stackalloc byte[VerificationKey.SignatureLength];
        key.Sign(token.AsSpan(0, signingLength), signature);
        token[signingLength] = (byte)'.';
        Base64Url.EncodeToUtf8(signature, token.AsSpan(signingLength + 1));
        return Encoding.ASCII.GetString(token);
    }

    /// <summary>Splits and decodes a compact JWS without verifying it; never throws for a malformed token.</summary>
    /// <param name="token">The token.</param>
    /// <param name="jws">The decoded token, on success.</param>
    /// <param name="error">Why the token is malformed, on failure.</param>
    /// <returns>
    /// Whether the token is at most <see cref="MaxTokenLength"/> characters in exactly three base64url parts (trailing
    /// <c>=</c> tolerated, nothing else outside the URL-safe alphabet), the header and payload are JSON objects
    /// (the header with a string <c>alg</c>, no <c>crit</c>, and a valid EC public key as any <c>jwk</c>) and the
    /// signature is 64 bytes.
    /// </returns>
    public static bool TryDecode(string token, out DecodedJws jws, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(token);
        jws = default;
        var first = token.IndexOf('.', StringComparison.Ordinal);
        var second = first < 0 ? -1 : token.IndexOf('.', first + 1);
        error = token.Length > MaxTokenLength ? $"the token is longer than {MaxTokenLength} characters"
            : second < 0 || token.IndexOf('.', second + 1) >= 0 ? "expected three parts"
            : null;
        if (error is not null
            || !TryDecodeObject(token.AsSpan(0, first), "header", out var headerJson, out error)
            || !TryDecodeObject(token.AsSpan(first + 1, second - first - 1), "payload", out var payload, out error))
            return false;
        if (!TryDecodeBase64Url(token.AsSpan(second + 1), out var signature) || signature.Length != VerificationKey.SignatureLength)
        {
            error = "the signature is not 64 bytes of base64url";
            return false;
        }
        JwsHeader? header;
        try
        {
            header = headerJson.Deserialize(JoseJsonContext.Default.JwsHeader);
        }
        catch (JsonException e)
        {
            error = $"the header is not a JOSE header ({e.Message})";
            return false;
        }
        error = header?.Algorithm is null ? "the header's alg is not a string"
            // RFC 7515 §4.1.11: no critical extension is understood here, so any "crit" is refused.
            : headerJson.TryGetProperty("crit", out _) ? "the header has critical extensions"
            : header.Jwk is { } jwk && !IsPublicKey(jwk) ? "the header's jwk is not a P-256 or secp256k1 public key"
            : null;
        if (error is not null) return false;
        // Both encoded parts passed the alphabet check, so ASCII is their exact encoding.
        jws = new(header!, headerJson, payload, Encoding.ASCII.GetBytes(token, 0, second), signature);
        return true;
    }

    /// <summary>Whether <paramref name="jws"/> is signed by <paramref name="key"/> with the algorithm of the key's curve.</summary>
    /// <param name="jws">The decoded token.</param>
    /// <param name="key">The expected signer.</param>
    /// <returns>
    /// Whether the header's <c>alg</c> is <c>ES256</c> with a P-256 key or <c>ES256K</c> with a K-256 key and the
    /// signature verifies; high-S is accepted (<see cref="SignaturePolicy.AcceptHighS"/>). <c>none</c>, HMAC, RSA and
    /// any mismatch are false.
    /// </returns>
    public static bool Verify(in DecodedJws jws, VerificationKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return jws.Header?.Algorithm == AlgorithmFor(key.Curve) && key.Verify(jws.SigningInput.Span, jws.Signature.Span, SignaturePolicy.AcceptHighS);
    }

    private static bool IsPublicKey(JsonWebKey jwk)
    {
        try
        {
            VerificationKey.FromJsonWebKey(jwk);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // RFC 7515 §2 base64url; trailing padding is tolerated on read (CRY-30).
    private static bool TryDecodeBase64Url(ReadOnlySpan<char> text, out byte[] bytes) => StrictBase64.TryDecode(text, url: true, allowPadding: true, out bytes);

    private static bool TryDecodeObject(ReadOnlySpan<char> part, string name, out JsonElement element, [NotNullWhen(false)] out string? error)
    {
        element = default;
        error = $"the {name} is not a base64url JSON object";
        if (!TryDecodeBase64Url(part, out var bytes)) return false;
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowDuplicateProperties = false });
            element = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return false;
        }
        if (element.ValueKind != JsonValueKind.Object) return false;
        error = null;
        return true;
    }
}
