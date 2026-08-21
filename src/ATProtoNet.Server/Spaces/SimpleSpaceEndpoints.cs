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
public abstract class SimpleSpaceEndpointBase
{
    /// <summary>
    /// Creates the endpoint.
    /// </summary>
    /// <param name="callerResolver">Identifies the authenticated account.</param>
    /// <param name="store">The spaces and member lists this authority holds.</param>
    protected SimpleSpaceEndpointBase(ISpaceCallerResolver callerResolver, ISimpleSpaceStore store)
    {
        ArgumentNullException.ThrowIfNull(callerResolver);
        ArgumentNullException.ThrowIfNull(store);

        CallerResolver = callerResolver;
        Store = store;
    }

    /// <summary>Identifies the authenticated account.</summary>
    protected ISpaceCallerResolver CallerResolver { get; }

    /// <summary>The spaces and member lists this authority holds.</summary>
    protected ISimpleSpaceStore Store { get; }

    /// <summary>
    /// Loads a space the caller owns, or throws.
    /// </summary>
    /// <param name="spaceValue">The space URI from the request.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected async Task<SimpleSpaceRecord> RequireOwnedSpaceAsync(
        string? spaceValue, HttpContext context, CancellationToken cancellationToken)
    {
        var uri = SpaceRequestValidation.RequireSpace(spaceValue);
        var caller = CallerResolver.RequireCallerDid(context);

        var space = await Store.GetSpaceAsync(uri, cancellationToken);
        if (space is null || space.Deleted)
            throw NotFound(uri);

        // Answering NotSpaceOwner only to the owner would confirm a space exists to anyone who
        // guessed its URI, so a non-owner gets the same answer as for a space that is not there.
        return string.Equals(space.Owner, caller, StringComparison.Ordinal)
            ? space
            : throw NotFound(uri);
    }

    /// <summary>The error a <c>simplespace</c> method answers with for a space the caller may not see.</summary>
    /// <param name="space">The space that was addressed.</param>
    protected static XrpcException NotFound(SpaceUri space) =>
        new(SimpleSpaceErrors.SpaceNotFound, $"No such space: {space}.", StatusCodes.Status404NotFound);
}

