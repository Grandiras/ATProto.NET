using System.Net;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;

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
