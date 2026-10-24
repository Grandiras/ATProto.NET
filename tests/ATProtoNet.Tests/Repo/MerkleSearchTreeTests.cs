using ATProtoNet.Repo;
using static ATProtoNet.Tests.Repo.RepoFixtures;

namespace ATProtoNet.Tests.Repo;

public class MerkleSearchTreeTests
{
    [Fact]
    [Trait("Spec", "REP-6")]
    public void Empty_Root_IsTheReferenceEmptyNode()
    {
        var tree = MerkleSearchTree.Empty();

        var block = Assert.Single(Blocks(tree).Blocks);
        Assert.Equal("a2616580616cf6", Convert.ToHexStringLower(block.Data.Span));
        Assert.Equal(EmptyRoot, tree.Root);
        Assert.Equal(EmptyRoot, block.Cid);
        Assert.Empty(tree.Enumerate());
        Assert.Equal(EmptyRoot, MerkleSearchTree.Build([]).Root);
    }

    [Fact]
    public void Get_InsertedAndMissingKeys_ReturnsTheValueOrNull()
    {
        var tree = Tree(("app.bsky.feed.post/aaa", Fake("a")), ("app.bsky.feed.like/ddd", Fake("d")), ("app.bsky.graph.follow/eee", Fake("e")));

        Assert.Equal(Fake("d"), tree.Get(Key("app.bsky.feed.like/ddd")));
        Assert.Null(tree.Get(Key("app.bsky.feed.post/xyz")));
    }

    [Fact]
    [Trait("Spec", "REP-3")]
    public void Enumerate_EntriesInsertedOutOfOrder_ReturnsAsciiOrder()
    {
        string[] ordered = ["B/1", "a./1", "a/1", "a~/1", "c/1"];
        var tree = ordered.Reverse().Aggregate(MerkleSearchTree.Empty(), (t, k) => t.Insert(Key(k), Fake(k)));

        Assert.Equal(ordered, tree.Enumerate().Select(e => e.Key.Value));
        Assert.Equal(ordered.Select(Fake), tree.Enumerate().Select(e => e.Value));
    }

    [Fact]
    public void Enumerate_From_StartsAtTheFirstKeyAtOrAfterIt()
    {
        var tree = GeneratedTree(3, 300);
        var all = tree.Enumerate().ToList();

        Assert.Equal(all.Skip(120), tree.Enumerate(all[120].Key));
        Assert.Equal(all.Skip(121), tree.Enumerate(Key(all[120].Key.Value + "0")));
        Assert.Empty(tree.Enumerate(Key("zzz/zzz")));
    }

    [Fact]
    [Trait("Spec", "REP-17")]
    public void Insert_ExistingKey_ThrowsInvalidOperationException()
    {
        var tree = Tree(("key/1", Fake("v1")));

        Assert.Throws<InvalidOperationException>(() => tree.Insert(Key("key/1"), Fake("v2")));
        Assert.Throws<InvalidOperationException>(() => tree.Insert(Key("key/1"), Fake("v1")));
    }

