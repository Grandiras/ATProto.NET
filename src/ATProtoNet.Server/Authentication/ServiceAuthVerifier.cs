using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Server.Authentication;

/// <summary>
/// A verified inter-service authentication token.
/// </summary>
public sealed class VerifiedServiceAuth
{
    /// <summary>The account or service the call is made as: the token's <c>iss</c>.</summary>
    public required Did Issuer { get; init; }

    /// <summary>
    /// The audience it addressed: one of the accepted values, a DID with or without a service
    /// fragment.
    /// </summary>
    public required string Audience { get; init; }

    /// <summary>
    /// The XRPC method it was scoped to (<c>lxm</c>), or <see langword="null"/> when it named
    /// none, which only a verifier with <see cref="ServiceAuthVerifierOptions.RequireLexiconMethod"/>
    /// off, or one binding no method, lets through.
    /// </summary>
    public Nsid? Method { get; init; }

    /// <summary>
    /// The verification method in the issuer's DID document the signature verified against: the
    /// token's <c>kid</c>, or <c>#atproto</c> when it named none.
    /// </summary>
    public required string KeyId { get; init; }

    /// <summary>The token's <c>jti</c>, now spent.</summary>
    public required string TokenId { get; init; }

    /// <summary>When the token was minted (<c>iat</c>).</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>When the token expires (<c>exp</c>).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// How strictly <see cref="ServiceAuthVerifier"/> checks a token, beyond the audience and method
/// each call names.
/// </summary>
public sealed class ServiceAuthVerifierOptions
{
    /// <summary>The key a token names when it carries no <c>kid</c>.</summary>
    public const string DefaultKeyId = DidDocument.SigningKeyId;

    /// <summary>
    /// The verification methods a token may be signed with, as <c>kid</c> fragments. Defaults to
    /// just <c>#atproto</c>, the account signing key.
    /// </summary>
    /// <remarks>
    /// The spec requires a receiver to accept only the key types its use case calls for, so that
    /// a key registered for one purpose cannot sign for another. Add, say,
    /// <c>#atproto_label</c> only where a labeler's own key is what should authenticate.
    /// </remarks>
    public ISet<string> AllowedKeyIds { get; } = new HashSet<string>(StringComparer.Ordinal) { DefaultKeyId };

    /// <summary>
    /// Whether a call to an XRPC method refuses a token that names no <c>lxm</c>. Defaults to
    /// <see langword="true"/>, as the spec requires since its 2026 revision.
    /// </summary>
    /// <remarks>
    /// Turn it off only for a sender that has not caught up. A token that does name a method is
    /// held to it either way.
    /// </remarks>
    public bool RequireLexiconMethod { get; set; } = true;

    /// <summary>
    /// How far the issuer's clock may disagree with this one: a token is accepted for this long
    /// past its <c>exp</c>, and with an <c>iat</c> or <c>nbf</c> this far ahead. Defaults to 30
    /// seconds.
    /// </summary>
    /// <remarks>
    /// Its <c>jti</c> is kept in the <see cref="IJtiReplayStore"/> for as long: until <c>exp</c>
    /// plus this skew, the last moment the token is accepted.
    /// </remarks>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The furthest ahead of now a token's <c>exp</c> may sit, and the furthest back its
    /// <c>iat</c> may. Defaults to five minutes.
    /// </summary>
    /// <remarks>
    /// The <c>exp</c> is the issuer's choice, and any DID can sign. Bounding it bounds how long a
    /// captured token stays usable at all, and how long its <c>jti</c> occupies the
    /// <see cref="IJtiReplayStore"/>. Five minutes is what <see cref="ServiceAuthGenerator"/> and
    /// <c>@atproto/lex-server</c> allow; a reference PDS mints tokens of up to an hour through
    /// <c>getServiceAuth</c> when a client asks for one, so raise this if your callers do.
    /// </remarks>
    public TimeSpan MaxTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// The XRPC error names a refused service auth token is answered with: the ones the reference
/// <c>@atproto/xrpc-server</c> uses, so callers see the same names from any AT Protocol service.
/// </summary>
public static class ServiceAuthErrors
{
    /// <summary>The token is malformed, lacks a required claim, or cannot be used again.</summary>
    public const string BadJwt = "BadJwt";

