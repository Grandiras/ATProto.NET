using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;

namespace ATProtoNet.Repo;

/// <summary>
/// A whole repository at one signed commit, verified: the CAR file
/// <c>com.atproto.sync.getRepo</c> returns, read into its commit, its Merkle Search Tree and its
/// records.
/// </summary>
/// <remarks>
/// <para>Verification checks every block against its CID, the commit's repository, version and
/// signature, and that the tree is complete and is the canonical MST of its records, so the
/// records are exactly the ones the account committed to at <see cref="Rev"/>, whichever host served
/// them.</para>
/// <para>This is what resynchronizing a repository fetches: compare <see cref="Records"/> with what
/// you hold for the repository, treating a record you lack as created, one whose CID differs as
/// updated, and one missing here as deleted.</para>
/// <para>The whole CAR is held in memory, so a large repository takes a matching amount of it.</para>
/// </remarks>
public sealed class RepoSnapshot
{
    private readonly CarReader _car;

    private RepoSnapshot(Did did, Cid commit, CommitBlock block, Tid rev, MerkleSearchTree tree, CarReader car)
    {
        Did = did;
        Commit = commit;
        CommitBlock = block;
        Rev = rev;
        Data = Cid.Parse(CidComputation.EncodeCidToString(block.Data));
        Tree = tree;
        _car = car;
    }

    /// <summary>The repository.</summary>
    public Did Did { get; }

    /// <summary>The CID of the signed commit.</summary>
    public Cid Commit { get; }

    /// <summary>The commit's revision.</summary>
    public Tid Rev { get; }

    /// <summary>The root of the commit's tree: the <c>prevData</c> the repository's next commit carries.</summary>
    public Cid Data { get; }

    /// <summary>The repository's tree: every record path mapped to the record's CID.</summary>
    public MerkleSearchTree Tree { get; }

    /// <summary>How many records the repository holds.</summary>
    public int Count => Tree.Count;

    /// <summary>The commit block, for verifying its signature.</summary>
    internal CommitBlock CommitBlock { get; }

    /// <summary>
    /// Every record, in path order: collection, then record key. A path that is not a valid
    /// <c>collection/rkey</c> is skipped.
    /// </summary>
    public IEnumerable<RepoSnapshotRecord> Records
    {
        get
        {
            foreach (var (path, cid) in Tree.GetEntries())
            {
                var slash = path.IndexOf('/');
                if (Nsid.TryParse(path[..slash], out var collection) && RecordKey.TryParse(path[(slash + 1)..], out var rkey))
                    yield return new RepoSnapshotRecord(path, collection, rkey, Cid.Parse(CidComputation.EncodeCidToString(cid)));
            }
        }
    }

    /// <summary>
    /// Reads a record, decoded from DAG-CBOR to the AT Protocol JSON data model, or null when its
    /// block is absent or does not decode.
    /// </summary>
    /// <param name="cid">The record's CID, from <see cref="Records"/> or <see cref="Tree"/>.</param>
    public JsonElement? GetRecord(Cid cid)
    {
        ArgumentNullException.ThrowIfNull(cid);
        if (_car.FindBlock(cid.AsSpan()) is not { } block)
            return null;

        try
        {
            return DagCborDecoder.Decode(block.Data);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads and verifies a repository export.
    /// </summary>
    /// <param name="car">The CAR file, as <c>com.atproto.sync.getRepo</c> returned it.</param>
    /// <param name="did">The repository it must be.</param>
    /// <param name="signingKey">
    /// The repository's signing key as a <c>did:key</c>: the <c>#atproto</c> verification method of
    /// the account's DID document, from a source you trust rather than the host that served the CAR.
    /// </param>
    /// <returns>The verified repository.</returns>
    /// <exception cref="RepoVerificationException">
    /// The CAR is malformed or incomplete, a block does not match its CID, the commit is for another
    /// repository, is not version 3 or is not signed by <paramref name="signingKey"/>, or the tree
    /// is malformed.
    /// </exception>
    /// <exception cref="FormatException"><paramref name="signingKey"/> is not a valid <c>did:key</c>.</exception>
    public static RepoSnapshot Verify(ReadOnlySpan<byte> car, Did did, string signingKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);

        var snapshot = Read(car, did);
        if (!AtProtoCrypto.VerifySignature(signingKey, snapshot.CommitBlock.Unsigned, snapshot.CommitBlock.Signature))
            throw new RepoVerificationException($"The signature on {did}'s commit {snapshot.Commit} does not verify against {signingKey}.");

        return snapshot;
    }

    /// <summary>
    /// Reads a repository export and verifies everything but the commit's signature, which the
    /// caller checks against <see cref="CommitBlock"/>.
    /// </summary>
    internal static RepoSnapshot Read(ReadOnlySpan<byte> car, Did did)
    {
        ArgumentNullException.ThrowIfNull(did);

        CarReader reader;
        try
        {
            reader = CarReader.FromBytes(car, verifyBlockCids: true);
        }
        catch (FormatException ex)
        {
            throw new RepoVerificationException($"The repository export is not a valid CAR file: {ex.Message}", ex);
        }

        if (reader.Roots.Count == 0)
            throw new RepoVerificationException("The repository export names no root commit.");

        var commitCid = reader.Roots[0];
        var commitBlock = reader.FindBlock(commitCid)
            ?? throw new RepoVerificationException("The repository export does not include its commit block.");

        CommitBlock block;
        try
        {
            block = CommitBlock.Read(commitBlock.Data);
        }
        catch (FormatException ex)
        {
            throw new RepoVerificationException(ex.Message, ex);
        }

        if (!string.Equals(block.Did, did.Value, StringComparison.Ordinal))
            throw new RepoVerificationException($"The repository export is for {block.Did}, not {did}.");
        if (block.Version != RepoCommit.CurrentVersion)
            throw new RepoVerificationException($"The commit is version {block.Version}; only version {RepoCommit.CurrentVersion} is supported.");
        if (!Tid.TryParse(block.Rev, out var rev))
            throw new RepoVerificationException($"The commit's revision '{block.Rev}' is not a TID.");

        MerkleSearchTree tree;
        try
        {
            tree = MerkleSearchTree.Deserialize(
                block.Data, cid => reader.FindBlock(CidComputation.DecodeCidString(cid))?.Data);
            tree.Validate();
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            throw new RepoVerificationException($"The repository's tree is malformed: {ex.Message}", ex);
        }

        return new RepoSnapshot(did, Cid.Parse(CidComputation.EncodeCidToString(commitCid)), block, rev, tree, reader);
    }
}

/// <summary>A record of a <see cref="RepoSnapshot"/>.</summary>
/// <param name="Path">The record's path in the repository: <c>collection/rkey</c>.</param>
/// <param name="Collection">The record's collection.</param>
/// <param name="Rkey">The record's key.</param>
/// <param name="Cid">The record's CID.</param>
public sealed record RepoSnapshotRecord(string Path, Nsid Collection, RecordKey Rkey, Cid Cid);
