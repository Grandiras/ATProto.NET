using System.Net;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// The shared shape of the repo-host read endpoints: verify the credential, then serve the
/// (space, repo) pair it names.
/// </summary>
/// <remarks>
/// The credential is checked against the space the <em>request</em> names, so a credential for
/// one space cannot be used to read another even on a host that serves both. Nothing else is
/// re-decided here — the authority already made the access decision, and a repo host holds no
/// state with which to second-guess it.
/// </remarks>
/// <typeparam name="TParams">The endpoint's query parameters.</typeparam>
[AuthenticatesItself]
internal abstract class SpaceRepoEndpointBase<TParams>(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    where TParams : SpaceRepoParameters
{
    /// <summary>The repos this service holds.</summary>
    protected ISpaceRepoHost RepoHost { get; } = repoHost;

    /// <summary>
    /// Validates the addressing parameters and authenticates the request against them.
    /// </summary>
    /// <returns>The space and repo the request addresses.</returns>
    protected async Task<(SpaceUri Space, Did Repo)> AuthenticateAsync(
        TParams parameters, HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var space = SpaceRequestValidation.RequireSpace(parameters.Space);
        var repo = SpaceRequestValidation.Require(parameters.Repo, "repo");

        await authenticator.AuthenticateCredentialAsync(context, space, cancellationToken);

        return (space, repo);
    }

    /// <summary>The error a repo host answers with when it holds nothing for a (space, repo) pair.</summary>
    /// <remarks>
    /// It deliberately does not distinguish "member who has never written" from "not a member":
    /// the protocol carries no reader set, and saying more would leak membership.
    /// </remarks>
    protected static XrpcException RepoNotFound(SpaceUri space, Did repo) =>
        new(SpaceErrors.RepoNotFound, $"'{repo}' holds no repo in {space}.", HttpStatusCode.NotFound);
}

/// <summary>Serves <c>com.atproto.space.getRecord</c>.</summary>
internal sealed class GetSpaceRecordEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<GetSpaceRecordParameters>(authenticator, repoHost),
      IXrpcQuery<GetSpaceRecordParameters, GetSpaceRecordResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetRecord);

    public async Task<GetSpaceRecordResponse> HandleAsync(
        GetSpaceRecordParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken);
        var collection = SpaceRequestValidation.Require(parameters.Collection, "collection");
        var rkey = SpaceRequestValidation.Require(parameters.Rkey, "rkey");

        return await RepoHost.GetRecordAsync(space, repo, collection, rkey, cancellationToken)
               ?? throw new XrpcException(
                   SpaceErrors.RecordNotFound,
                   $"No record at {collection}/{rkey}.",
                   HttpStatusCode.NotFound);
    }
}

/// <summary>Serves <c>com.atproto.space.listRecords</c>.</summary>
internal sealed class ListSpaceRecordsEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<ListSpaceRecordsParameters>(authenticator, repoHost),
      IXrpcQuery<ListSpaceRecordsParameters, ListSpaceRecordsResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.ListRecords);

    public async Task<ListSpaceRecordsResponse> HandleAsync(
        ListSpaceRecordsParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken);

        return await RepoHost.ListRecordsAsync(
            space,
            repo,
            parameters.Collection,
            parameters.Reverse ?? false,
            parameters.ExcludeValues ?? false,
            SpaceRequestValidation.Limit(parameters.Limit),
            parameters.Cursor,
            cancellationToken);
    }
}

/// <summary>Serves <c>com.atproto.space.getLatestCommit</c>.</summary>
internal sealed class GetSpaceLatestCommitEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<GetSpaceLatestCommitParameters>(authenticator, repoHost),
      IXrpcQuery<GetSpaceLatestCommitParameters, GetSpaceLatestCommitResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetLatestCommit);

    public async Task<GetSpaceLatestCommitResponse> HandleAsync(
        GetSpaceLatestCommitParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken);

        var commit = await RepoHost.GetLatestCommitAsync(space, repo, cancellationToken)
                     ?? throw RepoNotFound(space, repo);

        return new GetSpaceLatestCommitResponse { Commit = commit };
    }
}

/// <summary>Serves <c>com.atproto.space.listRepoOps</c>.</summary>
internal sealed class ListSpaceRepoOpsEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<ListSpaceRepoOpsParameters>(authenticator, repoHost),
      IXrpcQuery<ListSpaceRepoOpsParameters, ListSpaceRepoOpsResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.ListRepoOps);

    public async Task<ListSpaceRepoOpsResponse> HandleAsync(
        ListSpaceRepoOpsParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken);

        return await RepoHost.ListRepoOpsAsync(
                   space,
                   repo,
                   parameters.Since,
                   parameters.ExcludeValues ?? false,
                   SpaceRequestValidation.Limit(parameters.Limit, defaultLimit: 100),
                   parameters.Cursor,
                   cancellationToken)
               ?? throw RepoNotFound(space, repo);
    }
}

/// <summary>Serves <c>com.atproto.space.listBlobs</c>.</summary>
internal sealed class ListSpaceBlobsEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<ListSpaceBlobsParameters>(authenticator, repoHost),
      IXrpcQuery<ListSpaceBlobsParameters, ListSpaceBlobsResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.ListBlobs);

    public async Task<ListSpaceBlobsResponse> HandleAsync(
        ListSpaceBlobsParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken);

        return await RepoHost.ListBlobsAsync(
            space,
            repo,
            parameters.Since,
            SpaceRequestValidation.Limit(parameters.Limit, defaultLimit: 500),
            parameters.Cursor,
            cancellationToken);
    }
}

/// <summary>
/// Serves <c>com.atproto.space.getRepo</c>: an account's whole permissioned repo as a CAR.
/// </summary>
internal sealed class GetSpaceRepoEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<GetSpaceRepoParameters>(authenticator, repoHost),
      IXrpcBlobQuery<GetSpaceRepoParameters>
{
    /// <summary>The content type a repo CAR is served as.</summary>
    public const string CarContentType = "application/vnd.ipld.car";

    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetRepo);

    public async Task<XrpcBlobResult> HandleAsync(
        GetSpaceRepoParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken);

        var car = await RepoHost.GetRepoAsync(
                      space, repo, parameters.ExcludeValues ?? false, cancellationToken)
                  ?? throw RepoNotFound(space, repo);

        // Only what is left to read: a host handing back a shared, already-positioned stream
        // would otherwise declare a length longer than the body it writes.
        return new XrpcBlobResult(car, CarContentType, car.CanSeek ? car.Length - car.Position : null);
    }
}

/// <summary>
/// Serves <c>com.atproto.space.getBlob</c>: a blob referenced from a permissioned record.
/// </summary>
internal sealed class GetSpaceBlobEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    : SpaceRepoEndpointBase<GetSpaceBlobParameters>(authenticator, repoHost),
      IXrpcBlobQuery<GetSpaceBlobParameters>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetBlob);

    public async Task<XrpcBlobResult> HandleAsync(
        GetSpaceBlobParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        var (space, repo) = await AuthenticateAsync(parameters, context, cancellationToken);
        var cid = SpaceRequestValidation.Require(parameters.Cid, "cid");

        var blob = await RepoHost.GetBlobAsync(space, repo, cid, cancellationToken)
                   ?? throw new XrpcException(
                       SpaceErrors.BlobNotFound,
                       $"'{repo}' references no blob {cid} in {space}.",
                       HttpStatusCode.NotFound);

        return new XrpcBlobResult(blob.Content, blob.MimeType, blob.Length);
    }
}
