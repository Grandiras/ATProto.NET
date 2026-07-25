using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Repo;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Pds;

/// <summary>
/// Turns the PDS's record store into a real AT Protocol repository: DAG-CBOR record blocks
/// addressed by proper CIDv1, arranged in a Merkle Search Tree, rooted in a commit signed with
/// the account's repo signing key, and announced on the firehose.
/// <para>
/// The MST is rebuilt from the full record set on every commit rather than mutated in place.
/// That keeps <see cref="IRepoStore"/> free of block-storage concerns — a store only has to
/// hold records — at the cost of an O(n) rebuild per write, which is the right trade for the
/// self-hosted, few-thousand-record repositories this package targets. Because the MST is a
/// pure function of its key/value set, the rebuilt tree is byte-identical to an incrementally
/// maintained one.
/// </para>
/// </summary>
public sealed class PdsRepoManager
{
    private readonly IAccountStore _accounts;
    private readonly IRepoStore _repos;
    private readonly IRepoCommitStore _commits;
    private readonly PdsSequencer _sequencer;
    private readonly PdsOptions _options;
    private readonly ILogger? _logger;

    // Commits for one repo must be strictly ordered: two concurrent writes that both rebuilt
    // from the same record set would produce two commits claiming the same `since`.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _repoLocks = new(StringComparer.Ordinal);
    private readonly PdsRevisionGenerator _revisions = new();

    // Set the first time the record store turns out not to support enumeration, so the warning
    // is logged once rather than on every write.
    private int _enumerationUnsupported;

