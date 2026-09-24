using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Repo;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Repo;

/// <summary>
/// The producer path end to end: records into a <see cref="MerkleSearchTree"/>, a signed
/// <see cref="RepoCommit"/> over its root, all of it written by <see cref="CarWriter"/> — then read
/// back the way a relay or another PDS would, from nothing but the CAR bytes and the public key.
/// </summary>
public sealed class RepoWritePathTests
{
    private const string Did = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    [Fact]
    public void WrittenRepo_RoundTripsThroughCarReaderAndVerifies()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        // ── Write ────────────────────────────────────────────
        var mst = MerkleSearchTree.Create();
        var recordBlocks = new List<CarBlock>();
        for (var i = 0; i < 120; i++)
        {
            var collection = i % 3 == 0 ? "app.bsky.feed.like" : "app.bsky.feed.post";
            var record = JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["$type"] = collection,
                ["text"] = $"record {i}",
                ["createdAt"] = "2026-09-24T12:00:00.000Z",
            });
            var (bytes, cid) = DagCborEncoder.EncodeWithCid(record);
            var binaryCid = CidComputation.DecodeCidString(cid.Value);

            mst.Add($"{collection}/3l6oveex{i:D5}", binaryCid);
            recordBlocks.Add(new CarBlock(binaryCid, bytes));
        }

        var (root, mstBlocks) = mst.Serialize();
        var commit = new RepoCommit { Did = Did, Data = root, Rev = "3l6oveex3ii2l" }.Sign(key);

        var car = CarWriter.Write(
            commit.BinaryCid,
            [
                new CarBlock(commit.BinaryCid, commit.Bytes),
                .. mstBlocks.Select(b => new CarBlock(CidComputation.DecodeCidString(b.Key), b.Value)),
                .. recordBlocks,
            ]);

        // ── Read back from the bytes alone ───────────────────
        var reader = CarReader.FromBytes(car, verifyBlockCids: true);
        var commitBlock = reader.GetRootBlock();
        Assert.NotNull(commitBlock);

        // The signature covers the commit with its `sig` removed, as a relay recomputes it.
        var view = FirehoseVerifier.ExtractSignedView(commitBlock.Data);
        Assert.NotNull(view);
        Assert.True(key.Verify(view.Value.UnsignedBytes, view.Value.SigBytes!));

        var commitJson = DagCborDecoder.Decode(commitBlock.Data);
        Assert.Equal(Did, commitJson.GetProperty("did").GetString());
        Assert.Equal(3, commitJson.GetProperty("version").GetInt32());
        var dataCid = CidComputation.DecodeCidString(commitJson.GetProperty("data").GetProperty("$link").GetString()!);
        Assert.Equal(root, dataCid);

        var loaded = MerkleSearchTree.Deserialize(
            dataCid, cid => reader.FindBlock(CidComputation.DecodeCidString(cid))?.Data);

        // The blocks are the canonical tree for exactly these records.
        Assert.True(loaded.Validate());
        Assert.Equal(mst.GetEntries(), loaded.GetEntries());

        foreach (var (path, recordCid) in loaded.GetEntries())
        {
            var record = DagCborDecoder.Decode(reader.FindBlock(recordCid)!.Data);
            Assert.StartsWith(record.GetProperty("$type").GetString()! + "/", path, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CommitDiff_ProofBlocksCarryEverythingTheChangedPathsNeed()
    {
        // A #commit event ships the new commit plus the covering proof for the keys it touched.
        // A consumer holding only those blocks can walk from the root to each changed record.
        using var key = AtProtoCrypto.GenerateP256Key();
        var mst = MerkleSearchTree.Create();
        for (var i = 0; i < 500; i++)
            mst.Add($"app.bsky.feed.post/3l6oveex{i:D5}", CidComputation.ComputeBinaryForDagCbor([(byte)(i & 0x7f)]));

        const string added = "app.bsky.feed.post/3l6oveex99999";
        const string deleted = "app.bsky.feed.post/3l6oveex00250";
        var recordCid = CidComputation.ComputeBinaryForDagCbor([0xA0]);
        mst.Add(added, recordCid);
        mst.Delete(deleted);

        var (root, proof) = mst.SerializeProof([added, deleted]);
        var commit = new RepoCommit { Did = Did, Data = root, Rev = "3l6oveex3ii2m" }.Sign(key);

        var car = CarWriter.Write(
            commit.BinaryCid,
            [
                new CarBlock(commit.BinaryCid, commit.Bytes),
                .. proof.Select(b => new CarBlock(CidComputation.DecodeCidString(b.Key), b.Value)),
            ]);

        var reader = CarReader.FromBytes(car, verifyBlockCids: true);
        Assert.True(commit.Verify(key));
        Assert.Equal(proof.Count + 1, reader.Blocks.Count);
        Assert.Equal(root, commit.Data);

        // Every node on the path to either key is present.
        foreach (var target in new[] { added, deleted })
        {
            var cid = root;
            while (true)
            {
                var block = reader.FindBlock(cid);
                Assert.NotNull(block);
                var node = MstNodeData.FromBytes(block.Data);

                byte[]? next = node.Left;
                var previous = "";
                var found = false;
                foreach (var entry in node.Entries)
                {
                    var entryKey = previous[..entry.PrefixLength] + System.Text.Encoding.ASCII.GetString(entry.KeySuffix);
                    previous = entryKey;
                    var cmp = string.CompareOrdinal(target, entryKey);
                    if (cmp == 0) { found = true; break; }
                    if (cmp < 0) break;
                    next = entry.Tree;
                }

                if (found)
                {
                    Assert.Equal(added, target);
                    break;
                }

                if (next is null)
                {
                    Assert.Equal(deleted, target);
                    break;
                }

                cid = next;
            }
        }
    }
}
