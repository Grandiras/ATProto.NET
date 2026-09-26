using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Verifies the service auth tokens on the notification endpoints, resolving keys and endpoints
/// from DID documents.
/// </summary>
/// <remarks>
/// <para>Write notifications are not carried by a space credential. The caller is a PDS acting as
/// the account whose repo advanced, telling an authority that repo advanced, so it authenticates
/// with the ordinary AT Protocol
/// <see href="https://atproto.com/specs/xrpc#inter-service-authentication-jwt">service auth</see>
/// a feed generator or labeler already uses: a short-lived JWT with <c>iss</c>, <c>aud</c>,
/// <c>lxm</c> and a single-use <c>jti</c>, signed with the issuer's <c>#atproto</c> key.</para>
/// <para>Tokens are checked by the general <see cref="ServiceAuthVerifier"/>: the <c>lxm</c> and
/// <c>jti</c> are required, only an <c>#atproto</c> key is accepted, and the clock checks use
/// <see cref="SpaceServerOptions.ClockSkew"/> and
/// <see cref="SpaceServerOptions.MaxSingleUseTokenLifetime"/>. A refusal is reported as
/// <see cref="SpaceErrors.NotAuthorized"/>, the error the space Lexicons declare.</para>
/// </remarks>
public sealed class SpaceServiceAuthVerifier
{
    private readonly ServiceAuthVerifier _verifier;

    /// <summary>
    /// Creates a verifier.
    /// </summary>
    /// <param name="resolver">
    /// Resolves the caller's DID document; a <see cref="CachingDidResolver"/>, since every
    /// notification resolves one. Resolved from the container under
    /// <see cref="SpaceServerExtensions.DidResolverKey"/>.
    /// </param>
    /// <param name="replayStore">Consumes each token's <c>jti</c>.</param>
    /// <param name="options">Server options.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    public SpaceServiceAuthVerifier(
        [FromKeyedServices(SpaceServerExtensions.DidResolverKey)] IDidResolver resolver,
        IJtiReplayStore replayStore,
        SpaceServerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(replayStore);

        options ??= new SpaceServerOptions();

        _verifier = new ServiceAuthVerifier(
            resolver,
            replayStore,
            new ServiceAuthVerifierOptions
            {
                ClockSkew = options.ClockSkew,
                MaxTokenLifetime = options.MaxSingleUseTokenLifetime,
            },
            timeProvider);
    }

    /// <summary>
    /// Verifies the <c>Authorization: Bearer</c> service auth token on a request addressed to a
    /// single audience.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="expectedAudience">The service identifier the token's <c>aud</c> must equal.</param>
    /// <param name="expectedMethod">The <c>lxm</c> the token must be scoped to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">Thrown when any check fails.</exception>
    public Task<VerifiedServiceAuth> VerifyAsync(
        HttpContext context,
        string expectedAudience,
        Nsid expectedMethod,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAudience);
        return VerifyAsync(context, [expectedAudience], expectedMethod, cancellationToken);
    }

    /// <summary>
    /// Verifies the <c>Authorization: Bearer</c> service auth token on a request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="acceptedAudiences">
    /// The service identifiers this service answers to for the request. The token's <c>aud</c>
    /// must equal one of them exactly. Several are accepted where senders legitimately differ —
    /// a space authority is addressed by its bare DID by the reference implementation and by
    /// <c>{did}#atproto_space_host</c> or its own service DID by others.
    /// </param>
    /// <param name="expectedMethod">The <c>lxm</c> the token must be scoped to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SpaceVerificationException">Thrown when any check fails.</exception>
    public async Task<VerifiedServiceAuth> VerifyAsync(
        HttpContext context,
        IReadOnlyCollection<string> acceptedAudiences,
        Nsid expectedMethod,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(acceptedAudiences);
        ArgumentNullException.ThrowIfNull(expectedMethod);

        if (acceptedAudiences.Count == 0)
            throw new ArgumentException("At least one audience must be accepted.", nameof(acceptedAudiences));

        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw Invalid("A write notification is authenticated with a Bearer service auth token.");

        try
        {
            return await _verifier.VerifyAsync(
                header["Bearer ".Length..].Trim(), acceptedAudiences, expectedMethod, cancellationToken);
        }
        catch (ServiceAuthException ex)
        {
            throw new SpaceVerificationException(SpaceErrors.NotAuthorized, ex.ErrorMessage ?? ex.Error, ex);
        }
    }

    private static SpaceVerificationException Invalid(string message) =>
        new(SpaceErrors.NotAuthorized, message);
}
