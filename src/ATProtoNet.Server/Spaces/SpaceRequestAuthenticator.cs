using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// What a verified credential-mint request establishes: which user, which app, and which key the
/// credential about to be minted must be bound to.
/// </summary>
/// <param name="Delegation">The verified delegation token.</param>
/// <param name="Proof">
/// The verified DPoP proof. Its
/// <see cref="DPoPProof.KeyThumbprint"/> becomes the issued credential's <c>cnf.jkt</c>.
/// </param>
/// <param name="Attestation">
/// The verified client attestation, or <see langword="null"/> when the request carried none.
/// A space with open app access does not need one.
/// </param>
public sealed record SpaceCredentialRequestAuth(
    VerifiedDelegationToken Delegation, DPoPProof Proof, VerifiedClientAttestation? Attestation)
{
    /// <summary>The space the credential is being requested for.</summary>
    public SpaceUri Space => Delegation.Space;

    /// <summary>The user the requesting application is acting for.</summary>
    public string UserDid => Delegation.UserDid;

    /// <summary>
    /// The attested client ID, or <see langword="null"/> when the app did not attest.
    /// </summary>
    /// <remarks>
    /// An app access policy must be evaluated against <em>this</em> rather than against anything
    /// the request otherwise claims about itself. An unattested client ID is a self-report and
    /// carries no weight.
    /// </remarks>
    public string? AttestedClientId => Attestation?.ClientId;
}

/// <summary>
/// Pulls the space flow's credentials off an ASP.NET Core request and verifies them.
/// </summary>
/// <remarks>
/// <para>Two authentication shapes reach a space server, and they are not interchangeable.</para>
/// <list type="bullet">
/// <item><description><b>The credential exchange</b> (<c>getSpaceCredential</c>) carries a
/// delegation token as <c>Authorization: Bearer</c> — it is an authorization grant, not an
/// access token, so it travels as a bearer token and its proof carries no <c>ath</c> — plus a
/// DPoP proof naming the key the credential will be bound to.</description></item>
/// <item><description><b>Every subsequent read</b> carries the credential as
/// <c>Authorization: DPoP</c> plus a proof signed by the bound key, naming this host and this
/// method.</description></item>
/// </list>
/// <para>Both compare the proof's <c>htm</c> and <c>htu</c> against the request <em>as
/// received</em>, which is why <see cref="SpaceServerOptions.PublicBaseUrl"/> exists: behind a
/// reverse proxy the request line names an internal host, and comparing against that would
/// reject every proof a client could mint.</para>
/// </remarks>
public sealed class SpaceRequestAuthenticator
{
    private readonly SpaceDelegationTokenVerifier _delegationVerifier;
    private readonly SpaceCredentialVerifier _credentialVerifier;
    private readonly DPoPProofValidator _proofValidator;
    private readonly SpaceClientAttestationVerifier _attestationVerifier;
    private readonly SpaceServerOptions _options;

    /// <summary>
    /// Creates an authenticator.
    /// </summary>
    /// <param name="delegationVerifier">Verifies delegation tokens.</param>
    /// <param name="credentialVerifier">Verifies space credentials and their proofs.</param>
    /// <param name="proofValidator">Verifies the standalone proof on the credential exchange.</param>
    /// <param name="attestationVerifier">Verifies client attestations.</param>
    /// <param name="options">Server options.</param>
    public SpaceRequestAuthenticator(
        SpaceDelegationTokenVerifier delegationVerifier,
        SpaceCredentialVerifier credentialVerifier,
        DPoPProofValidator proofValidator,
        SpaceClientAttestationVerifier attestationVerifier,
        SpaceServerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(delegationVerifier);
        ArgumentNullException.ThrowIfNull(credentialVerifier);
        ArgumentNullException.ThrowIfNull(proofValidator);
        ArgumentNullException.ThrowIfNull(attestationVerifier);

        _delegationVerifier = delegationVerifier;
        _credentialVerifier = credentialVerifier;
        _proofValidator = proofValidator;
        _attestationVerifier = attestationVerifier;
        _options = options ?? new SpaceServerOptions();
    }