    /// <summary>The token's <c>typ</c> marks it as another kind of token, such as an OAuth access token.</summary>
    public const string BadJwtType = "BadJwtType";

    /// <summary>The token has expired.</summary>
    public const string JwtExpired = "JwtExpired";

    /// <summary>The token is addressed to another service.</summary>
    public const string BadJwtAudience = "BadJwtAudience";

    /// <summary>The token is scoped to another method, or to none where one is required.</summary>
    public const string BadJwtLexiconMethod = "BadJwtLexiconMethod";

    /// <summary>The token's <c>iss</c> is not a bare DID, or does not resolve.</summary>
    public const string BadJwtIss = "BadJwtIss";

    /// <summary>The signature does not verify against the issuer's key.</summary>
    public const string BadJwtSignature = "BadJwtSignature";
}

/// <summary>
/// A service auth token was refused. Answered as HTTP 401 with one of the
/// <see cref="ServiceAuthErrors"/> names.
/// </summary>
/// <remarks>
/// It is an <see cref="XrpcException"/>, so an XRPC endpoint that verifies a token itself and
/// lets the failure escape answers with the error envelope rather than a 500. The message says
/// what was wrong with the token, and nothing about this service's internals.
/// </remarks>
public sealed class ServiceAuthException : XrpcException
{
    /// <summary>Creates a refusal.</summary>
    /// <param name="error">
    /// The XRPC error name: one of <see cref="ServiceAuthErrors"/>, or <c>AuthenticationRequired</c>.
    /// </param>
    /// <param name="message">What was wrong with the token.</param>
    public ServiceAuthException(string error, string message)
        : base(error, message, HttpStatusCode.Unauthorized)
    {
    }

    /// <summary>Creates a refusal with an underlying cause.</summary>
    /// <param name="error">
    /// The XRPC error name: one of <see cref="ServiceAuthErrors"/>, or <c>AuthenticationRequired</c>.
    /// </param>
    /// <param name="message">What was wrong with the token.</param>
    /// <param name="innerException">The underlying cause.</param>
    public ServiceAuthException(string error, string message, Exception innerException)
        : base(error, message, innerException, HttpStatusCode.Unauthorized)
    {
    }
}

/// <summary>
/// Verifies AT Protocol
/// <see href="https://atproto.com/specs/xrpc#inter-service-authentication-jwt">service auth</see>
/// tokens: the JWTs an account's PDS, or another service, signs to call a service as that
/// account.
/// </summary>
/// <remarks>
/// <para>Checks, in order: the token's structure and <c>alg</c>; a <c>typ</c> other than
/// <c>JWT</c> or none, which marks another kind of token; an <c>iss</c> that is a DID without a
/// fragment; the <c>kid</c> (or the <c>#atproto</c> its absence means) against
/// <see cref="ServiceAuthVerifierOptions.AllowedKeyIds"/>; the <c>aud</c> against the accepted
/// audiences; the <c>lxm</c> against the method called; <c>exp</c>, <c>iat</c> and any
/// <c>nbf</c> against the clock, and <c>exp</c> and <c>iat</c> against
/// <see cref="ServiceAuthVerifierOptions.MaxTokenLifetime"/>; the signature, against the issuer's
/// key as resolved (and cached) by the <see cref="IDidResolver"/>; and last the <c>jti</c>, which
/// is spent in the <see cref="IJtiReplayStore"/> only once everything else has passed, so a forged
/// token cannot burn the identifier of a genuine one, and kept until the token stops being
/// accepted.</para>
/// <para>A signature that fails against the cached key is retried once against a refreshed DID
/// document, since the issuer may have rotated its key; the resolver rate-limits refreshes, so
/// forged tokens cannot each cost a directory request. As in the reference implementation, a
/// high-S signature is accepted: generic JOSE signers emit one about half the time, and a bearer
/// token is not content-addressed.</para>
/// <para>This is the check behind <c>AddAtProtoServiceAuth()</c>; call it directly to verify a
/// token that does not arrive as an ASP.NET Core request.</para>
/// </remarks>
public sealed class ServiceAuthVerifier
{
    private readonly IDidResolver _resolver;
    private readonly IJtiReplayStore _replayStore;
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<string> _allowedKeyIds;
    private readonly bool _requireLexiconMethod;
    private readonly TimeSpan _clockSkew;
    private readonly TimeSpan _maxTokenLifetime;

