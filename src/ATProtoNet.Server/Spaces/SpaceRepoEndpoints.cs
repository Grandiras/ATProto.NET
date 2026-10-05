using System.Net;
using ATProtoNet.Auth;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Server.Spaces;

// The shared shape of the repo-host read endpoints: verify the credential, then serve the (space, repo)
// pair it names.
//
// The credential is checked against the space the request names, so a credential for one space cannot be
// used to read another even on a host that serves both, and the request's signed audience against the repo
// it names, so a signature made for one account's repo cannot read another's. Nothing else is re-decided here — the authority
// already made the access decision, and a repo host holds no state with which to second-guess it.
//
// TParams: The endpoint's query parameters.
[AuthenticatesItself]
internal abstract class SpaceRepoEndpointBase<TParams>(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    where TParams : SpaceRepoParameters
{
    // The repos this service holds.
    protected ISpaceRepoHost RepoHost { get; } = repoHost;

    // Validates the addressing parameters and authenticates the request against them.
    //
    // Returns: The space and repo the request addresses.
    protected async Task<(SpaceUri Space, Did Repo)> AuthenticateAsync(
        TParams parameters, HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var space = SpaceRequestValidation.RequireSpace(parameters.Space);
        var repo = SpaceRequestValidation.Require(parameters.Repo, "repo");

        await authenticator.AuthenticateCredentialAsync(context, space, repo, cancellationToken).ConfigureAwait(false);

        return (space, repo);
    }

    // The error a repo host answers with when it holds nothing for a (space, repo) pair.
    //
    // It deliberately does not distinguish "member who has never written" from "not a member": the
    // protocol carries no reader set, and saying more would leak membership.
    protected static XrpcException RepoNotFound(SpaceUri space, Did repo) =>
        new(SpaceErrors.RepoNotFound, $"'{repo}' holds no repo in {space}.", HttpStatusCode.NotFound);
}

// Serves com.atproto.space.getRecord.
internal sealed class GetSpaceRecordEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<GetSpaceRecordParameters>(authenticator, repoHost),
      IXrpcQuery<GetSpaceRecordParameters, GetSpaceRecordResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetRecord);

    public async Task<GetSpaceRecordResponse> HandleAsync(
        GetSpaceRecordParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken).ConfigureAwait(false);
        var collection = SpaceRequestValidation.Require(parameters.Collection, "collection");
        var rkey = SpaceRequestValidation.Require(parameters.Rkey, "rkey");

        return await RepoHost.GetRecordAsync(space, repo, collection, rkey, cancellationToken).ConfigureAwait(false)
               ?? throw new XrpcException(
                   SpaceErrors.RecordNotFound,
                   $"No record at {collection}/{rkey}.",
                   HttpStatusCode.NotFound);
    }
}

// Serves com.atproto.space.listRecords.
internal sealed class ListSpaceRecordsEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<ListSpaceRecordsParameters>(authenticator, repoHost),
      IXrpcQuery<ListSpaceRecordsParameters, ListSpaceRecordsResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.ListRecords);

    public async Task<ListSpaceRecordsResponse> HandleAsync(
        ListSpaceRecordsParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken).ConfigureAwait(false);

        return await RepoHost.ListRecordsAsync(
            space,
            repo,
            parameters.Collection,
            parameters.Reverse ?? false,
            parameters.ExcludeValues ?? false,
            SpaceRequestValidation.Limit(parameters.Limit),
            parameters.Cursor,
            cancellationToken).ConfigureAwait(false);
    }
}

// Serves com.atproto.space.getLatestCommit.
internal sealed class GetSpaceLatestCommitEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<GetSpaceLatestCommitParameters>(authenticator, repoHost),
      IXrpcQuery<GetSpaceLatestCommitParameters, GetSpaceLatestCommitResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetLatestCommit);

    public async Task<GetSpaceLatestCommitResponse> HandleAsync(
        GetSpaceLatestCommitParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken).ConfigureAwait(false);

        var commit = await RepoHost.GetLatestCommitAsync(space, repo, cancellationToken).ConfigureAwait(false)
                     ?? throw RepoNotFound(space, repo);

        return new GetSpaceLatestCommitResponse { Commit = commit };
    }
}

