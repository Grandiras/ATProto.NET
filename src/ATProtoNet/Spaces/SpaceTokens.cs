using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Crypto;

namespace ATProtoNet.Spaces;

/// <summary>
/// The three classes of JWT the permissioned data protocol uses to reach a space.
/// </summary>
/// <remarks>
/// They share a wire shape and differ only in who signs them, who they are addressed to, and
/// how long they live.
/// </remarks>
public enum SpaceTokenType
{
    /// <summary>
    /// Minted by a user's PDS, proving an application is acting on that user's behalf.
    /// Single-use, 60 seconds, addressed to the space authority.
    /// </summary>
    /// <remarks>
    /// It asserts <em>only</em> the user-to-app delegation. Whether the user is a member of the
    /// space is the authority's determination, and the token says nothing about it.
    /// </remarks>
    Delegation,

    /// <summary>
    /// Issued by a space authority in exchange for a delegation token. Multi-use, two hours,
    /// with no audience — it is presented to every repo host in the space — and bound to the
    /// holder's key through its <c>cnf.jkt</c> claim.
    /// </summary>
    Credential,

    /// <summary>
    /// Signed by an application's own client authentication key, proving the application's
    /// identity to a space authority. Required only when a space gates on app identity.
    /// </summary>
    ClientAttestation,
}

/// <summary>
/// A parsed space token: its header, its claims, and the signing input its signature covers.
/// </summary>
public sealed class SpaceToken
{
    internal SpaceToken(
        SpaceTokenType type,
        string raw,
        string algorithm,
        string? keyId,
        string issuer,
        string subject,
        string? audience,
        string? confirmationThumbprint,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string? tokenId,
        byte[] signingInput,
        byte[] signature)
    {
        Type = type;
        Raw = raw;
        Algorithm = algorithm;
        KeyId = keyId;
        Issuer = issuer;
        Subject = subject;
        Audience = audience;
        ConfirmationThumbprint = confirmationThumbprint;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        TokenId = tokenId;
        SigningInput = signingInput;
        Signature = signature;
    }

    /// <summary>Which class of token this is.</summary>
    public SpaceTokenType Type { get; }

    /// <summary>The token as it arrived, for presenting on a request.</summary>
    public string Raw { get; }

    /// <summary>The JWT <c>alg</c> — <c>ES256</c> or <c>ES256K</c>.</summary>
    public string Algorithm { get; }

    /// <summary>The JWT <c>kid</c>, naming which of the issuer's keys signed the token.</summary>
    public string? KeyId { get; }

    /// <summary>The <c>iss</c>: the user's DID, the space authority's DID, or a client ID.</summary>
    public string Issuer { get; }

    /// <summary>The <c>sub</c>: the space URI, or the client ID for a client attestation.</summary>
    public string Subject { get; }

    /// <summary>The <c>aud</c>. Absent on a credential, which has many recipients.</summary>
    public string? Audience { get; }

    /// <summary>The <c>cnf.jkt</c>: the thumbprint of the key a credential is bound to.</summary>
    public string? ConfirmationThumbprint { get; }

    /// <summary>The <c>iat</c>.</summary>
    public DateTimeOffset IssuedAt { get; }

    /// <summary>The <c>exp</c>.</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>The <c>jti</c>, the nonce a single-use token is consumed by.</summary>
    public string? TokenId { get; }

    /// <summary>The bytes the signature covers: <c>{header}.{payload}</c>.</summary>
    public byte[] SigningInput { get; }

    /// <summary>The raw signature bytes.</summary>
    public byte[] Signature { get; }

    /// <summary>Whether the token is expired as of <paramref name="now"/>, allowing for clock skew.</summary>
    /// <param name="now">The instant to check against. Defaults to the current time.</param>
    /// <param name="clockSkew">Tolerance for clock skew. Defaults to <see cref="SpaceTokens.DefaultClockSkew"/>.</param>
    public bool IsExpired(DateTimeOffset? now = null, TimeSpan? clockSkew = null) =>
        (now ?? DateTimeOffset.UtcNow) - (clockSkew ?? SpaceTokens.DefaultClockSkew) >= ExpiresAt;