    /// <summary>
    /// Creates a verifier.
    /// </summary>
    /// <param name="resolver">
    /// Resolves the issuer's DID document. Use a caching one: every token resolves a document.
    /// </param>
    /// <param name="replayStore">Spends each token's <c>jti</c>.</param>
    /// <param name="options">
    /// How strictly to check. Defaults to the <see cref="ServiceAuthVerifierOptions"/> defaults.
    /// </param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    /// <exception cref="ArgumentException">
    /// An allowed key ID is not a <c>#fragment</c>, none is allowed, or a duration is negative
    /// (<see cref="ServiceAuthVerifierOptions.MaxTokenLifetime"/> must also be non-zero).
    /// </exception>
    public ServiceAuthVerifier(
        IDidResolver resolver,
        IJtiReplayStore replayStore,
        ServiceAuthVerifierOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(replayStore);

        options ??= new ServiceAuthVerifierOptions();

        if (options.AllowedKeyIds.Count == 0)
            throw new ArgumentException("At least one key ID must be allowed.", nameof(options));

        foreach (var keyId in options.AllowedKeyIds)
        {
            if (!ServiceAuthSyntax.IsKeyId(keyId))
            {
                throw new ArgumentException(
                    $"An allowed key ID is a verification-method fragment such as '#atproto'; got '{keyId}'.",
                    nameof(options));
            }
        }

        if (options.ClockSkew < TimeSpan.Zero)
            throw new ArgumentException("The clock skew cannot be negative.", nameof(options));

        if (options.MaxTokenLifetime <= TimeSpan.Zero)
            throw new ArgumentException("The maximum token lifetime must be positive.", nameof(options));

        _resolver = resolver;
        _replayStore = replayStore;
        _timeProvider = timeProvider ?? TimeProvider.System;

        // Copied, so a verifier's policy cannot change under it after construction.
        _allowedKeyIds = new HashSet<string>(options.AllowedKeyIds, StringComparer.Ordinal);
        _requireLexiconMethod = options.RequireLexiconMethod;
        _clockSkew = options.ClockSkew;
        _maxTokenLifetime = options.MaxTokenLifetime;
    }

