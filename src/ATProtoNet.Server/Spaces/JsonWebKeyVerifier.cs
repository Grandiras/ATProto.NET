using System.Security.Cryptography;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Caching;
using ATProtoNet.Crypto;

namespace ATProtoNet.Server.Spaces;

// Verifies a JWS signature against an elliptic-curve JWK, and computes its RFC 7638 thumbprint.
//
// This accepts high-S ECDSA signatures, unlike the SDK's verification of AT Protocol repository
// signatures, which rejects them as malleable. That rule is an AT Protocol rule, not a JWS one: a client
// attestation is an ordinary ES256 JWS, produced by generic JOSE libraries that do no such normalization,
// and rejecting half of them would be a conformance bug rather than a hardening measure. Nothing here
// depends on signature non-malleability — an attestation is bound to its jti, not to the bytes of its
// signature.
//
// Only EC keys are handled. AT Protocol's two curves are P-256 and secp256k1, and the space flow's
// tokens use one of them.
//
// Importing a key costs about as much as verifying with it, and an application's key signs every
// attestation it presents. So a key is imported and checked once, then kept by thumbprint as a
// did:key, and verification goes through the SDK's cache of imported keys. A thumbprint is a hash of the
// key itself, so an entry never goes stale; the bound only limits memory.
internal static class JsonWebKeyVerifier
{
    // thumbprint → did:key of a JWK that imported cleanly.
    private static readonly LruCache<string, string> Keys = new(1024, StringComparer.Ordinal);

    // Verifies a JWS signature against a JWK published in a client's JWKS.
    //
    // algorithm: The JWS alg, which must agree with the key's curve.
    //
    // signingInput: The bytes the signature covers.
    //
    // signature: The signature in IEEE P1363 form.
    //
    // fail: Builds the exception thrown when the key itself is unusable.
    //
    // thumbprint: The key's thumbprint, when the caller already computed it.
    public static bool Verify(
        JsonWebKey key,
        string algorithm,
        ReadOnlySpan<byte> signingInput,
        ReadOnlySpan<byte> signature,
        Func<string, SpaceVerificationException> fail,
        string? thumbprint = null)
    {
        ArgumentNullException.ThrowIfNull(key);

        var (curve, expectedAlg) = Curve(key, fail);

        // The alg header is what a verifier would otherwise take on trust. Pinning it to the
        // curve is what stops a signature being validated under an algorithm the key was never
        // meant for.
        if (!string.Equals(algorithm, expectedAlg, StringComparison.Ordinal))
            throw fail($"JWS algorithm '{algorithm}' does not match the key's {key.Crv} curve.");

        thumbprint ??= ComputeThumbprint(key, fail);
        if (!Keys.TryGetValue(thumbprint, out var didKey))
        {
            didKey = Import(key, curve, fail);
            Keys.Set(thumbprint, didKey);
        }

        // JWS carries an ECDSA signature as a fixed-width r || s concatenation. One of the wrong
        // length — a DER-encoded one, or a truncated one — is a rejected signature rather than a
        // server fault.
        return AtProtoCrypto.TryVerifyJwtSignature(didKey, algorithm, signingInput, signature);
    }

    // Computes a JWK's thumbprint per RFC 7638 (https://www.rfc-editor.org/rfc/rfc7638): SHA-256 over
    // the canonical JSON of the key's required members, in lexicographic order, base64url-encoded.
    //
    // For an EC key the required members are exactly crv, kty, x, and y, so any other member a key
    // carries — kid, use, alg — is excluded and cannot be used to make one key present two thumbprints.
    //
    // fail: Builds the exception thrown when the key is not a usable EC key.
    public static string ComputeThumbprint(JsonWebKey jwk, Func<string, SpaceVerificationException> fail)
    {
        if (jwk.Kty != "EC")
        {
            var kty = string.IsNullOrEmpty(jwk.Kty) ? "(none)" : jwk.Kty;
            throw fail($"Unsupported JWK key type '{kty}'; only EC keys are supported.");
        }

        if (jwk.Crv is null)
            throw fail("The JWK is missing its \"crv\" member.");
        if (jwk.X is null)
            throw fail("The JWK is missing its \"x\" member.");
        if (jwk.Y is null)
            throw fail("The JWK is missing its \"y\" member.");

        return DPoP.Thumbprint(jwk);
    }

    private static (KeyCurve Curve, string Algorithm) Curve(JsonWebKey key, Func<string, SpaceVerificationException> fail)
    {
        if (key.Kty != "EC")
            throw fail($"Unsupported JWK key type '{key.Kty ?? "(none)"}'; only EC keys are supported.");

        return key.Crv switch
        {
            "P-256" => (KeyCurve.P256, "ES256"),
            "secp256k1" => (KeyCurve.K256, "ES256K"),
            _ => throw fail($"Unsupported JWK curve '{key.Crv ?? "(none)"}'."),
        };
    }

    // Imports a JWK once, which checks the point lies on its curve, and returns the equivalent did:key.
    //
    // The check matters: a did:key keeps only X and the parity of Y, so a JWK whose Y is off the curve
    // would otherwise verify as the valid point sharing its X.
    private static string Import(JsonWebKey key, KeyCurve curve, Func<string, SpaceVerificationException> fail)
    {
        if (key.X is null || key.Y is null)
            throw fail("The JWK is missing its \"x\" or \"y\" coordinate.");

        try
        {
            var x = DecodeCoordinate(key.X);
            var y = DecodeCoordinate(key.Y);
            var parameters = new ECParameters
            {
                Curve = curve == KeyCurve.P256
                    ? ECCurve.NamedCurves.nistP256
                    : ECCurve.CreateFromValue("1.3.132.0.10"),
                Q = new ECPoint { X = x, Y = y },
            };

            // The import is the validation; the instance itself is not needed.
            ECDsa.Create(parameters).Dispose();

            return AtProtoCrypto.FormatDidKey([0x04, .. x, .. y], curve);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw fail($"The JWK is not a valid EC public key: {ex.Message}");
        }
    }

    private static byte[] DecodeCoordinate(string value)
    {
        if (!Jwt.TryDecodeBase64Url(value, out var bytes))
            throw new FormatException("EC coordinate is not base64url.");

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
}
