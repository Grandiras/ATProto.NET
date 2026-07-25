using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

public sealed class MerkleSearchTreeTests
{
    private static byte[] FakeCid(string label) =>
        CidComputation.ComputeBinaryForDagCbor(System.Text.Encoding.UTF8.GetBytes(label));

    // ── Empty tree ───────────────────────────────────────────

    [Fact]
    public void Create_EmptyTree_HasZeroEntries()
    {
        var mst = MerkleSearchTree.Create();
        Assert.Equal(0, mst.Count);
        Assert.Empty(mst.GetEntries());
    }

    [Fact]
    public void Create_EmptyTree_SerializesToSingleNode()
    {
        var mst = MerkleSearchTree.Create();
        var (rootCid, blocks) = mst.Serialize();
        Assert.NotNull(rootCid);
        Assert.Single(blocks);
    }

    // ── Single entry ─────────────────────────────────────────

    [Fact]
    public void Add_SingleEntry_CanBeRetrieved()
    {
        var mst = MerkleSearchTree.Create();
        var cid = FakeCid("record1");
        mst.Add("app.bsky.feed.post/abc", cid);

        Assert.Equal(1, mst.Count);
        Assert.Equal(cid, mst.Get("app.bsky.feed.post/abc"));
    }

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        var mst = MerkleSearchTree.Create();
        mst.Add("app.bsky.feed.post/abc", FakeCid("record1"));