    /// <summary>
    /// Verifies a service auth token and spends its <c>jti</c>.
    /// </summary>
    /// <param name="token">The token, as it followed <c>Bearer </c>.</param>
    /// <param name="acceptedAudiences">
    /// The <c>aud</c> values this service answers to; the token's must equal one of them exactly.
    /// Include the <c>did#serviceId</c> form, and the bare DID only while callers still send it:
    /// a bare DID names no service, and is not a wildcard for all of them.
    /// </param>
    /// <param name="method">
    /// The XRPC method being called, which the token's <c>lxm</c> must name. Pass
    /// <see langword="null"/> only for a call that is not to an XRPC method; the <c>lxm</c> is then
    /// not checked, and is the caller's to judge from <see cref="VerifiedServiceAuth.Method"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verified token.</returns>
    /// <exception cref="ServiceAuthException">The token is refused.</exception>
    /// <exception cref="ArgumentException"><paramref name="acceptedAudiences"/> is empty.</exception>
    public async Task<VerifiedServiceAuth> VerifyAsync(
        string token,
        IReadOnlyCollection<string> acceptedAudiences,
        Nsid? method,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(acceptedAudiences);

        if (acceptedAudiences.Count == 0)
            throw new ArgumentException("At least one audience must be accepted.", nameof(acceptedAudiences));

        if (!Jwt.TryDecode(token, out var decoded, out var error))
            throw Refuse(ServiceAuthErrors.BadJwt, $"Malformed service auth token: {error}.");

        var (header, payload, _, _) = decoded;

        var algorithm = header.GetStringOrNull("alg");
        if (algorithm is not ("ES256" or "ES256K"))
        {
            throw Refuse(
                ServiceAuthErrors.BadJwt,
                $"The service auth token's \"alg\" must be ES256 or ES256K; got '{algorithm ?? "(none)"}'.");
        }

        // Service auth is typed "JWT", or not at all. An explicit "+jwt" type (RFC 8725 section
        // 3.11) names a token of another kind — an OAuth access token, a DPoP proof, a space
        // delegation token — which must not pass for this one even when signed with the same key.
        if (header.TryGetProperty("typ", out var typ) &&
            (typ.ValueKind != JsonValueKind.String ||
             typ.GetString()!.EndsWith("+jwt", StringComparison.OrdinalIgnoreCase)))
        {
            throw Refuse(
                ServiceAuthErrors.BadJwtType,
                $"A token of type {typ.GetRawText()} is not a service auth token.");
        }

        var issuerText = payload.GetStringOrNull("iss")
            ?? throw Refuse(ServiceAuthErrors.BadJwtIss, "The service auth token is missing its \"iss\".");

        // A fragment named the issuing service until the 2026 revision; a key is now named by
        // "kid", and the issuer is only ever the account.
        if (issuerText.Contains('#', StringComparison.Ordinal))
        {
            throw Refuse(
                ServiceAuthErrors.BadJwtIss,
                $"The service auth token's \"iss\" must be a bare DID; got '{issuerText}'. " +
                "Name the signing key with \"kid\".");
        }

        if (!Did.TryParse(issuerText, out var issuer))
        {
            throw Refuse(
                ServiceAuthErrors.BadJwtIss, $"The service auth token's \"iss\" must be a DID; got '{issuerText}'.");
        }

        var keyId = ReadKeyId(header, issuer);

        var audience = payload.GetStringOrNull("aud")
            ?? throw Refuse(ServiceAuthErrors.BadJwtAudience, "The service auth token is missing its \"aud\".");

        if (!acceptedAudiences.Contains(audience, StringComparer.Ordinal))
        {
            throw Refuse(
                ServiceAuthErrors.BadJwtAudience,
                $"The service auth token is addressed to '{audience}', not to " +
                $"{string.Join(" or ", acceptedAudiences.Select(a => $"'{a}'"))}.");
        }

        var tokenMethod = ReadMethod(payload, method);

        var (issuedAt, expiresAt) = ReadLifetime(payload);

        var tokenId = payload.GetStringOrNull("jti");
        if (string.IsNullOrEmpty(tokenId))
            throw Refuse(ServiceAuthErrors.BadJwt, "The service auth token is missing its \"jti\".");
        if (!Jwt.IsUsableTokenId(tokenId))
        {
            throw Refuse(
                ServiceAuthErrors.BadJwt,
                "The service auth token's \"jti\" must be printable, not only whitespace, and at most " +
                $"{Jwt.MaxTokenIdLength} characters.");
        }

        await VerifySignatureAsync(issuer, keyId, algorithm, decoded, cancellationToken);

        // Kept until the token stops being accepted, which the skew puts past its exp.
        if (!await _replayStore.TryConsumeAsync(issuer.Value, tokenId, expiresAt + _clockSkew, cancellationToken))
            throw Refuse(ServiceAuthErrors.BadJwt, "The service auth token has already been used.");

        return new VerifiedServiceAuth
        {
            Issuer = issuer,
            Audience = audience,
            Method = tokenMethod,
            KeyId = keyId,
            TokenId = tokenId,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
        };
    }

