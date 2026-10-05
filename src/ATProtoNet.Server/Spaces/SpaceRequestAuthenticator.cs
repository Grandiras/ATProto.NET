using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Spaces;

/// <summary>What a verified credential-mint request establishes: which user, which app, and which key the credential about to be minted must be bound to.</summary>
/// <param name="Delegation">The verified delegation token.</param>
/// <param name="KeyId">
/// The <c>did:key</c> of the P-256 key that signed the request, from its <c>keyid</c>. It becomes
/// the issued credential's <c>cnf.kid</c>.
/// </param>
/// <param name="Attestation">
/// The verified client attestation, or <see langword="null"/> when the request carried none.
/// A space with open app access does not need one.
/// </param>
public sealed record SpaceCredentialRequestAuth(
    VerifiedDelegationToken Delegation, string KeyId, VerifiedClientAttestation? Attestation)
{
    /// <summary>The user the requesting application is acting for.</summary>
    public Did UserDid => Delegation.UserDid;

    /// <summary>The attested client ID, or <see langword="null"/> when the app did not attest.</summary>
    /// <remarks>
    /// An app access policy must be evaluated against <em>this</em> rather than against anything
    /// the request otherwise claims about itself. An unattested client ID is a self-report and
    /// carries no weight.
    /// </remarks>
    public string? AttestedClientId => Attestation?.ClientId;
}

/// <summary>Pulls the space flow's credentials off an ASP.NET Core request and verifies them.</summary>
/// <remarks>
/// <para>Two authentication shapes reach a space server, and they are not interchangeable. Both carry an
/// <see cref="SpaceHttpSignature">HTTP message signature</see>, and both refuse a request whose
/// <c>Authorization</c> header appears more than once.</para>
/// <list type="bullet">
/// <item><description><b>The credential exchange</b> (<c>getSpaceCredential</c>) carries a
/// delegation token as <c>Authorization: Bearer</c> — it is an authorization grant, not an
/// access token — signed by the key the credential will be bound to.</description></item>
/// <item><description><b>Every subsequent read</b> carries the credential as
/// <c>Authorization: Atproto-Space</c> and the DID it is addressed to, signed by the bound
/// key. The caller says which DID that must be: the repo owner for a repo operation, the space
/// authority for a space-host operation.</description></item>
/// </list>
/// </remarks>
public sealed class SpaceRequestAuthenticator
{
    private readonly SpaceDelegationTokenVerifier _delegationVerifier;
    private readonly SpaceCredentialVerifier _credentialVerifier;
    private readonly SpaceClientAttestationVerifier _attestationVerifier;
    private readonly SpaceServerOptions _options;

    /// <summary>Creates an authenticator.</summary>
    /// <param name="delegationVerifier">Verifies delegation tokens.</param>
    /// <param name="credentialVerifier">Verifies space credentials.</param>
    /// <param name="attestationVerifier">Verifies client attestations.</param>
    /// <param name="options">Server options.</param>
    public SpaceRequestAuthenticator(
        SpaceDelegationTokenVerifier delegationVerifier,
        SpaceCredentialVerifier credentialVerifier,
        SpaceClientAttestationVerifier attestationVerifier,
        SpaceServerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(delegationVerifier);
        ArgumentNullException.ThrowIfNull(credentialVerifier);
        ArgumentNullException.ThrowIfNull(attestationVerifier);

        _delegationVerifier = delegationVerifier;
        _credentialVerifier = credentialVerifier;
        _attestationVerifier = attestationVerifier;
        _options = options ?? new SpaceServerOptions();
    }

    /// <summary>Verifies a <c>getSpaceCredential</c> request: its delegation token, its signature, and its client attestation when it presented one.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="clientAttestation">
    /// The <c>clientAttestation</c> field from the request body, or <see langword="null"/>.
    /// </param>
    /// <param name="requestedSpace">
    /// The space named in the request body. The delegation token's subject must agree with it.
    /// </param>
    /// <exception cref="SpaceVerificationException">Thrown when any check fails.</exception>
    public async Task<SpaceCredentialRequestAuth> AuthenticateCredentialRequestAsync(
        HttpContext context,
        string? clientAttestation,
        SpaceUri requestedSpace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requestedSpace);

