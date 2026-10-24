using System.Text;
using ATProtoNet.Data;
using ATProtoNet.Repo;
using static ATProtoNet.Tests.Repo.RepoFixtures;

namespace ATProtoNet.Tests.Repo;

// Loading trees from untrusted blocks: nodes are hand-built DRISL, so they can break every rule.
// Keys whose name states their layer (A0 on layer 0, B1 on layer 1, ...) come from mst/example_keys.txt.
public class MstLoadTests
{
    private const string First = "com.example.record/abc";

    private static MerkleSearchTree LoadRoot(params byte[][] blocks) => MerkleSearchTree.Load(CidOf(blocks[0]), Store(blocks));

    // Loads the first block as the root of the tree all the blocks make, and returns why it was refused.
    private static void AssertMalformed(params byte[][] blocks)
    {
        var ex = Assert.Throws<RepoVerificationException>(() => MerkleSearchTree.LoadComplete(CidOf(blocks[0]), Store(blocks)));
        Assert.Equal(RepoVerificationError.MalformedTree, ex.Error);
    }

    private static void AssertRootMalformed(byte[] root)
    {
        Assert.Equal(RepoVerificationError.MalformedTree, Assert.Throws<RepoVerificationException>(() => LoadRoot(root)).Error);
        AssertMalformed(root);
    }

    // ── Node encoding ──

    [Fact]
    [Trait("Spec", "REP-5")]
    [Trait("Spec", "REP-6")]
    public void WriteBlocks_OneKey_WritesTheReferenceEncodingWithNullLinks()
    {
        var tree = Tree(("a/b", Leaf));

        var block = Assert.Single(Blocks(tree).Blocks);

        Assert.Equal("a2616581a4616b43612f626170006174f66176d82a582500" + Convert.ToHexStringLower(Leaf.ToByteArray()) + "616cf6",
            Convert.ToHexStringLower(block.Data.Span));
    }

    [Fact]
    [Trait("Spec", "REP-5")]
    public void WriteBlocks_KeysOfOneNode_ArePrefixCompressedAgainstThePreviousKey()
    {
        var keys = KeysOnLayer(0, 2);
        var tree = Tree((keys[0], Fake("a")), (keys[1], Fake("b")));

        var node = (DataModelMap)DataModelValue.FromDrisl(Assert.Single(Blocks(tree).Blocks).Data);
        var entries = (DataModelArray)node["e"];

        var prefix = keys[0].AsSpan().CommonPrefixLength(keys[1]);
        Assert.Equal(new DataModelInteger(0), ((DataModelMap)entries[0])["p"]);
        Assert.Equal(new DataModelInteger(prefix), ((DataModelMap)entries[1])["p"]);
        Assert.Equal(Encoding.ASCII.GetBytes(keys[1][prefix..]), ((DataModelBytes)((DataModelMap)entries[1])["k"]).Value.ToArray());
        Assert.Equal(new DataModelLink(Fake("b")), ((DataModelMap)entries[1])["v"]);
        Assert.Equal(DataModelNull.Instance, node["l"]);
    }

    [Fact]
    [Trait("Spec", "REP-5")]
    public void WriteBlocks_SubtreeLinks_NameTheChildNodes()
    {
        var tree = Tree(("A0/374913", Leaf), ("B1/986427", Leaf), ("C0/451630", Leaf));
        var blocks = Blocks(tree);

        Assert.True(blocks.TryGet(tree.Root, out var block));
        var root = (DataModelMap)DataModelValue.FromDrisl(block);
        var entry = (DataModelMap)((DataModelArray)root["e"]).Single();

        Assert.Equal(3, blocks.Count);
        Assert.True(blocks.TryGet(((DataModelLink)root["l"]).Cid, out _));
        Assert.True(blocks.TryGet(((DataModelLink)entry["t"]).Cid, out _));
    }

    [Fact]
    [Trait("Spec", "REP-6")]
    [Trait("Spec", "REP-15")]
    public void LoadComplete_ReferenceEncodedEmptyNode_IsTheEmptyTree()
    {
        byte[] node = [0xa2, 0x61, 0x65, 0x80, 0x61, 0x6c, 0xf6];

        var tree = MerkleSearchTree.LoadComplete(CidOf(node), Store(node));

        Assert.Equal(EmptyRoot, tree.Root);
        Assert.Empty(tree.Enumerate());
    }