    private string ReadKeyId(JsonElement header, Did issuer)
    {
        // No kid means #atproto, which is held to the allow-list like any named key: a service
        // accepting only #atproto_label must not take an account-key token that omits the header.
        if (!header.TryGetProperty("kid", out var kid))
        {
            return _allowedKeyIds.Contains(ServiceAuthVerifierOptions.DefaultKeyId)
                ? ServiceAuthVerifierOptions.DefaultKeyId
                : throw Refuse(
                    ServiceAuthErrors.BadJwt,
                    "The service auth token names no \"kid\", so it is signed with " +
                    $"'{ServiceAuthVerifierOptions.DefaultKeyId}', a key this service does not accept.");
        }

        if (kid.ValueKind != JsonValueKind.String)
            throw Refuse(ServiceAuthErrors.BadJwt, "The service auth token's \"kid\" is not a string.");

        var value = kid.GetString()!;

        // The spec asks for the bare fragment, but a DID URL naming the issuer's own key names
        // the same key. One naming another DID's key is refused below, as unknown.
        var hash = value.IndexOf('#', StringComparison.Ordinal);
        if (hash > 0 && value.AsSpan(0, hash).SequenceEqual(issuer.Value))
            value = value[hash..];

        if (!_allowedKeyIds.Contains(value))
        {
            throw Refuse(
                ServiceAuthErrors.BadJwt,
                $"The service auth token is signed with '{kid.GetString()}', a key this service does not accept.");
        }

        return value;
    }

    private Nsid? ReadMethod(JsonElement payload, Nsid? method)
    {
        Nsid? tokenMethod = null;
        if (payload.TryGetProperty("lxm", out var lxm))
        {
            if (lxm.ValueKind != JsonValueKind.String || !Nsid.TryParse(lxm.GetString(), out tokenMethod))
                throw Refuse(ServiceAuthErrors.BadJwtLexiconMethod, "The service auth token's \"lxm\" is not an NSID.");
        }

        if (method is null)
            return tokenMethod;

        if (tokenMethod is null)
        {
            if (_requireLexiconMethod)
            {
                throw Refuse(
                    ServiceAuthErrors.BadJwtLexiconMethod,
                    $"The service auth token names no \"lxm\"; a call to '{method}' needs one naming it.");
            }

            return null;
        }

        // Exact, as the reference compares: the token names the method its issuer approved.
        if (!string.Equals(tokenMethod.Value, method.Value, StringComparison.Ordinal))
        {
            throw Refuse(
                ServiceAuthErrors.BadJwtLexiconMethod,
                $"The service auth token is scoped to '{tokenMethod}', not to '{method}'.");
        }

        return tokenMethod;
    }