// Serves com.atproto.space.listRepoOps.
internal sealed class ListSpaceRepoOpsEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<ListSpaceRepoOpsParameters>(authenticator, repoHost),
      IXrpcQuery<ListSpaceRepoOpsParameters, ListSpaceRepoOpsResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.ListRepoOps);

    public async Task<ListSpaceRepoOpsResponse> HandleAsync(
        ListSpaceRepoOpsParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken).ConfigureAwait(false);

        return await RepoHost.ListRepoOpsAsync(
                   space,
                   repo,
                   parameters.Since,
                   parameters.ExcludeValues ?? false,
                   SpaceRequestValidation.Limit(parameters.Limit, defaultLimit: 100),
                   parameters.Cursor,
                   cancellationToken).ConfigureAwait(false)
               ?? throw RepoNotFound(space, repo);
    }
}

// Serves com.atproto.space.listBlobs.
internal sealed class ListSpaceBlobsEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<ListSpaceBlobsParameters>(authenticator, repoHost),
      IXrpcQuery<ListSpaceBlobsParameters, ListSpaceBlobsResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.ListBlobs);

    public async Task<ListSpaceBlobsResponse> HandleAsync(
        ListSpaceBlobsParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken).ConfigureAwait(false);

        return await RepoHost.ListBlobsAsync(
            space,
            repo,
            parameters.Since,
            SpaceRequestValidation.Limit(parameters.Limit, defaultLimit: 500),
            parameters.Cursor,
            cancellationToken).ConfigureAwait(false);
    }
}

// Serves com.atproto.space.getRepo: an account's whole permissioned repo as a CAR.
internal sealed class GetSpaceRepoEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<GetSpaceRepoParameters>(authenticator, repoHost),
      IXrpcBlobQuery<GetSpaceRepoParameters>
{
    // The content type a repo CAR is served as.
    public const string CarContentType = "application/vnd.ipld.car";

    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetRepo);

    public async Task<XrpcBlobResult> HandleAsync(
        GetSpaceRepoParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken).ConfigureAwait(false);

        var car = await RepoHost.GetRepoAsync(
                      space, repo, parameters.ExcludeValues ?? false, cancellationToken).ConfigureAwait(false)
                  ?? throw RepoNotFound(space, repo);

        // Only what is left to read: a host handing back a shared, already-positioned stream
        // would otherwise declare a length longer than the body it writes.
        return new XrpcBlobResult(car, CarContentType, car.CanSeek ? car.Length - car.Position : null);
    }
}

// Serves com.atproto.space.getBlob: a blob referenced from a permissioned record.
internal sealed class GetSpaceBlobEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<GetSpaceBlobParameters>(authenticator, repoHost),
      IXrpcBlobQuery<GetSpaceBlobParameters>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetBlob);

    public async Task<XrpcBlobResult> HandleAsync(
        GetSpaceBlobParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken).ConfigureAwait(false);
        var cid = SpaceRequestValidation.Require(parameters.Cid, "cid");

        var blob = await RepoHost.GetBlobAsync(space, repo, cid, cancellationToken).ConfigureAwait(false)
                   ?? throw new XrpcException(
                       SpaceErrors.BlobNotFound,
                       $"'{repo}' references no blob {cid} in {space}.",
                       HttpStatusCode.NotFound);

        return new XrpcBlobResult(blob.Content, blob.MimeType, blob.Length);
    }
}