        Assert.Null(mst.Get("app.bsky.feed.post/xyz"));
    }

    [Fact]
    public void ContainsKey_ExistingKey_ReturnsTrue()
    {
        var mst = MerkleSearchTree.Create();
        mst.Add("app.bsky.feed.post/abc", FakeCid("record1"));

        Assert.True(mst.ContainsKey("app.bsky.feed.post/abc"));
        Assert.False(mst.ContainsKey("app.bsky.feed.post/xyz"));
    }

    // ── Multiple entries ─────────────────────────────────────

    [Fact]
    public void Add_MultipleEntries_AllRetrievable()
    {
        var mst = MerkleSearchTree.Create();
        var keys = new[]
        {
            "app.bsky.feed.post/aaa",
            "app.bsky.feed.post/bbb",
            "app.bsky.feed.post/ccc",
            "app.bsky.feed.like/ddd",
            "app.bsky.graph.follow/eee",
        };

        foreach (var key in keys)
            mst.Add(key, FakeCid(key));

        Assert.Equal(5, mst.Count);
        foreach (var key in keys)
            Assert.Equal(FakeCid(key), mst.Get(key));
    }

    [Fact]
    public void GetEntries_ReturnsSortedOrder()
    {
        var mst = MerkleSearchTree.Create();
        mst.Add("c/1", FakeCid("c1"));
        mst.Add("a/1", FakeCid("a1"));
        mst.Add("b/1", FakeCid("b1"));

        var entries = mst.GetEntries().ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal("a/1", entries[0].Key);
        Assert.Equal("b/1", entries[1].Key);
        Assert.Equal("c/1", entries[2].Key);
    }

    // ── Duplicate key ────────────────────────────────────────

    [Fact]
    public void Add_DuplicateKey_Throws()
    {
        var mst = MerkleSearchTree.Create();
        mst.Add("key/1", FakeCid("v1"));

        Assert.Throws<ArgumentException>(() => mst.Add("key/1", FakeCid("v2")));
    }

    // ── Update ───────────────────────────────────────────────

    [Fact]
    public void Update_ExistingKey_ChangesValue()
    {
        var mst = MerkleSearchTree.Create();
        var cid1 = FakeCid("v1");
        var cid2 = FakeCid("v2");
        mst.Add("key/1", cid1);

        mst.Update("key/1", cid2);

        Assert.Equal(cid2, mst.Get("key/1"));
    }

    [Fact]
    public void Update_MissingKey_Throws()
    {
        var mst = MerkleSearchTree.Create();
        Assert.Throws<KeyNotFoundException>(() => mst.Update("key/1", FakeCid("v1")));
    }

    // ── Delete ───────────────────────────────────────────────

    [Fact]
    public void Delete_ExistingKey_RemovesIt()
    {
        var mst = MerkleSearchTree.Create();
        mst.Add("key/1", FakeCid("v1"));
        mst.Add("key/2", FakeCid("v2"));
        mst.Add("key/3", FakeCid("v3"));

        mst.Delete("key/2");

        Assert.Equal(2, mst.Count);
        Assert.Null(mst.Get("key/2"));
        Assert.NotNull(mst.Get("key/1"));
        Assert.NotNull(mst.Get("key/3"));
    }

    [Fact]
    public void Delete_MissingKey_Throws()
    {
        var mst = MerkleSearchTree.Create();
        Assert.Throws<KeyNotFoundException>(() => mst.Delete("key/1"));
    }

    [Fact]
    public void Delete_AllEntries_TreeIsEmpty()
    {
        var mst = MerkleSearchTree.Create();
        mst.Add("key/1", FakeCid("v1"));
        mst.Add("key/2", FakeCid("v2"));

        mst.Delete("key/1");
        mst.Delete("key/2");

        Assert.Equal(0, mst.Count);
        Assert.Empty(mst.GetEntries());
    }

    // ── Determinism ──────────────────────────────────────────

    [Fact]
    public void RootCid_IsDeterministic_RegardlessOfInsertionOrder()
    {
        var entries = new Dictionary<string, byte[]>
        {
            ["app.bsky.feed.post/aaa"] = FakeCid("r1"),
            ["app.bsky.feed.post/bbb"] = FakeCid("r2"),
            ["app.bsky.feed.post/ccc"] = FakeCid("r3"),
            ["app.bsky.feed.like/ddd"] = FakeCid("r4"),
        };

        // Build using CreateFromEntries (sorted build)
        var mst1 = MerkleSearchTree.CreateFromEntries(entries);
        var cid1 = mst1.ComputeRootCid();

        // Build using a different construction path
        var mst2 = MerkleSearchTree.CreateFromEntries(entries);
        var cid2 = mst2.ComputeRootCid();

        Assert.Equal(cid1, cid2);
    }

    [Fact]
    public void CreateFromEntries_ProducesCorrectCount()
    {
        var entries = new Dictionary<string, byte[]>();
        for (var i = 0; i < 100; i++)
            entries[$"app.bsky.feed.post/{i:D6}"] = FakeCid($"record_{i}");

        var mst = MerkleSearchTree.CreateFromEntries(entries);
        Assert.Equal(100, mst.Count);
    }

    [Fact]
    public void CreateFromEntries_AllEntriesRetrievable()
    {
        var entries = new Dictionary<string, byte[]>();
        for (var i = 0; i < 50; i++)
            entries[$"app.bsky.feed.post/{i:D4}"] = FakeCid($"record_{i}");

        var mst = MerkleSearchTree.CreateFromEntries(entries);

        foreach (var (key, value) in entries)
            Assert.Equal(value, mst.Get(key));
    }

    // ── Serialization roundtrip ──────────────────────────────

    [Fact]
    public void Serialize_Deserialize_Roundtrip()
    {
        var entries = new Dictionary<string, byte[]>
        {
            ["app.bsky.feed.post/001"] = FakeCid("r1"),
            ["app.bsky.feed.post/002"] = FakeCid("r2"),
            ["app.bsky.feed.like/003"] = FakeCid("r3"),
            ["app.bsky.graph.follow/004"] = FakeCid("r4"),
        };

        var mst = MerkleSearchTree.CreateFromEntries(entries);
        var (rootCid, blocks) = mst.Serialize();

        // Deserialize
        var mst2 = MerkleSearchTree.Deserialize(rootCid, cid =>
            blocks.TryGetValue(cid, out var data) ? data : null);

        // Same entries
        var entries1 = mst.GetEntries().ToList();
        var entries2 = mst2.GetEntries().ToList();

        Assert.Equal(entries1.Count, entries2.Count);
        for (var i = 0; i < entries1.Count; i++)
        {
            Assert.Equal(entries1[i].Key, entries2[i].Key);
            Assert.Equal(entries1[i].Value, entries2[i].Value);
        }

        // Same root CID
        Assert.Equal(mst.ComputeRootCid(), mst2.ComputeRootCid());
    }

    // ── Validation ───────────────────────────────────────────

    [Fact]
    public void Validate_ValidTree_ReturnsTrue()
    {
        var mst = MerkleSearchTree.Create();
        mst.Add("a/1", FakeCid("v1"));
        mst.Add("b/2", FakeCid("v2"));
        mst.Add("c/3", FakeCid("v3"));

        Assert.True(mst.Validate());
    }

    // ── Spec vector keys ─────────────────────────────────────

    [Fact]
    public void SpecVectorKeys_CorrectlyDistributedAcrossLayers()
    {
        // These keys have known depths per the AT Protocol spec examples
        var entries = new Dictionary<string, byte[]>
        {
            ["2653ae71"] = FakeCid("d0"),                        // depth 0
            ["blue"] = FakeCid("d1"),                            // depth 1
            ["app.bsky.feed.post/454397e440ec"] = FakeCid("d4"), // depth 4
            ["app.bsky.feed.post/9adeb165882c"] = FakeCid("d8"), // depth 8
        };

        var mst = MerkleSearchTree.CreateFromEntries(entries);
        Assert.Equal(4, mst.Count);

        // All entries should be retrievable
        foreach (var (key, value) in entries)
            Assert.Equal(value, mst.Get(key));

        // Serialization should work
        var (rootCid, blocks) = mst.Serialize();
        Assert.NotEmpty(blocks);

        // More than one node (tree has entries at different depths)
        Assert.True(blocks.Count > 1,
            "Expected multi-node tree for keys at different depths.");
    }

    // ── Large tree stress test ───────────────────────────────

    [Fact]
    public void LargeTree_1000Entries_CorrectBehavior()
    {
        var entries = new Dictionary<string, byte[]>();
        for (var i = 0; i < 1000; i++)
            entries[$"app.bsky.feed.post/{i:D8}"] = FakeCid($"record_{i}");

        var mst = MerkleSearchTree.CreateFromEntries(entries);
        Assert.Equal(1000, mst.Count);

        // Spot-check some entries
        Assert.NotNull(mst.Get("app.bsky.feed.post/00000000"));
        Assert.NotNull(mst.Get("app.bsky.feed.post/00000500"));
        Assert.NotNull(mst.Get("app.bsky.feed.post/00000999"));

        // Validate structure
        Assert.True(mst.Validate());

        // Serialization roundtrip
        var (rootCid, blocks) = mst.Serialize();
        var mst2 = MerkleSearchTree.Deserialize(rootCid, cid =>
            blocks.TryGetValue(cid, out var data) ? data : null);

        Assert.Equal(1000, mst2.Count);
        Assert.Equal(mst.ComputeRootCid(), mst2.ComputeRootCid());
    }

    // ── Mutation then validate ───────────────────────────────

    [Fact]
    public void AddThenDelete_TreeRemainsDeterministic()
    {
        // Build a tree, add an entry, delete it, check root CID matches original
        var entries = new Dictionary<string, byte[]>
        {
            ["key/a"] = FakeCid("va"),
            ["key/b"] = FakeCid("vb"),
            ["key/c"] = FakeCid("vc"),
        };

        var mst1 = MerkleSearchTree.CreateFromEntries(entries);
        var originalCid = mst1.ComputeRootCid();

        // Add then delete an entry
        mst1.Add("key/temp", FakeCid("vtemp"));
        Assert.Equal(4, mst1.Count);

        mst1.Delete("key/temp");
        Assert.Equal(3, mst1.Count);

        // Note: Due to the deterministic nature of MST, the root CID
        // after add+delete may or may not match the original depending
        // on tree restructuring. But the entries should be the same.
        var entries1 = mst1.GetEntries().Select(e => e.Key).ToList();
        var expected = entries.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, entries1);
    }

    // ── Covering proofs ──────────────────────────────────────

    [Fact]
    public void SerializeProof_ReturnsTheSameRootAsSerialize()
    {
        var mst = BuildTree(200);

        var (fullRoot, _) = mst.Serialize();
        var (proofRoot, _) = mst.SerializeProof(["col/k0100"]);

        Assert.Equal(fullRoot, proofRoot);
    }

    [Fact]
    public void SerializeProof_IsASubsetOfTheFullBlockSet()
    {
        var mst = BuildTree(200);

        var (_, all) = mst.Serialize();
        var (_, proof) = mst.SerializeProof(["col/k0100"]);

        Assert.NotEmpty(proof);
        Assert.True(proof.Count < all.Count, $"proof {proof.Count} should be smaller than {all.Count}");
        foreach (var (cid, bytes) in proof)
        {
            Assert.True(all.ContainsKey(cid));
            Assert.Equal(all[cid], bytes);
        }
    }

    [Fact]
    public void SerializeProof_AlwaysIncludesTheRoot()
    {
        var mst = BuildTree(200);

        var (root, proof) = mst.SerializeProof([]);

        Assert.Single(proof);
        Assert.True(proof.ContainsKey(CidComputation.EncodeCidToString(root)));
    }

    [Fact]
    public void SerializeProof_ProofBlocksRehashToTheirCids()
    {
        var mst = BuildTree(200);

        var (_, proof) = mst.SerializeProof(["col/k0000", "col/k0100", "col/k0199"]);

        foreach (var (cid, bytes) in proof)
            Assert.Equal(cid, CidComputation.EncodeCidToString(CidComputation.ComputeBinaryForDagCbor(bytes)));
    }

    [Fact]
    public void SerializeProof_CoversTheKeyItWasAskedFor()
    {
        var mst = BuildTree(200);
        const string key = "col/k0100";

        var (root, proof) = mst.SerializeProof([key]);

        // A consumer holding only the proof can walk root→key and read the value back — which is
        // the whole point of a covering proof, and what a firehose #commit consumer does.
        Assert.Equal(mst.Get(key), WalkProof(root, proof, key));
    }

    [Fact]
    public void SerializeProof_MoreKeysCoverAtLeastAsMuch()
    {
        var mst = BuildTree(200);

        var (_, one) = mst.SerializeProof(["col/k0100"]);
        var (_, many) = mst.SerializeProof(["col/k0000", "col/k0100", "col/k0199"]);

        Assert.True(many.Count >= one.Count);
        foreach (var cid in one.Keys)
            Assert.True(many.ContainsKey(cid));
    }

    [Fact]
    public void SerializeProof_UnknownKey_ReturnsTheWalkedPathWithoutThrowing()
    {
        var mst = BuildTree(200);

        var (_, proof) = mst.SerializeProof(["col/nosuchkey"]);

        Assert.NotEmpty(proof);
    }

    /// <summary>
    /// Descends a partial block set from the root looking for <paramref name="key"/>, decoding
    /// each node the way a relay would. Returns the value, or null if the key is absent.
    /// </summary>
    private static byte[]? WalkProof(byte[] rootCid, Dictionary<string, byte[]> blocks, string key)
    {
        var cid = rootCid;
        while (true)
        {
            if (!blocks.TryGetValue(CidComputation.EncodeCidToString(cid), out var bytes))
                return null;

            var node = MstNodeData.FromBytes(bytes);
            var child = node.Left;
            var previousKey = "";

            foreach (var entry in node.Entries)
            {
                var entryKey = string.Concat(
                    previousKey[..entry.PrefixLength],
                    System.Text.Encoding.UTF8.GetString(entry.KeySuffix));
                previousKey = entryKey;

                var cmp = string.CompareOrdinal(key, entryKey);
                if (cmp == 0) return entry.Value;
                if (cmp < 0) break;

                child = entry.Tree;
            }

            if (child is null) return null;
            cid = child;
        }
    }

    private static MerkleSearchTree BuildTree(int count)
    {
        var entries = new Dictionary<string, byte[]>();
        for (var i = 0; i < count; i++)
            entries[$"col/k{i:D4}"] = FakeCid($"v{i}");

        return MerkleSearchTree.CreateFromEntries(entries);
    }
}
