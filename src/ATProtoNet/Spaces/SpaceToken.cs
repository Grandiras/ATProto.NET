using System.Security.Cryptography;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Crypto.Jose;

namespace ATProtoNet.Spaces;

/// <summary>The three JWTs of the Spaces protocol, told apart by their <c>typ</c>.</summary>
public enum SpaceTokenType
{
    /// <summary><c>atproto-space-delegation+jwt</c>: a user's host vouches for the user to a space authority.</summary>
    Delegation,
    /// <summary><c>atproto-space-credential+jwt</c>: the authority grants access to a space, bound to a key.</summary>
    Credential,
    /// <summary><c>atproto-client-attestation+jwt</c>: an OAuth client vouches for itself.</summary>
    ClientAttestation,
}

/// <summary>Why a space token was refused, as the protocol's XRPC error names it.</summary>
public enum SpaceTokenError
{
    /// <summary>Malformed, or a claim is missing or out of range.</summary>
    BadJwt,
    /// <summary>The <c>typ</c> is not the type expected.</summary>
    BadJwtType,
    /// <summary>The issuer is missing, or an attestation's issuer is not its subject.</summary>
    BadJwtIss,
    /// <summary>The subject is missing or not the one expected.</summary>
    BadJwtSub,
    /// <summary>The audience is missing or not the one expected.</summary>
    BadJwtAudience,
    /// <summary>A credential's <c>cnf.kid</c> is missing or not a P-256 <c>did:key</c>.</summary>
    BadJwtCnf,
    /// <summary>The token has expired.</summary>
    JwtExpired,
    /// <summary>The signature does not verify against the issuer's key.</summary>
    BadJwtSignature,
}

/// <summary>A space token was refused.</summary>
/// <param name="error">Why.</param>
/// <param name="message">A description of the problem.</param>
public sealed class SpaceTokenException(SpaceTokenError error, string message) : AtProtoException(message)
{
    /// <summary>Why the token was refused.</summary>
    public SpaceTokenError Error { get; } = error;
}

/// <summary>A space token whose structure and claims were checked; its signature is checked with <see cref="VerifySignature"/>.</summary>
/// <remarks>See proposal 0016, <i>Access control</i> and <i>Space credential</i>.</remarks>
public sealed partial class SpaceToken
{
    /// <summary>The longest lifetime a credential may have.</summary>
    public static readonly TimeSpan MaxSpaceCredentialLifetime = TimeSpan.FromMinutes(60);

    /// <summary>The lifetime of a credential the SDK issues unless told otherwise.</summary>
    public static readonly TimeSpan DefaultSpaceCredentialLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The lifetime of a delegation token or attestation the SDK issues unless told otherwise.</summary>
    public static readonly TimeSpan DefaultShortTokenLifetime = TimeSpan.FromSeconds(60);

    /// <summary>The clock tolerance of expiry and of a credential's <c>iat</c>.</summary>
    public static readonly TimeSpan SpaceTokenClockSkew = TimeSpan.FromSeconds(5);

    /// <summary>The <c>kid</c> of delegation tokens and credentials: the issuer's <c>#atproto</c> signing key.</summary>
    public const string SigningKeyId = "#atproto";

    /// <summary>The longest <c>jti</c> accepted: one that a replay or revocation store can key by. The SDK issues 32 hex digits.</summary>
    public const int MaxJtiLength = 128;

    private readonly DecodedJws _jws;

    private SpaceToken(SpaceTokenType type, DecodedJws jws, Claims claims) =>
        (Type, _jws, Issuer, Subject, Audience, ConfirmationKey, IssuedAt, ExpiresAt, Jti) =
        (type, jws, claims.Issuer, claims.Subject, claims.Audience, claims.Cnf, claims.IssuedAt, claims.ExpiresAt, claims.Jti);

    /// <summary>The token's type.</summary>
    public SpaceTokenType Type { get; }

    /// <summary>The issuer: a DID, or a client ID for an attestation.</summary>
    public string Issuer { get; }

    /// <summary>The subject: the space URI, or the client ID for an attestation.</summary>
    public string Subject { get; }

    /// <summary>The audience, <c>{authority}#atproto_pds</c> for a delegation token or attestation; credentials have none.</summary>
    public string? Audience { get; }

    /// <summary>The header's <c>kid</c>.</summary>
    public string? KeyId => _jws.Header.KeyId;

    /// <summary>A credential's bound key (<c>cnf.kid</c>), which signs every request it authorizes.</summary>
    public DidKey? ConfirmationKey { get; }

    /// <summary>When the token was issued, if it says.</summary>
    public DateTimeOffset? IssuedAt { get; }

    /// <summary>When the token expires.</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>The token's unique identifier.</summary>
    public string Jti { get; }

    /// <summary>The <c>typ</c> of a token type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The media type, such as <c>atproto-space-credential+jwt</c>.</returns>
    public static string TypeName(SpaceTokenType type) => type switch
    {
        SpaceTokenType.Delegation => "atproto-space-delegation+jwt",
        SpaceTokenType.Credential => "atproto-space-credential+jwt",
        _ => "atproto-client-attestation+jwt",
    };

    /// <summary>Reads a token of the expected type and checks its claims, and with a clock its times.</summary>
    /// <param name="jwt">The compact JWS.</param>
    /// <param name="expected">The type the caller accepts.</param>
    /// <param name="time">The clock for expiry and a credential's <c>iat</c>; without one, times are not checked.</param>
    /// <returns>The token.</returns>
    /// <exception cref="SpaceTokenException">The token is malformed, of another type, or a claim is missing, invalid or (with a clock) out of time.</exception>
    public static SpaceToken Parse(string jwt, SpaceTokenType expected, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(jwt);
        if (!Jws.TryDecode(jwt, out var jws, out var error)) throw Refuse(SpaceTokenError.BadJwt, $"The token is malformed: {error}.");
        if (jws.Header.Type != TypeName(expected)) throw Refuse(SpaceTokenError.BadJwtType, $"The token is not of type {TypeName(expected)}.");
        if (expected is not SpaceTokenType.ClientAttestation && jws.Header.KeyId != SigningKeyId) throw Refuse(SpaceTokenError.BadJwt, $"The token's kid is not \"{SigningKeyId}\".");
        var claims = Claims.Read(jws.Payload, expected);
        if (time?.GetUtcNow() is { } now)
        {
            if (expected is SpaceTokenType.Credential && claims.IssuedAt > now + SpaceTokenClockSkew) throw Refuse(SpaceTokenError.BadJwt, "The credential was issued in the future.");
            if (now - SpaceTokenClockSkew >= claims.ExpiresAt) throw Refuse(SpaceTokenError.JwtExpired, "The token has expired.");
        }
        return new(expected, jws, claims);
    }

    /// <summary>Whether the token is signed by <paramref name="key"/>, with the algorithm of the key's curve.</summary>
    /// <param name="key">The issuer's key: the <c>#atproto</c> key of its DID document, or an attestation's client key.</param>
    /// <returns>Whether the signature verifies.</returns>
    public bool VerifySignature(VerificationKey key) => Jws.Verify(_jws, key);

    internal static SpaceTokenException Refuse(SpaceTokenError error, string message) => new(error, message);

    internal static string NewJti() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