// Serves com.atproto.space.notifyCredentialRevoked: a space's authority telling this repo host that credentials it
// issued are revoked.
//
// Authenticated with service auth, not a space credential: the caller is the authority, not a reader. The token
// is checked by ServiceAuthVerifier under SpaceServerOptions.ClockSkew and MaxSingleUseTokenLifetime, for this
// method (lxm) and for an audience that is a repo DID, and then two more things, each refused with 403:
//
//   - the token's iss is the space's own authority. Only that DID mints credentials for the space, so only it
//     may revoke them, and the check is what keeps any other service from locking readers out of a space;
//   - the audience is an account hosted here, as the reference host requires. A host records nothing it was not
//     addressed to, and a service DID or an account elsewhere is no address.
//
// The jtis are opaque, and an unknown one is recorded like any other, since a revocation may reach a host before
// the credential does. Entries are kept for the longest a credential can verify (see SpaceCredentialRevocation),
// and the call is idempotent.
[AuthenticatesItself]
internal sealed class NotifyCredentialRevokedEndpoint(
    [FromKeyedServices(SpaceServerExtensions.DidResolverKey)] IDidResolver resolver,
    IJtiReplayStore replayStore,
    ISpaceRepoHost repoHost,
    ISpaceCredentialRevocationStore revocations,
    SpaceServerOptions options,
    TimeProvider? timeProvider = null)
    : IXrpcProcedureVoid<NotifyCredentialRevokedRequest>
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private readonly ServiceAuthVerifier _serviceAuth = new(
        resolver,
        replayStore,
        new ServiceAuthVerifierOptions { ClockSkew = options.ClockSkew, MaxTokenLifetime = options.MaxSingleUseTokenLifetime },
        timeProvider);

    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.NotifyCredentialRevoked);

    public async Task HandleAsync(
        NotifyCredentialRevokedRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Malformed input is refused before any auth check, as the Lexicon's own types would be.
        var space = SpaceRequestValidation.RequireSpace(input.Space);
        var credentials = input.Credentials;
        // An identifier no credential could carry would only take up room in the store.
        if (credentials is not { Count: >= 1 and <= NotifyCredentialRevokedRequest.MaxCredentials } || !credentials.All(Jwt.IsUsableTokenId))
            throw new XrpcException(
                XrpcErrors.InvalidRequest,
                $"The \"credentials\" field must hold between 1 and {NotifyCredentialRevokedRequest.MaxCredentials} credential identifiers (jti).");

        var caller = await VerifyCallerAsync(context, cancellationToken).ConfigureAwait(false);

        if (caller.Issuer != space.Authority)
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized,
                $"A credential revocation for {space} must be signed by its authority, not by '{caller.Issuer}'.",
                HttpStatusCode.Forbidden);

        // Looked up after the signature, so an unauthenticated caller cannot probe which accounts this host holds.
        if (!await repoHost.HostsAccountAsync(Did.Parse(caller.Audience), cancellationToken).ConfigureAwait(false))
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized,
                $"'{caller.Audience}' is not an account hosted here.",
                HttpStatusCode.Forbidden);

        await revocations.RevokeAsync(
            space, credentials, SpaceCredentialRevocation.RetainUntil(_timeProvider.GetUtcNow(), options), cancellationToken).ConfigureAwait(false);
    }

    // Verifies the request's service auth, whose audience is the repo DID it is addressed to, reporting a refusal
    // as SpaceErrors.NotAuthorized.
    private async Task<VerifiedServiceAuth> VerifyCallerAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var token = AuthorizationHeader.Bearer(context.Request)
            ?? throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, "A credential revocation is authenticated with a Bearer service auth token.");

        // The audience varies with the repo the authority addresses, so it is read from the token to hand to the
        // verifier, which refuses the token unless its signature covers that audience.
        if (!Jwt.TryDecode(token, out var decoded, out _) ||
            decoded.Payload.GetStringOrNull("aud") is not { } audience || !Did.TryParse(audience, out _))
            throw new SpaceVerificationException(
                SpaceErrors.NotAuthorized, "A credential revocation is addressed to a repo DID.");

        try
        {
            return await _serviceAuth.VerifyAsync(token, [audience], Nsid, cancellationToken).ConfigureAwait(false);
        }
        catch (ServiceAuthException ex)
        {
            throw new SpaceVerificationException(SpaceErrors.NotAuthorized, ex.ErrorMessage ?? ex.Error, ex);
        }
    }
}

// What a repo host retains a revocation for.
internal static class SpaceCredentialRevocation
{
    // The longest a credential can verify: its lifetime ceiling, plus the clock skew allowed at issuance and again
    // at expiry. A revocation dropped sooner would revive the credential. Bluesky's 3600 + 2 x 5 s = 3610 s.
    // Credentials are checked with SpaceTokens.DefaultClockSkew whatever ClockSkew says, so the larger counts.
    public static DateTimeOffset RetainUntil(DateTimeOffset now, SpaceServerOptions options) =>
        now + SpaceTokens.MaxCredentialLifetime
            + 2 * (options.ClockSkew > SpaceTokens.DefaultClockSkew ? options.ClockSkew : SpaceTokens.DefaultClockSkew);
}