        var authorization = SingleAuthorization(context);
        var (scheme, token) = ReadAuthorization(authorization);
        if (!string.Equals(scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            throw new SpaceVerificationException(
                SpaceErrors.InvalidDelegationToken,
                "A delegation token is presented under the Bearer scheme.");

        // No credential exists yet, so the signing key names itself, and is what the credential about to be
        // minted will be bound to. Checked before the delegation token, as the reference does, so a request
        // with a bad signature does not spend the token's jti.
        var keyId = VerifySignature(
            () => SpaceHttpSignature.VerifyExchange(authorization, Header(context, "Signature-Input"), Header(context, "Signature")));

        var delegation = await _delegationVerifier.VerifyAsync(token, requestedSpace, cancellationToken).ConfigureAwait(false);

        VerifiedClientAttestation? attestation = null;
        if (!string.IsNullOrWhiteSpace(clientAttestation))
        {
            var audience = SpaceAuthority.HostAudience(
                _options.ServiceDid ?? delegation.Space.Authority);
            attestation = await _attestationVerifier.VerifyAsync(clientAttestation, audience, cancellationToken).ConfigureAwait(false);
        }

        return new SpaceCredentialRequestAuth(delegation, keyId, attestation);
    }

    /// <summary>Verifies a request authenticated with a space credential.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="requestedSpace">
    /// The space named in the request, which the credential must grant. Pass
    /// <see langword="null"/> to take the space from the credential.
    /// </param>
    /// <param name="audience">
    /// The DID the request must be addressed to: the owner of the repo it names for a repo operation,
    /// the space's authority for a space-host operation. Never the host name or a service identifier.
    /// </param>
    /// <exception cref="SpaceVerificationException">
    /// Thrown when any check fails: <c>BadSpaceSignature</c> for a signature or header problem,
    /// <c>BadSpaceAudience</c> when the signed audience is not <paramref name="audience"/>.
    /// </exception>
    public async Task<VerifiedSpaceCredential> AuthenticateCredentialAsync(
        HttpContext context,
        SpaceUri? requestedSpace,
        Did audience,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(audience);

        var authorization = SingleAuthorization(context);
        var (scheme, token) = ReadAuthorization(authorization);
        if (!string.Equals(scheme, SpaceHttpSignature.CredentialScheme, StringComparison.OrdinalIgnoreCase))
        {
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized,
                $"A space credential is presented under the {SpaceHttpSignature.CredentialScheme} scheme, not as a bearer token.")
            {
                Headers = { ["WWW-Authenticate"] = SpaceHttpSignature.CredentialScheme },
            };
        }

        var credential = await _credentialVerifier.VerifyAsync(token, requestedSpace, cancellationToken).ConfigureAwait(false);

        var signedAudience = Did.TryParse(SingleHeader(context, SpaceHttpSignature.AudienceHeader), out var parsed)
            ? parsed
            : throw new SpaceVerificationException(SpaceErrors.BadSpaceSignature, "The space audience is not a DID.");

        VerifySignature(() => SpaceHttpSignature.VerifyRequest(
            authorization, signedAudience, Header(context, "Signature-Input"), Header(context, "Signature"),
            credential.Token.ConfirmationKeyId!));

        if (signedAudience != audience)
            throw new SpaceVerificationException(
                SpaceErrors.BadSpaceAudience, "The space audience does not match the request.");

        return credential;
    }

    // Runs a signature check, answering its failure as BadSpaceSignature.
    private static string VerifySignature(Func<string> verify)
    {
        try
        {
            return verify();
        }
        catch (SpaceSignatureException ex)
        {
            throw new SpaceVerificationException(SpaceErrors.BadSpaceSignature, ex.Message, ex);
        }
    }

    // The Authorization header: absent is a challenge, repeated is malformed.
    private static string SingleAuthorization(HttpContext context) =>
        context.Request.Headers.Authorization.Count == 0
            ? throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, "The request carries no Authorization header.")
            {
                Headers = { ["WWW-Authenticate"] = SpaceHttpSignature.CredentialScheme },
            }
            : SingleHeader(context, "Authorization");

    // The value of a header that must appear exactly once: a second one is malformed rather than ambiguous,
    // and picking either would let a client smuggle a second value past a middlebox.
    private static string SingleHeader(HttpContext context, string name)
    {
        var values = context.Request.Headers[name];
        return values.Count == 1 && !string.IsNullOrEmpty(values[0])
            ? values[0]!
            : throw new SpaceVerificationException(
                SpaceErrors.BadSpaceSignature, $"The request requires exactly one \"{name.ToLowerInvariant()}\" field.");
    }

    // A header that may be missing, which verification then refuses.
    private static string Header(HttpContext context, string name) => context.Request.Headers[name].ToString();

    // Splits an Authorization header at its first space.
    private static (string Scheme, string Token) ReadAuthorization(string authorization)
    {
        var space = authorization.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? (authorization, string.Empty) : (authorization[..space], authorization[(space + 1)..].Trim());
    }
}
