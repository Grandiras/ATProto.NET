using System.Text;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;

namespace ATProtoNet.Repo;

/// <summary>
/// Verifies a record proof: the CAR file <c>com.atproto.sync.getRecord</c> returns, holding a
/// signed repository commit, the Merkle Search Tree nodes on the path to one record, and the
/// record itself.
/// </summary>
/// <remarks>
/// <para>A verified proof shows that the account's signing key committed to the record's content
/// at the commit's revision, whichever server delivered it. It also proves absence: a path that
/// ends without the key shows the repository held no such record at that revision.</para>
/// <para>Verification checks every block's CID against its bytes, the commit's repository DID and
/// its signature, and walks the tree from the signed root to the key, as <c>verifyProofs</c> in the
/// reference implementation (<c>@atproto/repo</c>) does. Beyond that, each node on the path must
/// hold valid keys in order within the range its parent leaves for it and sit on the layer the
/// MST rules put it on, so an answer, absence included, only comes from a path a well-formed tree
/// can have.</para>
/// <para>Where the signing key comes from is up to the caller: it is the <c>#atproto</c>
/// verification method of the account's DID document, resolved from a source the caller trusts
/// (not from the PDS that served the proof).</para>
/// <para>See: https://atproto.com/specs/repository and https://atproto.com/specs/sync</para>
/// </remarks>
public static class RecordProof
{
    // The CID codec of a DAG-CBOR block.
    private const byte DagCborCodec = 0x71;

    /// <summary>Verifies a record proof and returns the record it proves, or proof that there is none.</summary>
    /// <param name="car">The CAR file, as <c>com.atproto.sync.getRecord</c> returned it.</param>
    /// <param name="did">The repository the proof must be for.</param>
    /// <param name="collection">The record's collection.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="signingKey">
    /// The repository's signing key as a <c>did:key</c>, from the account's DID document.
    /// </param>
    /// <returns>
    /// The verified record; <see cref="VerifiedRecord.Exists"/> is <see langword="false"/> when the
    /// proof shows the repository holds no such record.
    /// </returns>
    /// <exception cref="RepoVerificationException">
    /// The CAR is malformed or incomplete, a block does not match its CID, the commit is for another
    /// repository or its signature does not verify, or the tree on the path is malformed.
    /// </exception>
    /// <exception cref="FormatException"><paramref name="signingKey"/> is not a valid <c>did:key</c>.</exception>
    public static VerifiedRecord Verify(
        ReadOnlySpan<byte> car, Did did, Nsid collection, RecordKey rkey, string signingKey)
    {
        ArgumentNullException.ThrowIfNull(did);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(rkey);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);

        CommitBlock commit;
        CarReader reader;
        try
        {
            commit = CommitBlock.FromCar(car, out reader);
        }
        catch (FormatException ex)
        {
            throw new RepoVerificationException($"The record proof is not valid: {ex.Message}", ex);
        }

        if (reader.Roots.Count != 1)
            throw new RepoVerificationException($"A record proof has one root, the commit; this one has {reader.Roots.Count}.");

        var commitCid = reader.Roots[0];
        if (commit.Did != did.Value)
            throw new RepoVerificationException($"The record proof is for the repository {commit.Did}, not {did}.");

        if (!AtProtoCrypto.VerifySignature(signingKey, commit.Unsigned, commit.Signature))
        {
            throw new RepoVerificationException(
                $"The signature on commit {CidComputation.EncodeCidToString(commitCid)} does not verify against {signingKey}.");
        }

        if (!Tid.TryParse(commit.Rev, out var rev))
            throw new RepoVerificationException($"The commit's revision '{commit.Rev}' is not a TID.");

        var uri = AtUri.Create(did, collection, rkey);
        var committed = Identity.Cid.FromBytes(commitCid);
        var recordCid = FindInTree(reader, commit.Data, $"{collection}/{rkey}");
        if (recordCid is null)
            return new VerifiedRecord(uri, committed, rev, null, null);

        var recordBlock = reader.FindBlock(recordCid)
            ?? throw new RepoVerificationException($"The record proof does not include the record block for {uri}.");

        if (recordCid.Length < 2 || recordCid[1] != DagCborCodec)
            throw new RepoVerificationException($"The record block for {uri} is not DAG-CBOR.");

        JsonElement value;
        try
        {
            value = DagCborDecoder.Decode(recordBlock.Data);
        }
        catch (FormatException ex)
        {
            throw new RepoVerificationException($"The record block for {uri} is not valid DAG-CBOR: {ex.Message}", ex);
        }

        if (value.ValueKind != JsonValueKind.Object)
            throw new RepoVerificationException($"The record block for {uri} is not a map.");

