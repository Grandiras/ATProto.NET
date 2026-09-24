using System.Formats.Cbor;
using System.Text;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

public sealed class MerkleSearchTreeTests
{
    private static byte[] FakeCid(string label) =>
        CidComputation.ComputeBinaryForDagCbor(Encoding.UTF8.GetBytes(label));

    private static Func<string, byte[]?> Lookup(Dictionary<string, byte[]> blocks) =>
        cid => blocks.TryGetValue(cid, out var data) ? data : null;

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

    [Fact]
    public void Create_DuplicateKey_Throws()
    {
        var entries = new[]
        {
            KeyValuePair.Create("key/1", FakeCid("v1")),
            KeyValuePair.Create("key/1", FakeCid("v2")),
        };

        Assert.Throws<ArgumentException>(() => MerkleSearchTree.Create(entries));
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
    public void Delete_AllEntries_ReturnsToTheEmptyTreeRoot()
    {
        var mst = MerkleSearchTree.Create();
        var empty = mst.ComputeRootCid();
        mst.Add("key/1", FakeCid("v1"));
        mst.Add("key/2", FakeCid("v2"));

        mst.Delete("key/1");
        mst.Delete("key/2");

        Assert.Equal(0, mst.Count);
        Assert.Empty(mst.GetEntries());
        Assert.Equal(empty, mst.ComputeRootCid());
    }

    // ── Determinism ──────────────────────────────────────────

    [Fact]
    public void RootCid_IsDeterministic_RegardlessOfInsertionOrder()
    {
        var entries = Enumerable.Range(0, 200)
            .Select(i => KeyValuePair.Create($"app.bsky.feed.post/{i:D6}", FakeCid($"r{i}")))
            .ToList();

        var forward = MerkleSearchTree.Create(entries);
        var backward = MerkleSearchTree.Create();
        foreach (var (key, value) in Enumerable.Reverse(entries))
            backward.Add(key, value);

        Assert.Equal(forward.ComputeRootCid(), backward.ComputeRootCid());
    }

    [Fact]
    public void AddThenDelete_RestoresTheOriginalRoot()
    {
        var mst = MerkleSearchTree.Create(new Dictionary<string, byte[]>
        {
            ["key/a"] = FakeCid("va"),
            ["key/b"] = FakeCid("vb"),
            ["key/c"] = FakeCid("vc"),
        });
        var original = mst.ComputeRootCid();

        mst.Add("key/temp", FakeCid("vtemp"));
        Assert.NotEqual(original, mst.ComputeRootCid());

        mst.Delete("key/temp");
        Assert.Equal(original, mst.ComputeRootCid());
    }

    [Fact]
    public void RandomEdits_AlwaysMatchTheBulkBuildOfTheFinalEntries()
    {
        // Any sequence of edits must land on the root a bulk build of the final entry set gives,
        // and on the root a reader rebuilds from the serialized blocks. Seeded so a failure
        // reproduces.
        var random = new Random(20260924);
        var pool = Enumerable.Range(0, 400).Select(i => $"com.example.record/{i:x5}").ToArray();

        for (var trial = 0; trial < 100; trial++)
        {
            var mst = MerkleSearchTree.Create();
            var expected = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            for (var step = 0; step < 150; step++)
            {
                var key = pool[random.Next(pool.Length)];
                var value = FakeCid($"{trial}/{step}");

                if (!expected.ContainsKey(key))
                {
                    mst.Add(key, value);
                    expected[key] = value;
                }
                else if (random.Next(3) == 0)
                {
                    mst.Update(key, value);
                    expected[key] = value;
                }
                else
                {
                    mst.Delete(key);
                    expected.Remove(key);
                }
            }

            var bulk = MerkleSearchTree.Create(expected.OrderBy(_ => random.Next()));
            Assert.Equal(bulk.ComputeRootCid(), mst.ComputeRootCid());
            Assert.Equal(expected.Count, mst.Count);

            var (root, blocks) = mst.Serialize();
            var reloaded = MerkleSearchTree.Deserialize(root, Lookup(blocks));
            Assert.True(reloaded.Validate());
            Assert.Equal(mst.GetEntries(), reloaded.GetEntries());
        }
    }

    // ── Serialization roundtrip ──────────────────────────────

    [Fact]
    public void Serialize_Deserialize_Roundtrip()
    {
        var mst = MerkleSearchTree.Create(new Dictionary<string, byte[]>
        {
            ["app.bsky.feed.post/001"] = FakeCid("r1"),
            ["app.bsky.feed.post/002"] = FakeCid("r2"),
            ["app.bsky.feed.like/003"] = FakeCid("r3"),
            ["app.bsky.graph.follow/004"] = FakeCid("r4"),
        });
        var (rootCid, blocks) = mst.Serialize();

        var mst2 = MerkleSearchTree.Deserialize(rootCid, Lookup(blocks));

        Assert.Equal(mst.GetEntries(), mst2.GetEntries());
        Assert.Equal(mst.ComputeRootCid(), mst2.ComputeRootCid());
        Assert.True(mst2.Validate());
    }

    [Fact]
    public void Deserialize_ReferenceEncodedEmptyNode_Succeeds()
    {
        // {"e": [], "l": null} as the reference implementation writes it. The explicit null used
        // to throw, which broke loading every repo served by com.atproto.sync.getRepo.
        byte[] node = [0xa2, 0x61, 0x65, 0x80, 0x61, 0x6c, 0xf6];
        var root = CidComputation.ComputeBinaryForDagCbor(node);

        var mst = MerkleSearchTree.Deserialize(root, _ => node);

        Assert.Equal(0, mst.Count);
        Assert.True(mst.Validate());
    }

    [Fact]
    public void LargeTree_1000Entries_RoundTrips()
    {
        var mst = BuildTree(1000);
        Assert.Equal(1000, mst.Count);
        Assert.NotNull(mst.Get("col/k0000"));
        Assert.NotNull(mst.Get("col/k0999"));

        var (rootCid, blocks) = mst.Serialize();
        var mst2 = MerkleSearchTree.Deserialize(rootCid, Lookup(blocks));

        Assert.Equal(1000, mst2.Count);
        Assert.Equal(rootCid, mst2.ComputeRootCid());
        Assert.True(mst2.Validate());
    }

    // ── Validation ───────────────────────────────────────────

    [Fact]
    public void Validate_TreeBuiltInMemory_ReturnsTrue()
    {
        var mst = MerkleSearchTree.Create();
        mst.Add("a/1", FakeCid("v1"));
        mst.Add("b/2", FakeCid("v2"));
        mst.Add("c/3", FakeCid("v3"));

        Assert.True(mst.Validate());
    }

    [Fact]
    public void Validate_LoadedTreeWithAnExtraWrapperLayer_Throws()
    {
        // The entries are fine and in order, but a root wrapping the real tree in an entry-less
        // node is not the canonical shape, so its CID is not the one the entries commit to.
        var leaf = EncodeNode(null, [(0, "com.example.record/3jqfcqzm3fo2j", FakeCid("v"), null)]);
        var wrapper = EncodeNode(CidOf(leaf), []);
        var blocks = Blocks(leaf, wrapper);

        var mst = MerkleSearchTree.Deserialize(CidOf(wrapper), Lookup(blocks));

        Assert.Equal(1, mst.Count);
        Assert.Throws<InvalidOperationException>(() => mst.Validate());
    }

    [Fact]
    public void Validate_TreeWrittenWithOmittedNullLinks_Throws()
    {
        // The encoding this SDK used to write — `l` and `t` left out instead of null — still
        // reads, but hashes to a different CID than the canonical node for the same entries.
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(1);
        writer.WriteTextString("e");
        writer.WriteStartArray(1);
        writer.WriteStartMap(3);
        writer.WriteTextString("k");
        writer.WriteByteString("com.example.record/3jqfcqzm3fo2j"u8);
        writer.WriteTextString("p");
        writer.WriteInt32(0);
        writer.WriteTextString("v");
        WriteLink(writer, FakeCid("v"));
        writer.WriteEndMap();
        writer.WriteEndArray();
        writer.WriteEndMap();
        var node = writer.Encode();

        var mst = MerkleSearchTree.Deserialize(CidOf(node), _ => node);

        Assert.Equal(FakeCid("v"), mst.Get("com.example.record/3jqfcqzm3fo2j"));
        Assert.Throws<InvalidOperationException>(() => mst.Validate());
    }

    [Fact]
    public void Validate_AfterAnEditToALoadedTree_ReturnsTrue()
    {
        var leaf = EncodeNode(null, [(0, "com.example.record/3jqfcqzm3fo2j", FakeCid("v"), null)]);
        var wrapper = EncodeNode(CidOf(leaf), []);
        var mst = MerkleSearchTree.Deserialize(CidOf(wrapper), Lookup(Blocks(leaf, wrapper)));

        // Once edited, the tree is rebuilt canonically; nothing is left to disagree with.
        mst.Add("com.example.record/other", FakeCid("w"));

        Assert.True(mst.Validate());
    }

    // ── Deserialization of untrusted blocks ──────────────────

    [Theory]
    [InlineData(1L)]            // first entry: no previous key to share bytes with
    [InlineData(100L)]
    [InlineData(-1L)]
    [InlineData(int.MaxValue)]
    [InlineData(1L << 40)]      // indigo SEC-4: once drove a terabyte-sized preallocation
    [InlineData(long.MinValue)]
    public void Deserialize_FirstEntryPrefixOutOfRange_ThrowsFormatException(long prefix)
    {
        var node = EncodeNode(null, [(prefix, "com.example.record/abc", FakeCid("v"), null)]);

        Assert.Throws<FormatException>(() => MerkleSearchTree.Deserialize(CidOf(node), _ => node));
    }

    [Fact]
    public void Deserialize_PrefixLongerThanThePreviousKey_ThrowsFormatException()
    {
        const string first = "com.example.record/abc";
        var node = EncodeNode(null,
        [
            (0, first, FakeCid("a"), null),
            (first.Length + 1, "d", FakeCid("b"), null),
        ]);

        Assert.Throws<FormatException>(() => MerkleSearchTree.Deserialize(CidOf(node), _ => node));
    }

    [Fact]
    public void Deserialize_PrefixEqualToThePreviousKey_IsAccepted()
    {
        const string first = "com.example.record/abc";
        var node = EncodeNode(null,
        [
            (0, first, FakeCid("a"), null),
            (first.Length, "d", FakeCid("b"), null),
        ]);

        var mst = MerkleSearchTree.Deserialize(CidOf(node), _ => node);

        Assert.Equal(FakeCid("b"), mst.Get(first + "d"));
    }

    [Fact]
    public void Deserialize_PrefixBeyondUInt64_ThrowsFormatException()
    {
        // p = 2^64 - 1, which fits no signed integer.
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(2);
        writer.WriteTextString("e");
        writer.WriteStartArray(1);
        writer.WriteStartMap(4);
        writer.WriteTextString("k");
        writer.WriteByteString("com.example.record/abc"u8);
        writer.WriteTextString("p");
        writer.WriteUInt64(ulong.MaxValue);
        writer.WriteTextString("t");
        writer.WriteNull();
        writer.WriteTextString("v");
        WriteLink(writer, FakeCid("v"));
        writer.WriteEndMap();
        writer.WriteEndArray();
        writer.WriteTextString("l");
        writer.WriteNull();
        writer.WriteEndMap();
        var node = writer.Encode();

        Assert.Throws<FormatException>(() => MerkleSearchTree.Deserialize(CidOf(node), _ => node));
    }

    [Theory]
    [InlineData("com.example.record/café")] // Latin-1, a single non-ASCII byte
    [InlineData("com.example.record")]            // no rkey
    [InlineData("com.example.record/a b")]
    public void Deserialize_InvalidKey_ThrowsFormatException(string key)
    {
        var node = EncodeNode(null, [(0, key, FakeCid("v"), null)], Encoding.Latin1);

        Assert.Throws<FormatException>(() => MerkleSearchTree.Deserialize(CidOf(node), _ => node));
    }

    [Fact]
    public void Deserialize_InvalidUtf8Key_ThrowsFormatException()
    {
        var writer = NodeWriter(null, 1);
        WriteEntry(writer, 0, [.. "com.example.record/"u8, 0xC3, 0x28], FakeCid("v"), null);
        var node = FinishNode(writer, null);

        Assert.Throws<FormatException>(() => MerkleSearchTree.Deserialize(CidOf(node), _ => node));
    }

    [Fact]
    public void Deserialize_KeysOutOfOrder_ThrowsFormatException()
    {
        var node = EncodeNode(null,
        [
            (0, "com.example.record/b", FakeCid("b"), null),
            (0, "com.example.record/a", FakeCid("a"), null),
        ]);

        Assert.Throws<FormatException>(() => MerkleSearchTree.Deserialize(CidOf(node), _ => node));
    }

    [Fact]
    public void Deserialize_SubtreeKeysOutsideTheirRange_ThrowsFormatException()
    {
        // The left subtree holds a key that sorts after the parent's first entry.
        var child = EncodeNode(null, [(0, "com.example.record/z", FakeCid("z"), null)]);
        var root = EncodeNode(CidOf(child), [(0, "com.example.record/m", FakeCid("m"), null)]);

        Assert.Throws<FormatException>(
            () => MerkleSearchTree.Deserialize(CidOf(root), Lookup(Blocks(child, root))));
    }

    [Fact]
    public void Deserialize_SubtreeLinkedTwice_ThrowsFormatException()
    {
        var shared = EncodeNode(null, [(0, "com.example.record/a", FakeCid("a"), null)]);
        var root = EncodeNode(CidOf(shared), [(0, "com.example.record/m", FakeCid("m"), CidOf(shared))]);

        Assert.Throws<FormatException>(
            () => MerkleSearchTree.Deserialize(CidOf(root), Lookup(Blocks(shared, root))));
    }

    [Fact]
    public void Deserialize_EmptyInnerNode_ThrowsFormatException()
    {
        var empty = EncodeNode(null, []);
        var root = EncodeNode(CidOf(empty), [(0, "com.example.record/m", FakeCid("m"), null)]);

        Assert.Throws<FormatException>(
            () => MerkleSearchTree.Deserialize(CidOf(root), Lookup(Blocks(empty, root))));
    }

    [Fact]
    public void Deserialize_ChainDeeperThanAnyRealTree_ThrowsFormatException()
    {
        // 10,000 entry-less wrapper nodes: the walk must stop long before the stack runs out.
        var node = EncodeNode(null, [(0, "com.example.record/a", FakeCid("a"), null)]);
        var blocks = Blocks(node);
        for (var i = 0; i < 10_000; i++)
        {
            node = EncodeNode(CidOf(node), []);
            blocks[CidComputation.EncodeCidToString(CidOf(node))] = node;
        }

        Assert.Throws<FormatException>(() => MerkleSearchTree.Deserialize(CidOf(node), Lookup(blocks)));
    }

    [Fact]
    public void Deserialize_MissingBlock_ThrowsFormatException()
    {
        var root = BuildTree(100).ComputeRootCid();

        Assert.Throws<FormatException>(() => MerkleSearchTree.Deserialize(root, _ => null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ff")]
    [InlineData("a1616501")]             // {"e": 1}
    [InlineData("a2616580616c01")]       // {"e": [], "l": 1}
    [InlineData("a1616581a0")]           // an entry with neither k nor v
    [InlineData("fb400921fb54442d18")]   // a float
    public void Deserialize_MalformedNode_ThrowsFormatException(string hex)
    {
        var node = Convert.FromHexString(hex);

        Assert.Throws<FormatException>(() => MerkleSearchTree.Deserialize(CidOf(node), _ => node));
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
    public void SerializeProof_UnknownKey_ProvesItsAbsence()
    {
        var mst = BuildTree(200);

        var (root, proof) = mst.SerializeProof(["col/k0100x"]);

        Assert.NotEmpty(proof);
        Assert.Null(WalkProof(root, proof, "col/k0100x", requireComplete: true));
    }

    /// <summary>
    /// Descends a partial block set from the root looking for <paramref name="key"/>, decoding
    /// each node the way a relay would. Returns the value, or null if the key is absent.
    /// </summary>
    private static byte[]? WalkProof(
        byte[] rootCid, Dictionary<string, byte[]> blocks, string key, bool requireComplete = false)
    {
        var cid = rootCid;
        while (true)
        {
            if (!blocks.TryGetValue(CidComputation.EncodeCidToString(cid), out var bytes))
            {
                Assert.False(requireComplete, "the proof stops before the search does");
                return null;
            }

            var node = MstNodeData.FromBytes(bytes);
            var child = node.Left;
            var previousKey = "";

            foreach (var entry in node.Entries)
            {
                var entryKey = string.Concat(
                    previousKey[..entry.PrefixLength],
                    Encoding.ASCII.GetString(entry.KeySuffix));
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

    // ── Helpers ──────────────────────────────────────────────

    private static MerkleSearchTree BuildTree(int count)
    {
        var entries = new Dictionary<string, byte[]>();
        for (var i = 0; i < count; i++)
            entries[$"col/k{i:D4}"] = FakeCid($"v{i}");

        return MerkleSearchTree.Create(entries);
    }

    private static byte[] CidOf(byte[] node) => CidComputation.ComputeBinaryForDagCbor(node);

    private static Dictionary<string, byte[]> Blocks(params byte[][] nodes) =>
        nodes.ToDictionary(n => CidComputation.EncodeCidToString(CidOf(n)), n => n, StringComparer.Ordinal);

    /// <summary>
    /// Encodes an MST node by hand, so tests can build nodes <see cref="MerkleSearchTree"/> would
    /// never write.
    /// </summary>
    private static byte[] EncodeNode(
        byte[]? left,
        (long Prefix, string Suffix, byte[] Value, byte[]? Tree)[] entries,
        Encoding? encoding = null)
    {
        var writer = NodeWriter(left, entries.Length);
        foreach (var (prefix, suffix, value, tree) in entries)
            WriteEntry(writer, prefix, (encoding ?? Encoding.ASCII).GetBytes(suffix), value, tree);
        return FinishNode(writer, left);
    }

    private static CborWriter NodeWriter(byte[]? left, int entryCount)
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(2);
        writer.WriteTextString("e");
        writer.WriteStartArray(entryCount);
        return writer;
    }

    private static void WriteEntry(CborWriter writer, long prefix, byte[] suffix, byte[] value, byte[]? tree)
    {
        writer.WriteStartMap(4);
        writer.WriteTextString("k");
        writer.WriteByteString(suffix);
        writer.WriteTextString("p");
        writer.WriteInt64(prefix);
        writer.WriteTextString("t");
        if (tree is null) writer.WriteNull(); else WriteLink(writer, tree);
        writer.WriteTextString("v");
        WriteLink(writer, value);
        writer.WriteEndMap();
    }

    private static byte[] FinishNode(CborWriter writer, byte[]? left)
    {
        writer.WriteEndArray();
        writer.WriteTextString("l");
        if (left is null) writer.WriteNull(); else WriteLink(writer, left);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static void WriteLink(CborWriter writer, byte[] cid)
    {
        writer.WriteTag((CborTag)42);
        writer.WriteByteString([0x00, .. cid]);
    }
}
