using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// A space as <c>com.atproto.simplespace</c> stores it.
/// </summary>
/// <param name="Uri">The space.</param>
/// <param name="Owner">The DID of the account that created it, and the only one that may administer it.</param>
/// <param name="Policy">How the authority decides whether to authorize a requesting user.</param>
/// <param name="AppAccess">How the authority decides whether to authorize a requesting app.</param>
/// <param name="Deleted">
/// Whether the space has been deleted. Deletion is a flag rather than a removal, because a
/// deleted space must keep answering <see cref="ATProtoNet.Lexicon.Com.AtProto.Space.SpaceErrors.SpaceDeleted"/>
/// on credential renewal — that is how a syncer that missed the deletion notification learns to
/// drop its copy, and a space that simply vanished would answer <c>SpaceNotFound</c>, which says
/// nothing and leaves the copy in place.
/// </param>
public sealed record SimpleSpaceRecord(
    SpaceUri Uri,
    string Owner,
    SimpleSpaceUserPolicy Policy,
    SimpleSpaceAppAccess AppAccess,
    bool Deleted = false);

/// <summary>
/// The state <c>com.atproto.simplespace</c> keeps: the spaces an authority hosts, and each
/// space's member list.
/// </summary>
/// <remarks>
/// <para>The member list is host-internal state consulted at credential-mint time. It is not a
/// synced protocol structure and is never enumerated to the network — <c>listRepos</c> returns
/// writers, not readers, and the protocol has no way to enumerate readers at all.</para>
/// <para>Removing a member stops the authority minting <em>new</em> credentials for them. One
/// already issued stays valid until it expires (two hours by default), and records they wrote
/// remain their own data in their own repo.</para>
/// </remarks>
public interface ISimpleSpaceStore
{
    /// <summary>
    /// Reads a space's configuration.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The space, or <see langword="null"/> when it has never existed.</returns>
    Task<SimpleSpaceRecord?> GetSpaceAsync(SpaceUri space, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a space.
    /// </summary>
    /// <param name="space">The space to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> when a space with this owner, type, and key already exists.</returns>
    Task<bool> CreateSpaceAsync(SimpleSpaceRecord space, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a space's configuration.
    /// </summary>
    /// <param name="space">The space, with its new configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateSpaceAsync(SimpleSpaceRecord space, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a space deleted. Idempotent.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteSpaceAsync(SpaceUri space, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a member. Idempotent.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="did">The member's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddMemberAsync(SpaceUri space, string did, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a member. Idempotent.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="did">The member's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveMemberAsync(SpaceUri space, string did, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether an account is on a space's member list.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="did">The account's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsMemberAsync(SpaceUri space, string did, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a space's members, for the owner's own administration.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListSimpleSpaceMembersResponse> ListMembersAsync(
        SpaceUri space, int limit, string? cursor, CancellationToken cancellationToken = default);
}
