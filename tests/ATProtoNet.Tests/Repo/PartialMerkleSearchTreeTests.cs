using System.Formats.Cbor;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

/// <summary>
/// Inverting a commit against the partial tree its blocks carry, the Sync 1.1 check: replaying
/// the operations backwards over the covering proof must land on the root before the commit.
/// </summary>
public sealed class PartialMerkleSearchTreeTests
{
    private static readonly byte[] Leaf = MstReferenceVectorTests.Leaf;

    private static string Text(byte[] cid) => CidComputation.EncodeCidToString(cid);

    private static Func<byte[], byte[]?> Lookup(Dictionary<string, byte[]> blocks) =>
        cid => blocks.TryGetValue(Text(cid), out var data) ? data : null;

    /// <summary>
    /// Inverts <paramref name="ops"/> as the relay does: deletions (put back) first, then the rest by path.
    /// </summary>
    private static void Invert(PartialMerkleSearchTree tree, IEnumerable<(string Key, byte[]? Before, byte[]? After)> ops)
    {
        foreach (var (key, before, after) in ops.OrderBy(o => o.After is null ? 0 : 1).ThenBy(o => o.Key, StringComparer.Ordinal))
        {
            if (after is null)
            {
                Assert.Null(tree.Insert(key, before!));
            }
            else if (before is null)
            {
                Assert.Equal(after, tree.Remove(key));
            }
            else
            {
                Assert.Equal(after, tree.Insert(key, before));
            }
        }
    }