    /// <summary>
    /// Verifies a <c>getSpaceCredential</c> request: its delegation token, its DPoP proof, and
    /// its client attestation when it presented one.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="clientAttestation">
    /// The <c>clientAttestation</c> field from the request body, or <see langword="null"/>.
    /// </param>
    /// <param name="requestedSpace">
    /// The space named in the request body. The delegation token's subject must agree with it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">Thrown when any check fails.</exception>
    public async Task<SpaceCredentialRequestAuth> AuthenticateCredentialRequestAsync(
        HttpContext context,
        string? clientAttestation,
        SpaceUri requestedSpace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requestedSpace);

        var (scheme, token) = ReadAuthorization(context);
        if (!string.Equals(scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new SpaceVerificationException(
                SpaceErrors.InvalidDelegationToken,
                "A delegation token is presented under the Bearer scheme.");
        }

        var delegation = await _delegationVerifier.VerifyAsync(token, requestedSpace, cancellationToken);

        // No credential exists yet, so the proof carries no `ath` and is bound to nothing; its
        // own thumbprint is what the credential about to be minted will name in `cnf.jkt`.
        var proof = await _proofValidator.ValidateAsync(
            ReadProof(context),
            context.Request.Method,
            BuildRequestUri(context),
            boundThumbprint: null,
            accessToken: null,
            cancellationToken);

        VerifiedClientAttestation? attestation = null;
        if (!string.IsNullOrWhiteSpace(clientAttestation))
        {
            var audience = SpaceAuthority.HostAudience(
                _options.ServiceDid ?? delegation.Space.Authority);
            attestation = await _attestationVerifier.VerifyAsync(clientAttestation, audience, cancellationToken);
        }

        return new SpaceCredentialRequestAuth(delegation, proof, attestation);
    }

    /// <summary>
    /// Verifies a request authenticated with a space credential.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="requestedSpace">
    /// The space named in the request, which the credential must grant. Pass
    /// <see langword="null"/> to take the space from the credential.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">Thrown when any check fails.</exception>
    public async Task<VerifiedSpaceCredential> AuthenticateCredentialAsync(
        HttpContext context,
        SpaceUri? requestedSpace = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (scheme, token) = ReadAuthorization(context);
        if (!string.Equals(scheme, "DPoP", StringComparison.OrdinalIgnoreCase))
        {
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized,
                "A space credential is presented under the DPoP scheme, not as a bearer token.")
            {
                Headers = { ["WWW-Authenticate"] = "DPoP" },
            };
        }

        return await _credentialVerifier.VerifyAsync(
            token,
            ReadProof(context),
            context.Request.Method,
            BuildRequestUri(context),
            requestedSpace,
            cancellationToken);
    }

    /// <summary>
    /// The URL a DPoP proof presented on this request must name, honouring
    /// <see cref="SpaceServerOptions.PublicBaseUrl"/>.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public string BuildRequestUri(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        return _options.BuildRequestUri(
            request.Scheme, request.Host.Value ?? string.Empty, request.PathBase + request.Path);
    }

    private static (string Scheme, string Token) ReadAuthorization(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, "The request carries no Authorization header.")
            {
                Headers = { ["WWW-Authenticate"] = "DPoP" },
            };
        }

        var space = header.IndexOf(' ');
        return space < 0
            ? (header, string.Empty)
            : (header[..space], header[(space + 1)..].Trim());
    }

    private static string ReadProof(HttpContext context)
    {
        // More than one DPoP header is malformed rather than ambiguous: RFC 9449 permits exactly
        // one, and picking either would let a client smuggle a second proof past a middlebox.
        var proofs = context.Request.Headers["DPoP"];
        return proofs.Count switch
        {
            0 => throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, "The request carries no DPoP header.")
            {
                Headers = { ["WWW-Authenticate"] = "DPoP" },
            },
            1 => proofs[0] ?? string.Empty,
            _ => throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, "The request carries more than one DPoP header."),
        };
    }
}