    [Fact]
    [Trait("Spec", "REP-7")]
    [Trait("Spec", "REP-15")]
    public void Load_OmittedNullLinks_ReadsThemAsNullButIsNotCanonical()
    {
        // The encoding 0.8 used to write: l and t left out instead of null.
        var node = Map(("e", Array(Map(("k", Bytes("com.example.record/3jqfcqzm3fo2j"u8)), ("p", Int(0)), ("v", Link(Leaf))))));

        Assert.Equal(Leaf, LoadRoot(node).Get(Key("com.example.record/3jqfcqzm3fo2j")));
        AssertMalformed(node);
    }

    [Fact]
    [Trait("Spec", "REP-15")]
    public void LoadComplete_HandBuiltCanonicalTree_IsTheTreeOfItsEntries()
    {
        // The control for the hostile nodes below: built by the rules, the helper gives the canonical tree.
        var left = Node(null, ("A0/374913", Leaf, null));
        var right = Node(null, ("C0/451630", Leaf, null));
        var root = Node(CidOf(left), ("B1/986427", Leaf, CidOf(right)));

        var tree = MerkleSearchTree.LoadComplete(CidOf(root), Store(root, left, right));

        Assert.Equal(Tree(("A0/374913", Leaf), ("B1/986427", Leaf), ("C0/451630", Leaf)).Root, tree.Root);
    }

    // ── Strict decoding ──

    [Theory]
    [Trait("Spec", "REP-10")]
    [InlineData("")]
    [InlineData("ff")]
    [InlineData("80")]                         // an array, not a map
    [InlineData("a1616501")]                   // {"e": 1}
    [InlineData("a2616580616c01")]             // {"e": [], "l": 1}
    [InlineData("a1616581a0")]                 // an entry with neither k nor v
    [InlineData("a1616581a1616b4161")]         // an entry without v
    [InlineData("a1616581a2616b416161760a")]   // v is not a link
    [InlineData("a161658161")]                 // truncated
    [InlineData("fb400921fb54442d18")]         // a float
    [InlineData("bf6165806161f6ff")]           // indefinite-length map
    [InlineData("a2616580616580")]             // e twice
    [InlineData("a2616cf6616580")]             // keys out of canonical order
    [InlineData("a2616580616cf600")]           // trailing bytes
    public void Load_MalformedNode_ThrowsMalformedTree(string hex) => AssertRootMalformed(Convert.FromHexString(hex));

    [Fact]
    [Trait("Spec", "REP-10")]
    public void Load_NonCanonicalIntegerOrForeignTag_ThrowsMalformedTree()
    {
        AssertRootMalformed(RawNode(null, Entry([0x18, 0x00], Encoding.ASCII.GetBytes(First), Leaf, null)));
        AssertRootMalformed(RawNode(null, Map(("k", Bytes(Encoding.ASCII.GetBytes(First))), ("p", Int(0)), ("t", Null), ("v", [0xD8, 0x2B, .. Bytes([0x00, .. Leaf.ToByteArray()])]))));
    }

    [Fact]
    [Trait("Spec", "REP-10")]
    [Trait("Spec", "REP-15")]
    public void Load_UnknownFields_AreSkippedButAreNotCanonical()
    {
        var entry = Map(("k", Bytes(Encoding.ASCII.GetBytes(First))), ("p", Int(0)), ("t", Null), ("v", Link(Leaf)), ("x", Int(1)));
        var node = Map(("e", Array(entry)), ("l", Null), ("zz", Text("ignored")));

        Assert.Equal(Leaf, LoadRoot(node).Get(Key(First)));
        AssertMalformed(node);
    }

    [Fact]
    [Trait("Spec", "REP-10")]
    public void Load_MoreEntriesThanAnyRealNode_ThrowsMalformedTree()
    {
        var keys = KeysOnLayer(0, MerkleSearchTree.MaxEntriesPerNode + 1);

        AssertRootMalformed(Node(null, [.. keys.Select(k => (k, Leaf, (Cid?)null))]));
    }

    // ── Prefix lengths (indigo SEC-4: a prefix of 1 << 40 once drove a terabyte preallocation) ──

    [Theory]
    [Trait("Spec", "REP-11")]
    [InlineData(1L)]
    [InlineData(100L)]
    [InlineData(-1L)]
    [InlineData(int.MaxValue)]
    [InlineData(1L << 40)]
    [InlineData(long.MinValue)]
    public void Load_FirstEntryPrefixOutOfRange_ThrowsMalformedTree(long prefix) =>
        AssertRootMalformed(RawNode(null, Entry(Int(prefix), Encoding.ASCII.GetBytes(First), Leaf, null)));

