using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Verifies a JWS signature against an elliptic-curve JWK, and computes the RFC 7638 thumbprint
/// a DPoP binding is expressed in.
/// </summary>
/// <remarks>
/// <para>This is deliberately separate from the SDK's <c>AtProtoCrypto</c> signature path, which
/// verifies AT Protocol <em>repository</em> signatures and rejects high-S ECDSA signatures as
/// malleable. That rule is an AT Protocol rule, not a JWS one: a DPoP proof and a client
/// attestation are ordinary <c>ES256</c> JWS, produced by generic JOSE libraries that do no
/// such normalization, and rejecting half of them would be a conformance bug rather than a
/// hardening measure. Nothing here depends on signature non-malleability — a proof is bound to
/// its <c>jti</c> and its <c>htu</c>, not to the bytes of its signature.</para>
/// <para>Only EC keys are handled. AT Protocol's two curves are P-256 and secp256k1, and the
/// space flow's tokens use one of them.</para>
/// </remarks>
internal static class JsonWebKeyVerifier
{
    /// <summary>
    /// Verifies a JWS signature against a JWK given as a JSON object.
    /// </summary>
    /// <param name="jwk">The JWK.</param>
    /// <param name="algorithm">The JWS <c>alg</c>, which must agree with the key's curve.</param>
    /// <param name="signingInput">The bytes the signature covers: <c>{header}.{payload}</c>.</param>
    /// <param name="signature">The signature in IEEE P1363 (r || s) form, as JWS carries it.</param>
    /// <param name="fail">Builds the exception thrown when the key itself is unusable.</param>
    public static bool Verify(
        JsonElement jwk,
        string algorithm,
        ReadOnlySpan<byte> signingInput,
        ReadOnlySpan<byte> signature,
        Func<string, SpaceVerificationException> fail)
    {
        using var ecdsa = Import(
            GetString(jwk, "kty"), GetString(jwk, "crv"), GetString(jwk, "x"), GetString(jwk, "y"),
            algorithm, fail);

        return VerifyData(ecdsa, signingInput, signature);
    }

    /// <summary>
    /// Verifies a JWS signature against a typed <see cref="JsonWebKey"/>, as published in a
    /// client's JWKS.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="algorithm">The JWS <c>alg</c>.</param>
    /// <param name="signingInput">The bytes the signature covers.</param>
    /// <param name="signature">The signature in IEEE P1363 form.</param>
    /// <param name="fail">Builds the exception thrown when the key itself is unusable.</param>
    public static bool Verify(
        JsonWebKey key,
        string algorithm,
        ReadOnlySpan<byte> signingInput,
        ReadOnlySpan<byte> signature,
        Func<string, SpaceVerificationException> fail)
    {
        ArgumentNullException.ThrowIfNull(key);

        using var ecdsa = Import(key.Kty, key.Crv, key.X, key.Y, algorithm, fail);

        return VerifyData(ecdsa, signingInput, signature);
    }

    /// <summary>
    /// Verifies a JWS signature, treating a malformed one as a failed verification.
    /// </summary>
    /// <remarks>
    /// JWS carries an ECDSA signature as a fixed-width <c>r || s</c> concatenation. A signature of
    /// the wrong length — a DER-encoded one, or a truncated one — is a rejected signature rather
    /// than a server fault, so it must not surface as an unhandled exception.
    /// </remarks>
    private static bool VerifyData(ECDsa ecdsa, ReadOnlySpan<byte> signingInput, ReadOnlySpan<byte> signature)
    {
        try
        {
            return ecdsa.VerifyData(
                signingInput,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Computes a JWK's thumbprint per
    /// <see href="https://www.rfc-editor.org/rfc/rfc7638">RFC 7638</see>: SHA-256 over the
    /// canonical JSON of the key's required members, in lexicographic order, base64url-encoded.
    /// </summary>
    /// <param name="jwk">The JWK.</param>
    /// <param name="fail">Builds the exception thrown when the key is not a usable EC key.</param>
    /// <remarks>
    /// For an EC key the required members are exactly <c>crv</c>, <c>kty</c>, <c>x</c>, and
    /// <c>y</c>, so any other member a proof carries — <c>kid</c>, <c>use</c>, <c>alg</c> — is
    /// excluded and cannot be used to make one key present two thumbprints.
    /// </remarks>
    public static string ComputeThumbprint(JsonElement jwk, Func<string, SpaceVerificationException> fail)
    {
        var kty = GetString(jwk, "kty");
        if (kty != "EC")
            throw fail($"Unsupported JWK key type '{kty ?? "(none)"}'; only EC keys are supported.");

        var crv = GetString(jwk, "crv") ?? throw fail("The JWK is missing its \"crv\" member.");
        var x = GetString(jwk, "x") ?? throw fail("The JWK is missing its \"x\" member.");
        var y = GetString(jwk, "y") ?? throw fail("The JWK is missing its \"y\" member.");

        var canonical = JsonSerializer.Serialize(new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["crv"] = crv,
            ["kty"] = kty,
            ["x"] = x,
            ["y"] = y,
        });

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static ECDsa Import(
        string? kty, string? crv, string? x, string? y, string algorithm,
        Func<string, SpaceVerificationException> fail)
    {
        if (kty != "EC")
            throw fail($"Unsupported JWK key type '{kty ?? "(none)"}'; only EC keys are supported.");

        var (curve, expectedAlg) = crv switch
        {
            "P-256" => (ECCurve.NamedCurves.nistP256, "ES256"),
            "secp256k1" => (ECCurve.CreateFromValue("1.3.132.0.10"), "ES256K"),
            _ => throw fail($"Unsupported JWK curve '{crv ?? "(none)"}'."),
        };

        // The alg header is what a verifier would otherwise take on trust. Pinning it to the
        // curve is what stops a signature being validated under an algorithm the key was never
        // meant for.
        if (!string.Equals(algorithm, expectedAlg, StringComparison.Ordinal))
            throw fail($"JWS algorithm '{algorithm}' does not match the key's {crv} curve.");

        if (x is null || y is null)
            throw fail("The JWK is missing its \"x\" or \"y\" coordinate.");

        try
        {
            var parameters = new ECParameters
            {
                Curve = curve,
                Q = new ECPoint { X = DecodeCoordinate(x), Y = DecodeCoordinate(y) },
            };

            return ECDsa.Create(parameters);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw fail($"The JWK is not a valid EC public key: {ex.Message}");
        }
    }

    private static byte[] DecodeCoordinate(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        var padding = (4 - (padded.Length % 4)) % 4;
        var bytes = Convert.FromBase64String(padding == 0 ? padded : padded + new string('=', padding));

        // Both supported curves are 256-bit, so a coordinate is 32 bytes. A JWK that trimmed a
        // leading zero is still valid and has to be left-padded rather than rejected.
        if (bytes.Length == 32)
            return bytes;
        if (bytes.Length > 32)
            throw new FormatException($"EC coordinate is {bytes.Length} bytes; expected at most 32.");

        var padded32 = new byte[32];
        bytes.CopyTo(padded32, 32 - bytes.Length);
        return padded32;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