    /// <summary>Creates a repo manager.</summary>
    /// <param name="accounts">The account store.</param>
    /// <param name="repos">The record and blob store.</param>
    /// <param name="commits">The repository head store.</param>
    /// <param name="sequencer">The firehose sequencer.</param>
    /// <param name="options">PDS configuration.</param>
    /// <param name="logger">
    /// Optional logger. Used to report a record store that cannot enumerate a repository, which
    /// downgrades this manager to a no-op rather than failing writes — see
    /// <see cref="CommitAsync"/>.
    /// </param>
    public PdsRepoManager(
        IAccountStore accounts,
        IRepoStore repos,
        IRepoCommitStore commits,
        PdsSequencer sequencer,
        PdsOptions options,
        ILogger? logger = null)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _repos = repos ?? throw new ArgumentNullException(nameof(repos));
        _commits = commits ?? throw new ArgumentNullException(nameof(commits));
        _sequencer = sequencer ?? throw new ArgumentNullException(nameof(sequencer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    /// <summary>The sequencer this manager publishes firehose events through.</summary>
    public PdsSequencer Sequencer => _sequencer;

    /// <summary>
    /// Whether the configured <see cref="IRepoStore"/> has been found not to implement
    /// <see cref="IRepoStore.ListAllRecordsAsync"/>. While this is <c>true</c> the PDS keeps
    /// serving repo CRUD but produces no signed commits and no firehose events.
    /// </summary>
    public bool IsRepositoryEnumerationUnsupported => Volatile.Read(ref _enumerationUnsupported) != 0;

    // ──────────────────────────────────────────────────────────
    //  Commits
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the repository from its current records, signs a new commit, stores it as the
    /// head, and publishes a <c>#commit</c> firehose event describing <paramref name="ops"/>.
    /// </summary>
    /// <param name="did">The repository DID.</param>
    /// <param name="ops">The operations this commit represents, for the firehose event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new head, or <c>null</c> when the repository could not be committed.</returns>
    /// <remarks>
    /// Returns <c>null</c> — rather than throwing — when the configured <see cref="IRepoStore"/>
    /// does not implement <see cref="IRepoStore.ListAllRecordsAsync"/>. A store written before
    /// federation support therefore keeps serving plain repo CRUD; it just produces no signed
    /// commits and nothing on the firehose. The gap is logged once and reported by
    /// <see cref="IsRepositoryEnumerationUnsupported"/>, and the sync endpoints that genuinely
    /// need the record set still surface the underlying <see cref="NotSupportedException"/>.
    /// </remarks>
    public async Task<RepoCommitState?> CommitAsync(
        string did, IReadOnlyList<PdsRepoOp> ops, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        ArgumentNullException.ThrowIfNull(ops);

        if (IsRepositoryEnumerationUnsupported) return null;

        var gate = _repoLocks.GetOrAdd(did, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CommitCoreAsync(did, ops, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Ensures the repository has a signed head, creating the initial empty commit if it has
    /// none. Called when an account is created so that relays can crawl it immediately.
    /// </summary>
    /// <param name="did">The repository DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The repository head, or <c>null</c> when the record store cannot enumerate the repository
    /// — see <see cref="CommitAsync"/>.
    /// </returns>
    public async Task<RepoCommitState?> EnsureRepoAsync(
        string did, CancellationToken cancellationToken = default)
    {
        var existing = await _commits.GetAsync(did, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return existing;

        return await CommitAsync(did, [], cancellationToken).ConfigureAwait(false);
    }

    private async Task<RepoCommitState?> CommitCoreAsync(
        string did, IReadOnlyList<PdsRepoOp> ops, CancellationToken cancellationToken)
    {
        var account = await _accounts.GetByDidAsync(did, cancellationToken).ConfigureAwait(false)
            ?? throw new PdsException("RepoNotFound", $"No account for {did}.");

        var previous = await _commits.GetAsync(did, cancellationToken).ConfigureAwait(false);

        RepoSnapshot snapshot;
        try
        {
            snapshot = await BuildSnapshotAsync(did, cancellationToken).ConfigureAwait(false);
        }
        catch (NotSupportedException ex)
        {
            // The store predates federation and cannot hand back the full record set, so no MST
            // and no signed commit can be built. Writes must still succeed — that is the
            // compatibility promise on IRepoStore.ListAllRecordsAsync — so degrade to a
            // non-federating PDS instead of failing every createRecord/putRecord/deleteRecord.
            if (Interlocked.Exchange(ref _enumerationUnsupported, 1) == 0)
            {
                _logger?.LogWarning(ex,
                    "{StoreType} does not implement IRepoStore.ListAllRecordsAsync, so this PDS " +
                    "cannot build a Merkle Search Tree or sign commits. Repository CRUD keeps " +
                    "working, but no repo is federated: nothing is published on the firehose and " +
                    "the com.atproto.sync.* endpoints will fail. Implement ListAllRecordsAsync " +
                    "(and ListBlobCidsAsync) to federate.",
                    _repos.GetType().Name);
            }

            return null;
        }

        // Clamped against the stored head so the revision still advances across a restart, when
        // this process has issued nothing yet.
        var rev = _revisions.Next(previous?.Rev);
        using var signingKey = ImportSigningKey(account.SigningKey);

        var commit = new RepoCommit
        {
            Did = did,
            Data = snapshot.MstRootCid,
            Rev = rev,
            Prev = null,
        }.Sign(signingKey);

        var state = new RepoCommitState
        {
            Did = did,
            CommitCid = commit.Cid.Value,
            Rev = rev,
            DataCid = commit.DataCid.Value,
            CommitBlock = commit.Bytes,
        };

        await _commits.SetAsync(state, cancellationToken).ConfigureAwait(false);

        PublishCommit(did, commit, previous, snapshot, ops);
        return state;
    }

    private void PublishCommit(
        string did,
        SignedRepoCommit commit,
        RepoCommitState? previous,
        RepoSnapshot snapshot,
        IReadOnlyList<PdsRepoOp> ops)
    {
        // The event carries the covering proof for the touched paths — the root plus the MST
        // nodes on the way down to each of them — rather than the whole tree, so frame size
        // grows with the number of operations and the log of the repo size instead of with the
        // repo itself. Inlining every node would push any repo of real size past
        // MaxFirehoseFrameBytes on nearly every write, degrading consumers to a tooBig refetch.
        var (_, proofBlocks) = snapshot.Mst.SerializeProof(ops.Select(op => op.Path));

        var carBlocks = new List<CarBlock> { new(commit.BinaryCid, commit.Bytes) };
        foreach (var (cidString, bytes) in proofBlocks)
            carBlocks.Add(new CarBlock(CidComputation.DecodeCidString(cidString), bytes));
        foreach (var op in ops)
        {
            if (op.Cid is null) continue;
            var key = CidComputation.EncodeCidToString(op.Cid);
            if (snapshot.RecordBlocks.TryGetValue(key, out var recordBytes))
                carBlocks.Add(new CarBlock(op.Cid, recordBytes));
        }

        var car = CarWriter.Write([commit.BinaryCid], carBlocks);
        var tooBig = car.Length > _options.MaxFirehoseFrameBytes;

        byte[]? prevData = null;
        if (previous is not null && CidComputation.TryDecodeCidString(previous.DataCid, out var decoded))
            prevData = decoded;

        var time = DateTimeOffset.UtcNow;
        _sequencer.Publish("#commit", time, seq => PdsFirehoseFrame.Commit(
            seq, time, did, commit.BinaryCid, commit.Rev, previous?.Rev, car, ops, prevData, tooBig));
    }

    /// <summary>
    /// Publishes an <c>#identity</c> event, telling the network to re-resolve the DID document.
    /// </summary>
    /// <param name="did">The affected DID.</param>
    /// <param name="handle">The current handle.</param>
    public PdsFirehoseEvent PublishIdentity(string did, string? handle)
    {
        var time = DateTimeOffset.UtcNow;
        return _sequencer.Publish("#identity", time, seq => PdsFirehoseFrame.Identity(seq, time, did, handle));
    }

    /// <summary>
    /// Publishes an <c>#account</c> event, reporting the account's hosting status on this PDS.
    /// </summary>
    /// <param name="did">The affected DID.</param>
    /// <param name="active">Whether the account is active here.</param>
    /// <param name="status">The inactive reason, omitted when <paramref name="active"/> is true.</param>
    public PdsFirehoseEvent PublishAccount(string did, bool active, string? status = null)
    {
        var time = DateTimeOffset.UtcNow;
        return _sequencer.Publish("#account", time,
            seq => PdsFirehoseFrame.Account(seq, time, did, active, active ? null : status));
    }

    // ──────────────────────────────────────────────────────────
    //  Sync reads
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Exports the complete repository as a CAR file, as served by <c>com.atproto.sync.getRepo</c>.
    /// The signed commit is the CAR's single root.
    /// </summary>
    /// <param name="did">The repository DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The CAR bytes, or <c>null</c> when the repo has no head.</returns>
    public async Task<byte[]?> ExportRepoAsync(string did, CancellationToken cancellationToken = default)
    {
        var head = await _commits.GetAsync(did, cancellationToken).ConfigureAwait(false);
        if (head is null) return null;

        var snapshot = await BuildSnapshotAsync(did, cancellationToken).ConfigureAwait(false);
        var commitCid = CidComputation.DecodeCidString(head.CommitCid);

        var blocks = new List<CarBlock> { new(commitCid, head.CommitBlock) };
        AppendBlocks(blocks, snapshot.MstBlocks);
        AppendBlocks(blocks, snapshot.RecordBlocks);

        return CarWriter.Write([commitCid], blocks);
    }

    /// <summary>
    /// Exports a single record together with the MST nodes proving its inclusion, as served by
    /// <c>com.atproto.sync.getRecord</c>.
    /// </summary>
    /// <param name="did">The repository DID.</param>
    /// <param name="collection">The collection NSID.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The CAR bytes, or <c>null</c> when the repo has no head.</returns>
    /// <remarks>
    /// The proof is the MST nodes on the root→record path, which is what a consumer needs to
    /// verify the record against the signed commit.
    /// </remarks>
    public async Task<byte[]?> ExportRecordProofAsync(
        string did, string collection, string rkey, CancellationToken cancellationToken = default)
    {
        var head = await _commits.GetAsync(did, cancellationToken).ConfigureAwait(false);
        if (head is null) return null;

        var snapshot = await BuildSnapshotAsync(did, cancellationToken).ConfigureAwait(false);
        var commitCid = CidComputation.DecodeCidString(head.CommitCid);
        var path = $"{collection}/{rkey}";

        var blocks = new List<CarBlock> { new(commitCid, head.CommitBlock) };
        var (_, proofBlocks) = snapshot.Mst.SerializeProof([path]);
        AppendBlocks(blocks, proofBlocks);

        if (snapshot.RecordCids.TryGetValue(path, out var recordCid))
        {
            var key = CidComputation.EncodeCidToString(recordCid);
            if (snapshot.RecordBlocks.TryGetValue(key, out var recordBytes))
                blocks.Add(new CarBlock(recordCid, recordBytes));
        }

        return CarWriter.Write([commitCid], blocks);
    }

    /// <summary>
    /// Exports the specific blocks named by <paramref name="cids"/>, as served by
    /// <c>com.atproto.sync.getBlocks</c>. Unknown CIDs are skipped.
    /// </summary>
    /// <param name="did">The repository DID.</param>
    /// <param name="cids">The requested block CIDs (base32 strings).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<byte[]?> ExportBlocksAsync(
        string did, IReadOnlyList<string> cids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cids);

        var head = await _commits.GetAsync(did, cancellationToken).ConfigureAwait(false);
        if (head is null) return null;

        var snapshot = await BuildSnapshotAsync(did, cancellationToken).ConfigureAwait(false);

        var blocks = new List<CarBlock>();
        foreach (var cid in cids)
        {
            if (!CidComputation.TryDecodeCidString(cid, out var binary)) continue;

            if (string.Equals(cid, head.CommitCid, StringComparison.Ordinal))
                blocks.Add(new CarBlock(binary, head.CommitBlock));
            else if (snapshot.MstBlocks.TryGetValue(cid, out var mstBytes))
                blocks.Add(new CarBlock(binary, mstBytes));
            else if (snapshot.RecordBlocks.TryGetValue(cid, out var recordBytes))
                blocks.Add(new CarBlock(binary, recordBytes));
        }

        // getBlocks returns a rootless CAR: the requester already knows which blocks it wants.
        return CarWriter.Write(Array.Empty<byte[]>(), blocks);
    }

    /// <summary>Gets the stored head for a DID, or <c>null</c>.</summary>
    public Task<RepoCommitState?> GetHeadAsync(string did, CancellationToken cancellationToken = default)
        => _commits.GetAsync(did, cancellationToken);

    /// <summary>Lists repository heads for <c>com.atproto.sync.listRepos</c>.</summary>
    /// <param name="limit">Maximum number of repos.</param>
    /// <param name="cursor">The DID to resume after.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<RepoCommitState>> ListReposAsync(
        int limit, string? cursor, CancellationToken cancellationToken = default)
        => _commits.ListAsync(limit, cursor, cancellationToken);

    /// <summary>
    /// Lists repository heads together with each account's hosting status, as reported by
    /// <c>com.atproto.sync.listRepos</c>.
    /// </summary>
    /// <param name="limit">Maximum number of repos.</param>
    /// <param name="cursor">The DID to resume after.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The head store carries no activation flag, so the status comes from the account store —
    /// the same source <c>com.atproto.sync.getRepoStatus</c> reads, so the two endpoints agree.
    /// A head whose account no longer exists is reported as <c>deleted</c>.
    /// </remarks>
    public async Task<IReadOnlyList<RepoListing>> ListRepoListingsAsync(
        int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        var heads = await _commits.ListAsync(limit, cursor, cancellationToken).ConfigureAwait(false);

        var listings = new List<RepoListing>(heads.Count);
        foreach (var head in heads)
        {
            var account = await _accounts.GetByDidAsync(head.Did, cancellationToken).ConfigureAwait(false);
            var status = account is null ? "deleted" : account.IsActive ? null : "deactivated";
            listings.Add(new RepoListing(head, status is null, status));
        }

        return listings;
    }

    /// <summary>Removes a repository's head and announces the account as gone.</summary>
    /// <param name="did">The repository DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteRepoAsync(string did, CancellationToken cancellationToken = default)
    {
        await _commits.DeleteAsync(did, cancellationToken).ConfigureAwait(false);
        PublishAccount(did, active: false, status: "deleted");

        // Drop the commit gate too, so the lock table stays bounded by the number of live repos
        // rather than by every DID this process has ever written. Not disposed: a commit racing
        // this delete may still hold it, and its Release() must not hit a disposed semaphore.
        // Nothing here uses AvailableWaitHandle, so collection is all the cleanup needed.
        _repoLocks.TryRemove(did, out _);
    }

    // ──────────────────────────────────────────────────────────
    //  Snapshot construction
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the MST and every block of a repository from its stored records.
    /// </summary>
    internal async Task<RepoSnapshot> BuildSnapshotAsync(string did, CancellationToken cancellationToken)
    {
        var records = await _repos.ListAllRecordsAsync(did, cancellationToken).ConfigureAwait(false);

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var recordBlocks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var recordCids = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            var (cbor, binaryCid) = EncodeRecord(record.Value);
            var path = $"{record.Collection}/{record.Rkey}";

            entries[path] = binaryCid;
            recordCids[path] = binaryCid;
            recordBlocks[CidComputation.EncodeCidToString(binaryCid)] = cbor;
        }

        var mst = MerkleSearchTree.CreateFromEntries(entries);
        var (rootCid, mstBlocks) = mst.Serialize();

        return new RepoSnapshot(rootCid, mstBlocks, recordBlocks, recordCids, mst);
    }

    /// <summary>
    /// Encodes a record to DAG-CBOR and computes its CIDv1 — the real content address that
    /// replaces the pre-federation <c>"bafyrei" + hex(sha256)</c> placeholder.
    /// </summary>
    internal static (byte[] Cbor, byte[] BinaryCid) EncodeRecord(JsonElement value)
    {
        var cbor = DagCborEncoder.Encode(value);
        return (cbor, CidComputation.ComputeBinaryForDagCbor(cbor));
    }

    /// <summary>
    /// Loads an account's repo signing key.
    /// <para>
    /// Accepts both the PKCS#8 encoding written since federation support landed and the bare
    /// SEC1 encoding written before it, so repositories created by an earlier version keep
    /// signing with the same key instead of silently getting a new identity.
    /// </para>
    /// </summary>
    internal static AtProtoKey ImportSigningKey(string base64PrivateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64PrivateKey);

        var raw = Convert.FromBase64String(base64PrivateKey);
        using var ecdsa = ECDsa.Create();

        try
        {
            ecdsa.ImportPkcs8PrivateKey(raw, out _);
        }
        catch (CryptographicException)
        {
            ecdsa.ImportECPrivateKey(raw, out _);
        }

        var oid = ecdsa.ExportParameters(false).Curve.Oid?.Value;
        var curve = oid == "1.3.132.0.10" ? KeyCurve.K256 : KeyCurve.P256;

        return AtProtoCrypto.ImportPrivateKey(ecdsa.ExportPkcs8PrivateKey(), curve);
    }

    private static void AppendBlocks(List<CarBlock> destination, IReadOnlyDictionary<string, byte[]> blocks)
    {
        foreach (var (cidString, bytes) in blocks)
            destination.Add(new CarBlock(CidComputation.DecodeCidString(cidString), bytes));
    }
}

/// <summary>
/// One entry of <c>com.atproto.sync.listRepos</c>: a repository head plus the hosting status of
/// the account that owns it.
/// </summary>
/// <param name="Head">The stored repository head.</param>
/// <param name="Active">Whether the account is currently active on this PDS.</param>
/// <param name="Status">
/// The inactive reason (<c>deactivated</c>, <c>deleted</c>), or <c>null</c> when
/// <paramref name="Active"/> is <c>true</c>.
/// </param>
public sealed record RepoListing(RepoCommitState Head, bool Active, string? Status);

/// <summary>
/// A fully materialized repository: the MST root, every MST node block, every record block, and
/// the mapping from record path to record CID.
/// </summary>
/// <param name="MstRootCid">Binary CID of the MST root node.</param>
/// <param name="MstBlocks">MST node blocks, keyed by base32 CID string.</param>
/// <param name="RecordBlocks">Record blocks, keyed by base32 CID string.</param>
/// <param name="RecordCids">Binary record CIDs, keyed by <c>collection/rkey</c>.</param>
/// <param name="Mst">The tree itself, so a caller can serialize a covering proof for a subset of paths.</param>
internal sealed record RepoSnapshot(
    byte[] MstRootCid,
    Dictionary<string, byte[]> MstBlocks,
    Dictionary<string, byte[]> RecordBlocks,
    Dictionary<string, byte[]> RecordCids,
    MerkleSearchTree Mst);