    /// <summary>Parses <see cref="Subject"/> as a space URI. Not meaningful for a client attestation.</summary>
    public SpaceUri ToSpaceUri() => SpaceUri.Parse(Subject);
}

/// <summary>
/// Creates, parses, and verifies the JWTs that gate access to a permissioned space.
/// </summary>
/// <remarks>
/// <para>Reaching a space takes two tokens on two axes. <b>Which user</b> is being acted for is
/// established by a <see cref="SpaceTokenType.Delegation">delegation token</see> minted by that
/// user's PDS. <b>Which application</b> is acting is established by a
/// <see cref="SpaceTokenType.ClientAttestation">client attestation</see> signed by the
/// application itself. They are presented together but signed by different parties and
/// evaluated independently, and the authority exchanges them for a
/// <see cref="SpaceTokenType.Credential">space credential</see>.</para>
/// <para>Most applications do not call this directly —
/// <see cref="SpaceCredentialProvider"/> runs the whole exchange. It is public because a
/// service acting as a space authority, or as a PDS, has to mint and verify these itself.</para>
/// </remarks>
public static class SpaceTokens
{
    /// <summary>The <c>typ</c> header of a delegation token.</summary>
    public const string DelegationType = "atproto-space-delegation+jwt";

    /// <summary>The <c>typ</c> header of a space credential.</summary>
    public const string CredentialType = "atproto-space-credential+jwt";

    /// <summary>The <c>typ</c> header of a client attestation.</summary>
    public const string ClientAttestationType = "atproto-client-attestation+jwt";

    /// <summary>Default lifetime of a delegation token and a client attestation.</summary>
    public static readonly TimeSpan DefaultShortLifetime = TimeSpan.FromSeconds(60);

    /// <summary>Default lifetime of a space credential.</summary>
    public static readonly TimeSpan DefaultCredentialLifetime = TimeSpan.FromHours(2);

    /// <summary>Tolerance applied to expiry checks.</summary>
    public static readonly TimeSpan DefaultClockSkew = TimeSpan.FromSeconds(5);

    /// <summary>Returns the <c>typ</c> header value for a token type.</summary>
    /// <param name="type">The token type.</param>
    public static string TypeHeader(SpaceTokenType type) => type switch
    {
        SpaceTokenType.Delegation => DelegationType,
        SpaceTokenType.Credential => CredentialType,
        SpaceTokenType.ClientAttestation => ClientAttestationType,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>
    /// Mints a space token.
    /// </summary>
    /// <param name="type">Which class of token to create.</param>
    /// <param name="issuer">The <c>iss</c>: a DID, or a client ID for a client attestation.</param>
    /// <param name="subject">The <c>sub</c>: the space URI, or the client ID for a client attestation.</param>
    /// <param name="signingKey">The issuer's signing key.</param>
    /// <param name="audience">
    /// The <c>aud</c>. Required for a delegation token and a client attestation, and rejected
    /// for a credential, which is presented to many hosts.
    /// </param>
    /// <param name="dpopThumbprint">
    /// The JWK thumbprint to bind a credential to, copied into <c>cnf.jkt</c>. Required for a
    /// credential.
    /// </param>
    /// <param name="lifetime">Token lifetime. Defaults to the type's standard lifetime.</param>
    /// <param name="keyId">
    /// The <c>kid</c>. Defaults to <c>#atproto</c>; a space authority publishing a dedicated
    /// <c>#atproto_space</c> key names it here.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when a claim required by the token type is missing.</exception>
    public static string Create(
        SpaceTokenType type,
        string issuer,
        string subject,
        AtProtoKey signingKey,
        string? audience = null,
        string? dpopThumbprint = null,
        TimeSpan? lifetime = null,
        string? keyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(signingKey);

        var requiresAudience = type is SpaceTokenType.Delegation or SpaceTokenType.ClientAttestation;
        if (requiresAudience && string.IsNullOrEmpty(audience))
            throw new ArgumentException($"A {type} token requires an audience.", nameof(audience));

        if (type == SpaceTokenType.Credential && string.IsNullOrEmpty(dpopThumbprint))
        {
            throw new ArgumentException(
                "A space credential must be DPoP-bound; supply the holder's JWK thumbprint.",
                nameof(dpopThumbprint));
        }

        if (type == SpaceTokenType.ClientAttestation && !string.Equals(issuer, subject, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A client attestation's issuer and subject must both be the client ID.", nameof(subject));
        }

        var now = DateTimeOffset.UtcNow;
        var expiry = lifetime ?? (type == SpaceTokenType.Credential
            ? DefaultCredentialLifetime
            : DefaultShortLifetime);

        var header = new Dictionary<string, object>
        {
            ["typ"] = TypeHeader(type),
            ["alg"] = signingKey.Curve == KeyCurve.P256 ? "ES256" : "ES256K",
        };

        // A client attestation's key comes from the client's own JWKS, so it has no default kid.
        var kid = keyId ?? (type == SpaceTokenType.ClientAttestation ? null : "#atproto");
        if (kid is not null)
            header["kid"] = kid;

        var payload = new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["sub"] = subject,
        };

        if (!string.IsNullOrEmpty(audience))
            payload["aud"] = audience;
        if (!string.IsNullOrEmpty(dpopThumbprint))
            payload["cnf"] = new Dictionary<string, string> { ["jkt"] = dpopThumbprint };

        payload["iat"] = now.ToUnixTimeSeconds();
        payload["exp"] = now.Add(expiry).ToUnixTimeSeconds();
        payload["jti"] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

        var headerB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}");

        return $"{headerB64}.{payloadB64}.{Base64UrlEncode(signingKey.Sign(signingInput))}";
    }

