using System.Buffers;
using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Data.Car;

namespace ATProtoNet.Repo;

/// <summary>
/// Record proofs: the CAR <c>com.atproto.sync.getRecord</c> returns, holding a signed commit, the tree
/// nodes on the path to one record, and the record itself. A proof also shows a record absent.
/// </summary>
/// <remarks>See <see href="https://atproto.com/specs/sync"/>.</remarks>
public static class RecordProof
{
    /// <summary>Verifies a record proof and returns the record it proves, or that there is none.</summary>
    /// <param name="car">The CAR.</param>
    /// <param name="did">The repository the proof must be for.</param>
    /// <param name="collection">The record's collection.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="key">The repository's signing key: the <c>#atproto</c> key of a DID document the caller
    /// resolved itself, never one from the host that served the proof.</param>
    /// <returns>The verified record; <see cref="VerifiedRecord.Exists"/> is <see langword="false"/> when the proof shows it absent.</returns>
    /// <exception cref="RepoVerificationException">The proof does not verify; <see cref="RepoVerificationException.Error"/> says why.</exception>
    public static VerifiedRecord Verify(ReadOnlySpan<byte> car, Did did, Nsid collection, RecordKey rkey, VerificationKey key)
    {
        ArgumentNullException.ThrowIfNull(did);
        ArgumentNullException.ThrowIfNull(key);
        var mstKey = MstKey.For(collection, rkey);
        var (commit, blocks) = ReadCommit(car.ToArray(), did, key, new CarReaderOptions());

        Cid? cid;
        try
        {
            cid = MerkleSearchTree.Load(commit.Commit.Data, blocks).Get(mstKey);
        }
        catch (MstIncompleteException ex)
        {
            throw new RepoVerificationException(RepoVerificationError.Incomplete, $"The proof does not cover {mstKey}: it lacks node {ex.Cid}.", ex);
        }
        if (cid is null) return new(did, commit.Commit.Rev, commit.Cid, collection, rkey, null, null);

        if (!blocks.TryGet(cid, out var record))
            throw new RepoVerificationException(RepoVerificationError.MissingBlock, $"The proof lacks the record block {cid}.");
        try
        {
            if (cid.Codec != CidCodec.DagCbor || DataModelValue.FromDrisl(record) is not DataModelMap) throw new FormatException("The record is not a map.");
        }
        catch (FormatException ex)
        {
            throw new RepoVerificationException(RepoVerificationError.MalformedCar, $"The record block {cid} is not a DRISL map: {ex.Message}", ex);
        }
        return new(did, commit.Commit.Rev, commit.Cid, collection, rkey, cid, record);
    }

    /// <summary>Writes the proofs of <paramref name="keys"/>, present or absent, as one CAR.</summary>
    /// <param name="commit">The commit whose tree is <paramref name="tree"/>.</param>
    /// <param name="tree">The repository's tree.</param>
    /// <param name="keys">The keys to prove.</param>
    /// <param name="records">The store holding the record blocks.</param>
    /// <param name="destination">Where the CAR goes: the commit as its root, the covering proof nodes and the record blocks.</param>
    /// <exception cref="ArgumentException"><paramref name="tree"/> is not the commit's tree.</exception>
    /// <exception cref="InvalidOperationException">A present record's block is not in <paramref name="records"/>.</exception>
    public static void Write(SignedCommit commit, MerkleSearchTree tree, IEnumerable<MstKey> keys, IBlockStore records, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(destination);
        if (tree.Root != commit.Commit.Data) throw new ArgumentException("The tree is not the commit's tree.", nameof(tree));
        var keyList = keys.ToList();
        var proof = new MemoryBlockStore();
        tree.WriteCoveringProof(keyList, proof);
        List<CarBlock> blocks = [new(commit.Cid, commit.Block), .. proof.Blocks];
        foreach (var key in keyList)
        {
            if (tree.Get(key) is not { } cid) continue;
            blocks.Add(new(cid, records.TryGet(cid, out var record) ? record : throw new InvalidOperationException($"The record block {cid} of {key} is not in the store.")));
        }
        var car = new ArrayBufferWriter<byte>();
        CarWriter.Write(car, [commit.Cid], blocks);
        destination.Write(car.WrittenSpan);
    }

    // Reads a CAR whose one root is a commit of did signed by key.
    internal static (SignedCommit Commit, MemoryBlockStore Blocks) ReadCommit(ReadOnlyMemory<byte> car, Did did, VerificationKey key, CarReaderOptions options)
    {
        CarFile file;
        try
        {
            file = CarReader.Read(car, options with { VerifyBlockCids = false });
        }
        catch (DataLimitExceededException ex)
        {
            throw new RepoVerificationException(RepoVerificationError.LimitExceeded, ex.Message, ex);
        }
        catch (FormatException ex)
        {
            throw new RepoVerificationException(RepoVerificationError.MalformedCar, $"The CAR is malformed: {ex.Message}", ex);
        }
        foreach (var block in file.Blocks)
            if (!block.Cid.Matches(block.Data.Span)) throw new RepoVerificationException(RepoVerificationError.CidMismatch, $"The block {block.Cid} does not match its CID.");
        return Check(file.Header, MemoryBlockStore.FromCar(file), did, key);
    }

    internal static (SignedCommit Commit, MemoryBlockStore Blocks) Check(CarHeader header, MemoryBlockStore blocks, Did did, VerificationKey key)
    {
        if (header.Roots.Count != 1)
            throw new RepoVerificationException(RepoVerificationError.MalformedCar, $"The CAR has {header.Roots.Count} roots, not one commit.");
        if (!blocks.TryGet(header.Roots[0], out var block))
            throw new RepoVerificationException(RepoVerificationError.MissingBlock, $"The CAR lacks its root commit {header.Roots[0]}.");
        var commit = SignedCommit.Read(block);
        if (commit.Commit.Did != did)
            throw new RepoVerificationException(RepoVerificationError.WrongRepository, $"The commit is for {commit.Commit.Did}, not {did}.");
        if (!commit.Verify(key))
            throw new RepoVerificationException(RepoVerificationError.BadSignature, $"The signature on commit {commit.Cid} does not verify against {key.DidKey}.");
        return (commit, blocks);
    }
}

/// <summary>A record, or its absence, proven by a signed commit.</summary>
/// <param name="Did">The repository.</param>
/// <param name="Rev">The revision of the commit.</param>
/// <param name="Commit">The CID of the commit.</param>
/// <param name="Collection">The record's collection.</param>
/// <param name="Rkey">The record key.</param>
/// <param name="Cid">The record's CID, or <see langword="null"/> when the record is absent.</param>
/// <param name="Block">The record's DRISL block, or <see langword="null"/> when the record is absent.</param>
public sealed record VerifiedRecord(Did Did, Tid Rev, Cid Commit, Nsid Collection, RecordKey Rkey, Cid? Cid, ReadOnlyMemory<byte>? Block)
{
    /// <summary>Whether the record exists at <see cref="Rev"/>.</summary>
    public bool Exists => Cid is not null;
}
