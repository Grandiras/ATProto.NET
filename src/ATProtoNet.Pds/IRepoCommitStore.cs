namespace ATProtoNet.Pds;

/// <summary>
/// The signed head of a repository — everything a relay needs to know about its current state.
/// </summary>
public sealed class RepoCommitState
{
    /// <summary>The DID of the repository.</summary>
    public required string Did { get; init; }

    /// <summary>The CID of the signed commit block (base32, <c>bafyrei…</c>).</summary>
    public required string CommitCid { get; init; }

    /// <summary>The commit revision (a TID).</summary>
    public required string Rev { get; init; }

    /// <summary>The CID of the MST root referenced by the commit's <c>data</c> field.</summary>
    public required string DataCid { get; init; }

    /// <summary>The DAG-CBOR bytes of the signed commit block.</summary>
    public required byte[] CommitBlock { get; init; }

    /// <summary>When this commit was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Persists the signed head of each hosted repository.
/// <para>
/// Kept separate from <see cref="IRepoStore"/> so that a host can pair the default in-memory
/// head store with a durable record store (or vice versa) without either interface growing
/// concerns that belong to the other.
/// </para>
/// </summary>
public interface IRepoCommitStore
{
    /// <summary>Gets the current head for a DID, or <c>null</c> if the repo has never committed.</summary>
    Task<RepoCommitState?> GetAsync(string did, CancellationToken cancellationToken = default);

    /// <summary>Stores a new head, replacing any previous one.</summary>
    Task SetAsync(RepoCommitState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists repository heads in ascending DID order for <c>com.atproto.sync.listRepos</c>.
    /// </summary>
    /// <param name="limit">Maximum number of repos to return.</param>
    /// <param name="cursor">The DID to resume after, or <c>null</c> to start from the beginning.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<RepoCommitState>> ListAsync(
        int limit, string? cursor, CancellationToken cancellationToken = default);

    /// <summary>Removes the head for a DID (account deletion).</summary>
    Task DeleteAsync(string did, CancellationToken cancellationToken = default);
}
