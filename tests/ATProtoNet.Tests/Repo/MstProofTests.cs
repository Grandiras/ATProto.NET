using ATProtoNet.Repo;
using static ATProtoNet.Tests.Repo.RepoFixtures;

namespace ATProtoNet.Tests.Repo;

// Covering proofs, partial trees loaded from them, and inverting a commit on its blocks: the
// Sync 1.1 check, where replaying the operations backwards must land on the root before the commit.
public class MstProofTests
{
    private static MerkleSearchTree Partial(MerkleSearchTree tree, MemoryBlockStore blocks) => MerkleSearchTree.Load(tree.Root, blocks);

    // Undoes each operation, checking the value it finds is the one the operation wrote.
    private static MerkleSearchTree Invert(MerkleSearchTree tree, IEnumerable<(MstKey Key, Cid? Before, Cid? After)> operations)
    {
        foreach (var (key, before, after) in operations)
        {
            tree = tree.Apply(key, before, out var found);
            Assert.Equal(after, found);
        }
        return tree;
    }

    // ── Covering proofs ──

    [Fact]
    [Trait("Spec", "REP-21")]
    public void WriteCoveringProof_NoKeys_WritesOnlyTheRoot()
    {
        var tree = GeneratedTree(4, 200);

        Assert.Equal(tree.Root, Assert.Single(Proof(tree).Blocks).Cid);
        Assert.Equal(EmptyRoot, Assert.Single(Proof(MerkleSearchTree.Empty(), "a/b").Blocks).Cid);
    }

    [Fact]
    [Trait("Spec", "REP-21")]
    public void WriteCoveringProof_Key_IsASmallerSubsetOfTheTreeThatHashesToItsCids()
    {
        var tree = GeneratedTree(4, 200);
        var all = Blocks(tree);
        var key = tree.Enumerate().ElementAt(100).Key.Value;

        var proof = Proof(tree, key);

        Assert.InRange(proof.Count, 2, all.Count - 1);
        Assert.All(proof.Blocks, block =>
        {
            Assert.True(all.TryGet(block.Cid, out var full));
            Assert.Equal(full.ToArray(), block.Data.ToArray());
            Assert.True(block.Cid.Matches(block.Data.Span));
        });
        Assert.True(proof.TryGet(tree.Root, out _));
    }

    [Fact]
    [Trait("Spec", "REP-21")]
    public void WriteCoveringProof_MoreKeys_CoverAtLeastAsMuch()
    {
        var tree = GeneratedTree(4, 200);
        var keys = tree.Enumerate().Select(e => e.Key.Value).ToList();

        var one = Proof(tree, keys[100]);
        var many = Proof(tree, keys[0], keys[100], keys[199]);

        Assert.All(one.Blocks, block => Assert.True(many.TryGet(block.Cid, out _)));
    }

    [Fact]
    [Trait("Spec", "REP-21")]
    public void WriteCoveringProof_PresentAndAbsentKeys_LetAHolderOfOnlyTheProofAnswer()
    {
        var tree = GeneratedTree(4, 200);
        var (key, value) = tree.Enumerate().ElementAt(100);
        var absent = Key(key.Value + "x");

        Assert.Equal(value, Partial(tree, Proof(tree, key.Value)).Get(key));
        Assert.Null(Partial(tree, Proof(tree, absent.Value)).Get(absent));
        Assert.Null(Partial(tree, Proof(tree, key.Value)).Get(absent));
    }

    // Covering proofs @atproto/repo 0.10.14 computes over GeneratedTree(42, 1000); all eight run in the conformance suite.
    public static TheoryData<string, string[]> ReferenceCoveringProofs => new()
    {
        {
            "app.bsky.feed.post/lprwy5qxewx4d",
            [
                "bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4", "bafyreid5soaawg2lja3k2irffrtkc4tqoa5z35hvksjvtk6qavj5ab6qry",
                "bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve", "bafyreidhormwlyipxyuuzqc6mspskhovsiybzk76rtn5fnr264hhotls3u",
                "bafyreidq6zfiwkylcx7ghqm7eiuzaqsufbhy3wmahjr2qp5tvtrruvjc7a", "bafyreigxtswdsppzccgafh3gmpgrxf7wll6xi6c45umqzyb2bhjpdb57re",
            ]
        },
        {
            // Absent, and before every key in the tree.
            "app.bsky.feed.like/2222222222222",
            [
                "bafyreia63p4zgpog5zar2fgsainugvepfozgtwz6fky5rg5dxmfb4go5p4", "bafyreiapmqhxlive6glurf3hxltg56wykn732mzx7nejrhnd27vgj2wnji",
                "bafyreiaua6eo3e2m53tyxdsudjajujwhv7mgwyhca6gsc26xwt5kt3wuu4", "bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4",
                "bafyreigmzkz73l34yr2abkaev7snxfibzhi2tny7356y7u6o5y7ginkwca", "bafyreihfhxoksnqb35ozxdhwjjwbqusacmzmqarqqltrpupnzj65onxdti",
            ]
        },
        {
            // Absent, between two keys.
            "app.bsky.feed.post/aaaaaaaaaaaaa",
            [
                "bafyreibbzbgktynyemaziojna3ekz25gfdqt7wcarpop6z3dna3of4ovfu", "bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4",
                "bafyreicztkzrp6yo4zfm53a5aaltk24vn5wbrepbxcgqp6qwmcqkwranyq", "bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve",
                "bafyreidhormwlyipxyuuzqc6mspskhovsiybzk76rtn5fnr264hhotls3u", "bafyreiens3jc6j45nkcqhbxq3jjyzkffjv7pnb4qolb6bmtnpyrt26fulu",
            ]
        },
    };

