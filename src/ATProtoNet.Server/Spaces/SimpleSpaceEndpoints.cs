using System.Net;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Server.Xrpc;
using ATProtoNet.Spaces;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// The shared shape of the <c>com.atproto.simplespace</c> administration endpoints: an
/// authenticated account acting on a space it owns.
/// </summary>
/// <remarks>
/// These are administered over the owner's own OAuth session rather than with a space
/// credential — creating a space is what happens before any credential for it can exist — and
/// every one of them is scoped to the caller's own DID. A space's authority is its owner's DID,
/// so an account can only ever create spaces under itself.
/// </remarks>
internal abstract class SimpleSpaceEndpointBase(ISpaceCallerResolver callerResolver, ISimpleSpaceStore store)
{
    /// <summary>Identifies the authenticated account.</summary>
    protected ISpaceCallerResolver CallerResolver { get; } = callerResolver;

    /// <summary>The spaces and member lists this authority holds.</summary>
    protected ISimpleSpaceStore Store { get; } = store;

    /// <summary>
    /// Loads a space the caller owns, or throws.
    /// </summary>
    protected async Task<SimpleSpaceRecord> RequireOwnedSpaceAsync(
        SpaceUri? spaceValue, HttpContext context, CancellationToken cancellationToken)
    {
        var uri = SpaceRequestValidation.RequireSpace(spaceValue);
        var caller = CallerResolver.RequireCallerDid(context);

        var space = await Store.GetSpaceAsync(uri, cancellationToken);
        if (space is null || space.Deleted)
            throw NotFound(uri);

        // Answering NotSpaceOwner only to the owner would confirm a space exists to anyone who
        // guessed its URI, so a non-owner gets the same answer as for a space that is not there.
        return space.Owner == caller
            ? space
            : throw NotFound(uri);
    }

    /// <summary>The error a <c>simplespace</c> method answers with for a space the caller may not see.</summary>
    protected static XrpcException NotFound(SpaceUri space) =>
        new(SimpleSpaceErrors.SpaceNotFound, $"No such space: {space}.", HttpStatusCode.NotFound);
}

/// <summary>Serves <c>com.atproto.simplespace.createSpace</c>.</summary>
internal sealed class CreateSimpleSpaceEndpoint(ISpaceCallerResolver callerResolver, ISimpleSpaceStore store)
    : SimpleSpaceEndpointBase(callerResolver, store),
      IXrpcProcedure<CreateSimpleSpaceRequest, CreateSimpleSpaceResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.CreateSimpleSpace);

    public async Task<CreateSimpleSpaceResponse> HandleAsync(
        CreateSimpleSpaceRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var caller = CallerResolver.RequireCallerDid(context);
        var type = SpaceRequestValidation.Require(input.Type, "type");

        // A TID when the caller names no key, so repeated creates do not collide.
        var uri = SpaceUri.Create(caller, type, input.Skey ?? RecordKey.NewTid());

        // Required on the wire; a JSON null gets past deserialization, so it is caught here rather
        // than stored as a space with no policy to enforce.
        var readPolicy = input.ReadPolicy ?? throw Missing("readPolicy");
        var writePolicy = input.WritePolicy ?? throw Missing("writePolicy");
        var appAccess = input.AppAccess ?? throw Missing("appAccess");

        RequireSupported(readPolicy, writePolicy, appAccess);

        var created = await Store.CreateSpaceAsync(
            new SimpleSpaceRecord(uri, caller, readPolicy, writePolicy, appAccess), cancellationToken);

        return created
            ? new CreateSimpleSpaceResponse { Uri = uri }
            : throw new XrpcException(
                SimpleSpaceErrors.SpaceAlreadyExists, $"{uri} already exists.", HttpStatusCode.Conflict);
    }

    private static XrpcException Missing(string name) =>
        new(XrpcErrors.InvalidRequest, $"The \"{name}\" field is required.");

    /// <summary>
    /// Rejects a policy variant this host does not implement — one a newer schema added, which
    /// reads as an <c>Unknown…</c> variant — rather than storing one it could not enforce.
    /// </summary>
    internal static void RequireSupported(
        SimpleSpaceUserPolicy? readPolicy, SimpleSpaceUserPolicy? writePolicy, SimpleSpaceAppAccess? appAccess)
    {
        RequireSupported(readPolicy);
        RequireSupported(writePolicy);

        if (appAccess is not (null or OpenAppAccess or AllowListAppAccess))
        {
            throw new XrpcException(
                SimpleSpaceErrors.UnsupportedAppAccess,
                $"This host does not implement the '{VariantName(appAccess)}' app access variant.");
        }
    }

    private static void RequireSupported(SimpleSpaceUserPolicy? policy)
    {
        if (policy is not (null or PublicPolicy or MemberListPolicy or ManagingAppPolicy))
        {
            throw new XrpcException(
                SimpleSpaceErrors.UnsupportedPolicy,
                $"This host does not implement the '{VariantName(policy)}' user policy.");
        }

        if (policy is ManagingAppPolicy managing)
            SpaceRequestValidation.RequireServiceIdentifier(managing.ManagingApp, "managingApp");
    }

    private static string VariantName(object variant) =>
        variant is Serialization.IUnknownUnionVariant unknown ? unknown.Type : variant.GetType().Name;
}

