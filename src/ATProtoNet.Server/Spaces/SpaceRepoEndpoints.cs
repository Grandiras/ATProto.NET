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
public abstract class SpaceRepoEndpointBase<TParams>
    where TParams : SpaceRepoParameters
{
    /// <summary>
    /// Creates the endpoint.
    /// </summary>
    /// <param name="authenticator">Verifies the presented credential and its DPoP proof.</param>
    /// <param name="repoHost">The repos this service holds.</param>
    protected SpaceRepoEndpointBase(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(repoHost);

        Authenticator = authenticator;
        RepoHost = repoHost;
    }

    /// <summary>Verifies the presented credential and its DPoP proof.</summary>
    protected SpaceRequestAuthenticator Authenticator { get; }

    /// <summary>The repos this service holds.</summary>
    protected ISpaceRepoHost RepoHost { get; }

    /// <summary>
    /// Validates the addressing parameters and authenticates the request against them.
    /// </summary>
    /// <param name="parameters">The request's query parameters.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The space and repo the request addresses.</returns>
    protected async Task<(SpaceUri Space, Did Repo)> AuthenticateAsync(
        TParams parameters, HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var space = SpaceRequestValidation.RequireSpace(parameters.Space);
        var repo = SpaceRequestValidation.Require(parameters.Repo, "repo");

        await Authenticator.AuthenticateCredentialAsync(context, space, cancellationToken);

        return (space, repo);
    }

    /// <summary>The error a repo host answers with when it holds nothing for a (space, repo) pair.</summary>
    /// <param name="space">The space that was addressed.</param>
    /// <param name="repo">The repo that was addressed.</param>
    /// <remarks>
    /// It deliberately does not distinguish "member who has never written" from "not a member":
    /// the protocol carries no reader set, and saying more would leak membership.
    /// </remarks>
    protected static XrpcException RepoNotFound(SpaceUri space, Did repo) =>
        new(SpaceErrors.RepoNotFound, $"'{repo}' holds no repo in {space}.", HttpStatusCode.NotFound);
}

/// <summary>Serves <c>com.atproto.space.getRecord</c>.</summary>
public sealed class GetSpaceRecordEndpoint
    : SpaceRepoEndpointBase<GetSpaceRecordParameters>, IXrpcQuery<GetSpaceRecordParameters, GetSpaceRecordResponse>
{
    /// <summary>Creates the endpoint.</summary>
    /// <param name="authenticator">Verifies the presented credential.</param>
    /// <param name="repoHost">The repos this service holds.</param>
    public GetSpaceRecordEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
        : base(authenticator, repoHost)
    {
    }

    /// <inheritdoc/>
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetRecord);

    /// <inheritdoc/>
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
public sealed class ListSpaceRecordsEndpoint
    : SpaceRepoEndpointBase<ListSpaceRecordsParameters>,
      IXrpcQuery<ListSpaceRecordsParameters, ListSpaceRecordsResponse>
{
    /// <summary>Creates the endpoint.</summary>
    /// <param name="authenticator">Verifies the presented credential.</param>
    /// <param name="repoHost">The repos this service holds.</param>
    public ListSpaceRecordsEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
        : base(authenticator, repoHost)
    {
    }

    /// <inheritdoc/>
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.ListRecords);

    /// <inheritdoc/>
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
public sealed class GetSpaceLatestCommitEndpoint
    : SpaceRepoEndpointBase<GetSpaceLatestCommitParameters>,
      IXrpcQuery<GetSpaceLatestCommitParameters, GetSpaceLatestCommitResponse>
{
    /// <summary>Creates the endpoint.</summary>
    /// <param name="authenticator">Verifies the presented credential.</param>
    /// <param name="repoHost">The repos this service holds.</param>
    public GetSpaceLatestCommitEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
        : base(authenticator, repoHost)
    {
    }

    /// <inheritdoc/>
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetLatestCommit);

    /// <inheritdoc/>
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
public sealed class ListSpaceRepoOpsEndpoint
    : SpaceRepoEndpointBase<ListSpaceRepoOpsParameters>,
      IXrpcQuery<ListSpaceRepoOpsParameters, ListSpaceRepoOpsResponse>
{
    /// <summary>Creates the endpoint.</summary>
    /// <param name="authenticator">Verifies the presented credential.</param>
    /// <param name="repoHost">The repos this service holds.</param>
    public ListSpaceRepoOpsEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
        : base(authenticator, repoHost)
    {
    }

    /// <inheritdoc/>
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.ListRepoOps);

    /// <inheritdoc/>
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
public sealed class ListSpaceBlobsEndpoint
    : SpaceRepoEndpointBase<ListSpaceBlobsParameters>,
      IXrpcQuery<ListSpaceBlobsParameters, ListSpaceBlobsResponse>
{
    /// <summary>Creates the endpoint.</summary>
    /// <param name="authenticator">Verifies the presented credential.</param>
    /// <param name="repoHost">The repos this service holds.</param>
    public ListSpaceBlobsEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
        : base(authenticator, repoHost)
    {
    }

    /// <inheritdoc/>
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.ListBlobs);

    /// <inheritdoc/>
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
public sealed class GetSpaceRepoEndpoint
    : SpaceRepoEndpointBase<GetSpaceRepoParameters>, IXrpcBlobQuery<GetSpaceRepoParameters>
{
    /// <summary>The content type a repo CAR is served as.</summary>
    public const string CarContentType = "application/vnd.ipld.car";

    /// <summary>Creates the endpoint.</summary>
    /// <param name="authenticator">Verifies the presented credential.</param>
    /// <param name="repoHost">The repos this service holds.</param>
    public GetSpaceRepoEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
        : base(authenticator, repoHost)
    {
    }

    /// <inheritdoc/>
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetRepo);

    /// <inheritdoc/>
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
public sealed class GetSpaceBlobEndpoint
    : SpaceRepoEndpointBase<GetSpaceBlobParameters>, IXrpcBlobQuery<GetSpaceBlobParameters>
{
    /// <summary>Creates the endpoint.</summary>
    /// <param name="authenticator">Verifies the presented credential.</param>
    /// <param name="repoHost">The repos this service holds.</param>
    public GetSpaceBlobEndpoint(SpaceRequestAuthenticator authenticator, ISpaceRepoHost repoHost)
        : base(authenticator, repoHost)
    {
    }

    /// <inheritdoc/>
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetBlob);

    /// <inheritdoc/>
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
