using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// A space as <c>com.atproto.simplespace</c> stores it.
/// </summary>
/// <param name="Uri">The space.</param>
/// <param name="Owner">The DID of the account that created it, and the only one that may administer it.</param>
/// <param name="ReadPolicy">How the authority decides whether to authorize a user to read the space.</param>
/// <param name="WritePolicy">
/// How the authority decides whether to track a user's writes and forward their write
/// notifications.
/// </param>
/// <param name="AppAccess">How the authority decides whether to authorize a requesting app. Applies to reads only.</param>
/// <param name="Deleted">
/// Whether the space has been deleted. Deletion is a flag rather than a removal, because a
/// deleted space must keep answering <see cref="ATProtoNet.Lexicon.Com.AtProto.Space.SpaceErrors.SpaceDeleted"/>
/// on credential renewal — that is how a syncer that missed the deletion notification learns to
/// drop its copy, and a space that simply vanished would answer <c>SpaceNotFound</c>, which says
/// nothing and leaves the copy in place.
/// </param>
public sealed record SimpleSpaceRecord(
    SpaceUri Uri,
    Did Owner,
    SimpleSpaceUserPolicy ReadPolicy,
    SimpleSpaceUserPolicy WritePolicy,
    SimpleSpaceAppAccess AppAccess,
    bool Deleted = false);

/// <summary>
/// The state <c>com.atproto.simplespace</c> keeps: the spaces an authority hosts, and each
/// space's member list.
/// </summary>
/// <remarks>
/// <para>The member list is host-internal state, consulted at credential-mint time under a
/// member-list read policy and on each write notification under a member-list write policy. Each
/// member carries the two as independent flags. It is not a synced protocol structure and is
/// never enumerated to the network — <c>listRepos</c> returns the writers the write policy
/// admitted, and the protocol has no way to enumerate readers at all.</para>
/// <para>Removing a member, or clearing their read flag, stops the authority minting
/// <em>new</em> credentials for them. One already issued stays valid until it expires (two
/// hours by default), and records they wrote remain their own data in their own repo.</para>
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
    /// Adds a member, or replaces an existing member's access. Both flags are replaced.
    /// </summary>
    /// <param name="space">The space. A space that does not exist is left alone.</param>
    /// <param name="did">The member's DID.</param>
    /// <param name="read">Whether the member may read under a member-list read policy.</param>
    /// <param name="write">Whether the member's writes are tracked under a member-list write policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PutMemberAsync(
        SpaceUri space, Did did, bool read, bool write, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a member. Idempotent.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="did">The member's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveMemberAsync(SpaceUri space, Did did, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one account's entry on a space's member list.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="did">The account's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The member and their access, or <see langword="null"/> when they are not on the list.</returns>
    Task<SimpleSpaceMember?> GetMemberAsync(
        SpaceUri space, Did did, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a space's members and their access, for the owner's own administration.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ListSimpleSpaceMembersResponse> ListMembersAsync(
        SpaceUri space, int limit, string? cursor, CancellationToken cancellationToken = default);
}
