using ATProtoNet.Http;
using ATProtoNet.Spaces;

namespace ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;

/// <summary>
/// Client for <c>com.atproto.simplespace.*</c> — the space-management implementation every PDS
/// must support.
/// </summary>
/// <remarks>
/// <para>The permissioned data protocol deliberately does not specify how spaces are created or
/// how an authority decides who may read one. Those belong to a space-management implementation
/// sitting above the protocol, identified by its own Lexicon namespace.
/// <c>simplespace</c> is the baseline every account's PDS is required to offer, so an
/// application can build against it without standing up a bespoke space service. Its spaces are
/// anchored on a user's own DID and governed by an explicit member list, or by the
/// <see cref="PublicPolicy"/> and <see cref="ManagingAppPolicy"/> alternatives.</para>
/// <para>It is neither the only permitted implementation nor a privileged one. Other space types
/// may define their own management implementations and are full protocol participants; they are
/// simply hosted on their own space services rather than on a PDS.</para>
/// <para>The procedures require an OAuth credential with the relevant <c>manage</c> scope. The
/// read queries need only read access — <see cref="GetSpaceAsync(string, CancellationToken)"/> accepts a
/// <c>read_self</c> grant or a space credential, and <see cref="ListMembersAsync(string, int?, string, CancellationToken)"/> a
/// <c>read_self</c> grant.</para>
/// </remarks>
public sealed class SimpleSpaceClient
{
    private readonly XrpcClient _xrpc;

    internal SimpleSpaceClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Creates a space anchored on the authenticated user's DID, who becomes its owner.
    /// </summary>
    /// <param name="type">The space type NSID.</param>
    /// <param name="skey">The space key. A TID is generated when omitted.</param>
    /// <param name="policy">
    /// How to authorize requesting users. Defaults to <see cref="MemberListPolicy"/>.
    /// </param>
    /// <param name="appAccess">
    /// How to authorize requesting apps. Defaults to <see cref="OpenAppAccess"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CreateSimpleSpaceResponse> CreateSpaceAsync(
        string type,
        string? skey = null,
        SimpleSpaceUserPolicy? policy = null,
        SimpleSpaceAppAccess? appAccess = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var request = new CreateSimpleSpaceRequest
        {
            Type = type,
            Skey = skey,
            Policy = policy ?? new MemberListPolicy(),
            AppAccess = appAccess ?? new OpenAppAccess(),
        };

        return await _xrpc.ProcedureAsync<CreateSimpleSpaceRequest, CreateSimpleSpaceResponse>(
            "com.atproto.simplespace.createSpace", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Updates a space's configuration. Omitted arguments are left unchanged; a supplied one
    /// replaces that policy wholesale.
    /// </summary>
    /// <param name="space">The space to update.</param>
    /// <param name="policy">The new user policy, or <see langword="null"/> to leave it.</param>
    /// <param name="appAccess">The new app access policy, or <see langword="null"/> to leave it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UpdateSpaceAsync(
        string space,
        SimpleSpaceUserPolicy? policy = null,
        SimpleSpaceAppAccess? appAccess = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);