/// <summary>Serves <c>com.atproto.simplespace.createSpace</c>.</summary>
[XrpcEndpoint(Nsid = SpaceNsids.CreateSimpleSpace)]
public sealed class CreateSimpleSpaceEndpoint
    : SimpleSpaceEndpointBase, IXrpcProcedure<CreateSimpleSpaceRequest, CreateSimpleSpaceResponse>
{
    /// <summary>Creates the endpoint.</summary>
    /// <param name="callerResolver">Identifies the authenticated account.</param>
    /// <param name="store">The spaces this authority holds.</param>
    public CreateSimpleSpaceEndpoint(ISpaceCallerResolver callerResolver, ISimpleSpaceStore store)
        : base(callerResolver, store)
    {
    }

    /// <inheritdoc/>
    public string Nsid => SpaceNsids.CreateSimpleSpace;

    /// <inheritdoc/>
    public async Task<CreateSimpleSpaceResponse> HandleAsync(
        CreateSimpleSpaceRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var caller = CallerResolver.RequireCallerDid(context);
        var type = SpaceRequestValidation.RequireNsid(input.Type, "type");

        // A TID when the caller names no key, so repeated creates do not collide.
        var skey = string.IsNullOrEmpty(input.Skey) ? Tid.NextString() : input.Skey;
        if (!SpaceUri.TryParse($"at://{caller}/space/{type}/{skey}", out var uri))
            throw new XrpcException("InvalidRequest", $"'{skey}' is not a valid space key.");

        RequireSupported(input.Policy, input.AppAccess);

        var created = await Store.CreateSpaceAsync(
            new SimpleSpaceRecord(uri, caller, input.Policy, input.AppAccess), cancellationToken);

        return created
            ? new CreateSimpleSpaceResponse { Uri = uri.Value }
            : throw new XrpcException(
                SimpleSpaceErrors.SpaceAlreadyExists, $"{uri} already exists.", StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// Rejects a policy variant this host does not implement, rather than storing one it could
    /// not enforce at mint time.
    /// </summary>
    internal static void RequireSupported(SimpleSpaceUserPolicy? policy, SimpleSpaceAppAccess? appAccess)
    {
        if (policy is not (null or PublicPolicy or MemberListPolicy or ManagingAppPolicy))
        {
            throw new XrpcException(
                SimpleSpaceErrors.UnsupportedPolicy,
                $"This host does not implement the '{policy.GetType().Name}' user policy.");
        }

        if (policy is ManagingAppPolicy managing)
            SpaceRequestValidation.RequireServiceIdentifier(managing.ManagingApp, "managingApp");

        if (appAccess is not (null or OpenAppAccess or AllowListAppAccess))
        {
            throw new XrpcException(
                SimpleSpaceErrors.UnsupportedAppAccess,
                $"This host does not implement the '{appAccess.GetType().Name}' app access variant.");
        }
    }
}

/// <summary>Serves <c>com.atproto.simplespace.updateSpace</c>.</summary>
[XrpcEndpoint(Nsid = SpaceNsids.UpdateSimpleSpace)]
public sealed class UpdateSimpleSpaceEndpoint
    : SimpleSpaceEndpointBase, IXrpcProcedureVoid<UpdateSimpleSpaceRequest>
{
    /// <summary>Creates the endpoint.</summary>
    /// <param name="callerResolver">Identifies the authenticated account.</param>
    /// <param name="store">The spaces this authority holds.</param>
    public UpdateSimpleSpaceEndpoint(ISpaceCallerResolver callerResolver, ISimpleSpaceStore store)
        : base(callerResolver, store)
    {
    }

    /// <inheritdoc/>
    public string Nsid => SpaceNsids.UpdateSimpleSpace;

    /// <inheritdoc/>
    public async Task HandleAsync(
        UpdateSimpleSpaceRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = await RequireOwnedSpaceAsync(input.Space, context, cancellationToken);
        CreateSimpleSpaceEndpoint.RequireSupported(input.Policy, input.AppAccess);

        // Omitted fields are left unchanged; a supplied one replaces that policy wholesale.
        await Store.UpdateSpaceAsync(
            space with { Policy = input.Policy ?? space.Policy, AppAccess = input.AppAccess ?? space.AppAccess },
            cancellationToken);
    }
}

/// <summary>Serves <c>com.atproto.simplespace.deleteSpace</c>.</summary>
/// <remarks>
/// Deleting a space stops the authority issuing credentials for it and deletes the authority's
/// own repo in it. Other members' repos are <em>not</em> deleted: a member's records are the
/// member's own data, and deleting the space does not entitle the authority to destroy them —
/// they simply become unreadable to everyone but the member's own account.
/// </remarks>
[XrpcEndpoint(Nsid = SpaceNsids.DeleteSimpleSpace)]
public sealed class DeleteSimpleSpaceEndpoint
    : SimpleSpaceEndpointBase, IXrpcProcedureVoid<DeleteSimpleSpaceRequest>
{
    private readonly SpaceWriteNotifier? _notifier;

    /// <summary>Creates the endpoint.</summary>
    /// <param name="callerResolver">Identifies the authenticated account.</param>
    /// <param name="store">The spaces this authority holds.</param>
    /// <param name="notifier">
    /// Tells registered syncers to drop their copies. Optional: a syncer that is never told
    /// learns on its next credential renewal, which answers
    /// <see cref="SpaceErrors.SpaceDeleted"/>.
    /// </param>
    public DeleteSimpleSpaceEndpoint(
        ISpaceCallerResolver callerResolver, ISimpleSpaceStore store, SpaceWriteNotifier? notifier = null)
        : base(callerResolver, store)
    {
        _notifier = notifier;
    }

    /// <inheritdoc/>
    public string Nsid => SpaceNsids.DeleteSimpleSpace;

    /// <inheritdoc/>
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

        if (!string.Equals(space.Owner, caller, StringComparison.Ordinal))
            throw NotFound(uri);

        // Notify before deleting, so the subscriber list is still readable.
        if (_notifier is not null)
            await _notifier.NotifySpaceDeletedAsync(uri, cancellationToken);

        await Store.DeleteSpaceAsync(uri, cancellationToken);
    }
}

/// <summary>Serves <c>com.atproto.simplespace.getSpace</c>.</summary>
[XrpcEndpoint(Nsid = SpaceNsids.GetSimpleSpace)]
public sealed class GetSimpleSpaceEndpoint
    : SimpleSpaceEndpointBase, IXrpcQuery<GetSimpleSpaceParameters, GetSimpleSpaceResponse>
{
    private readonly SpaceRequestAuthenticator _authenticator;

    /// <summary>Creates the endpoint.</summary>
    /// <param name="callerResolver">Identifies the authenticated account.</param>
    /// <param name="store">The spaces this authority holds.</param>
    /// <param name="authenticator">Verifies a space credential, for callers presenting one.</param>
    public GetSimpleSpaceEndpoint(
        ISpaceCallerResolver callerResolver, ISimpleSpaceStore store, SpaceRequestAuthenticator authenticator)
        : base(callerResolver, store)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        _authenticator = authenticator;
    }

    /// <inheritdoc/>
    public string Nsid => SpaceNsids.GetSimpleSpace;

    /// <inheritdoc/>
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
        if (!string.Equals(space.Owner, caller, StringComparison.Ordinal))
            await _authenticator.AuthenticateCredentialAsync(context, uri, cancellationToken);

        return new GetSimpleSpaceResponse
        {
            Uri = space.Uri.Value,
            Policy = space.Policy,
            AppAccess = space.AppAccess,
        };
    }
}

