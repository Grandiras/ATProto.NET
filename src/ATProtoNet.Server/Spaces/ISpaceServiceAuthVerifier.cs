using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Verifies the service auth tokens on the notification endpoints.
/// </summary>
/// <remarks>
/// Write notifications are not carried by a space credential. The caller is a PDS acting as
/// itself, telling an authority that one of the repos it hosts advanced, so it authenticates
/// with the ordinary AT Protocol
/// <see href="https://atproto.com/specs/xrpc#inter-service-authentication-jwt">service auth</see>
/// a feed generator or labeler already uses: a short-lived JWT with <c>iss</c>, <c>aud</c>,
/// <c>lxm</c> and a single-use <c>jti</c>, signed with the issuer's <c>#atproto</c> key.
/// </remarks>
public interface ISpaceServiceAuthVerifier
{
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
    Task<VerifiedServiceAuth> VerifyAsync(
        HttpContext context, IReadOnlyCollection<string> acceptedAudiences, Nsid expectedMethod,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether a service speaks for an account's permissioned repos.
    /// </summary>
    /// <param name="serviceDid">The calling service's DID.</param>
    /// <param name="repoDid">The account whose repo the call concerns.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Without this check any service could advance any account's revision in a space's writer
    /// set, which is what a syncer decides from whether to re-read a repo.
    /// </remarks>
    Task<bool> IsRepoHostAsync(Did serviceDid, Did repoDid, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="ISpaceServiceAuthVerifier"/>, resolving keys and endpoints from DID
/// documents.
/// </summary>
/// <remarks>
/// Tokens are checked by the general <see cref="ServiceAuthVerifier"/>: the <c>lxm</c> and
/// <c>jti</c> are required, only an <c>#atproto</c> key is accepted, and the clock checks use
/// <see cref="SpaceServerOptions.ClockSkew"/> and
/// <see cref="SpaceServerOptions.MaxSingleUseTokenLifetime"/>. A refusal is reported as
/// <see cref="SpaceErrors.NotAuthorized"/>, the error the space Lexicons declare.
/// </remarks>
public sealed class SpaceServiceAuthVerifier : ISpaceServiceAuthVerifier
{
    private readonly IDidResolver _resolver;
    private readonly ServiceAuthVerifier _verifier;

    /// <summary>
    /// Creates a verifier.
    /// </summary>
    /// <param name="resolver">
    /// Resolves the caller's DID document; a <see cref="CachingDidResolver"/>, since every
    /// notification resolves one.
    /// </param>
    /// <param name="replayStore">Consumes each token's <c>jti</c>.</param>
    /// <param name="options">Server options.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    public SpaceServiceAuthVerifier(
        IDidResolver resolver,
        IJtiReplayStore replayStore,
        SpaceServerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(replayStore);

        options ??= new SpaceServerOptions();

        _resolver = resolver;
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public async Task<bool> IsRepoHostAsync(
        Did serviceDid, Did repoDid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceDid);
        ArgumentNullException.ThrowIfNull(repoDid);

        // The usual AT Protocol shape: a PDS signs service auth with the account's own key, so
        // the issuer is the account rather than the host.
        if (serviceDid == repoDid)
            return true;

        // Both documents are needed, so they are resolved together rather than one round trip
        // after the other.
        var repoTask = _resolver.ResolveOrRefuseAsync(repoDid, refresh: false, cancellationToken);
        var serviceTask = _resolver.ResolveOrRefuseAsync(serviceDid, refresh: false, cancellationToken);

        Uri? repoHost;
        try
        {
            repoHost = SpaceAuthority.GetHostEndpoint(await repoTask);
        }
        catch
        {
            Observe(serviceTask);
            throw;
        }

        // A repo with no host is hosted by nobody, whatever the service's own document says.
        if (repoHost is null)
        {
            Observe(serviceTask);
            return false;
        }

        var serviceDocument = await serviceTask;

        // A host may publish several service entries; any of them answering at the same origin
        // as the repo's host is the same service.
        return serviceDocument.Service.Any(s => s is not null && SameOrigin(s.Endpoint, repoHost));
    }

    private static void Observe(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static bool SameOrigin(string? left, Uri right) =>
        Uri.TryCreate(left, UriKind.Absolute, out var a) &&
        string.Equals(a.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
        a.Port == right.Port;

    private static SpaceVerificationException Invalid(string message) =>
        new(SpaceErrors.NotAuthorized, message);
}
