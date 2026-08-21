using ATProtoNet.Crypto;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Mints the space credentials this authority issues.
/// </summary>
/// <remarks>
/// Separate from <see cref="ISpaceAccessPolicy"/> because the two answer different questions and
/// tend to live in different places: the policy is application logic, while this holds the
/// authority's signing key and is the natural seam for an HSM, a KMS, or a signing service.
/// </remarks>
public interface ISpaceCredentialIssuer
{
    /// <summary>
    /// Mints a credential for a space, bound to the key that signed the request's DPoP proof.
    /// </summary>
    /// <param name="space">The space the credential reads.</param>
    /// <param name="dpopThumbprint">
    /// The RFC 7638 thumbprint of the requester's key, copied into the credential's
    /// <c>cnf.jkt</c>. This is what stops the credential being a bearer token.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The signed credential JWT.</returns>
    Task<string> IssueAsync(
        SpaceUri space, string dpopThumbprint, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="ISpaceCredentialIssuer"/>: signs credentials with a key held in
/// process.
/// </summary>
/// <remarks>
/// The key must be the one published in the authority's DID document at
/// <see cref="SpaceAuthority.SigningKeyId"/>, or at <c>#atproto</c> when the authority publishes
/// no dedicated entry — a reader resolves it from there and nowhere else. Name which one in
/// <see cref="SpaceServerOptions.CredentialKeyId"/> so the credential's <c>kid</c> says
/// unambiguously which key to verify against.
/// </remarks>
public sealed class SpaceCredentialIssuer : ISpaceCredentialIssuer, IDisposable
{
    private readonly AtProtoKey _signingKey;
    private readonly bool _ownsKey;
    private readonly SpaceServerOptions _options;

    /// <summary>
    /// Creates an issuer.
    /// </summary>
    /// <param name="signingKey">The authority's credential signing key.</param>
    /// <param name="options">
    /// Server options. <see cref="SpaceServerOptions.ServiceDid"/> is required and becomes the
    /// credential's <c>iss</c>.
    /// </param>
    /// <param name="ownsKey">
    /// Whether disposing the issuer disposes the key. Defaults to <see langword="false"/>, since
    /// a signing key usually outlives any one consumer.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when no service DID is configured.</exception>
    public SpaceCredentialIssuer(AtProtoKey signingKey, SpaceServerOptions options, bool ownsKey = false)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ServiceDid))
        {
            throw new ArgumentException(
                $"A space authority must know its own DID; set {nameof(SpaceServerOptions)}.{nameof(SpaceServerOptions.ServiceDid)}.",
                nameof(options));
        }

        _signingKey = signingKey;
        _ownsKey = ownsKey;
        _options = options;
    }

    /// <inheritdoc/>
    public Task<string> IssueAsync(
        SpaceUri space, string dpopThumbprint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(dpopThumbprint);

        // An authority mints credentials only for the spaces it gates. Minting one whose subject
        // names another authority would produce a token no reader will accept, because a reader
        // resolves the signer from the space URI rather than from the credential's issuer.
        if (!string.Equals(space.Authority, _options.ServiceDid, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"This service ({_options.ServiceDid}) is not the authority for {space}.");
        }

        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential,
            issuer: _options.ServiceDid!,
            subject: space.Value,
            signingKey: _signingKey,
            dpopThumbprint: dpopThumbprint,
            lifetime: _options.CredentialLifetime,
            keyId: _options.CredentialKeyId);

        return Task.FromResult(credential);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsKey)
            _signingKey.Dispose();
    }
}
