using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// A delegation token that verified, together with what it establishes.
/// </summary>
/// <param name="Token">The parsed token.</param>
/// <param name="Space">The space named by its <c>sub</c>.</param>
/// <param name="UserDid">
/// The user the requesting application is acting for — the token's <c>iss</c>.
/// </param>
/// <remarks>
/// A delegation token asserts <em>only</em> the user-to-app delegation. Whether that user may
/// read the space is the authority's own determination, made against its policy after this;
/// the token says nothing about it.
/// </remarks>
public sealed record VerifiedDelegationToken(SpaceToken Token, SpaceUri Space, string UserDid);

/// <summary>
/// Verifies the delegation tokens presented to a space authority at credential-mint time.
/// </summary>
/// <remarks>
/// <para>The audience check is the one that carries the weight. A delegation token's <c>aud</c>
/// must equal <c>{authority}#atproto_space_host</c> for the authority named in its <em>own</em>
/// <c>sub</c> — derived from the token, never taken from the request. That is what confines a
/// token to the authority it was minted for: an authority that received one for space A cannot
/// turn around and present it to the authority of space B, because the audience it carries names
/// A.</para>
/// <para>Verification is otherwise the usual JWT shape — parse, check expiry, verify against the
/// issuer's <c>#atproto</c> key from its DID document — plus single use. A delegation token
/// lives 60 seconds and is spent once; the replay store is what enforces the second half.</para>
/// </remarks>
public sealed class SpaceDelegationTokenVerifier
{
    private readonly ISpaceDidDocumentResolver _resolver;
    private readonly ISpaceReplayStore _replayStore;
    private readonly SpaceServerOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a verifier.
    /// </summary>
    /// <param name="resolver">Resolves the issuing account's DID document.</param>
    /// <param name="replayStore">The store that consumes each token's <c>jti</c>.</param>
    /// <param name="options">Server options.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    public SpaceDelegationTokenVerifier(
        ISpaceDidDocumentResolver resolver,
        ISpaceReplayStore replayStore,
        SpaceServerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(replayStore);

        _resolver = resolver;
        _replayStore = replayStore;
        _options = options ?? new SpaceServerOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Verifies a delegation token.
    /// </summary>
    /// <param name="jwt">The token, as presented in the <c>Authorization: Bearer</c> header.</param>
    /// <param name="expectedSpace">
    /// The space the request names, which the token's <c>sub</c> must match. Pass
    /// <see langword="null"/> to take the space from the token instead.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">Thrown when any check fails.</exception>
    public async Task<VerifiedDelegationToken> VerifyAsync(
        string jwt, SpaceUri? expectedSpace = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jwt))
            throw Invalid("The request carries no delegation token.");

        SpaceToken parsed;
        try
        {
            parsed = SpaceTokens.Parse(SpaceTokenType.Delegation, jwt);
        }
        catch (SpaceTokenException ex)
        {
            throw new SpaceVerificationException(SpaceErrors.InvalidDelegationToken, ex.Message, ex);
        }

        if (!SpaceUri.TryParse(parsed.Subject, out var space))
            throw Invalid($"A delegation token's subject must be a space URI; got '{parsed.Subject}'.");

        if (expectedSpace is not null && space != expectedSpace)
            throw Invalid($"The delegation token names {space}, not the requested {expectedSpace}.");

        // Derived from the token's own subject, so a token minted for one authority does not
        // verify at another even if that other authority is the one holding it.
        var expectedAudience = SpaceAuthority.HostAudience(space.Authority);
        if (!string.Equals(parsed.Audience, expectedAudience, StringComparison.Ordinal))
        {
            throw Invalid(
                $"The delegation token is addressed to '{parsed.Audience}', not to '{expectedAudience}'.");
        }

        var issuerKey = await _resolver.ResolveAccountKeyAsync(
            parsed.Issuer, parsed.KeyId, SpaceErrors.InvalidDelegationToken, cancellationToken);

        SpaceToken verified;
        try
        {
            verified = SpaceTokens.Verify(
                SpaceTokenType.Delegation,
                jwt,
                issuerKey,
                expectedAudience,
                space.Value,
                _timeProvider.GetUtcNow());
        }
        catch (SpaceTokenException ex)
        {
            throw new SpaceVerificationException(SpaceErrors.InvalidDelegationToken, ex.Message, ex);
        }

        // A delegation token lives 60 seconds; its issuer chooses the `exp` it actually carries,
        // so one dated far ahead is refused rather than remembered until then.
        if (!_options.IsWithinSingleUseWindow(verified.ExpiresAt, _timeProvider.GetUtcNow()))
        {
            throw Invalid(
                $"The delegation token is valid for longer than the {_options.MaxSingleUseTokenLifetime} " +
                "this service accepts.");
        }

        // Spent only once everything else has passed, so a forged token cannot burn the
        // identifier of one the legitimate holder is about to present.
        if (!await _replayStore.TryConsumeAsync(
                verified.Issuer, verified.TokenId!, verified.ExpiresAt, cancellationToken))
        {
            throw Invalid("The delegation token has already been used; delegation tokens are single-use.");
        }

        return new VerifiedDelegationToken(verified, space, verified.Issuer);
    }

    private static SpaceVerificationException Invalid(string message) =>
        new(SpaceErrors.InvalidDelegationToken, message);
}