/// <summary>Serves <c>com.atproto.simplespace.updateSpace</c>.</summary>
internal sealed class UpdateSimpleSpaceEndpoint(ISpaceCallerResolver callerResolver, ISimpleSpaceStore store)
    : SimpleSpaceEndpointBase(callerResolver, store), IXrpcProcedureVoid<UpdateSimpleSpaceRequest>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.UpdateSimpleSpace);

    public async Task HandleAsync(
        UpdateSimpleSpaceRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = await RequireOwnedSpaceAsync(input.Space, context, cancellationToken);
        CreateSimpleSpaceEndpoint.RequireSupported(input.ReadPolicy, input.WritePolicy, input.AppAccess);

        // Omitted fields are left unchanged; a supplied one replaces that policy wholesale.
        await Store.UpdateSpaceAsync(
            space with
            {
                ReadPolicy = input.ReadPolicy ?? space.ReadPolicy,
                WritePolicy = input.WritePolicy ?? space.WritePolicy,
                AppAccess = input.AppAccess ?? space.AppAccess,
            },
            cancellationToken);
    }
}

/// <summary>Serves <c>com.atproto.simplespace.deleteSpace</c>.</summary>
/// <remarks>
/// Deleting a space stops the authority issuing credentials for it and deletes the authority's
/// own repo in it. Other members' repos are <em>not</em> deleted: a member's records are the
/// member's own data, and deleting the space does not entitle the authority to destroy them —
/// they simply become unreadable to everyone but the member's own account. Registered syncers
/// are told to drop their copies when a <see cref="SpaceWriteNotifier"/> is registered; one that
/// is never told learns on its next credential renewal, which answers
/// <see cref="SpaceErrors.SpaceDeleted"/>.
/// </remarks>
internal sealed class DeleteSimpleSpaceEndpoint(
    ISpaceCallerResolver callerResolver, ISimpleSpaceStore store, SpaceWriteNotifier? notifier = null)
    : SimpleSpaceEndpointBase(callerResolver, store), IXrpcProcedureVoid<DeleteSimpleSpaceRequest>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.DeleteSimpleSpace);

    public async Task HandleAsync(
        DeleteSimpleSpaceRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var uri = SpaceRequestValidation.RequireSpace(input.Space);
        var caller = CallerResolver.RequireCallerDid(context);

        var space = await Store.GetSpaceAsync(uri, cancellationToken);

        // Idempotent: deleting a space that is already gone is a success, not a 404.
        if (space is null || space.Deleted)
            return;

        if (space.Owner != caller)
            throw NotFound(uri);

        // Notify before deleting, so the subscriber list is still readable.
        if (notifier is not null)
            await notifier.NotifySpaceDeletedAsync(uri, cancellationToken);

        await Store.DeleteSpaceAsync(uri, cancellationToken);
    }
}

