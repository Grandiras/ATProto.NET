using System.Net;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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
[AuthenticatesItself]
internal sealed class GetSpaceCredentialEndpoint(
    SpaceRequestAuthenticator authenticator,
    ISpaceAccessPolicy policy,
    ISpaceCredentialIssuer issuer,
    SpaceServerOptions options,
    ILogger<GetSpaceCredentialEndpoint> logger)
    : IXrpcProcedure<GetSpaceCredentialRequest, GetSpaceCredentialResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetSpaceCredential);

    public async Task<GetSpaceCredentialResponse> HandleAsync(
        GetSpaceCredentialRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = SpaceRequestValidation.RequireSpace(input.Space);

        if (options.ServiceDid is { } serviceDid && space.Authority != serviceDid)
        {
            // Another authority's space. Answering SpaceNotFound rather than a routing error is
            // the same answer this service gives for a space it does gate but will not disclose.
            throw new XrpcException(
                SpaceErrors.SpaceNotFound,
                $"This service is not the authority for {space}.",
                HttpStatusCode.NotFound);
        }

        var auth = await authenticator.AuthenticateCredentialRequestAsync(
            context, input.ClientAttestation, space, cancellationToken);

        var decision = await policy.EvaluateAsync(
            new SpaceAccessRequest(space, auth.UserDid, auth.AttestedClientId, SpaceAccessKind.Read), cancellationToken);

        if (!decision.IsGranted)
        {
            logger.LogInformation(
                "Refused a credential for {Space} to {User} (app {App}): {Outcome} {Reason}",
                space, auth.UserDid, auth.AttestedClientId ?? "(unattested)", decision.Outcome, decision.Reason);

            // The reason is for the operator's logs. The caller gets the error name and nothing
            // that would let it probe the space's membership.
            throw new XrpcException(
                decision.ErrorName,
                "The authority refused a credential for this space.",
                HttpStatusCode.Forbidden);
        }

        var credential = await issuer.IssueAsync(space, auth.Proof.KeyThumbprint, cancellationToken);

        logger.LogDebug(
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
/// accounts that have written at least one record and that the space's write policy admitted —
/// never the broader set allowed to write, and never readers; the protocol does not enumerate
/// readers at all. It is also only what this authority claims, kept current by the write
/// notifications it has accepted; a listed account's repo host is the source of truth, which is
/// what the per-entry revision is for.
/// </remarks>
[AuthenticatesItself]
internal sealed class ListSpaceReposEndpoint(SpaceRequestAuthenticator authenticator, ISpaceAuthorityStore store)
    : IXrpcQuery<ListSpaceReposParameters, ListSpaceReposResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.ListRepos);

    public async Task<ListSpaceReposResponse> HandleAsync(
        ListSpaceReposParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var space = SpaceRequestValidation.RequireSpace(parameters.Space);
        await authenticator.AuthenticateCredentialAsync(context, space, cancellationToken);
        await RequireLiveSpaceAsync(store, space, cancellationToken);

        return await store.ListReposAsync(
            space, SpaceRequestValidation.Limit(parameters.Limit), parameters.Cursor, cancellationToken);
    }

    /// <summary>
    /// Refuses a request for a space this authority does not gate, or has deleted.
    /// </summary>
    internal static async Task RequireLiveSpaceAsync(
        ISpaceAuthorityStore store, SpaceUri space, CancellationToken cancellationToken)
    {
        var state = await store.GetSpaceStateAsync(space, cancellationToken);
        if (state != SpaceAccessOutcome.Granted)
            throw SpaceStateError(space, state);
    }

    internal static XrpcException SpaceStateError(SpaceUri space, SpaceAccessOutcome state) => state switch
    {
        SpaceAccessOutcome.SpaceDeleted => new XrpcException(
            SpaceErrors.SpaceDeleted, $"{space} was deleted.", HttpStatusCode.NotFound),
        _ => new XrpcException(
            SpaceErrors.SpaceNotFound, $"{space} does not exist.", HttpStatusCode.NotFound),
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
[AuthenticatesItself]
internal sealed class RegisterNotifyEndpoint(
    SpaceRequestAuthenticator authenticator,
    ISpaceAuthorityStore store,
    SpaceServerOptions options,
    TimeProvider? timeProvider = null)
    : IXrpcProcedure<RegisterNotifyRequest, RegisterNotifyResponse>
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.RegisterNotify);

    public async Task<RegisterNotifyResponse> HandleAsync(
        RegisterNotifyRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = SpaceRequestValidation.RequireSpace(input.Space);
        var service = SpaceRequestValidation.RequireServiceIdentifier(input.Service, "service");

        await authenticator.AuthenticateCredentialAsync(context, space, cancellationToken);
        await ListSpaceReposEndpoint.RequireLiveSpaceAsync(store, space, cancellationToken);

        // The registration may outlive the credential the request was authenticated with, which
        // is the point: a syncer holds a subscription across credential renewals rather than
        // re-registering every two hours.
        var expiresAt = _timeProvider.GetUtcNow().Add(options.NotifyRegistrationLifetime);
        await store.RegisterNotifyAsync(space, service, expiresAt, cancellationToken);

        return new RegisterNotifyResponse { ExpiresAt = AtDatetime.FromDateTimeOffset(expiresAt) };
    }
}