    [Theory]
    [Trait("Spec", "REP-11")]
    [InlineData(-1L)]
    [InlineData(23L)]                 // one past the previous key
    [InlineData(int.MaxValue)]
    public void Load_PrefixOutsideThePreviousKey_ThrowsMalformedTree(long prefix) =>
        AssertRootMalformed(RawNode(null, Entry(Int(0), Encoding.ASCII.GetBytes(First), Leaf, null), Entry(Int(prefix), "d"u8, Leaf, null)));

    [Fact]
    [Trait("Spec", "REP-11")]
    public void Load_PrefixBeyondInt64_ThrowsMalformedTree() =>
        AssertRootMalformed(RawNode(null, Entry(UInt(ulong.MaxValue), Encoding.ASCII.GetBytes(First), Leaf, null)));

    [Fact]
    [Trait("Spec", "REP-11")]
    public void Load_PrefixEqualToThePreviousKey_IsAccepted()
    {
        // Both keys must share a layer to share a node.
        var first = KeysOnLayer(0, 1)[0];
        var second = Enumerable.Range(0, 1000).Select(i => first + i).First(k => Key(k).Layer == 0);
        var node = RawNode(null, Entry(Int(0), Encoding.ASCII.GetBytes(first), Fake("a"), null), Entry(Int(first.Length), Encoding.ASCII.GetBytes(second[first.Length..]), Fake("b"), null));

        var tree = MerkleSearchTree.LoadComplete(CidOf(node), Store(node));

        Assert.Equal(Fake("b"), tree.Get(Key(second)));
    }

    // ── Keys ──

    [Theory]
    [Trait("Spec", "REP-12")]
    [InlineData("636f6d2e6578616d706c652e7265636f72642f636166e9")] // com.example.record/café in Latin-1
    [InlineData("636f6d2e6578616d706c652e7265636f72642fc328")]     // invalid UTF-8
    [InlineData("636f6d2e6578616d706c652e7265636f7264")]           // no rkey
    [InlineData("636f6d2e6578616d706c652e7265636f72642f612062")]   // a space
    public void Load_InvalidKey_ThrowsMalformedTree(string key) =>
        AssertRootMalformed(RawNode(null, Entry(Int(0), Convert.FromHexString(key), Leaf, null)));

    [Theory]
    [Trait("Spec", "REP-12")]
    [InlineData("com.example.record/b", "com.example.record/a")]
    [InlineData("com.example.record/a", "com.example.record/a")]
    public void Load_KeysNotStrictlyIncreasing_ThrowsMalformedTree(string first, string second) =>
        AssertRootMalformed(Node(null, (first, Leaf, null), (second, Leaf, null)));

    [Fact]
    [Trait("Spec", "REP-12")]
    public void Load_LeftSubtreeKeyAboveItsParent_ThrowsMalformedTree()
    {
        // F1 sits on the layer below B2, as a subtree of it must, but sorts after it.
        var intruder = Node(null, ("F1/085263", Leaf, null));
        var root = Node(CidOf(intruder), ("B2/827649", Leaf, null));

        AssertMalformed(root, intruder);
        Assert.Throws<RepoVerificationException>(() => LoadRoot(root, intruder).Get(Key("A0/374913")));
    }

    [Fact]
    [Trait("Spec", "REP-12")]
    public void Load_RightSubtreeKeyBelowItsParent_ThrowsMalformedTree()
    {
        var intruder = Node(null, ("A1/076595", Leaf, null));

        AssertMalformed(Node(null, ("B2/827649", Leaf, CidOf(intruder))), intruder);
    }

    // ── Layers ──

    [Fact]
    [Trait("Spec", "REP-13")]
    public void Load_ChildTwoLayersDown_ThrowsMalformedTree()
    {
        var child = Node(null, ("A0/374913", Leaf, null));

        AssertMalformed(Node(CidOf(child), ("D2/269196", Leaf, null)), child);
    }

    [Fact]
    [Trait("Spec", "REP-13")]
    public void Load_ChildOnItsParentsLayer_ThrowsMalformedTree()
    {
        var child = Node(null, ("A1/076595", Leaf, null));

        AssertMalformed(Node(CidOf(child), ("B1/986427", Leaf, null)), child);
    }

