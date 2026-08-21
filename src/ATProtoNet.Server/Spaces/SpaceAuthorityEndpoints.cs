using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Serves <c>com.atproto.space.getSpaceCredential</c>: the credential exchange, and the only
/// point at which a space authority decides who may read a space.
/// </summary>
/// <remarks>
/// <para>The exchange takes a delegation token minted by the requesting user's PDS and a DPoP
/// proof, optionally alongside a client attestation, and returns a credential bound to the key
/// that signed the proof. Everything downstream of it — every repo host in the space — trusts
/// this decision and does not revisit it, which is why all of the policy lives here.</para>
/// <para>Refusals are attributed deliberately. <c>AppNotAuthorized</c> is what tells a client
/// holding an attestation to retry with it, since whether a space gates on app identity is not
/// advertised anywhere; a space that does not wish to disclose which perimeter failed answers
/// <c>NotAuthorized</c> instead, which a client does not retry.</para>
/// </remarks>
[XrpcEndpoint(Nsid = SpaceNsids.GetSpaceCredential)]
public sealed class GetSpaceCredentialEndpoint
    : IXrpcProcedure<GetSpaceCredentialRequest, GetSpaceCredentialResponse>
{
    private readonly SpaceRequestAuthenticator _authenticator;
    private readonly ISpaceAccessPolicy _policy;
    private readonly ISpaceCredentialIssuer _issuer;
    private readonly SpaceServerOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates the endpoint.
    /// </summary>
    /// <param name="authenticator">Verifies the delegation token, proof, and attestation.</param>
    /// <param name="policy">Decides whether to mint.</param>
    /// <param name="issuer">Mints the credential.</param>
    /// <param name="options">Server options.</param>
    /// <param name="logger">Optional logger.</param>
    public GetSpaceCredentialEndpoint(
        SpaceRequestAuthenticator authenticator,
        ISpaceAccessPolicy policy,
        ISpaceCredentialIssuer issuer,
        SpaceServerOptions options,
        ILogger<GetSpaceCredentialEndpoint>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(options);

        _authenticator = authenticator;
        _policy = policy;
        _issuer = issuer;
        _options = options;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc/>
    public string Nsid => SpaceNsids.GetSpaceCredential;

    /// <inheritdoc/>
    public async Task<GetSpaceCredentialResponse> HandleAsync(
        GetSpaceCredentialRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = SpaceRequestValidation.RequireSpace(input.Space);

        if (_options.ServiceDid is { } serviceDid &&
            !string.Equals(space.Authority, serviceDid, StringComparison.Ordinal))
        {
            // Another authority's space. Answering SpaceNotFound rather than a routing error is
            // the same answer this service gives for a space it does gate but will not disclose.
            throw new XrpcException(
                SpaceErrors.SpaceNotFound,
                $"This service is not the authority for {space}.",
                StatusCodes.Status404NotFound);
        }

        var auth = await _authenticator.AuthenticateCredentialRequestAsync(
            context, input.ClientAttestation, space, cancellationToken);

        var decision = await _policy.EvaluateAsync(
            new SpaceAccessRequest(space, auth.UserDid, auth.AttestedClientId), cancellationToken);

        if (!decision.IsGranted)
        {
            _logger.LogInformation(
                "Refused a credential for {Space} to {User} (app {App}): {Outcome} {Reason}",
                space, auth.UserDid, auth.AttestedClientId ?? "(unattested)", decision.Outcome, decision.Reason);

            // The reason is for the operator's logs. The caller gets the error name and nothing
            // that would let it probe the space's membership.
            throw new XrpcException(
                decision.ErrorName,
                "The authority refused a credential for this space.",
                StatusCodes.Status403Forbidden);
        }

        var credential = await _issuer.IssueAsync(space, auth.Proof.KeyThumbprint, cancellationToken);

        _logger.LogDebug(
            "Issued a credential for {Space} to {User} (app {App})",
            space, auth.UserDid, auth.AttestedClientId ?? "(unattested)");

        return new GetSpaceCredentialResponse { Credential = credential };
    }
}

/// <summary>
/// Serves <c>com.atproto.space.listRepos</c>: a space's writer set.
/// </summary>
/// <remarks>
/// The writer set is the <em>sync boundary</em>, not an access-control list. It enumerates the
/// accounts that have written at least one record, never the broader set allowed to write and
/// never readers — the protocol does not enumerate readers at all. It is also only what this
/// authority claims, kept current by the write notifications it has received; a listed account's
/// repo host is the source of truth, which is what the per-entry revision is for.
/// </remarks>
[XrpcEndpoint(Nsid = SpaceNsids.ListRepos)]
public sealed class ListSpaceReposEndpoint : IXrpcQuery<ListSpaceReposParameters, ListSpaceReposResponse>
{
    private readonly SpaceRequestAuthenticator _authenticator;
    private readonly ISpaceAuthorityStore _store;

    /// <summary>
    /// Creates the endpoint.
    /// </summary>
    /// <param name="authenticator">Verifies the presented credential.</param>
    /// <param name="store">The authority's state.</param>
    public ListSpaceReposEndpoint(SpaceRequestAuthenticator authenticator, ISpaceAuthorityStore store)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(store);

        _authenticator = authenticator;
        _store = store;
    }

    /// <inheritdoc/>
    public string Nsid => SpaceNsids.ListRepos;

    /// <inheritdoc/>
    public async Task<ListSpaceReposResponse> HandleAsync(
        ListSpaceReposParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var space = SpaceRequestValidation.RequireSpace(parameters.Space);
        await _authenticator.AuthenticateCredentialAsync(context, space, cancellationToken);

        var state = await _store.GetSpaceStateAsync(space, cancellationToken);
        if (state != SpaceAccessOutcome.Granted)
            throw SpaceStateError(space, state);

        return await _store.ListReposAsync(
            space, SpaceRequestValidation.Limit(parameters.Limit), parameters.Cursor, cancellationToken);
    }

    internal static XrpcException SpaceStateError(SpaceUri space, SpaceAccessOutcome state) => state switch
    {
        SpaceAccessOutcome.SpaceDeleted => new XrpcException(
            SpaceErrors.SpaceDeleted, $"{space} was deleted.", StatusCodes.Status404NotFound),
        _ => new XrpcException(
            SpaceErrors.SpaceNotFound, $"{space} does not exist.", StatusCodes.Status404NotFound),
    };
}