    /// <summary>
    /// Parses and structurally validates a space token, without checking its signature.
    /// </summary>
    /// <param name="type">The token class the caller expects.</param>
    /// <param name="jwt">The encoded token.</param>
    /// <exception cref="SpaceTokenException">Thrown when the token is malformed or is not of the expected class.</exception>
    /// <remarks>
    /// This is as far as verification can go for a client attestation, whose key comes from the
    /// client's published JWKS rather than a DID document.
    /// </remarks>
    public static SpaceToken Parse(SpaceTokenType type, string jwt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jwt);

        var parts = jwt.Split('.');
        if (parts.Length != 3)
            throw new SpaceTokenException("Malformed token: expected three parts.");

        var header = DecodeJsonPart(parts[0], "header");
        var payload = DecodeJsonPart(parts[1], "payload");

        var expectedType = TypeHeader(type);
        var actualType = GetString(header, "typ");
        if (!string.Equals(actualType, expectedType, StringComparison.Ordinal))
        {
            throw new SpaceTokenException(
                $"Wrong token type: expected \"{expectedType}\", got \"{actualType ?? "(none)"}\".");
        }

        var algorithm = GetString(header, "alg")
            ?? throw new SpaceTokenException("Token is missing its \"alg\" header.");
        var issuer = GetString(payload, "iss")
            ?? throw new SpaceTokenException("Token is missing its \"iss\" claim.");
        var subject = GetString(payload, "sub")
            ?? throw new SpaceTokenException("Token is missing its \"sub\" claim.");

