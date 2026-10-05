using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using ATProtoNet.Server.Authentication;

namespace ATProtoNet.Server.Spaces;

/// <summary>A DPoP proof that has been parsed and verified, per <see href="https://www.rfc-editor.org/rfc/rfc9449">RFC 9449</see>.</summary>
public sealed class DPoPProof
{
    internal DPoPProof(string keyThumbprint) => KeyThumbprint = keyThumbprint;

    /// <summary>The RFC 7638 thumbprint of the proof's own embedded <c>jwk</c>: what a credential's <c>cnf.jkt</c> is compared against, and on the credential exchange what the new credential is bound to.</summary>
    public string KeyThumbprint { get; }
}

/// <summary>Verifies the DPoP proofs presented alongside space credentials.</summary>
/// <remarks>
/// <para>A space credential reads a whole space and is presented to every repo host in it, so as
/// a bearer token a host could replay it against every other host. Every request therefore
/// carries a fresh proof, signed by the key the credential is bound to (<c>cnf.jkt</c>) and naming
/// the credential (<c>ath</c>), the method and the URL it is addressed to: a captured credential
/// without the key is inert, and a captured proof does not travel.</para>
/// <para>Proposal 0016 narrows RFC 9449, as the reference implementation does: the proof is signed
/// with <c>ES256</c> only, and the proof on the credential exchange carries <em>no</em>
/// <c>ath</c>, since the delegation token it travels with is a grant rather than an access token.
/// Server-provided nonces are not used; a <c>nonce</c> claim is ignored.</para>
/// </remarks>
public sealed class DPoPProofValidator
{
    private const string ProofType = DPoP.TokenType;
    private const string ProofAlgorithm = "ES256";

    private readonly IJtiReplayStore _replayStore;
    private readonly SpaceServerOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a validator.</summary>
    /// <param name="replayStore">The store that consumes each proof's <c>jti</c>.</param>
    /// <param name="options">Server options; supplies the proof lifetime.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    public DPoPProofValidator(
        IJtiReplayStore replayStore,
        SpaceServerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(replayStore);

        _replayStore = replayStore;
        _options = options ?? new SpaceServerOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Verifies a proof against the request it arrived on.</summary>
    /// <param name="proofJwt">The <c>DPoP</c> header value.</param>
    /// <param name="httpMethod">The HTTP method as received.</param>
    /// <param name="requestUri">
    /// The request URL as received. Query and fragment are stripped before comparison, per
    /// RFC 9449 section 4.3 — one proof therefore covers any query on a given path, which is
    /// what lets a client mint a proof before it has built the query string. Userinfo, which
    /// never belongs in an <c>htu</c>, is stripped too.
    /// </param>
    /// <param name="boundThumbprint">
    /// The <c>cnf.jkt</c> of the credential this proof accompanies, or <see langword="null"/> on
    /// the credential exchange, where the proof's own thumbprint is what the credential will be
    /// bound to.
    /// </param>
    /// <param name="accessToken">
    /// The credential presented on the request, whose hash <c>ath</c> must match, or
    /// <see langword="null"/> on the credential exchange, whose proof must carry no <c>ath</c>.
    /// </param>
    /// <exception cref="SpaceVerificationException">Thrown when any check fails.</exception>
    public Task<DPoPProof> ValidateAsync(
        string proofJwt,
        string httpMethod,
        string requestUri,
        string? boundThumbprint = null,
        string? accessToken = null,
        CancellationToken cancellationToken = default) =>
        ValidateWithHashAsync(
            proofJwt,
            httpMethod,
            requestUri,
            boundThumbprint,
            accessToken is null ? null : DPoP.AccessTokenHash(accessToken),
            cancellationToken);

    // Verifies a proof, given the ath the credential it accompanies hashes to, for a caller that already
    // holds the hash.
    internal async Task<DPoPProof> ValidateWithHashAsync(
        string proofJwt,
        string httpMethod,
        string requestUri,
        string? boundThumbprint,
        string? expectedAccessTokenHash,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);

        // The trusted side of the comparison. A request URI that does not normalize is a
        // misconfiguration of this service rather than anything the caller did — most likely a
        // PublicBaseUrl with no scheme — so it is a fault here, not a failed verification.
        var normalizedRequestUri = DPoP.NormalizeHtu(requestUri)
            ?? throw new ArgumentException(
                $"'{requestUri}' is not an absolute URL; check SpaceServerOptions.PublicBaseUrl.",
                nameof(requestUri));

        if (string.IsNullOrWhiteSpace(proofJwt))
            throw Invalid("The request carries no DPoP proof.");

        if (!Jwt.TryDecode(proofJwt, out var decoded, out var error))
            throw Invalid($"Malformed DPoP proof: {error}.");