/// <summary>
/// Serves <c>com.atproto.space.registerNotify</c>: a syncer subscribing to a space's writes.
/// </summary>
/// <remarks>
/// Notifications are the latency optimization, not the correctness guarantee. They carry no
/// record data — only that a repo reached a new revision and hash — and are best-effort: a
/// dropped one is not a lost write, because the syncer's periodic sweep over
/// <c>listRepos</c> catches it. That is why the registration merely has to be recorded, and why
/// letting one lapse is not an error.
/// </remarks>
[XrpcEndpoint(Nsid = SpaceNsids.RegisterNotify)]
public sealed class RegisterNotifyEndpoint : IXrpcProcedure<RegisterNotifyRequest, RegisterNotifyResponse>
{
    private readonly SpaceRequestAuthenticator _authenticator;
    private readonly ISpaceAuthorityStore _store;
    private readonly SpaceServerOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the endpoint.
    /// </summary>
    /// <param name="authenticator">Verifies the presented credential.</param>
    /// <param name="store">The authority's state.</param>
    /// <param name="options">Server options; supplies the registration lifetime.</param>
    /// <param name="timeProvider">The clock. Defaults to the system clock.</param>
    public RegisterNotifyEndpoint(
        SpaceRequestAuthenticator authenticator,
        ISpaceAuthorityStore store,
        SpaceServerOptions options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);

        _authenticator = authenticator;
        _store = store;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public string Nsid => SpaceNsids.RegisterNotify;

    /// <inheritdoc/>
    public async Task<RegisterNotifyResponse> HandleAsync(
        RegisterNotifyRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = SpaceRequestValidation.RequireSpace(input.Space);
        var service = SpaceRequestValidation.RequireServiceIdentifier(input.Service, "service");

        await _authenticator.AuthenticateCredentialAsync(context, space, cancellationToken);

        var state = await _store.GetSpaceStateAsync(space, cancellationToken);
        if (state != SpaceAccessOutcome.Granted)
            throw ListSpaceReposEndpoint.SpaceStateError(space, state);

        // The registration may outlive the credential the request was authenticated with, which
        // is the point: a syncer holds a subscription across credential renewals rather than
        // re-registering every two hours.
        var expiresAt = _timeProvider.GetUtcNow().Add(_options.NotifyRegistrationLifetime);
        await _store.RegisterNotifyAsync(space, service, expiresAt, cancellationToken);

        return new RegisterNotifyResponse { ExpiresAt = AtProtoJsonDefaults.FormatTimestamp(expiresAt.UtcDateTime) };
    }
}

