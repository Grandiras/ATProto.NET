using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>A service registered to receive a space's write notifications.</summary>
/// <param name="Service">
/// The service identifier, a DID with an optional fragment naming the entry in its DID document
/// to deliver to (e.g. <c>did:web:syncer.example.com#atproto_space_syncer</c>).
/// </param>
/// <param name="ExpiresAt">
/// When the registration lapses. A syncer renews before this; an authority that let
/// registrations live forever would keep delivering to services that stopped listening years
/// ago.
/// </param>
public sealed record SpaceNotifySubscriber(string Service, DateTimeOffset ExpiresAt);

/// <summary>A space revision the authority assigned to an accepted write.</summary>
/// <param name="SpaceRev">The revision assigned: strictly greater than every one assigned before it in the space.</param>
/// <param name="PrevSpaceRev">The revision assigned before <paramref name="SpaceRev"/>, or <see langword="null"/> for the space's first.</param>
public sealed record SpaceWriteSequence(Tid SpaceRev, Tid? PrevSpaceRev);

/// <summary>The state a space <em>authority</em> keeps: which repos hold data in each space, and who has asked to be told when they advance.</summary>
/// <remarks>
/// <para>Notably absent is the member list. Membership is a space-management concern
/// (<see cref="ISimpleSpaceStore"/> in the baseline implementation), not a protocol structure —
/// <c>listRepos</c> returns <em>writers</em>, and the protocol does not enumerate readers at
/// all.</para>
/// <para>The writer set is only what the authority claims, kept current by the
/// <c>notifyWrite</c> calls it receives. A listed account's repo host is the source of truth,
/// which is why each entry carries revisions: the space-wide <c>spaceRev</c>, assigned here in the
/// order writes are accepted, lets a syncer resume from a checkpoint and re-sync only what advanced.</para>
/// </remarks>
public interface ISpaceAuthorityStore
{
    /// <summary>Reports whether a space exists, and whether it has been deleted.</summary>
    Task<SpaceAccessOutcome> GetSpaceStateAsync(SpaceUri space, CancellationToken cancellationToken = default);

    /// <summary>Lists the accounts that hold data in a space — the sync boundary, not an access-control list.</summary>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="cursor">An exclusive <c>spaceRev</c> checkpoint, or <see langword="null"/> for the first page.</param>
    /// <returns>
    /// The entries after <paramref name="cursor"/> in ascending <c>spaceRev</c> order. A non-empty page
    /// carries its last entry's <c>spaceRev</c> as its cursor (even a short page); an empty one has none.
    /// </returns>
    Task<ListSpaceReposResponse> ListReposAsync(
        SpaceUri space, int limit, string? cursor, CancellationToken cancellationToken = default);

    /// <summary>Records that a repo advanced and sequences it in the space.</summary>
    /// <param name="repoDid">The DID of the account whose repo advanced.</param>
    /// <param name="repoRev">The repo's revision after the write.</param>
    /// <param name="hash">The repo's commit hash after the write.</param>
    /// <returns>
    /// The space revision assigned, or <see langword="null"/> when <paramref name="repoRev"/> is not
    /// after the one already recorded for the repo: a stale or duplicate notification, which is neither
    /// recorded nor sequenced, and which the caller must not forward.
    /// </returns>
    /// <remarks>
    /// <para>This is also how an account joins the writer set: the first notification for a repo adds
    /// it, since the set is defined as the accounts that have written at least one record.</para>
    /// <para>Allocating the revision and updating the writer are one atomic step. Revisions are strictly
    /// increasing across every writer in the order the steps commit, which is what makes a
    /// <c>listRepos</c> checkpoint safe to resume from: nothing can later appear below one.</para>
    /// </remarks>
    Task<SpaceWriteSequence?> RecordWriteAsync(
        SpaceUri space, Did repoDid, Tid repoRev, byte[] hash, CancellationToken cancellationToken = default);

    /// <summary>Registers a service to receive a space's write notifications, or renews an existing registration.</summary>
    /// <param name="service">The subscriber's service identifier.</param>
    /// <param name="expiresAt">When the registration lapses.</param>
    Task RegisterNotifyAsync(
        SpaceUri space, string service, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    /// <summary>Removes a notification registration. Idempotent.</summary>
    /// <param name="service">The subscriber's service identifier.</param>
    Task UnregisterNotifyAsync(SpaceUri space, string service, CancellationToken cancellationToken = default);

    /// <summary>Lists the services currently registered for a space's notifications.</summary>
    /// <returns>The subscribers whose registrations have not lapsed.</returns>
    Task<IReadOnlyList<SpaceNotifySubscriber>> ListSubscribersAsync(
        SpaceUri space, CancellationToken cancellationToken = default);
}

/// <summary>The reads a <em>repo host</em> serves for the permissioned repos it holds.</summary>
/// <remarks>
/// <para>Every method here is reached with a space credential the authority issued, verified by
/// <see cref="SpaceCredentialVerifier"/> before the call. A repo host does not re-evaluate the
/// authority's decision and has no way to: it holds no member list, and the protocol enumerates
/// no readers.</para>
/// <para><see cref="SpaceErrors.RepoNotFound"/> deliberately does not distinguish a member that
/// has never written from an account that is not a member at all. The protocol carries no reader
/// set, so a repo host has nothing else to say — and saying more would leak membership.</para>
/// </remarks>
public interface ISpaceRepoHost
{
    /// <summary>Reports whether an account's repo is hosted here.</summary>
    /// <param name="did">The account's DID.</param>
    /// <returns><see langword="true"/> when this host holds the account, whatever its status: deactivated and taken-down accounts count.</returns>
    /// <remarks>
    /// Answers whether a credential revocation addressed to <paramref name="did"/> is meant for this
    /// host (<c>notifyCredentialRevoked</c>), so a host never records a revocation it was not sent.
    /// </remarks>
    Task<bool> HostsAccountAsync(Did did, CancellationToken cancellationToken = default);