        return new VerifiedRecord(
            uri, committed, rev, Identity.Cid.FromBytes(recordCid), value);
    }

    private static string Name(byte[] cid) => CidComputation.EncodeCidToString(cid);

    // Follows key down the tree from root and returns the record CID it maps to, or null when the path
    // shows the key is absent.
    //
    // The walk is MST.get of the reference implementation: find the first entry at or after the key, and
    // if it is not the key, descend into the subtree just before it. On top of that it checks that the
    // path is one a well-formed tree can have: each node's keys are valid, strictly increasing and inside
    // the range the parent's entries leave for the subtree, all share one layer, and each child sits
    // exactly one layer below its parent (an entry-less node only as such a step).
    private static byte[]? FindInTree(CarReader car, byte[] root, string key)
    {
        var target = Encoding.ASCII.GetBytes(key);
        var cid = root;
        byte[]? lower = null;
        byte[]? upper = null;
        int? layer = null;

        for (var depth = 0; ; depth++)
        {
            if (depth > MerkleSearchTree.MaxTreeDepth)
                throw new RepoVerificationException($"The tree on the path is deeper than {MerkleSearchTree.MaxTreeDepth} layers.");

            var block = car.FindBlock(cid)
                ?? throw new RepoVerificationException($"The record proof is incomplete: it lacks tree node {Name(cid)}.");

            MstNodeData node;
            byte[][] keys;
            try
            {
                node = MstNodeData.FromBytes(block.Data);
                keys = node.ReadKeys(lower, upper);
            }
            catch (FormatException ex)
            {
                throw new RepoVerificationException($"Tree node {Name(cid)} is malformed: {ex.Message}", ex);
            }

            // The layer this node must sit on, when a parent fixed it.
            int? expected = layer - 1;
            if (expected < 0)
                throw new RepoVerificationException($"Tree node {Name(cid)} sits below layer 0.");

            if (node.Entries.Count == 0)
            {
                // Only the root of an empty repository, or a step between layers that links on.
                if (node.Left is null && depth == 0)
                    return null;
                if (node.Left is null || depth == 0)
                    throw new RepoVerificationException($"Tree node {Name(cid)} is empty.");

                cid = node.Left;
                layer = expected;
                continue;
            }

            var nodeLayer = MstKeyDepth.ComputeDepth(keys[0]);
            foreach (var entryKey in keys)
            {
                if (MstKeyDepth.ComputeDepth(entryKey) != nodeLayer || (expected is { } e && nodeLayer != e))
                    throw new RepoVerificationException($"Tree node {Name(cid)} holds keys of the wrong layer.");
            }

            var index = 0;
            while (index < keys.Length && keys[index].AsSpan().SequenceCompareTo(target) < 0)
                index++;

            if (index < keys.Length && keys[index].AsSpan().SequenceEqual(target))
                return node.Entries[index].Value;

            var subtree = index == 0 ? node.Left : node.Entries[index - 1].Tree;
            if (subtree is null)
                return null;

            if (index > 0)
                lower = keys[index - 1];
            if (index < keys.Length)
                upper = keys[index];

            cid = subtree;
            layer = nodeLayer;
        }
    }
}

/// <summary>
/// A record, or its absence, proven by a signed repository commit. Returned by
/// <see cref="RecordProof.Verify"/> and
/// <see cref="Lexicon.Com.AtProto.Sync.SyncClient.GetVerifiedRecordAsync"/>.
/// </summary>
public sealed class VerifiedRecord
{
    internal VerifiedRecord(AtUri uri, Cid commit, Tid rev, Cid? cid, JsonElement? value)
    {
        Uri = uri;
        Commit = commit;
        Rev = rev;
        Cid = cid;
        Value = value;
    }

    /// <summary>The record's AT URI.</summary>
    public AtUri Uri { get; }

    /// <summary>The CID of the signed commit the proof is anchored to.</summary>
    public Cid Commit { get; }

    /// <summary>The repository revision of that commit.</summary>
    public Tid Rev { get; }

    /// <summary>Whether the record exists at <see cref="Rev"/>.</summary>
    public bool Exists => Cid is not null;

    /// <summary>The record's CID, or <see langword="null"/> when it does not exist.</summary>
    public Cid? Cid { get; }

    /// <summary>
    /// The record, decoded from DAG-CBOR to AT Protocol JSON (links as <c>$link</c>, bytes as
    /// <c>$bytes</c>), or <see langword="null"/> when it does not exist.
    /// </summary>
    public JsonElement? Value { get; }
}

/// <summary>Thrown when repository data — a record proof, a commit or the tree under it — fails verification.</summary>
public sealed class RepoVerificationException : AtProtoException
{
    /// <summary>Creates a new exception with the given message.</summary>
    /// <param name="message">A description of what failed to verify.</param>
    public RepoVerificationException(string message) : base(message)
    {
    }

    /// <summary>Creates a new exception with the given message and cause.</summary>
    /// <param name="message">A description of what failed to verify.</param>
    /// <param name="innerException">The underlying cause.</param>
    public RepoVerificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
