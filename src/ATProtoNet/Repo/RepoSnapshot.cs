using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Data.Car;

namespace ATProtoNet.Repo;

/// <summary>Bounds on a repository CAR, checked while it is read and before a block is allocated.</summary>
public sealed record RepoLimits
{
    /// <summary>The longest CAR, in bytes.</summary>
    public long MaxCarBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>The most blocks a CAR may hold.</summary>
    public int MaxBlockCount { get; init; } = 2_000_000;

    /// <summary>The longest block, record or node, in bytes.</summary>
    public int MaxRecordBytes { get; init; } = 1_000_000;

    internal CarReaderOptions ToCarOptions() => new() { MaxTotalBytes = MaxCarBytes, MaxBlocks = MaxBlockCount, MaxBlockBytes = MaxRecordBytes, VerifyBlockCids = false };
}

/// <summary>An entry of a repository's tree.</summary>
/// <param name="Key">The key.</param>
/// <param name="Cid">The value: the CID of the record block.</param>
public sealed record RepoSnapshotEntry(MstKey Key, Cid Cid);

/// <summary>A record of a repository snapshot, undecoded.</summary>
/// <param name="Collection">The record's collection.</param>
/// <param name="Rkey">The record key.</param>
/// <param name="Cid">The record's CID.</param>
/// <param name="Block">The record's DRISL block.</param>
public sealed record RepoSnapshotRecord(Nsid Collection, RecordKey Rkey, Cid Cid, ReadOnlyMemory<byte> Block)
{
    /// <summary>Decodes the record into the data model.</summary>
    /// <returns>The record.</returns>
    /// <exception cref="FormatException">The block is not strict DRISL.</exception>
    public DataModelValue Decode() => DataModelValue.FromDrisl(Block);
}

/// <summary>A verified repository: a <c>com.atproto.sync.getRepo</c> CAR read into its signed commit, complete tree and records.</summary>
public sealed class RepoSnapshot
{
    private RepoSnapshot(SignedCommit commit, MerkleSearchTree tree, MemoryBlockStore blocks) => (Commit, Tree, Blocks) = (commit, tree, blocks);

    /// <summary>The signed commit at the CAR's root.</summary>
    public SignedCommit Commit { get; }

    /// <summary>The repository's tree, every node loaded.</summary>
    public MerkleSearchTree Tree { get; }

    /// <summary>The CAR's blocks.</summary>
    public IBlockStore Blocks { get; }

    /// <summary>Every tree entry in key order, including keys that are not record paths.</summary>
    public IEnumerable<RepoSnapshotEntry> Entries => Tree.Enumerate().Select(e => new RepoSnapshotEntry(e.Key, e.Value));

    /// <summary>The entries whose keys are an NSID and a record key, with their blocks.</summary>
    public IEnumerable<RepoSnapshotRecord> Records
    {
        get
        {
            foreach (var (key, cid) in Tree.Enumerate())
            {
                if (!key.TryGetRecordPath(out var collection, out var rkey)) continue;
                Blocks.TryGet(cid, out var block);
                yield return new(collection, rkey, cid, block);
            }
        }
    }

    /// <summary>Verifies a repository CAR held in memory.</summary>
    /// <param name="car">The CAR.</param>
    /// <param name="did">The repository it must be.</param>
    /// <param name="key">The repository's signing key, from a DID document the caller resolved.</param>
    /// <param name="limits">The bounds; the defaults when omitted.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="RepoVerificationException">The CAR does not verify; <see cref="RepoVerificationException.Error"/> says why.</exception>
    public static RepoSnapshot Verify(ReadOnlySpan<byte> car, Did did, VerificationKey key, RepoLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(did);
        ArgumentNullException.ThrowIfNull(key);
        limits ??= new();
        if (car.Length > limits.MaxCarBytes) throw new RepoVerificationException(RepoVerificationError.LimitExceeded, $"The CAR is longer than {limits.MaxCarBytes} bytes.");
        var (commit, blocks) = RecordProof.ReadCommit(car.ToArray(), did, key, limits.ToCarOptions());
        return Complete(commit, blocks);
    }

    /// <summary>Verifies a repository CAR read from a stream, holding at most <see cref="RepoLimits.MaxCarBytes"/> of it.</summary>
    /// <param name="car">The stream; read to its end, not disposed.</param>
    /// <param name="did">The repository it must be.</param>
    /// <param name="key">The repository's signing key, from a DID document the caller resolved.</param>
    /// <param name="limits">The bounds; the defaults when omitted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="RepoVerificationException">The CAR does not verify; <see cref="RepoVerificationException.Error"/> says why.</exception>
    public static async ValueTask<RepoSnapshot> VerifyAsync(Stream car, Did did, VerificationKey key, RepoLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(car);
        ArgumentNullException.ThrowIfNull(did);
        ArgumentNullException.ThrowIfNull(key);
        var options = (limits ?? new()).ToCarOptions();
        var blocks = new MemoryBlockStore();
        CarHeader header;
        try
        {
            header = await CarReader.ReadHeaderAsync(car, options, cancellationToken).ConfigureAwait(false);
            await foreach (var block in CarReader.ReadBlocksAsync(car, options, cancellationToken).ConfigureAwait(false))
            {
                if (!block.Cid.Matches(block.Data.Span)) throw new RepoVerificationException(RepoVerificationError.CidMismatch, $"The block {block.Cid} does not match its CID.");
                blocks.Put(block.Cid, block.Data);
            }
        }
        catch (DataLimitExceededException ex)
        {
            throw new RepoVerificationException(RepoVerificationError.LimitExceeded, ex.Message, ex);
        }
        catch (FormatException ex)
        {
            throw new RepoVerificationException(RepoVerificationError.MalformedCar, $"The CAR is malformed: {ex.Message}", ex);
        }
        var (commit, checkedBlocks) = RecordProof.Check(header, blocks, did, key);
        return Complete(commit, checkedBlocks);
    }

    private static RepoSnapshot Complete(SignedCommit commit, MemoryBlockStore blocks)
    {
        var tree = MerkleSearchTree.LoadComplete(commit.Commit.Data, blocks);
        foreach (var (key, cid) in tree.Enumerate())
            if (!blocks.TryGet(cid, out _)) throw new RepoVerificationException(RepoVerificationError.MissingBlock, $"The CAR lacks the record block {cid} of {key}.");
        return new(commit, tree, blocks);
    }
}
