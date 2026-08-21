using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Lexicon.Com.AtProto.Space;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// A DPoP proof that has been parsed and verified, per
/// <see href="https://www.rfc-editor.org/rfc/rfc9449">RFC 9449</see>.
/// </summary>
/// <remarks>
/// The proof is what turns a space credential from a bearer token into one that only its holder
/// can present. Its <see cref="KeyThumbprint"/> is the value a credential's <c>cnf.jkt</c> names,
/// and — on the credential exchange, where no credential exists yet — the value the authority
/// binds the credential it is about to mint to.
/// </remarks>
public sealed class DPoPProof
{
    internal DPoPProof(
        string raw,
        string algorithm,
        string keyThumbprint,
        string tokenId,
        string method,
        string uri,
        DateTimeOffset issuedAt,
        string? accessTokenHash,
        string? nonce)
    {
        Raw = raw;
        Algorithm = algorithm;
        KeyThumbprint = keyThumbprint;
        TokenId = tokenId;
        Method = method;
        Uri = uri;
        IssuedAt = issuedAt;
        AccessTokenHash = accessTokenHash;
        Nonce = nonce;
    }

    /// <summary>The proof as it arrived on the <c>DPoP</c> header.</summary>
    public string Raw { get; }

    /// <summary>The JWS <c>alg</c>: <c>ES256</c> or <c>ES256K</c>.</summary>
    public string Algorithm { get; }

    /// <summary>
    /// The RFC 7638 thumbprint of the proof's own embedded <c>jwk</c>, which a credential's
    /// <c>cnf.jkt</c> is compared against.
    /// </summary>
    public string KeyThumbprint { get; }

    /// <summary>The proof's <c>jti</c>, spent once.</summary>
    public string TokenId { get; }

    /// <summary>The <c>htm</c>: the HTTP method the proof was minted for.</summary>
    public string Method { get; }

    /// <summary>The <c>htu</c>: the request URL, without query or fragment.</summary>
    public string Uri { get; }

    /// <summary>The <c>iat</c>.</summary>
    public DateTimeOffset IssuedAt { get; }

    /// <summary>The <c>ath</c>, the hash of the credential this proof accompanies. Absent on the credential exchange.</summary>
    public string? AccessTokenHash { get; }

    /// <summary>The server-supplied <c>nonce</c>, when the proof carries one.</summary>
    public string? Nonce { get; }
}

/// <summary>
/// Verifies the DPoP proofs presented alongside space credentials.
/// </summary>
/// <remarks>
/// <para>A space credential reads a whole space and is presented to every repo host in it, so as
/// a bearer token it would be a shared secret: a host given one in order to serve its own repo
/// could replay it against every other host in the space. What stops that is this — every
/// request carries a fresh proof, signed by the key the credential is bound to and naming the
/// method and URL it is addressed to. A captured credential without the key is inert, and a
/// captured proof names a host and a method, so it does not travel.</para>
/// <para>Six things are checked, and all six matter:</para>
/// <list type="number">
/// <item><description>the signature verifies against the proof's <em>own</em> embedded
/// <c>jwk</c> — which proves nothing on its own, since an attacker can embed any key;</description></item>
/// <item><description>that key's thumbprint matches the credential's <c>cnf.jkt</c> — which is
/// what makes step 1 mean something;</description></item>
/// <item><description><c>ath</c> matches the credential actually presented, so a proof minted
/// for one credential cannot carry another;</description></item>
/// <item><description><c>htm</c> and <c>htu</c> match the request as received, so a proof
/// captured by one host cannot be replayed at another;</description></item>
/// <item><description><c>iat</c> is recent, bounding how long a captured proof is useful;</description></item>
/// <item><description>the <c>jti</c> has not been seen, so it cannot be used twice inside that
/// window.</description></item>
/// </list>
/// </remarks>
public sealed class DPoPProofValidator
{
    /// <summary>The <c>typ</c> header every DPoP proof carries.</summary>
    public const string ProofType = "dpop+jwt";

    private readonly ISpaceReplayStore _replayStore;
    private readonly SpaceServerOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a validator.
    /// </summary>
    /// <param name="replayStore">The store that consumes each proof's <c>jti</c>.</param>
    /// <param name="options">Server options; supplies the proof lifetime.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    public DPoPProofValidator(
        ISpaceReplayStore replayStore,
        SpaceServerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(replayStore);

        _replayStore = replayStore;
        _options = options ?? new SpaceServerOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Verifies a proof against the request it arrived on.
    /// </summary>
    /// <param name="proofJwt">The <c>DPoP</c> header value.</param>
    /// <param name="httpMethod">The HTTP method as received.</param>
    /// <param name="requestUri">
    /// The request URL as received. Query and fragment are stripped before comparison, per
    /// RFC 9449 section 4.3 — one proof therefore covers any query on a given path, which is
    /// what lets a client mint a proof before it has built the query string.
    /// </param>
    /// <param name="boundThumbprint">
    /// The <c>cnf.jkt</c> of the credential this proof accompanies, or <see langword="null"/> on
    /// the credential exchange, where the proof's own thumbprint is what the credential will be
    /// bound to.
    /// </param>
    /// <param name="accessToken">
    /// The credential presented on the request, whose hash <c>ath</c> must match, or
    /// <see langword="null"/> when the request carries no credential.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">Thrown when any check fails.</exception>
    public async Task<DPoPProof> ValidateAsync(
        string proofJwt,
        string httpMethod,
        string requestUri,
        string? boundThumbprint = null,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);