        var request = new UpdateSimpleSpaceRequest { Space = space, Policy = policy, AppAccess = appAccess };
        await _xrpc.ProcedureAsync<UpdateSimpleSpaceRequest>(
            "com.atproto.simplespace.updateSpace", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Deletes a space. The authenticated user must be its owner. Idempotent.
    /// </summary>
    /// <param name="space">The space to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para>The authority stops issuing credentials and deletes its own repo in the space.
    /// Afterwards every read and write answers <see cref="Space.SpaceErrors.SpaceNotFound"/>,
    /// and <c>getSpaceCredential</c> answers <see cref="Space.SpaceErrors.SpaceDeleted"/> so a
    /// syncer that missed the deletion notification still learns to drop its copy.</para>
    /// <para>Other members' repos are flagged as belonging to a deleted space rather than
    /// erased. A member's records are the member's own data, and deleting the space does not
    /// entitle the authority to destroy them — they simply become unreadable to everyone but
    /// the member's own account.</para>
    /// </remarks>
    public async Task DeleteSpaceAsync(string space, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);

        var request = new DeleteSimpleSpaceRequest { Space = space };
        await _xrpc.ProcedureAsync<DeleteSimpleSpaceRequest>(
            "com.atproto.simplespace.deleteSpace", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Describes a space and its configuration. Served by the space host.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetSimpleSpaceResponse> GetSpaceAsync(
        string space, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);

        var parameters = new XrpcParams().Add("space", space);
        return _xrpc.QueryAsync<GetSimpleSpaceResponse>(
            "com.atproto.simplespace.getSpace", parameters, cancellationToken);
    }

    /// <summary>
    /// Adds a member to a space's member list.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="did">The DID of the member to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The member list is host-internal state consulted at credential-mint time when the space's
    /// policy is <see cref="MemberListPolicy"/>. It is not a synced protocol structure and is
    /// never enumerated to the network.
    /// </remarks>
    public async Task AddMemberAsync(
        string space, string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        var request = new AddSimpleSpaceMemberRequest { Space = space, Did = did };
        await _xrpc.ProcedureAsync<AddSimpleSpaceMemberRequest>(
            "com.atproto.simplespace.addMember", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Removes a member from a space's member list.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="did">The DID of the member to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Removal stops the authority minting <em>new</em> credentials for that member. A
    /// credential already issued stays valid until it expires, and any records the member wrote
    /// remain their own data in their own repo.
    /// </remarks>
    public async Task RemoveMemberAsync(
        string space, string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        var request = new RemoveSimpleSpaceMemberRequest { Space = space, Did = did };
        await _xrpc.ProcedureAsync<RemoveSimpleSpaceMemberRequest>(
            "com.atproto.simplespace.removeMember", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Lists a space's member list. Must be called on the space authority's PDS.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="limit">Maximum number of results per page (1–1000, default 100).</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Requires OAuth with a covering read grant; a space credential is not sufficient, so
    /// members hosted elsewhere cannot enumerate the list. This reflects the
    /// <c>simplespace</c> member list, not a protocol-level reader set — the protocol has none.
    /// </remarks>
    public Task<ListSimpleSpaceMembersResponse> ListMembersAsync(
        string space,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);

        var parameters = new XrpcParams()
            .Add("space", space)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<ListSimpleSpaceMembersResponse>(
            "com.atproto.simplespace.listMembers", parameters, cancellationToken);
    }

    /// <summary>
    /// Enumerates a space's whole member list, following pagination.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="pageSize">Results per request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<SimpleSpaceMember> EnumerateMembersAsync(
        string space,
        int? pageSize = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var page = await ListMembersAsync(space, pageSize, cursor, cancellationToken);
            foreach (var member in page.Members)
                yield return member;

            cursor = page.Members.Count == 0 ? null : page.Cursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    /// <summary>
    /// Asks a space's managing app whether to authorize a requesting user.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="user">The DID of the requesting user.</param>
    /// <param name="clientId">The attested client ID, if a client attestation was presented.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Unlike the other <c>simplespace</c> methods this one is served by the managing app rather
    /// than by a PDS, and is called by the space authority at credential-mint time when the
    /// space's policy is <see cref="ManagingAppPolicy"/>. The authority issues it with itself as
    /// <c>iss</c> and the managing app as <c>aud</c>, so the app can verify the call genuinely
    /// came from the space's authority. Included here for applications implementing the
    /// managing-app side, and for authorities written against this SDK.
    /// </remarks>
    public Task<CheckUserAccessResponse> CheckUserAccessAsync(
        string space,
        string user,
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);

        var parameters = new XrpcParams()
            .Add("space", space)
            .Add("user", user)
            .Add("clientId", clientId);

        return _xrpc.QueryAsync<CheckUserAccessResponse>(
            "com.atproto.simplespace.checkUserAccess", parameters, cancellationToken);
    }

    // ── SpaceUri overloads ───────────────────────────────────────

    /// <inheritdoc cref="GetSpaceAsync(string, CancellationToken)"/>
    /// <param name="space">The space.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetSimpleSpaceResponse> GetSpaceAsync(
        SpaceUri space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        return GetSpaceAsync(space.Value, cancellationToken);
    }

    /// <inheritdoc cref="AddMemberAsync(string, string, CancellationToken)"/>
    /// <param name="space">The space.</param>
    /// <param name="did">The DID of the member to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task AddMemberAsync(SpaceUri space, string did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        return AddMemberAsync(space.Value, did, cancellationToken);
    }

    /// <inheritdoc cref="RemoveMemberAsync(string, string, CancellationToken)"/>
    /// <param name="space">The space.</param>
    /// <param name="did">The DID of the member to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RemoveMemberAsync(SpaceUri space, string did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        return RemoveMemberAsync(space.Value, did, cancellationToken);
    }
}
