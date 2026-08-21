using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// A space credential that verified, together with the proof that accompanied it.
/// </summary>
/// <param name="Token">The parsed credential.</param>
/// <param name="Space">The space it grants read access to, from its <c>sub</c>.</param>
/// <param name="AuthorityDid">The space authority that issued it.</param>
/// <param name="Proof">The DPoP proof presented with it, already verified against the request.</param>
public sealed record VerifiedSpaceCredential(
    SpaceToken Token, SpaceUri Space, string AuthorityDid, DPoPProof Proof);

/// <summary>
/// Verifies the space credentials presented to a repo host.
/// </summary>
/// <remarks>
/// <para>This is the repo host's side of the read path. A credential says the space authority
/// admitted this reader to this space; the repo host does not re-evaluate that decision, and has
/// no way to — it holds no member list and the protocol enumerates no readers. What it does
/// check is that the credential is genuine, current, addressed to this space, and presented by
/// the party it was issued to.</para>
/// <para>That last part is the DPoP binding, and it is what makes a credential safe to hand to
/// a host at all. A credential reads a whole space and is presented to every host in it, so
/// without the binding any one of those hosts could replay it against the rest.</para>
/// </remarks>
public sealed class SpaceCredentialVerifier
{
    private readonly ISpaceDidDocumentResolver _resolver;
    private readonly DPoPProofValidator _proofValidator;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a verifier.
    /// </summary>
    /// <param name="resolver">Resolves the issuing authority's DID document.</param>
    /// <param name="proofValidator">Verifies the accompanying DPoP proof.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    public SpaceCredentialVerifier(
        ISpaceDidDocumentResolver resolver,
        DPoPProofValidator proofValidator,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(proofValidator);

        _resolver = resolver;
        _proofValidator = proofValidator;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Verifies a credential and the proof presented with it.
    /// </summary>
    /// <param name="credentialJwt">The credential, from the <c>Authorization: DPoP</c> header.</param>
    /// <param name="proofJwt">The proof, from the <c>DPoP</c> header.</param>
    /// <param name="httpMethod">The HTTP method as received.</param>
    /// <param name="requestUri">The request URL as received.</param>
    /// <param name="expectedSpace">
    /// The space the request names, which the credential must grant. Pass <see langword="null"/>
    /// to take the space from the credential instead.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">Thrown when any check fails.</exception>
    public async Task<VerifiedSpaceCredential> VerifyAsync(
        string credentialJwt,
        string proofJwt,
        string httpMethod,
        string requestUri,
        SpaceUri? expectedSpace = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentialJwt))
            throw Invalid("The request carries no space credential.");

        SpaceToken parsed;
        try
        {
            parsed = SpaceTokens.Parse(SpaceTokenType.Credential, credentialJwt);
        }
        catch (SpaceTokenException ex)
        {
            throw new SpaceVerificationException(SpaceErrors.NotAuthorized, ex.Message, ex);
        }

        if (!SpaceUri.TryParse(parsed.Subject, out var space))
            throw Invalid($"A space credential's subject must be a space URI; got '{parsed.Subject}'.");

        if (expectedSpace is not null && space != expectedSpace)
            throw Invalid($"The credential grants {space}, not the requested {expectedSpace}.");

        // A space's credentials are minted by its own authority and by nobody else. Taking the
        // signer from the space URI rather than from the credential's iss is what stops an
        // authority minting credentials for a space it does not gate.
        if (!string.Equals(parsed.Issuer, space.Authority, StringComparison.Ordinal))
        {
            throw Invalid(
                $"The credential for {space} was issued by '{parsed.Issuer}', not by the space's authority.");
        }

        var authorityKey = await _resolver.ResolveAuthorityKeyAsync(
            space.Authority, parsed.KeyId, cancellationToken);

        SpaceToken verified;
        try
        {
            verified = SpaceTokens.Verify(
                SpaceTokenType.Credential,
                credentialJwt,
                authorityKey,
                expectedAudience: null,
                expectedSubject: space.Value,
                _timeProvider.GetUtcNow());
        }
        catch (SpaceTokenException ex)
        {
            throw new SpaceVerificationException(SpaceErrors.NotAuthorized, ex.Message, ex);
        }

        var proof = await _proofValidator.ValidateAsync(
            proofJwt,
            httpMethod,
            requestUri,
            boundThumbprint: verified.ConfirmationThumbprint,
            accessToken: credentialJwt,
            cancellationToken);

        return new VerifiedSpaceCredential(verified, space, space.Authority, proof);
    }

    private static SpaceVerificationException Invalid(string message) =>
        new(SpaceErrors.NotAuthorized, message);
}