/// <summary>Serves <c>com.atproto.simplespace.getSpace</c>.</summary>
internal sealed class GetSimpleSpaceEndpoint(
    ISpaceCallerResolver callerResolver, ISimpleSpaceStore store, SpaceRequestAuthenticator authenticator)
    : SimpleSpaceEndpointBase(callerResolver, store), IXrpcQuery<GetSimpleSpaceParameters, GetSimpleSpaceResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.GetSimpleSpace);

    public async Task<GetSimpleSpaceResponse> HandleAsync(
        GetSimpleSpaceParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var uri = SpaceRequestValidation.RequireSpace(parameters.Space);
        var space = await Store.GetSpaceAsync(uri, cancellationToken);
        if (space is null || space.Deleted)
            throw NotFound(uri);

        // Two ways in: the owner's own session, or a credential the authority already issued for
        // this space — a reader that holds one has already been admitted, so describing the
        // space to it discloses nothing new.
        var caller = CallerResolver.GetCallerDid(context);
        if (space.Owner != caller)
            await authenticator.AuthenticateCredentialAsync(context, uri, cancellationToken);

        return new GetSimpleSpaceResponse
        {
            Uri = space.Uri,
            ReadPolicy = space.ReadPolicy,
            WritePolicy = space.WritePolicy,
            AppAccess = space.AppAccess,
        };
    }
}

/// <summary>Serves <c>com.atproto.simplespace.putMember</c>.</summary>
/// <remarks>
/// An upsert: both access flags are replaced every time. Clearing a member's read flag stops the
/// authority minting <em>new</em> credentials for them, as removal does; clearing the write flag
/// stops it recording their writes and forwarding their notifications from the next one on.
/// </remarks>
internal sealed class PutSimpleSpaceMemberEndpoint(ISpaceCallerResolver callerResolver, ISimpleSpaceStore store)
    : SimpleSpaceEndpointBase(callerResolver, store), IXrpcProcedureVoid<PutSimpleSpaceMemberRequest>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.PutSimpleSpaceMember);

    public async Task HandleAsync(
        PutSimpleSpaceMemberRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = await RequireOwnedSpaceAsync(input.Space, context, cancellationToken);
        var did = SpaceRequestValidation.Require(input.Did, "did");

        await Store.PutMemberAsync(space.Uri, did, input.Read, input.Write, cancellationToken);
    }
}

/// <summary>Serves <c>com.atproto.simplespace.removeMember</c>.</summary>
/// <remarks>
/// Removing a member stops the authority minting <em>new</em> credentials for them. One already
/// issued stays valid until it expires, and records they wrote remain their own data in their
/// own repo.
/// </remarks>
internal sealed class RemoveSimpleSpaceMemberEndpoint(ISpaceCallerResolver callerResolver, ISimpleSpaceStore store)
    : SimpleSpaceEndpointBase(callerResolver, store), IXrpcProcedureVoid<RemoveSimpleSpaceMemberRequest>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.RemoveSimpleSpaceMember);

    public async Task HandleAsync(
        RemoveSimpleSpaceMemberRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = await RequireOwnedSpaceAsync(input.Space, context, cancellationToken);
        var did = SpaceRequestValidation.Require(input.Did, "did");

        await Store.RemoveMemberAsync(space.Uri, did, cancellationToken);
    }
}

/// <summary>Serves <c>com.atproto.simplespace.listMembers</c>.</summary>
/// <remarks>
/// The member list is host-internal state, so it is served — with each member's read and write
/// access — to the space's owner and to nobody else. It is never enumerated to the network —
/// <c>listRepos</c> returns the writers the write policy admitted, not the member list.
/// </remarks>
internal sealed class ListSimpleSpaceMembersEndpoint(ISpaceCallerResolver callerResolver, ISimpleSpaceStore store)
    : SimpleSpaceEndpointBase(callerResolver, store),
      IXrpcQuery<ListSimpleSpaceMembersParameters, ListSimpleSpaceMembersResponse>
{
    public static Nsid Nsid { get; } = Nsid.Parse(SpaceNsids.ListSimpleSpaceMembers);

    public async Task<ListSimpleSpaceMembersResponse> HandleAsync(
        ListSimpleSpaceMembersParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var space = await RequireOwnedSpaceAsync(parameters.Space, context, cancellationToken);

        return await Store.ListMembersAsync(
            space.Uri, SpaceRequestValidation.Limit(parameters.Limit), parameters.Cursor, cancellationToken);
    }
}