/// <summary>Serves <c>com.atproto.space.unregisterNotify</c>.</summary>
[XrpcEndpoint(Nsid = SpaceNsids.UnregisterNotify)]
public sealed class UnregisterNotifyEndpoint : IXrpcProcedureVoid<UnregisterNotifyRequest>
{
    private readonly SpaceRequestAuthenticator _authenticator;
    private readonly ISpaceAuthorityStore _store;

    /// <summary>
    /// Creates the endpoint.
    /// </summary>
    /// <param name="authenticator">Verifies the presented credential.</param>
    /// <param name="store">The authority's state.</param>
    public UnregisterNotifyEndpoint(SpaceRequestAuthenticator authenticator, ISpaceAuthorityStore store)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(store);

        _authenticator = authenticator;
        _store = store;
    }

    /// <inheritdoc/>
    public string Nsid => SpaceNsids.UnregisterNotify;

    /// <inheritdoc/>
    public async Task HandleAsync(
        UnregisterNotifyRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = SpaceRequestValidation.RequireSpace(input.Space);
        var service = SpaceRequestValidation.RequireServiceIdentifier(input.Service, "service");

        await _authenticator.AuthenticateCredentialAsync(context, space, cancellationToken);

        // Idempotent, and deliberately unconditional on the space's state: a syncer unsubscribing
        // from a space that has since been deleted is exactly the case that must not fail.
        await _store.UnregisterNotifyAsync(space, service, cancellationToken);
    }
}

/// <summary>
/// Serves <c>com.atproto.space.notifyWrite</c>: a repo host telling this authority that one of
/// its repos advanced.
/// </summary>
/// <remarks>
/// <para>This is what keeps <c>listRepos</c> current, and it is how an account joins the writer
/// set at all — the set is defined as the accounts that have written at least one record, and
/// this notification is the authority's only evidence of that.</para>
/// <para>It is authenticated with <em>service auth</em> rather than with a space credential:
/// the caller is a PDS acting as itself, not an application acting for a user. The notification
/// is accepted only from the host that actually answers for the named repo, so an arbitrary
/// service cannot advance another account's revision in the writer set.</para>
/// </remarks>
[XrpcEndpoint(Nsid = SpaceNsids.NotifyWrite)]
public sealed class NotifyWriteEndpoint : IXrpcProcedureVoid<NotifyWriteRequest>
{
    private readonly ISpaceServiceAuthVerifier _serviceAuth;
    private readonly ISpaceAuthorityStore _store;
    private readonly SpaceServerOptions _options;

    /// <summary>
    /// Creates the endpoint.
    /// </summary>
    /// <param name="serviceAuth">Verifies the calling host's service auth token.</param>
    /// <param name="store">The authority's state.</param>
    /// <param name="options">Server options.</param>
    public NotifyWriteEndpoint(
        ISpaceServiceAuthVerifier serviceAuth, ISpaceAuthorityStore store, SpaceServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(serviceAuth);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);

        _serviceAuth = serviceAuth;
        _store = store;
        _options = options;
    }

    /// <inheritdoc/>
    public string Nsid => SpaceNsids.NotifyWrite;

    /// <inheritdoc/>
    public async Task HandleAsync(
        NotifyWriteRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = SpaceRequestValidation.RequireSpace(input.Space);
        var repo = SpaceRequestValidation.RequireDid(input.Repo, "repo");
        var rev = SpaceRequestValidation.RequireString(input.Rev, "rev");

        var caller = await _serviceAuth.VerifyAsync(
            context, _options.ServiceDid ?? space.Authority, Nsid, cancellationToken);

        // The account itself, or the service that hosts its repo. Anything else is a stranger
        // claiming another account's repo advanced.
        if (!string.Equals(caller.Issuer, repo, StringComparison.Ordinal) &&
            !await _serviceAuth.IsRepoHostAsync(caller.Issuer, repo, cancellationToken))
        {
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized,
                $"'{caller.Issuer}' does not host the repo for '{repo}'.",
                StatusCodes.Status403Forbidden);
        }

        var state = await _store.GetSpaceStateAsync(space, cancellationToken);
        if (state != SpaceAccessOutcome.Granted)
            throw ListSpaceReposEndpoint.SpaceStateError(space, state);

        await _store.RecordWriteAsync(space, repo, rev, input.Hash, cancellationToken);
    }
}
