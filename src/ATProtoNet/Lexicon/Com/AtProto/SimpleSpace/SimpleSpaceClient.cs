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
/// <see cref="PublicPolicy"/> and <see cref="ManagingAppPolicy"/> alternatives — separately for
/// reading (who the authority mints credentials for) and writing (whose writes it tracks and
/// forwards).</para>
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
    /// <param name="readPolicy">
    /// How to authorize users to read the space. Defaults to <see cref="MemberListPolicy"/>.
    /// </param>
    /// <param name="writePolicy">
    /// How to decide whose writes the authority tracks and forwards. Defaults to
    /// <see cref="MemberListPolicy"/>.
    /// </param>
    /// <param name="appAccess">
    /// How to authorize requesting apps. Defaults to <see cref="OpenAppAccess"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// All three policies are required on the wire, so the defaults are always sent. The write
    /// policy does not stop anyone writing to their own repo; it decides whether the authority
    /// lists them in the writer set and forwards their write notifications to syncers.
    /// </remarks>
    public async Task<CreateSimpleSpaceResponse> CreateSpaceAsync(
        string type,
        string? skey = null,
        SimpleSpaceUserPolicy? readPolicy = null,
        SimpleSpaceUserPolicy? writePolicy = null,
        SimpleSpaceAppAccess? appAccess = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var request = new CreateSimpleSpaceRequest
        {
            Type = type,
            Skey = skey,
            ReadPolicy = readPolicy ?? new MemberListPolicy(),
            WritePolicy = writePolicy ?? new MemberListPolicy(),
            AppAccess = appAccess ?? new OpenAppAccess(),
        };

        return await _xrpc.ProcedureAsync<CreateSimpleSpaceResponse>(
            "com.atproto.simplespace.createSpace", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Updates a space's configuration. Omitted arguments are left unchanged; a supplied one
    /// replaces that policy wholesale.
    /// </summary>
    /// <param name="space">The space to update.</param>
    /// <param name="readPolicy">The new read policy, or <see langword="null"/> to leave it.</param>
    /// <param name="writePolicy">The new write policy, or <see langword="null"/> to leave it.</param>
    /// <param name="appAccess">The new app access policy, or <see langword="null"/> to leave it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UpdateSpaceAsync(
        string space,
        SimpleSpaceUserPolicy? readPolicy = null,
        SimpleSpaceUserPolicy? writePolicy = null,
        SimpleSpaceAppAccess? appAccess = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);

        var request = new UpdateSimpleSpaceRequest
        {
            Space = space,
            ReadPolicy = readPolicy,
            WritePolicy = writePolicy,
            AppAccess = appAccess,
        };

        await _xrpc.ProcedureAsync(
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
        await _xrpc.ProcedureAsync(
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
            "com.atproto.simplespace.getSpace", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Adds a member to a space's member list, or replaces an existing member's access.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="did">The DID of the member.</param>
    /// <param name="read">Whether the member may read under a member-list read policy.</param>
    /// <param name="write">
    /// Whether the member's writes are tracked under a member-list write policy.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para>An upsert: both flags are replaced every time, so to change one pass the other's
    /// current value too. A member with neither flag stays on the list but is admitted to
    /// nothing; <see cref="RemoveMemberAsync(string, string, CancellationToken)"/> takes them off
    /// it.</para>
    /// <para>The member list is host-internal state. It is not a synced protocol structure and
    /// is never enumerated to the network.</para>
    /// </remarks>
    public async Task PutMemberAsync(
        string space, string did, bool read, bool write, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        var request = new PutSimpleSpaceMemberRequest { Space = space, Did = did, Read = read, Write = write };
        await _xrpc.ProcedureAsync(
            "com.atproto.simplespace.putMember", request, cancellationToken: cancellationToken);
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
        await _xrpc.ProcedureAsync(
            "com.atproto.simplespace.removeMember", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Lists a space's member list, with each member's read and write access. Must be called on
    /// the space authority's PDS.
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
            "com.atproto.simplespace.listMembers", parameters, cancellationToken: cancellationToken);
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
    /// Asks a space's managing app whether to authorize a user to read or write the space.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="user">The DID of the user.</param>
    /// <param name="access">
    /// The kind of access being checked: <see cref="SimpleSpaceAccess.Read"/> or
    /// <see cref="SimpleSpaceAccess.Write"/>.
    /// </param>
    /// <param name="clientId">
    /// The attested client ID, if a client attestation was presented. Omit it for write checks,
    /// which have no app behind them.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Unlike the other <c>simplespace</c> methods this one is served by the managing app rather
    /// than by a PDS. The space authority calls it when the matching policy is a
    /// <see cref="ManagingAppPolicy"/> — with <c>read</c> at credential-mint time, and with
    /// <c>write</c> when a write notification arrives. It issues the call with itself as
    /// <c>iss</c> and the managing app's service identifier as <c>aud</c>, so the app can verify
    /// the call genuinely came from the space's authority. Included here for applications
    /// implementing the managing-app side, and for authorities written against this SDK.
    /// </remarks>
    public Task<CheckUserAccessResponse> CheckUserAccessAsync(
        string space,
        string user,
        string access,
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(access);

        var parameters = new XrpcParams()
            .Add("space", space)
            .Add("user", user)
            .Add("access", access)
            .Add("clientId", clientId);

        return _xrpc.QueryAsync<CheckUserAccessResponse>(
            "com.atproto.simplespace.checkUserAccess", parameters, cancellationToken: cancellationToken);
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

    /// <inheritdoc cref="PutMemberAsync(string, string, bool, bool, CancellationToken)"/>
    /// <param name="space">The space.</param>
    /// <param name="did">The DID of the member.</param>
    /// <param name="read">Whether the member may read under a member-list read policy.</param>
    /// <param name="write">
    /// Whether the member's writes are tracked under a member-list write policy.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task PutMemberAsync(
        SpaceUri space, string did, bool read, bool write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        return PutMemberAsync(space.Value, did, read, write, cancellationToken);
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