    [Theory]
    [Trait("Spec", "REP-21")]
    [MemberData(nameof(ReferenceCoveringProofs))]
    public void WriteCoveringProof_GeneratedTree_MatchesTheReferenceExactly(string key, string[] expected) =>
        Assert.Equal(expected, Proof(GeneratedTree(42, 1000), key).Blocks.Select(b => b.Cid.Value).Order(StringComparer.Ordinal));

    // ── Partial trees ──

    [Fact]
    [Trait("Spec", "REP-19")]
    public void Load_Proof_KeepsTheRootAndEditsOnlyTheProvenPath()
    {
        var tree = GeneratedTree(7, 400);
        var key = Key("app.bsky.feed.post/aaaaaaaaaaaaa");
        var partial = Partial(tree, Proof(tree, key.Value));

        Assert.Equal(tree.Root, partial.Root);
        Assert.Equal(tree.Insert(key, Leaf).Root, partial.Insert(key, Leaf).Root);
        Assert.Throws<MstIncompleteException>(() => partial.Enumerate().ToList());
        Assert.Throws<MstIncompleteException>(() => partial.WriteBlocks(new MemoryBlockStore()));
    }

    [Fact]
    [Trait("Spec", "REP-16")]
    [Trait("Spec", "REP-19")]
    public void Delete_KeyUnderAMissingSubtree_ThrowsMstIncompleteException()
    {
        var tree = GeneratedTree(8, 300);
        var victim = tree.Enumerate().First().Key;

        // A proof of another key does not carry the path to this one.
        var partial = Partial(tree, Proof(tree, tree.Enumerate().Last().Key.Value));

        Assert.Throws<MstIncompleteException>(() => partial.Delete(victim));
        Assert.Throws<MstIncompleteException>(() => partial.Get(victim));
    }

    [Fact]
    [Trait("Spec", "REP-17")]
    [Trait("Spec", "REP-19")]
    public void Apply_SameValueOnAPartialTree_ChangesNothing()
    {
        var tree = GeneratedTree(10, 50);
        var (key, value) = tree.Enumerate().First();
        var partial = Partial(tree, Proof(tree, key.Value));

        Assert.Equal(value, partial.Get(key));
        Assert.Same(partial, partial.Apply(key, value, out _));
    }

    // ── Inversion ──

    public static TheoryData<string> CommitProofFixtures() =>
        new(Vector("repo/commit-proof-fixtures.json").AsArray().Select(f => (string)f!["comment"]!));

    [Theory]
    [Trait("Spec", "REP-20")]
    [Trait("Spec", "REP-21")]
    [MemberData(nameof(CommitProofFixtures))]
    public void Invert_CommitProofFixture_LandsOnTheRootBeforeTheCommit(string comment)
    {
        // firehose/commit-proof-fixtures.json of atproto-interop-tests.
        var fixture = Vector("repo/commit-proof-fixtures.json").AsArray().Single(f => (string)f!["comment"]! == comment)!;
        string[] Keys(string name) => [.. fixture[name]!.AsArray().Select(k => (string)k!)];
        var leaf = Cid.Parse((string)fixture["leafValue"]!);
        var before = Tree([.. Keys("keys").Select(k => (k, leaf))]);
        var after = Keys("adds").Aggregate(before, (t, k) => t.Insert(Key(k), leaf));
        after = Keys("dels").Aggregate(after, (t, k) => t.Delete(Key(k)));
        var proof = Proof(after, [.. Keys("adds"), .. Keys("dels")]);

        Assert.Equal((string)fixture["rootBeforeCommit"]!, before.Root.Value);
        Assert.Equal((string)fixture["rootAfterCommit"]!, after.Root.Value);
        Assert.Equal(Keys("blocksInProof").Order(StringComparer.Ordinal), proof.Blocks.Select(b => b.Cid.Value).Order(StringComparer.Ordinal));
        var inverted = Invert(Partial(after, proof),
            [.. Keys("adds").Select(k => (Key(k), (Cid?)null, (Cid?)leaf)), .. Keys("dels").Select(k => (Key(k), (Cid?)leaf, (Cid?)null))]);
        Assert.Equal(before.Root, inverted.Root);
    }