    [Fact]
    [Trait("Spec", "REP-13")]
    public void Load_NodeMixingLayers_ThrowsMalformedTree() =>
        AssertRootMalformed(Node(null, ("A0/374913", Leaf, null), ("B1/986427", Leaf, null)));

    [Fact]
    [Trait("Spec", "REP-13")]
    public void Load_LayerZeroNodeWithASubtree_ThrowsMalformedTree()
    {
        var child = Node(null, ("B0/601692", Leaf, null));

        AssertRootMalformed(Node(null, ("A0/374913", Leaf, CidOf(child))));
    }

    // ── Shape ──

    [Fact]
    [Trait("Spec", "REP-14")]
    public void Load_EmptyInnerNode_ThrowsMalformedTree()
    {
        var empty = RawNode(null);

        AssertMalformed(Node(CidOf(empty), ("B1/986427", Leaf, null)), empty);
    }

    [Fact]
    [Trait("Spec", "REP-14")]
    [Trait("Spec", "REP-15")]
    public void Load_RootWithOnlyALink_ThrowsMalformedTree()
    {
        // An extra wrapper layer: the entries are fine, but the canonical tree has no such root.
        var child = Node(null, ("A0/374913", Leaf, null));
        var root = RawNode(CidOf(child));

        Assert.Throws<RepoVerificationException>(() => LoadRoot(root, child));
        AssertMalformed(root, child);
    }

    [Fact]
    [Trait("Spec", "REP-14")]
    public void Load_SameNodeReferencedTwice_ThrowsMalformedTree()
    {
        // One node is both the left subtree of F1 and the subtree after it (atproto #5584).
        var shared = Node(null, ("A0/374913", Leaf, null));

        AssertMalformed(Node(CidOf(shared), ("F1/085263", Leaf, CidOf(shared))), shared);
    }

    [Fact]
    [Trait("Spec", "REP-14")]
    public void Load_ChainDeeperThanAnyRealTree_ThrowsMalformedTreeWithoutRecursing()
    {
        // 10,000 key-less wrappers: the walk must stop long before the stack runs out.
        List<byte[]> chain = [Node(null, ("A0/374913", Leaf, null))];
        for (var i = 0; i < 10_000; i++) chain.Add(RawNode(CidOf(chain[^1])));
        var root = Node(CidOf(chain[^1]), ("D2/269196", Leaf, null));

        AssertMalformed([chain[^1], .. chain]);
        AssertMalformed([root, .. chain]);
    }

    [Fact]
    [Trait("Spec", "REP-15")]
    public void LoadComplete_KeysNotPrefixCompressed_ThrowsMalformedTree()
    {
        var keys = KeysOnLayer(0, 2);
        var node = RawNode(null, Entry(Int(0), Encoding.ASCII.GetBytes(keys[0]), Leaf, null), Entry(Int(0), Encoding.ASCII.GetBytes(keys[1]), Leaf, null));

        Assert.Equal(Leaf, LoadRoot(node).Get(Key(keys[1])));
        AssertMalformed(node);
    }

    // ── Missing blocks ──

    [Fact]
    [Trait("Spec", "REP-16")]
    public void Load_MissingRoot_ThrowsIncompleteOrMissingBlock()
    {
        var root = GeneratedTree(4, 100).Root;

        Assert.Equal(root, Assert.Throws<MstIncompleteException>(() => MerkleSearchTree.Load(root, new MemoryBlockStore())).Cid);
        Assert.Equal(RepoVerificationError.MissingBlock, Assert.Throws<RepoVerificationException>(() => MerkleSearchTree.LoadComplete(root, new MemoryBlockStore())).Error);
    }

    [Fact]
    [Trait("Spec", "REP-16")]
    public void LoadComplete_MissingInnerNode_ThrowsMissingBlock()
    {
        var tree = GeneratedTree(4, 100);
        var blocks = Blocks(tree);
        var dropped = blocks.Blocks.First(b => b.Cid != tree.Root).Cid;
        var partial = new MemoryBlockStore();
        foreach (var block in blocks.Blocks.Where(b => b.Cid != dropped)) partial.Put(block.Cid, block.Data);

        Assert.Equal(RepoVerificationError.MissingBlock, Assert.Throws<RepoVerificationException>(() => MerkleSearchTree.LoadComplete(tree.Root, partial)).Error);
    }
}