    private (DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt) ReadLifetime(JsonElement payload)
    {
        if (!payload.TryGetNumericDate("exp", out var exp))
            throw Refuse(ServiceAuthErrors.BadJwt, "The service auth token's \"exp\" is not a valid time.");
        if (exp is not { } expiresAt)
            throw Refuse(ServiceAuthErrors.BadJwt, "The service auth token is missing its \"exp\".");

        if (!payload.TryGetNumericDate("iat", out var iat))
            throw Refuse(ServiceAuthErrors.BadJwt, "The service auth token's \"iat\" is not a valid time.");
        if (iat is not { } issuedAt)
            throw Refuse(ServiceAuthErrors.BadJwt, "The service auth token is missing its \"iat\".");

        // Differences, never sums: two representable instants are always a representable
        // TimeSpan apart, while now plus a configured duration need not be a representable instant.
        var now = _timeProvider.GetUtcNow();

        if (now - expiresAt >= _clockSkew)
            throw Refuse(ServiceAuthErrors.JwtExpired, "The service auth token is expired.");

        if (expiresAt - now - _clockSkew > _maxTokenLifetime)
        {
            throw Refuse(
                ServiceAuthErrors.BadJwt,
                $"The service auth token is valid for longer than the {_maxTokenLifetime} this service accepts.");
        }

        if (issuedAt - now > _clockSkew)
            throw Refuse(ServiceAuthErrors.BadJwt, "The service auth token is dated in the future.");

        // As @atproto/lex-server bounds it: a token minted longer ago than any this service would
        // accept a lifetime of is refused, whatever exp it names.
        if (now - issuedAt - _clockSkew > _maxTokenLifetime)
        {
            throw Refuse(
                ServiceAuthErrors.BadJwt,
                $"The service auth token was issued more than the {_maxTokenLifetime} this service accepts ago.");
        }

        if (!payload.TryGetNumericDate("nbf", out var nbf))
            throw Refuse(ServiceAuthErrors.BadJwt, "The service auth token's \"nbf\" is not a valid time.");
        if (nbf is { } notBefore && notBefore - now > _clockSkew)
            throw Refuse(ServiceAuthErrors.BadJwt, "The service auth token is not valid yet.");

        return (issuedAt, expiresAt);
    }

    private async Task VerifySignatureAsync(
        Did issuer, string keyId, string algorithm, DecodedJwt decoded, CancellationToken cancellationToken)
    {
        var key = await ResolveKeyAsync(issuer, keyId, refresh: false, cancellationToken);
        if (key is not null && Verify(key, algorithm, decoded))
            return;

        // The cached document may predate a key rotation, or the key's publication: refetch once
        // before refusing.
        var refreshed = await ResolveKeyAsync(issuer, keyId, refresh: true, cancellationToken);
        if (refreshed is null)
        {
            throw Refuse(
                ServiceAuthErrors.BadJwtSignature,
                $"'{issuer}' publishes no usable '{keyId}' verification method to verify against.");
        }

        if (string.Equals(refreshed, key, StringComparison.Ordinal) || !Verify(refreshed, algorithm, decoded))
            throw Refuse(ServiceAuthErrors.BadJwtSignature, "The service auth token's signature does not verify.");
    }

    /// <summary>
    /// The issuer's key named <paramref name="keyId"/>, or <see langword="null"/> when its document
    /// publishes none, or one this SDK cannot read.
    /// </summary>
    private async Task<string?> ResolveKeyAsync(
        Did issuer, string keyId, bool refresh, CancellationToken cancellationToken)
    {
        DidDocument document;
        try
        {
            document = refresh
                ? await _resolver.RefreshAsync(issuer, cancellationToken)
                : await _resolver.ResolveAsync(issuer, cancellationToken);
        }
        catch (DidResolutionException ex)
        {
            // An issuer that does not resolve cannot be verified, whatever the reason: a refusal,
            // not a fault of this service. What went wrong stays in the inner exception.
            throw new ServiceAuthException(
                ServiceAuthErrors.BadJwtIss, $"Could not resolve the issuer '{issuer}'.", ex);
        }

        try
        {
            return document.GetVerificationKey(keyId);
        }
        catch (FormatException)
        {
            // The issuer's own document is broken, which is no more usable than absent.
            return null;
        }
    }

    private static bool Verify(string key, string algorithm, DecodedJwt decoded)
    {
        try
        {
            return AtProtoCrypto.VerifyJwtSignature(key, algorithm, decoded.SigningInput, decoded.Signature);
        }
        catch (Exception ex) when (
            ex is ArgumentException or FormatException or NotSupportedException or CryptographicException)
        {
            // Key material the document publishes but this platform cannot use.
            return false;
        }
    }

    private static ServiceAuthException Refuse(string error, string message) => new(error, message);
}