/// <summary>Serves <c>com.atproto.simplespace.addMember</c>.</summary>
[XrpcEndpoint(Nsid = SpaceNsids.AddSimpleSpaceMember)]
public sealed class AddSimpleSpaceMemberEndpoint
    : SimpleSpaceEndpointBase, IXrpcProcedureVoid<AddSimpleSpaceMemberRequest>
{
    /// <summary>Creates the endpoint.</summary>
    /// <param name="callerResolver">Identifies the authenticated account.</param>
    /// <param name="store">The spaces and member lists this authority holds.</param>
    public AddSimpleSpaceMemberEndpoint(ISpaceCallerResolver callerResolver, ISimpleSpaceStore store)
        : base(callerResolver, store)
    {
    }

    /// <inheritdoc/>
    public string Nsid => SpaceNsids.AddSimpleSpaceMember;

    /// <inheritdoc/>
    public async Task HandleAsync(
        AddSimpleSpaceMemberRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = await RequireOwnedSpaceAsync(input.Space, context, cancellationToken);
        var did = SpaceRequestValidation.RequireDid(input.Did, "did");

        await Store.AddMemberAsync(space.Uri, did, cancellationToken);
    }
}

/// <summary>Serves <c>com.atproto.simplespace.removeMember</c>.</summary>
/// <remarks>
/// Removing a member stops the authority minting <em>new</em> credentials for them. One already
/// issued stays valid until it expires, and records they wrote remain their own data in their
/// own repo.
/// </remarks>
[XrpcEndpoint(Nsid = SpaceNsids.RemoveSimpleSpaceMember)]
public sealed class RemoveSimpleSpaceMemberEndpoint
    : SimpleSpaceEndpointBase, IXrpcProcedureVoid<RemoveSimpleSpaceMemberRequest>
{
    /// <summary>Creates the endpoint.</summary>
    /// <param name="callerResolver">Identifies the authenticated account.</param>
    /// <param name="store">The spaces and member lists this authority holds.</param>
    public RemoveSimpleSpaceMemberEndpoint(ISpaceCallerResolver callerResolver, ISimpleSpaceStore store)
        : base(callerResolver, store)
    {
    }

    /// <inheritdoc/>
    public string Nsid => SpaceNsids.RemoveSimpleSpaceMember;

    /// <inheritdoc/>
    public async Task HandleAsync(
        RemoveSimpleSpaceMemberRequest input, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var space = await RequireOwnedSpaceAsync(input.Space, context, cancellationToken);
        var did = SpaceRequestValidation.RequireDid(input.Did, "did");

        await Store.RemoveMemberAsync(space.Uri, did, cancellationToken);
    }
}

/// <summary>Serves <c>com.atproto.simplespace.listMembers</c>.</summary>
/// <remarks>
/// The member list is host-internal state consulted at credential-mint time, so it is served to
/// the space's owner and to nobody else. It is never enumerated to the network —
/// <c>listRepos</c> returns writers, not readers.
/// </remarks>
[XrpcEndpoint(Nsid = SpaceNsids.ListSimpleSpaceMembers)]
public sealed class ListSimpleSpaceMembersEndpoint
    : SimpleSpaceEndpointBase, IXrpcQuery<ListSimpleSpaceMembersParameters, ListSimpleSpaceMembersResponse>
{
    /// <summary>Creates the endpoint.</summary>
    /// <param name="callerResolver">Identifies the authenticated account.</param>
    /// <param name="store">The spaces and member lists this authority holds.</param>
    public ListSimpleSpaceMembersEndpoint(ISpaceCallerResolver callerResolver, ISimpleSpaceStore store)
        : base(callerResolver, store)
    {
    }

    /// <inheritdoc/>
    public string Nsid => SpaceNsids.ListSimpleSpaceMembers;

    /// <inheritdoc/>
    public async Task<ListSimpleSpaceMembersResponse> HandleAsync(
        ListSimpleSpaceMembersParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var space = await RequireOwnedSpaceAsync(parameters.Space, context, cancellationToken);

        return await Store.ListMembersAsync(
            space.Uri, SpaceRequestValidation.Limit(parameters.Limit), parameters.Cursor, cancellationToken);
    }
}