    [Fact]
    public void Build_DuplicateKey_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => Tree(("key/1", Fake("v1")), ("key/1", Fake("v2"))));

    [Fact]
    public void Insert_DefaultKey_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => MerkleSearchTree.Empty().Insert(default, Leaf));

    [Fact]
    [Trait("Spec", "REP-17")]
    public void Update_ExistingKey_ChangesItsValue()
    {
        var tree = Tree(("key/1", Fake("v1")), ("key/2", Fake("v2")));

        var updated = tree.Update(Key("key/1"), Fake("new"));

        Assert.Equal(Fake("new"), updated.Get(Key("key/1")));
        Assert.Equal(Tree(("key/1", Fake("new")), ("key/2", Fake("v2"))).Root, updated.Root);
    }

    [Fact]
    [Trait("Spec", "REP-17")]
    public void UpdateOrDelete_MissingKey_ThrowsInvalidOperationException()
    {
        var tree = Tree(("key/1", Fake("v1")));

        Assert.Throws<InvalidOperationException>(() => tree.Update(Key("key/2"), Fake("v")));
        Assert.Throws<InvalidOperationException>(() => tree.Delete(Key("key/2")));
        Assert.Throws<InvalidOperationException>(() => MerkleSearchTree.Empty().Delete(Key("key/1")));
    }

    [Fact]
    public void Delete_MiddleKey_RemovesOnlyIt()
    {
        var tree = Tree(("key/1", Fake("v1")), ("key/2", Fake("v2")), ("key/3", Fake("v3")));

        var deleted = tree.Delete(Key("key/2"));

        Assert.Equal(["key/1", "key/3"], deleted.Enumerate().Select(e => e.Key.Value));
        Assert.Null(deleted.Get(Key("key/2")));
    }

    [Fact]
    [Trait("Spec", "REP-9")]
    public void Delete_AllEntries_ReturnsToTheEmptyTreeRoot()
    {
        var tree = Tree(("key/1", Fake("v1")), ("key/2", Fake("v2")));

        Assert.Equal(EmptyRoot, tree.Delete(Key("key/1")).Delete(Key("key/2")).Root);
    }

    [Fact]
    [Trait("Spec", "REP-17")]
    public void Apply_UpsertAndRemove_ReportsThePreviousValue()
    {
        var tree = Tree(("key/1", Fake("v1")));

        var added = tree.Apply(Key("key/2"), Fake("v2"), out var absent);
        var changed = added.Apply(Key("key/1"), Fake("w"), out var old);
        var removed = changed.Apply(Key("key/2"), null, out var gone);
        var unchanged = removed.Apply(Key("key/9"), null, out var none);

        Assert.Null(absent);
        Assert.Null(none);
        Assert.Equal(Fake("v1"), old);
        Assert.Equal(Fake("v2"), gone);
        Assert.Same(removed, unchanged);
        Assert.Equal(Tree(("key/1", Fake("w"))).Root, removed.Root);
    }

    [Fact]
    [Trait("Spec", "REP-17")]
    public void Apply_ValueTheKeyAlreadyHas_ReturnsTheSameTree()
    {
        var tree = GeneratedTree(10, 50);
        var (key, value) = tree.Enumerate().ElementAt(20);

        Assert.Same(tree, tree.Apply(key, value, out var previous));
        Assert.Equal(value, previous);
    }

    [Fact]
    [Trait("Spec", "REP-9")]
    public void Root_IsDeterministic_RegardlessOfInsertionOrder()
    {
        var entries = Enumerable.Range(0, 200).Select(i => KeyValuePair.Create(Key($"app.bsky.feed.post/{i:D6}"), Fake($"r{i}"))).ToList();

        var backward = Enumerable.Reverse(entries).Aggregate(MerkleSearchTree.Empty(), (t, e) => t.Insert(e.Key, e.Value));

        Assert.Equal(MerkleSearchTree.Build(entries).Root, backward.Root);
    }

    [Fact]
    [Trait("Spec", "REP-9")]
    [Trait("Spec", "REP-18")]
    public void InsertThenDelete_RestoresTheOriginalRootAndLeavesTheOriginalTree()
    {
        var tree = Tree(("key/a", Fake("va")), ("key/b", Fake("vb")), ("key/c", Fake("vc")));
        var original = tree.Root;

        var added = tree.Insert(Key("key/temp"), Fake("vtemp"));

        Assert.NotEqual(original, added.Root);
        Assert.Equal(original, added.Delete(Key("key/temp")).Root);
        Assert.Equal(original, tree.Root);
        Assert.Null(tree.Get(Key("key/temp")));
    }

    [Fact]
    [Trait("Spec", "REP-15")]
    public void WriteBlocks_ThenLoadComplete_RoundTripsALargeTree()
    {
        var tree = MerkleSearchTree.Build(Enumerable.Range(0, 1000).Select(i => KeyValuePair.Create(Key($"col/k{i:D4}"), Fake($"v{i}"))));

        var loaded = MerkleSearchTree.LoadComplete(tree.Root, Blocks(tree));

        Assert.Equal(tree.Root, loaded.Root);
        Assert.Equal(tree.Enumerate(), loaded.Enumerate());
        Assert.Equal(Fake("v999"), loaded.Get(Key("col/k0999")));
    }

    [Fact]
    [Trait("Spec", "REP-15")]
    [Trait("Spec", "REP-18")]
    public void Insert_LoadedNodeNotInCanonicalForm_WritesTheEditedNodeCanonically()
    {
        // A node written with omitted null links loads; once an edit replaces it, the tree is canonical again.
        var keys = KeysOnLayer(0, 2);
        var node = Map(("e", Array(Map(("k", Bytes(System.Text.Encoding.ASCII.GetBytes(keys[0]))), ("p", Int(0)), ("v", Link(Leaf))))));

        var edited = MerkleSearchTree.Load(CidOf(node), Store(node)).Insert(Key(keys[1]), Fake("w"));

        Assert.Equal(Tree((keys[0], Leaf), (keys[1], Fake("w"))).Root, edited.Root);
        Assert.Equal(edited.Root, MerkleSearchTree.LoadComplete(edited.Root, Blocks(edited)).Root);
    }

    [Fact]
    [Trait("Spec", "REP-18")]
    public void Insert_LargeTree_ChangesOnlyTheNodesOnItsPath()
    {
        var tree = GeneratedTree(6, 500);
        var before = Blocks(tree);

        var edited = tree.Insert(Key("com.example.record/zzzzzzzzzzzzz"), Leaf);

        // Only the nodes on the path to the new key are new; every other block is the old one.
        var after = Blocks(edited);
        var fresh = after.Blocks.Count(b => !before.TryGet(b.Cid, out _));
        Assert.InRange(fresh, 1, 8);
    }

    [Fact]
    [Trait("Spec", "REP-9")]
    [Trait("Spec", "REP-18")]
    public void RandomEdits_AlwaysMatchTheBulkBuildOfTheFinalEntries()
    {
        // Seeded so a failure reproduces. Every step also keeps the tree before it, which must not change.
        var random = new Random(20260924);
        var pool = Enumerable.Range(0, 400).Select(i => Key($"com.example.record/{i:x5}")).ToArray();

        for (var trial = 0; trial < 60; trial++)
        {
            var tree = MerkleSearchTree.Empty();
            var expected = new Dictionary<MstKey, Cid>();
            for (var step = 0; step < 150; step++)
            {
                var (key, value) = (pool[random.Next(pool.Length)], Fake($"{trial}/{step}"));
                var (before, root, entries) = (tree, tree.Root, expected.Count);
                if (!expected.ContainsKey(key)) (tree, expected[key]) = (tree.Insert(key, value), value);
                else if (random.Next(3) == 0) (tree, expected[key]) = (tree.Update(key, value), value);
                else
                {
                    tree = tree.Delete(key);
                    expected.Remove(key);
                }
                Assert.Equal(root, before.Root);
                Assert.Equal(entries, before.Enumerate().Count());
            }

            var bulk = MerkleSearchTree.Build(expected.OrderBy(_ => random.Next()));
            Assert.Equal(bulk.Root, tree.Root);
            Assert.Equal(expected.OrderBy(e => e.Key), tree.Enumerate());
            Assert.Equal(tree.Enumerate(), MerkleSearchTree.LoadComplete(tree.Root, Blocks(tree)).Enumerate());
        }
    }
}