/// <summary>Serves <c>com.atproto.space.unregisterNotify</c>.</summary>
/// <remarks>
/// As in the reference authority, any caller with a credential for the space may remove any
/// registration in it: a credential names the space and the reading client, not a subscriber
/// service, so there is nothing to match <c>service</c> against. A removed syncer loses only
/// latency — its <c>listRepos</c> sweep still catches every write — and its next renewal restores it.
/// </remarks>
[AuthenticatesItself]
internal sealed class UnregisterNotifyEndpoint(SpaceRequestAuthenticator authenticator, ISpaceAuthorityStore store)
    : IXrpcProcedureVoid<UnregisterNotifyRequest>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.UnregisterNotify);

    public async Task HandleAsync(
        UnregisterNotifyRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = SpaceRequestValidation.RequireSpace(input.Space);
        var service = SpaceRequestValidation.RequireServiceIdentifier(input.Service, "service");

        await authenticator.AuthenticateCredentialAsync(context, space, cancellationToken);

        // Idempotent, and deliberately unconditional on the space's state: a syncer unsubscribing
        // from a space that has since been deleted is exactly the case that must not fail.
        await store.UnregisterNotifyAsync(space, service, cancellationToken);
    }
}

/// <summary>
/// Serves <c>com.atproto.space.notifyWrite</c>: a repo host telling this authority that one of
/// its repos advanced.
/// </summary>
/// <remarks>
/// <para>This is what keeps <c>listRepos</c> current, and it is how an account joins the writer
/// set at all — the set is the accounts that have written at least one record <em>and</em> that
/// the space's write policy admits, and this notification is the authority's only evidence of the
/// first.</para>
/// <para>It is authenticated with <em>service auth</em> rather than with a space credential:
/// the caller is the writer's PDS, not an application acting for a user. As in the reference
/// implementation, the token's <c>iss</c> must be the writer itself — a PDS signs it with the
/// account's own key (<see cref="ISpaceAccountSigner"/> on an SDK host) — so no service can
/// advance another account's revision in the writer set, however its DID document describes
/// it. The token may address this authority by its bare DID (what the reference implementation
/// sends), as <c>{authority}#atproto_space_host</c>, or by
/// <see cref="SpaceServerOptions.ServiceDid"/>.</para>
/// <para>The writer is then put to the access policy as a <see cref="SpaceAccessKind.Write"/>.
/// One it refuses is answered with 403 and neither recorded nor forwarded — refusing a write
/// notification does not stop anyone writing to their own repo, only this authority listing and
/// relaying it. One it admits is recorded, and forwarded in the background to every service
/// registered for the space.</para>
/// </remarks>
[AuthenticatesItself]
internal sealed class NotifyWriteEndpoint(
    SpaceServiceAuthVerifier serviceAuth,
    ISpaceAuthorityStore store,
    ISpaceAccessPolicy policy,
    SpaceServerOptions options,
    ILogger<NotifyWriteEndpoint> logger,
    SpaceWriteNotifier? notifier = null)
    : IXrpcProcedureVoid<NotifyWriteRequest>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.NotifyWrite);

    public async Task HandleAsync(
        NotifyWriteRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Malformed input is refused before any auth check, as the Lexicon's own types would be.
        var space = SpaceRequestValidation.RequireSpace(input.Space);
        var repo = SpaceRequestValidation.Require(input.Repo, "repo");
        var rev = SpaceRequestValidation.Require(input.Rev, "rev");
        var hash = input.Hash ?? throw new XrpcException(XrpcErrors.InvalidRequest, "The \"hash\" field is required.");

        var caller = await serviceAuth.VerifyAsync(context, AcceptedAudiences(space), Nsid, cancellationToken);

        // The signer is the writer and nobody else. A service listing an endpoint at the writer's
        // PDS origin proves nothing: any DID document can name any URL.
        if (caller.Issuer != repo)
        {
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized,
                $"A write notification for '{repo}' must be signed by '{repo}', not by '{caller.Issuer}'.",
                HttpStatusCode.Forbidden);
        }

        await ListSpaceReposEndpoint.RequireLiveSpaceAsync(store, space, cancellationToken);

        // No attested client: the caller is the writer's repo host, not an application.
        var decision = await policy.EvaluateAsync(
            new SpaceAccessRequest(space, repo, AttestedClientId: null, SpaceAccessKind.Write), cancellationToken);

        if (!decision.IsGranted)
        {
            if (decision.Outcome is SpaceAccessOutcome.SpaceNotFound or SpaceAccessOutcome.SpaceDeleted)
                throw ListSpaceReposEndpoint.SpaceStateError(space, decision.Outcome);

            logger.LogInformation(
                "Refused a write notification for {Space} from {Repo}: {Outcome} {Reason}",
                space, repo, decision.Outcome, decision.Reason);

            throw new XrpcException(
                SpaceErrors.NotAuthorized,
                "The writer is not authorized for this space.",
                HttpStatusCode.Forbidden);
        }

        await store.RecordWriteAsync(space, repo, rev, hash, cancellationToken);

        // Not awaited: neither the writer's repo host nor this request waits on downstream
        // syncers, whose deliveries are best-effort anyway.
        _ = notifier?.ForwardWriteAsync(space, repo, rev, hash);
    }

    private string[] AcceptedAudiences(SpaceUri space) =>
        options.ServiceDid is { } serviceDid && serviceDid != space.Authority
            ? [space.Authority.Value, SpaceAuthority.HostAudience(space.Authority), serviceDid.Value]
            : [space.Authority.Value, SpaceAuthority.HostAudience(space.Authority)];
}