    /// <summary>Reads one record.</summary>
    /// <param name="repoDid">The DID of the account whose repo to read.</param>
    /// <param name="collection">The record collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    /// <returns>The record, or <see langword="null"/> when there is none at that path.</returns>
    Task<GetSpaceRecordResponse?> GetRecordAsync(
        SpaceUri space, Did repoDid, Nsid collection, RecordKey rkey,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the records in an account's repo within a space.</summary>
    /// <param name="repoDid">The DID of the account whose repo to list.</param>
    /// <param name="collection">Restrict to one collection, or <see langword="null"/> for all.</param>
    /// <param name="reverse">Reverse the order of the returned records.</param>
    /// <param name="excludeValues">Return only metadata.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="cursor">Pagination cursor.</param>
    Task<ListSpaceRecordsResponse> ListRecordsAsync(
        SpaceUri space, Did repoDid, Nsid? collection, bool reverse, bool excludeValues, int limit,
        string? cursor, CancellationToken cancellationToken = default);

    /// <summary>Returns an account's current signed commit for a space.</summary>
    /// <param name="repoDid">The DID of the account.</param>
    /// <returns>The commit, or <see langword="null"/> when the account holds no repo here.</returns>
    Task<SignedSpaceCommit?> GetLatestCommitAsync(
        SpaceUri space, Did repoDid, CancellationToken cancellationToken = default);

    /// <summary>Serializes an account's whole permissioned repo as a CAR, for full-state recovery.</summary>
    /// <param name="repoDid">The DID of the account.</param>
    /// <param name="excludeValues">Write only the commit and index roots, with no record blocks.</param>
    /// <returns>The CAR bytes, or <see langword="null"/> when the account holds no repo here.</returns>
    /// <remarks>
    /// Build the CAR with <see cref="SpaceRepoCar.Serialize"/>; its two-root layout is what lets
    /// a consumer verify the whole thing in one pass.
    /// </remarks>
    Task<Stream?> GetRepoAsync(
        SpaceUri space, Did repoDid, bool excludeValues, CancellationToken cancellationToken = default);

    /// <summary>Returns a page of an account's operation log for a space.</summary>
    /// <param name="repoDid">The DID of the account.</param>
    /// <param name="since">Return operations after this revision.</param>
    /// <param name="excludeValues">Return operation metadata only.</param>
    /// <param name="limit">Maximum number of operations.</param>
    /// <param name="cursor">Opaque pagination cursor; takes precedence over <paramref name="since"/>.</param>
    /// <returns>The page, or <see langword="null"/> when the account holds no repo here.</returns>
    /// <remarks>
    /// The oplog is a transport optimization, not a committed data structure: a host may compact
    /// or drop it, and a <paramref name="since"/> it can no longer serve is an expected condition
    /// rather than an error. Answer such a request by returning the window that <em>is</em>
    /// retained together with the current commit — the syncer's digest comparison will disagree
    /// and it will recover through <see cref="GetRepoAsync"/> on its own.
    /// </remarks>
    Task<ListSpaceRepoOpsResponse?> ListRepoOpsAsync(
        SpaceUri space, Did repoDid, Tid? since, bool excludeValues, int limit, string? cursor,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the CIDs of blobs referenced by an account's records within a space.</summary>
    /// <param name="repoDid">The DID of the account.</param>
    /// <param name="since">List blobs referenced since this revision.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="cursor">Pagination cursor.</param>
    Task<ListSpaceBlobsResponse> ListBlobsAsync(
        SpaceUri space, Did repoDid, Tid? since, int limit, string? cursor,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a blob referenced from a record in a space.</summary>
    /// <param name="repoDid">The DID of the account whose repo references the blob.</param>
    /// <param name="cid">The blob's CID.</param>
    /// <returns>The blob, or <see langword="null"/> when the repo does not reference it in this space.</returns>
    /// <remarks>
    /// The reference check is the access check. A blob is not uploaded through this namespace —
    /// it is uploaded with <c>com.atproto.repo.uploadBlob</c> and merely referenced by a
    /// permissioned record — so serving one on the basis of its CID alone would hand out any
    /// blob the account holds to anyone with a credential for any of its spaces.
    /// </remarks>
    Task<SpaceBlobContent?> GetBlobAsync(
        SpaceUri space, Did repoDid, Cid cid, CancellationToken cancellationToken = default);
}

/// <summary>A blob served from a permissioned repo.</summary>
/// <param name="Content">The blob bytes. The routing disposes the stream after writing it.</param>
/// <param name="MimeType">The blob's MIME type, as recorded when it was uploaded.</param>
/// <param name="Length">The blob's length when known.</param>
public sealed record SpaceBlobContent(Stream Content, string MimeType, long? Length = null);