        if (!payload.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expSeconds))
            throw new SpaceTokenException("Token is missing its \"exp\" claim.");

        var audience = GetString(payload, "aud");
        if (type is SpaceTokenType.Delegation or SpaceTokenType.ClientAttestation && audience is null)
            throw new SpaceTokenException("Token is missing its \"aud\" claim.");

        string? thumbprint = null;
        if (payload.TryGetProperty("cnf", out var cnf) && cnf.ValueKind == JsonValueKind.Object)
            thumbprint = GetString(cnf, "jkt");

        if (type == SpaceTokenType.Credential && string.IsNullOrEmpty(thumbprint))
            throw new SpaceTokenException("A space credential must carry a \"cnf.jkt\" claim.");

        var tokenId = GetString(payload, "jti");
        if (type != SpaceTokenType.Credential && string.IsNullOrEmpty(tokenId))
            throw new SpaceTokenException($"A {type} token requires a \"jti\" to be consumed by.");

        if (type == SpaceTokenType.ClientAttestation && !string.Equals(issuer, subject, StringComparison.Ordinal))
            throw new SpaceTokenException("A client attestation's \"iss\" and \"sub\" must both be the client ID.");

        var issuedAt = payload.TryGetProperty("iat", out var iat) && iat.TryGetInt64(out var iatSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(iatSeconds)
            : DateTimeOffset.MinValue;

        return new SpaceToken(
            type,
            jwt,
            algorithm,
            GetString(header, "kid"),
            issuer,
            subject,
            audience,
            thumbprint,
            issuedAt,
            DateTimeOffset.FromUnixTimeSeconds(expSeconds),
            tokenId,
            Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"),
            DecodeBase64Url(parts[2], "signature"));
    }

    /// <summary>
    /// Attempts to parse a space token, returning <see langword="false"/> rather than throwing.
    /// </summary>
    /// <param name="type">The token class the caller expects.</param>
    /// <param name="jwt">The encoded token.</param>
    /// <param name="token">The parsed token on success.</param>
    public static bool TryParse(SpaceTokenType type, string? jwt, [NotNullWhen(true)] out SpaceToken? token)
    {
        try
        {
            token = jwt is null ? null : Parse(type, jwt);
            return token is not null;
        }
        catch (SpaceTokenException)
        {
            token = null;
            return false;
        }
    }

    /// <summary>
    /// Parses a token, checks its expiry and the claims the caller pins, and verifies its
    /// signature against the issuer's key.
    /// </summary>
    /// <param name="type">The token class the caller expects.</param>
    /// <param name="jwt">The encoded token.</param>
    /// <param name="issuerDidKey">The issuer's signing key as a <c>did:key</c> string.</param>
    /// <param name="expectedAudience">The audience this service answers to, or <see langword="null"/> to skip the check.</param>
    /// <param name="expectedSubject">The space the token must name, or <see langword="null"/> to skip the check.</param>
    /// <param name="now">The instant to evaluate expiry against. Defaults to the current time.</param>
    /// <exception cref="SpaceTokenException">Thrown when any check fails.</exception>
    public static SpaceToken Verify(
        SpaceTokenType type,
        string jwt,
        string issuerDidKey,
        string? expectedAudience = null,
        string? expectedSubject = null,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerDidKey);

        var token = Parse(type, jwt);

        if (token.IsExpired(now))
            throw new SpaceTokenException("Token is expired.");

        if (expectedAudience is not null &&
            !string.Equals(token.Audience, expectedAudience, StringComparison.Ordinal))
        {
            throw new SpaceTokenException("Token audience does not match this service.");
        }

        if (expectedSubject is not null &&
            !string.Equals(token.Subject, expectedSubject, StringComparison.Ordinal))
        {
            throw new SpaceTokenException("Token subject does not match the requested space.");
        }

        bool valid;
        try
        {
            valid = AtProtoCrypto.VerifySignature(issuerDidKey, token.SigningInput, token.Signature);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException)
        {
            throw new SpaceTokenException($"Could not verify token signature: {ex.Message}", ex);
        }

        return valid ? token : throw new SpaceTokenException("Invalid token signature.");
    }

    private static JsonElement DecodeJsonPart(string part, string name)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(DecodeBase64Url(part, name));
        }
        catch (JsonException ex)
        {
            throw new SpaceTokenException($"Could not parse token {name}: {ex.Message}", ex);
        }
    }

    private static byte[] DecodeBase64Url(string value, string name)
    {
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            var padding = (4 - (padded.Length % 4)) % 4;
            return Convert.FromBase64String(padding == 0 ? padded : padded + new string('=', padding));
        }
        catch (FormatException ex)
        {
            throw new SpaceTokenException($"Could not decode token {name}: {ex.Message}", ex);
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Base64UrlEncode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Thrown when a space token is malformed, expired, or fails verification.</summary>
public sealed class SpaceTokenException : Exception
{
    /// <summary>Creates a new exception with the given message.</summary>
    /// <param name="message">A description of what went wrong.</param>
    public SpaceTokenException(string message) : base(message)
    {
    }

    /// <summary>Creates a new exception with the given message and cause.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The underlying cause.</param>
    public SpaceTokenException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
