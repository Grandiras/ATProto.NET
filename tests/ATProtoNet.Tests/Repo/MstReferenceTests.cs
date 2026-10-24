using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Repo;
using static ATProtoNet.Tests.Repo.RepoFixtures;

namespace ATProtoNet.Tests.Repo;

// Pins the tree to the reference implementation: packages/repo/tests/mst.test.ts of
// bluesky-social/atproto, and trees @atproto/repo 0.10.14 built from GeneratedKeys. The full
// vector files run in the conformance suite; these are the named scenarios.
public class MstReferenceTests
{
    private static MstKey Record(string rkey) => Key("com.example.record/" + rkey);

    private static MerkleSearchTree With(params string[] rkeys) =>
        rkeys.Aggregate(MerkleSearchTree.Empty(), (tree, rkey) => tree.Insert(Record(rkey), Leaf));

    [Theory]
    [Trait("Spec", "REP-9")]
    [InlineData("3jqfcqzm3fo2j", "bafyreibj4lsc3aqnrvphp5xmrnfoorvru4wynt6lwidqbm2623a6tatzdu")] // trivial
    [InlineData("3jqfcqzm3fx2j", "bafyreih7wfei65pxzhauoibu3ls7jgmkju4bspy4t2ha2qdjnzqvoy33ai")] // singlelayer2
    public void Root_SingleKey_MatchesReference(string rkey, string expected) =>
        Assert.Equal(expected, With(rkey).Root.Value);

    [Fact]
    [Trait("Spec", "REP-9")]
    public void Root_SimpleTree_MatchesReference() =>
        Assert.Equal("bafyreicmahysq4n6wfuxo522m6dpiy7z7qzym3dzs756t5n7nfdgccwq7m",
            With("3jqfcqzm3fp2j", "3jqfcqzm3fr2j", "3jqfcqzm3fs2j", "3jqfcqzm3ft2j", "3jqfcqzm4fc2j").Root.Value);

    [Fact]
    [Trait("Spec", "REP-8")]
    public void Delete_OnlyKeyOfTheTopLayer_TrimsTheTree()
    {
        // 3jqfcqzm3fs2j is the only layer-1 key.
        var tree = With("3jqfcqzm3fn2j", "3jqfcqzm3fo2j", "3jqfcqzm3fp2j", "3jqfcqzm3fs2j", "3jqfcqzm3ft2j", "3jqfcqzm3fu2j");
        Assert.Equal("bafyreifnqrwbk6ffmyaz5qtujqrzf5qmxf7cbxvgzktl4e3gabuxbtatv4", tree.Root.Value);

        Assert.Equal("bafyreie4kjuxbwkhzg2i5dljaswcroeih4dgiqq6pazcmunwt2byd725vi", tree.Delete(Record("3jqfcqzm3fs2j")).Root.Value);
    }

    [Fact]
    [Trait("Spec", "REP-8")]
    public void Insert_KeyThatSplitsTwoLayersDown_MatchesReferenceBothWays()
    {
        const string l1Root = "bafyreiettyludka6fpgp33stwxfuwhkzlur6chs4d2v4nkmq2j3ogpdjem";
        var tree = With("3jqfcqzm3fo2j", "3jqfcqzm3fp2j", "3jqfcqzm3fr2j", "3jqfcqzm3fs2j", "3jqfcqzm3ft2j", "3jqfcqzm3fz2j",
            "3jqfcqzm4fc2j", "3jqfcqzm4fd2j", "3jqfcqzm4ff2j", "3jqfcqzm4fg2j", "3jqfcqzm4fh2j");
        Assert.Equal(l1Root, tree.Root.Value);

        // 3jqfcqzm3fx2j sits on layer 2 and pushes 3jqfcqzm3ft2j out of the node it shared with its neighbours.
        var split = tree.Insert(Record("3jqfcqzm3fx2j"), Leaf);

        Assert.Equal("bafyreid2x5eqs4w4qxvc5jiwda4cien3gw2q6cshofxwnvv7iucrmfohpm", split.Root.Value);
        Assert.Equal(l1Root, split.Delete(Record("3jqfcqzm3fx2j")).Root.Value);
    }