    [Theory]
    [Trait("Spec", "REP-20")]
    [Trait("Spec", "REP-22")]
    [InlineData(1UL, 0)]
    [InlineData(2UL, 1)]
    [InlineData(3UL, 5)]
    [InlineData(4UL, 40)]
    [InlineData(5UL, 250)]
    [InlineData(6UL, 1500)]
    public void Invert_RandomCommits_LandOnTheRootBeforeTheCommitFromTheProofOrTheDiff(ulong seed, int size)
    {
        var random = new Random((int)seed);
        var keys = GeneratedKeys(seed, size + 30);
        var before = GeneratedTree(seed, size);

        for (var round = 0; round < 40; round++)
        {
            // Up to eight creates, updates and deletes of distinct keys.
            var after = before;
            List<(MstKey Key, Cid? Before, Cid? After)> operations = [];
            HashSet<MstKey> touched = [];
            var count = random.Next(1, 9);
            for (var i = 0; i < count; i++)
            {
                var kind = size == 0 ? 0 : random.Next(3);
                var key = kind == 0 ? keys[size + random.Next(30)] : keys[random.Next(size)];
                if (!touched.Add(key)) continue;
                Cid? value = kind == 2 ? null : GeneratedValue(10_000 + round * 10 + i);
                after = after.Apply(key, value, out var previous);
                operations.Add((key, previous, value));
            }

            var proof = Proof(after, [.. operations.Select(o => o.Key.Value)]);
            var diff = new MemoryBlockStore();
            after.WriteDiffBlocks(before, diff);

            Assert.Equal(before.Root, Invert(Partial(after, proof), Enumerable.Reverse(operations)).Root);
            Assert.Equal(before.Root, Invert(Partial(after, diff), operations).Root);
        }
    }

    [Fact]
    [Trait("Spec", "REP-20")]
    public void Invert_DeletingTheLastKey_LandsOnTheEmptyTree()
    {
        var tree = Tree(("com.example.record/3jqfcqzm3fo2j", Leaf));
        var partial = Partial(tree, Proof(tree, "com.example.record/3jqfcqzm3fo2j"));

        Assert.Equal(EmptyRoot, Invert(partial, [(Key("com.example.record/3jqfcqzm3fo2j"), null, Leaf)]).Root);
    }

    // ── Diff blocks ──

    [Fact]
    [Trait("Spec", "REP-22")]
    public void WriteDiffBlocks_Commit_HoldsEveryNewNodeAndOnlyTreeNodes()
    {
        var before = MerkleSearchTree.Build(Enumerable.Range(0, 500).Select(i => KeyValuePair.Create(Key($"app.bsky.feed.post/3l6oveex{i:D5}"), Fake($"{i}"))));
        var after = before.Insert(Key("app.bsky.feed.post/3l6oveex99999"), Leaf).Delete(Key("app.bsky.feed.post/3l6oveex00250"))
            .Update(Key("app.bsky.feed.post/3l6oveex00100"), Leaf);
        var old = Blocks(before);
        var all = Blocks(after);

        var diff = new MemoryBlockStore();
        after.WriteDiffBlocks(before, diff);

        Assert.All(all.Blocks.Where(b => !old.TryGet(b.Cid, out _)), block => Assert.True(diff.TryGet(block.Cid, out _)));
        Assert.All(diff.Blocks, block => Assert.True(all.TryGet(block.Cid, out _)));
        Assert.InRange(diff.Count, 1, all.Count / 4);
    }

    [Fact]
    [Trait("Spec", "REP-22")]
    public void WriteDiffBlocks_NoChange_WritesOnlyTheRoot()
    {
        var tree = GeneratedTree(5, 100);
        var diff = new MemoryBlockStore();

        tree.WriteDiffBlocks(tree, diff);

        Assert.Equal(tree.Root, Assert.Single(diff.Blocks).Cid);
    }
}