        // The trusted side of the comparison. A request URI that does not normalize is a
        // misconfiguration of this service rather than anything the caller did — most likely a
        // PublicBaseUrl with no scheme — so it is a fault here, not a failed verification.
        var normalizedRequestUri = NormalizeUri(requestUri)
            ?? throw new ArgumentException(
                $"'{requestUri}' is not an absolute URL; check SpaceServerOptions.PublicBaseUrl.",
                nameof(requestUri));

        if (string.IsNullOrWhiteSpace(proofJwt))
            throw Invalid("The request carries no DPoP proof.");

        var parts = proofJwt.Split('.');
        if (parts.Length != 3)
            throw Invalid("Malformed DPoP proof: expected three parts.");

        var header = DecodeJson(parts[0], "header");
        var payload = DecodeJson(parts[1], "payload");

        if (GetString(header, "typ") != ProofType)
            throw Invalid($"A DPoP proof must carry a \"{ProofType}\" typ header.");

        var algorithm = GetString(header, "alg")
            ?? throw Invalid("The DPoP proof is missing its \"alg\" header.");

        if (!header.TryGetProperty("jwk", out var jwk) || jwk.ValueKind != JsonValueKind.Object)
            throw Invalid("The DPoP proof is missing its \"jwk\" header.");

        // A proof carrying a private key is not a proof of anything; it is a client leaking its
        // own secret. Reject it rather than helpfully verifying against the public half.
        if (jwk.TryGetProperty("d", out _))
            throw Invalid("The DPoP proof's embedded JWK carries private key material.");

        var thumbprint = JsonWebKeyVerifier.ComputeThumbprint(jwk, Invalid);

        // Step 2 before step 1 is deliberate: with no binding to check against, verifying the
        // signature proves only that whoever minted the proof holds the key they chose.
        if (boundThumbprint is not null &&
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(thumbprint), Encoding.UTF8.GetBytes(boundThumbprint)))
        {
            throw Invalid("The DPoP proof is signed by a key the credential is not bound to.");
        }

        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = DecodeBase64Url(parts[2], "signature");

        if (!JsonWebKeyVerifier.Verify(jwk, algorithm, signingInput, signature, Invalid))
            throw Invalid("The DPoP proof's signature does not verify against its embedded key.");

        var tokenId = GetString(payload, "jti")
            ?? throw Invalid("The DPoP proof is missing its \"jti\" claim.");
        var method = GetString(payload, "htm")
            ?? throw Invalid("The DPoP proof is missing its \"htm\" claim.");
        var uri = GetString(payload, "htu")
            ?? throw Invalid("The DPoP proof is missing its \"htu\" claim.");

        if (!string.Equals(method, httpMethod, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"The DPoP proof was minted for {method}, not {httpMethod}.");

        // A relative or otherwise unparseable htu is rejected outright rather than compared
        // verbatim: normalization is what makes the comparison below meaningful, and a value
        // that skips it has not been checked against anything.
        var normalizedProofUri = NormalizeUri(uri)
            ?? throw Invalid("The DPoP proof's \"htu\" is not an absolute URL.");

        if (!string.Equals(normalizedProofUri, normalizedRequestUri, StringComparison.Ordinal))
            throw Invalid("The DPoP proof was minted for a different URL than the one it was presented at.");

        if (!payload.TryGetProperty("iat", out var iat) || !iat.TryGetInt64(out var iatSeconds))
            throw Invalid("The DPoP proof is missing its \"iat\" claim.");

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iatSeconds);
        var now = _timeProvider.GetUtcNow();
        if (issuedAt > now + _options.ClockSkew)
            throw Invalid("The DPoP proof is dated in the future.");
        if (issuedAt + _options.ProofLifetime < now)
            throw Invalid("The DPoP proof has aged out.");

        var accessTokenHash = GetString(payload, "ath");
        if (accessToken is not null)
        {
            var expected = Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));
            if (accessTokenHash is null)
                throw Invalid("The DPoP proof is missing the \"ath\" hash of the credential it accompanies.");
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(accessTokenHash), Encoding.UTF8.GetBytes(expected)))
            {
                throw Invalid("The DPoP proof's \"ath\" names a different credential than the one presented.");
            }
        }

        // Consumed last, and only once everything else has passed, so a forged proof cannot burn
        // the identifier of one a legitimate holder is about to present.
        if (!await _replayStore.TryConsumeAsync(thumbprint, tokenId, issuedAt + _options.ProofLifetime, cancellationToken))
            throw Invalid("The DPoP proof has already been used.");

        return new DPoPProof(
            proofJwt, algorithm, thumbprint, tokenId, method, uri, issuedAt,
            accessTokenHash, GetString(payload, "nonce"));
    }

    /// <summary>
    /// Strips the query and fragment from a URL, per RFC 9449 section 4.3, and normalizes the
    /// scheme, host, and default port so a proof is not rejected over casing. Returns
    /// <see langword="null"/> for anything that is not an absolute URL naming a host.
    /// </summary>
    internal static string? NormalizeUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return null;

        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{port}{uri.AbsolutePath}";
    }

    private static SpaceVerificationException Invalid(string message) =>
        new(SpaceErrors.NotAuthorized, message);

    private static JsonElement DecodeJson(string part, string name)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(DecodeBase64Url(part, name));
        }
        catch (JsonException ex)
        {
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, $"Could not parse the DPoP proof {name}: {ex.Message}", ex);
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
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, $"Could not decode the DPoP proof {name}: {ex.Message}", ex);
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