    [Fact]
    [Trait("Spec", "REP-8")]
    public void Insert_KeyTwoLayersAboveTheRest_MatchesReferenceBothWays()
    {
        const string l0Root = "bafyreidfcktqnfmykz2ps3dbul35pepleq7kvv526g47xahuz3rqtptmky";
        const string l2Root = "bafyreiavxaxdz7o7rbvr3zg2liox2yww46t7g6hkehx4i4h3lwudly7dhy";
        var tree = With("3jqfcqzm3ft2j", "3jqfcqzm3fz2j");
        Assert.Equal(l0Root, tree.Root.Value);

        var high = tree.Insert(Record("3jqfcqzm3fx2j"), Leaf); // layer 2
        Assert.Equal(l2Root, high.Root.Value);
        Assert.Equal(l0Root, high.Delete(Record("3jqfcqzm3fx2j")).Root.Value);

        var both = high.Insert(Record("3jqfcqzm4fd2j"), Leaf); // layer 1
        Assert.Equal("bafyreig4jv3vuajbsybhyvb7gggvpwh2zszwfyttjrj6qwvcsp24h6popu", both.Root.Value);
        Assert.Equal(l2Root, both.Delete(Record("3jqfcqzm4fd2j")).Root.Value);
    }

    [Theory]
    [Trait("Spec", "REP-6")]
    [Trait("Spec", "REP-8")]
    [Trait("Spec", "REP-9")]
    [InlineData(1UL, 1, "app.bsky.feed.post/w52uz3lpjvpsa", "bafyreihdm2xrznzcjbnrggjrad6lape2ykltcj5oz6s3q4rlw557lwjzfu", 1, "204eeab3fc950b4896949b60093267370cf1f4e0f57236d787d96bc9e45b0ddf")]
    [InlineData(5UL, 50, "app.bsky.feed.post/kdz77v7g2ubvg", "bafyreifu6trvtqrvnkfa244narjaddympypuxwnydbjjqkj5zqru2dqhhe", 17, "b1ee22b3cf6f93c7b7c774e27c92a5171c2dc0c90933f38ec95faa52a795ab2e")]
    [InlineData(8UL, 1000, "app.bsky.feed.post/rpya3i7xwc7hh", "bafyreib2yr6vw4h2u7n6jfw3fsoikwdp7ntgwfombrzgh4ops72nhgs7wm", 236, "1a6915f640eeecaa0226cf23055715c62bc8237b93b91118a6cde0cdcbefb2d2")]
    public void WriteBlocks_GeneratedTree_MatchesReferenceRootAndBlocks(ulong seed, int count, string firstKey, string root, int blockCount, string digest)
    {
        Assert.Equal(firstKey, GeneratedKeys(seed, count)[0].Value);

        var tree = GeneratedTree(seed, count);
        var blocks = Blocks(tree);

        Assert.Equal(root, tree.Root.Value);
        Assert.Equal(blockCount, blocks.Count);
        Assert.Equal(digest, Digest(blocks));
    }

    [Fact]
    [Trait("Spec", "REP-9")]
    public void Delete_EveryThirdKeyOfAGeneratedTree_MatchesReference()
    {
        var keys = GeneratedKeys(11, 500);
        var tree = GeneratedTree(11, 500);
        for (var i = 0; i < keys.Count; i += 3) tree = tree.Delete(keys[i]);

        var blocks = Blocks(tree);

        Assert.Equal("bafyreifkb6ayacuo2w7riqqhcafmftvstqmzret5eql3uftjktenwgjpki", tree.Root.Value);
        Assert.Equal(93, blocks.Count);
        Assert.Equal("3d069cd538ac9947cb3c8ea7619cf9ab29bda0ffcde1404ea1c1483697f9465b", Digest(blocks));
    }

    // SHA-256 over the sorted block CIDs, newline-joined: pins the whole block set.
    private static string Digest(MemoryBlockStore blocks) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', blocks.Blocks.Select(b => b.Cid.Value).Order(StringComparer.Ordinal)))));
}