        var (header, payload, signingInput, signature) = decoded;

        if (header.GetStringOrNull("typ") != ProofType)
            throw Invalid($"A DPoP proof must carry a \"{ProofType}\" typ header.");

        var algorithm = header.GetStringOrNull("alg")
            ?? throw Invalid("The DPoP proof is missing its \"alg\" header.");
        if (!string.Equals(algorithm, ProofAlgorithm, StringComparison.Ordinal))
            throw Invalid($"A DPoP proof must be signed with {ProofAlgorithm}, not '{algorithm}'.");

        if (!header.TryGetProperty("jwk", out var jwkElement) || jwkElement.ValueKind != JsonValueKind.Object)
            throw Invalid("The DPoP proof is missing its \"jwk\" header.");

        // A proof carrying a private key is not a proof of anything; it is a client leaking its
        // own secret. Reject it rather than helpfully verifying against the public half.
        if (jwkElement.TryGetProperty("d", out _))
            throw Invalid("The DPoP proof's embedded JWK carries private key material.");

        JsonWebKey jwk;
        try
        {
            jwk = jwkElement.Deserialize<JsonWebKey>()!;
        }
        catch (JsonException ex)
        {
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, $"The DPoP proof's embedded JWK is malformed: {ex.Message}", ex);
        }

        var thumbprint = JsonWebKeyVerifier.ComputeThumbprint(jwk, Invalid);

        // The binding before the signature, deliberately: without it, verifying the signature
        // proves only that whoever minted the proof holds the key they chose to embed.
        if (boundThumbprint is not null &&
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(thumbprint), Encoding.UTF8.GetBytes(boundThumbprint)))
            throw Invalid("The DPoP proof is signed by a key the credential is not bound to.");

        if (!JsonWebKeyVerifier.Verify(jwk, algorithm, signingInput, signature, Invalid, thumbprint))
            throw Invalid("The DPoP proof's signature does not verify against its embedded key.");

        var tokenId = Jwt.RequireTokenId(payload, "DPoP proof", Invalid);
        var method = payload.GetStringOrNull("htm")
            ?? throw Invalid("The DPoP proof is missing its \"htm\" claim.");
        var uri = payload.GetStringOrNull("htu")
            ?? throw Invalid("The DPoP proof is missing its \"htu\" claim.");

        if (!string.Equals(method, httpMethod, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"The DPoP proof was minted for {method}, not {httpMethod}.");

        // A relative or otherwise unparseable htu is rejected outright rather than compared
        // verbatim: normalization is what makes the comparison below meaningful, and a value
        // that skips it has not been checked against anything.
        var normalizedProofUri = DPoP.NormalizeHtu(uri)
            ?? throw Invalid("The DPoP proof's \"htu\" is not an absolute URL.");

        if (!string.Equals(normalizedProofUri, normalizedRequestUri, StringComparison.Ordinal))
            throw Invalid("The DPoP proof was minted for a different URL than the one it was presented at.");

        if (!payload.TryGetNumericDate("iat", out var iat))
            throw Invalid("The DPoP proof's \"iat\" claim is not a valid time.");
        if (iat is not { } issuedAt)
            throw Invalid("The DPoP proof is missing its \"iat\" claim.");

        var now = _timeProvider.GetUtcNow();
        if (issuedAt > now + _options.ClockSkew)
            throw Invalid("The DPoP proof is dated in the future.");
        // Refused from the very instant its jti may leave the replay store, not one tick later.
        if (issuedAt + _options.ProofLifetime <= now)
            throw Invalid("The DPoP proof has aged out.");

        var accessTokenHash = payload.GetStringOrNull("ath");
        if (expectedAccessTokenHash is not null)
        {
            if (accessTokenHash is null)
                throw Invalid("The DPoP proof is missing the \"ath\" hash of the credential it accompanies.");
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(accessTokenHash), Encoding.UTF8.GetBytes(expectedAccessTokenHash)))
                throw Invalid("The DPoP proof's \"ath\" names a different credential than the one presented.");
        }
        else if (payload.TryGetProperty("ath", out _))
        {
            throw Invalid(
                "The DPoP proof on a credential exchange must carry no \"ath\": the delegation token is a " +
                "grant, not an access token.");
        }

        // Consumed last, and only once everything else has passed, so a forged proof cannot burn
        // the identifier of one a legitimate holder is about to present.
        if (!await _replayStore.TryConsumeAsync(thumbprint, tokenId, issuedAt + _options.ProofLifetime, cancellationToken).ConfigureAwait(false))
            throw Invalid("The DPoP proof has already been used.");

        return new DPoPProof(thumbprint);
    }

    private static SpaceVerificationException Invalid(string message) =>
        new(SpaceErrors.NotAuthorized, message);
}