    // ── Reference vectors ────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Invert_CommitProofFixture_LandsOnTheRootBeforeTheCommit(int index)
    {
        var fixture = MstReferenceVectorTests.CommitProofFixtures[index];
        var tree = MerkleSearchTree.Create(fixture.Keys.Select(k => KeyValuePair.Create(k, Leaf)));
        foreach (var key in fixture.Adds)
            tree.Add(key, Leaf);
        foreach (var key in fixture.Dels)
            tree.Delete(key);

        var (root, proof) = tree.SerializeProof([.. fixture.Adds, .. fixture.Dels]);
        var partial = PartialMerkleSearchTree.Load(root, Lookup(proof));
        Assert.Equal(fixture.RootAfter, Text(partial.RootCid()));

        Invert(partial,
        [
            .. fixture.Adds.Select(k => (k, (byte[]?)null, (byte[]?)Leaf)),
            .. fixture.Dels.Select(k => (k, (byte[]?)Leaf, (byte[]?)null)),
        ]);

        Assert.Equal(fixture.RootBefore, Text(partial.RootCid()));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 5)]
    [InlineData(4, 40)]
    [InlineData(5, 250)]
    [InlineData(6, 1500)]
    public void Invert_RandomCommits_LandOnTheRootBeforeTheCommit(ulong seed, int size)
    {
        var random = new Random((int)seed);
        var keys = MstReferenceVectorTests.GeneratedKeys(seed, size + 30);
        var fresh = keys.Skip(size).ToList();

        for (var round = 0; round < 40; round++)
        {
            var tree = MerkleSearchTree.Create(keys.Take(size).Select((k, i) => KeyValuePair.Create(k, MstReferenceVectorTests.GeneratedValue(i))));
            var before = tree.ComputeRootCid();
            var existing = tree.GetEntries().Select(e => e.Key).ToList();

            // Up to eight operations on distinct keys: creates, updates and deletes.
            var ops = new List<(string Key, byte[]? Before, byte[]? After)>();
            var touched = new HashSet<string>();
            var count = random.Next(1, 9);
            for (var i = 0; i < count; i++)
            {
                var kind = existing.Count == 0 ? 0 : random.Next(3);
                if (kind == 0)
                {
                    var key = fresh[random.Next(fresh.Count)];
                    if (!touched.Add(key))
                        continue;
                    var value = MstReferenceVectorTests.GeneratedValue(10_000 + round * 10 + i);
                    tree.Add(key, value);
                    ops.Add((key, null, value));
                }
                else
                {
                    var key = existing[random.Next(existing.Count)];
                    if (!touched.Add(key))
                        continue;
                    var old = tree.Get(key)!;
                    if (kind == 1)
                    {
                        var value = MstReferenceVectorTests.GeneratedValue(20_000 + round * 10 + i);
                        tree.Update(key, value);
                        ops.Add((key, old, value));
                    }
                    else
                    {
                        tree.Delete(key);
                        ops.Add((key, old, null));
                    }
                }
            }

            var (after, proof) = tree.SerializeProof(ops.Select(o => o.Key));
            var partial = PartialMerkleSearchTree.Load(after, Lookup(proof));
            Invert(partial, ops);

            Assert.Equal(Text(before), Text(partial.RootCid()));
        }
    }

    [Fact]
    public void Invert_DeletingTheLastKey_LandsOnTheEmptyTree()
    {
        var tree = MerkleSearchTree.Create();
        var empty = tree.ComputeRootCid();
        tree.Add("com.example.record/3jqfcqzm3fo2j", Leaf);

        var (root, proof) = tree.SerializeProof(["com.example.record/3jqfcqzm3fo2j"]);
        var partial = PartialMerkleSearchTree.Load(root, Lookup(proof));
        Assert.Equal(Leaf, partial.Remove("com.example.record/3jqfcqzm3fo2j"));

        Assert.Equal(Text(empty), Text(partial.RootCid()));
    }

    // ── Partial trees ────────────────────────────────────────

    [Fact]
    public void Load_UntouchedSubtrees_StayCidReferencesAndKeepTheRoot()
    {
        var tree = MerkleSearchTree.Create(MstReferenceVectorTests.GeneratedKeys(7, 400)
            .Select((k, i) => KeyValuePair.Create(k, MstReferenceVectorTests.GeneratedValue(i))));
        var (root, proof) = tree.SerializeProof(["app.bsky.feed.post/aaaaaaaaaaaaa"]);
        var (_, all) = tree.Serialize();
        Assert.True(proof.Count < all.Count);

        var partial = PartialMerkleSearchTree.Load(root, Lookup(proof));

        Assert.Equal(Text(root), Text(partial.RootCid()));
    }

    [Fact]
    public void Remove_KeyUnderAMissingSubtree_ThrowsPartialTreeException()
    {
        var tree = MerkleSearchTree.Create(MstReferenceVectorTests.GeneratedKeys(8, 300)
            .Select((k, i) => KeyValuePair.Create(k, MstReferenceVectorTests.GeneratedValue(i))));
        var victim = tree.GetEntries().First().Key;
        var other = tree.GetEntries().Last().Key;

        // A proof for another key does not carry the path to this one.
        var (root, proof) = tree.SerializeProof([other]);
        var partial = PartialMerkleSearchTree.Load(root, Lookup(proof));

        Assert.Throws<PartialTreeException>(() => partial.Remove(victim));
    }

    [Fact]
    public void Get_ReadsLoadedKeys()
    {
        var tree = MerkleSearchTree.Create(MstReferenceVectorTests.GeneratedKeys(9, 100)
            .Select((k, i) => KeyValuePair.Create(k, MstReferenceVectorTests.GeneratedValue(i))));
        var key = tree.GetEntries().ElementAt(50).Key;
        var (root, proof) = tree.SerializeProof([key]);

        var partial = PartialMerkleSearchTree.Load(root, Lookup(proof));

        Assert.Equal(tree.Get(key), partial.Get(key));
    }

    [Fact]
    public void Insert_SameValue_ChangesNothing()
    {
        var tree = MerkleSearchTree.Create(MstReferenceVectorTests.GeneratedKeys(10, 50)
            .Select((k, i) => KeyValuePair.Create(k, MstReferenceVectorTests.GeneratedValue(i))));
        var (key, value) = tree.GetEntries().First();
        var (root, proof) = tree.SerializeProof([key]);
        var partial = PartialMerkleSearchTree.Load(root, Lookup(proof));

        Assert.Equal(value, partial.Insert(key, value));
        Assert.Equal(Text(root), Text(partial.RootCid()));
    }

    // ── Malformed blocks ─────────────────────────────────────

    [Fact]
    public void Load_MissingRootBlock_ThrowsFormatException()
    {
        var root = CidComputation.ComputeBinaryForDagCbor([0xA0]);
        Assert.Throws<FormatException>(() => PartialMerkleSearchTree.Load(root, _ => null));
    }

    [Fact]
    public void Load_KeysOutOfOrder_ThrowsFormatException()
    {
        // Two layer-0 keys in descending order.
        var node = Node(null, [("com.example.record/b", Leaf, null), ("com.example.record/a", Leaf, null)]);

        Assert.Throws<FormatException>(() => PartialMerkleSearchTree.Load(CidOf(node), _ => node));
    }

    [Fact]
    public void Load_RootWithOnlyALink_ThrowsFormatException()
    {
        var child = Node(null, [("A0/374913", Leaf, null)]);
        var root = Node(CidOf(child), []);
        var blocks = new Dictionary<string, byte[]> { [Text(CidOf(child))] = child, [Text(CidOf(root))] = root };

        Assert.Throws<FormatException>(() => PartialMerkleSearchTree.Load(CidOf(root), Lookup(blocks)));
    }

    [Fact]
    public void Load_ChildOnTheWrongLayer_ThrowsFormatException()
    {
        // "A0/374913" sits on layer 0 and "D2/269196" on layer 2: a layer-0 node cannot hang
        // straight off a layer-2 one.
        var child = Node(null, [("A0/374913", Leaf, null)]);
        var root = Node(CidOf(child), [("D2/269196", Leaf, null)]);
        var blocks = new Dictionary<string, byte[]> { [Text(CidOf(child))] = child, [Text(CidOf(root))] = root };

        Assert.Throws<FormatException>(() => PartialMerkleSearchTree.Load(CidOf(root), Lookup(blocks)));
    }

    [Fact]
    public void Load_SubtreeKeyOutsideItsRange_ThrowsFormatException()
    {
        // A left subtree must sort below the root's first key.
        var tree = MerkleSearchTree.Create([KeyValuePair.Create("A0/374913", Leaf), KeyValuePair.Create("B2/827649", Leaf)]);
        var (root, blocks) = tree.Serialize();
        Assert.Equal(Text(root), Text(PartialMerkleSearchTree.Load(root, Lookup(blocks)).RootCid()));

        // "F1/085263" sits on the layer below, as a left subtree of "B2/827649" must, but sorts above it.
        var intruder = Node(null, [("F1/085263", Leaf, null)]);
        var forged = Node(CidOf(intruder), [("B2/827649", Leaf, null)]);
        blocks[Text(CidOf(intruder))] = intruder;
        blocks[Text(CidOf(forged))] = forged;

        Assert.Throws<FormatException>(() => PartialMerkleSearchTree.Load(CidOf(forged), Lookup(blocks)));
    }

    private static byte[] CidOf(byte[] node) => CidComputation.ComputeBinaryForDagCbor(node);

    /// <summary>Encodes an MST node with full keys (no prefix compression), as a hostile producer may.</summary>
    private static byte[] Node(byte[]? left, (string Key, byte[] Value, byte[]? Tree)[] entries)
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(2);
        writer.WriteTextString("e");
        writer.WriteStartArray(entries.Length);
        foreach (var (key, value, tree) in entries)
        {
            writer.WriteStartMap(4);
            writer.WriteTextString("k");
            writer.WriteByteString(System.Text.Encoding.ASCII.GetBytes(key));
            writer.WriteTextString("p");
            writer.WriteInt32(0);
            writer.WriteTextString("t");
            WriteLink(writer, tree);
            writer.WriteTextString("v");
            WriteLink(writer, value);
            writer.WriteEndMap();
        }

        writer.WriteEndArray();
        writer.WriteTextString("l");
        WriteLink(writer, left);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static void WriteLink(CborWriter writer, byte[]? cid)
    {
        if (cid is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteTag((CborTag)42);
        writer.WriteByteString([0x00, .. cid]);
    }
}
