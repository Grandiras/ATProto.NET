using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

/// <summary>
/// Pins <see cref="MerkleSearchTree"/> to outputs of the reference implementation, so that a repo
/// the SDK writes hashes to the same CIDs every other implementation computes.
/// </summary>
/// <remarks>
/// Sources: <c>packages/repo/tests/mst.test.ts</c> in bluesky-social/atproto, the MST and firehose
/// fixtures of bluesky-social/atproto-interop-tests (CC0), and trees built with the published
/// <c>@atproto/repo</c> 0.10.14 from the deterministic key generator below.
/// </remarks>
public sealed class MstReferenceVectorTests
{
    /// <summary>The leaf value every reference vector maps its keys to.</summary>
    internal static readonly byte[] Leaf =
        CidComputation.DecodeCidString("bafyreie5cvv4h45feadgeuwhbcutmh6t2ceseocckahdoe6uat64zmz454");

    private static string Root(MerkleSearchTree tree) => CidComputation.EncodeCidToString(tree.ComputeRootCid());

    private static string Record(string rkey) => "com.example.record/" + rkey;

    // ── mst.test.ts: known maps ──────────────────────────────

    [Fact]
    public void ComputeRootCid_EmptyTree_MatchesReference()
    {
        Assert.Equal("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm", Root(MerkleSearchTree.Create()));
    }

    [Fact]
    public void Serialize_EmptyTree_IsTheReferenceEncodedNode()
    {
        // {"e": [], "l": null}: both fields present, the absent left link written as null.
        var (root, blocks) = MerkleSearchTree.Create().Serialize();

        var block = Assert.Single(blocks);
        Assert.Equal("a2616580616cf6", Convert.ToHexStringLower(block.Value));
        Assert.Equal(CidComputation.EncodeCidToString(root), block.Key);
    }

    [Theory]
    [InlineData("3jqfcqzm3fo2j", "bafyreibj4lsc3aqnrvphp5xmrnfoorvru4wynt6lwidqbm2623a6tatzdu")] // trivial
    [InlineData("3jqfcqzm3fx2j", "bafyreih7wfei65pxzhauoibu3ls7jgmkju4bspy4t2ha2qdjnzqvoy33ai")] // singlelayer2
    public void ComputeRootCid_SingleKey_MatchesReference(string rkey, string expected)
    {
        var tree = MerkleSearchTree.Create();
        tree.Add(Record(rkey), Leaf);

        Assert.Equal(expected, Root(tree));
    }

    [Fact]
    public void ComputeRootCid_SimpleTree_MatchesReference()
    {
        var tree = MerkleSearchTree.Create();
        tree.Add(Record("3jqfcqzm3fp2j"), Leaf); // level 0
        tree.Add(Record("3jqfcqzm3fr2j"), Leaf); // level 0
        tree.Add(Record("3jqfcqzm3fs2j"), Leaf); // level 1
        tree.Add(Record("3jqfcqzm3ft2j"), Leaf); // level 0
        tree.Add(Record("3jqfcqzm4fc2j"), Leaf); // level 0

        Assert.Equal(5, tree.Count);
        Assert.Equal("bafyreicmahysq4n6wfuxo522m6dpiy7z7qzym3dzs756t5n7nfdgccwq7m", Root(tree));
    }

    // ── mst.test.ts: edge cases ──────────────────────────────

    [Fact]
    public void Delete_OnlyKeyOfTheTopLayer_TrimsTheTree()
    {
        const string l1Root = "bafyreifnqrwbk6ffmyaz5qtujqrzf5qmxf7cbxvgzktl4e3gabuxbtatv4";
        const string l0Root = "bafyreie4kjuxbwkhzg2i5dljaswcroeih4dgiqq6pazcmunwt2byd725vi";

        var tree = MerkleSearchTree.Create();
        tree.Add(Record("3jqfcqzm3fn2j"), Leaf); // level 0
        tree.Add(Record("3jqfcqzm3fo2j"), Leaf); // level 0
        tree.Add(Record("3jqfcqzm3fp2j"), Leaf); // level 0
        tree.Add(Record("3jqfcqzm3fs2j"), Leaf); // level 1
        tree.Add(Record("3jqfcqzm3ft2j"), Leaf); // level 0
        tree.Add(Record("3jqfcqzm3fu2j"), Leaf); // level 0
        Assert.Equal(l1Root, Root(tree));

        tree.Delete(Record("3jqfcqzm3fs2j"));
        Assert.Equal(5, tree.Count);
        Assert.Equal(l0Root, Root(tree));
    }

    [Fact]
    public void Add_KeyThatSplitsTwoLayersDown_MatchesReferenceBothWays()
    {
        const string l1Root = "bafyreiettyludka6fpgp33stwxfuwhkzlur6chs4d2v4nkmq2j3ogpdjem";
        const string l2Root = "bafyreid2x5eqs4w4qxvc5jiwda4cien3gw2q6cshofxwnvv7iucrmfohpm";

        var tree = MerkleSearchTree.Create();
        tree.Add(Record("3jqfcqzm3fo2j"), Leaf); // A; level 0
        tree.Add(Record("3jqfcqzm3fp2j"), Leaf); // B; level 0
        tree.Add(Record("3jqfcqzm3fr2j"), Leaf); // C; level 0
        tree.Add(Record("3jqfcqzm3fs2j"), Leaf); // D; level 1
        tree.Add(Record("3jqfcqzm3ft2j"), Leaf); // E; level 0
        tree.Add(Record("3jqfcqzm3fz2j"), Leaf); // G; level 0
        tree.Add(Record("3jqfcqzm4fc2j"), Leaf); // H; level 0
        tree.Add(Record("3jqfcqzm4fd2j"), Leaf); // I; level 1
        tree.Add(Record("3jqfcqzm4ff2j"), Leaf); // J; level 0
        tree.Add(Record("3jqfcqzm4fg2j"), Leaf); // K; level 0
        tree.Add(Record("3jqfcqzm4fh2j"), Leaf); // L; level 0
        Assert.Equal(11, tree.Count);
        Assert.Equal(l1Root, Root(tree));

        // F sits at level 2 and pushes E out of the node it shared with G and H.
        tree.Add(Record("3jqfcqzm3fx2j"), Leaf);
        Assert.Equal(12, tree.Count);
        Assert.Equal(l2Root, Root(tree));

        tree.Delete(Record("3jqfcqzm3fx2j"));
        Assert.Equal(11, tree.Count);
        Assert.Equal(l1Root, Root(tree));
    }

    [Fact]
    public void Add_KeyTwoLayersAboveTheRest_MatchesReferenceBothWays()
    {
        const string l0Root = "bafyreidfcktqnfmykz2ps3dbul35pepleq7kvv526g47xahuz3rqtptmky";
        const string l2Root = "bafyreiavxaxdz7o7rbvr3zg2liox2yww46t7g6hkehx4i4h3lwudly7dhy";
        const string l2Root2 = "bafyreig4jv3vuajbsybhyvb7gggvpwh2zszwfyttjrj6qwvcsp24h6popu";

        var tree = MerkleSearchTree.Create();
        tree.Add(Record("3jqfcqzm3ft2j"), Leaf); // A; level 0
        tree.Add(Record("3jqfcqzm3fz2j"), Leaf); // C; level 0
        Assert.Equal(l0Root, Root(tree));

        tree.Add(Record("3jqfcqzm3fx2j"), Leaf); // B; level 2
        Assert.Equal(l2Root, Root(tree));

        tree.Delete(Record("3jqfcqzm3fx2j"));
        Assert.Equal(l0Root, Root(tree));

        tree.Add(Record("3jqfcqzm3fx2j"), Leaf); // B; level 2
        tree.Add(Record("3jqfcqzm4fd2j"), Leaf); // D; level 1
        Assert.Equal(4, tree.Count);
        Assert.Equal(l2Root2, Root(tree));

        tree.Delete(Record("3jqfcqzm4fd2j"));
        Assert.Equal(3, tree.Count);
        Assert.Equal(l2Root, Root(tree));
    }

    // ── mst.test.ts: allowable keys ──────────────────────────

    public static TheoryData<string> RejectedKeys => new()
    {
        "",
        "asdf",
        "nested/collection/asdf",
        "coll/",
        "/rkey",
        "coll/jalapeñoA",
        "coll/coöperative",
        "coll/abc💩",
        "coll/key$",
        "coll/key%",
        "coll/key(",
        "coll/key)",
        "coll/key+",
        "coll/key=",
        "coll/@handle",
        "coll/any space",
        "coll/#extra",
        "coll/any+space",
        "coll/number[3]",
        "coll/number(3)",
        "coll/dHJ1ZQ==",
        "coll/\"quote\"",
        "coll/" + new string('a', 1020), // 1025 characters
    };

    [Theory]
    [MemberData(nameof(RejectedKeys))]
    public void Add_KeyTheReferenceRejects_Throws(string key)
    {
        var tree = MerkleSearchTree.Create();

        Assert.Throws<ArgumentException>(() => tree.Add(key, Leaf));
        Assert.Equal(0, tree.Count);
    }

    [Theory]
    [InlineData("coll/3jui7kd54zh2y")]
    [InlineData("coll/self")]
    [InlineData("coll/example.com")]
    [InlineData("com.example/rkey")]
    [InlineData("coll/~1.2-3_")]
    [InlineData("coll/dHJ1ZQ")]
    [InlineData("coll/pre:fix")]
    [InlineData("coll/_")]
    public void Add_KeyTheReferenceAllows_Succeeds(string key)
    {
        var tree = MerkleSearchTree.Create();

        tree.Add(key, Leaf);

        Assert.Equal(Leaf, tree.Get(key));
    }

    [Fact]
    public void Add_KeyOfExactly1024Characters_Succeeds()
    {
        var key = "coll/" + new string('a', 1019);
        Assert.Equal(1024, key.Length);

        var tree = MerkleSearchTree.Create();
        tree.Add(key, Leaf);

        Assert.Equal(1, tree.Count);
    }

    // ── atproto-interop-tests: mst/common_prefix.json ────────

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "abc", 3)]
    [InlineData("", "abc", 0)]
    [InlineData("abc", "", 0)]
    [InlineData("ab", "abc", 2)]
    [InlineData("abc", "ab", 2)]
    [InlineData("abcde", "abc", 3)]
    [InlineData("abc", "abcde", 3)]
    [InlineData("abcde", "abc1", 3)]
    [InlineData("abcde", "abb", 2)]
    [InlineData("abcde", "qbb", 0)]
    [InlineData("abc", "abc\u0000", 3)]
    [InlineData("abc\u0000", "abc", 3)]
    public void SharedPrefixLength_InteropVectors(string left, string right, int expected)
    {
        Assert.Equal(expected, MerkleSearchTree.SharedPrefixLength(left, right));
    }

    // ── atproto-interop-tests: firehose/commit-proof-fixtures.json ──

    internal sealed record CommitProofFixture(
        string Comment,
        string[] Keys,
        string[] Adds,
        string[] Dels,
        string RootBefore,
        string RootAfter,
        string[] BlocksInProof);

    internal static readonly CommitProofFixture[] CommitProofFixtures =
    [
        new(
            "two deep split",
            Keys: ["A0/374913", "B1/986427", "C0/451630", "E0/670489", "F1/085263", "G0/765327"],
            Adds: ["D2/269196"],
            Dels: [],
            RootBefore: "bafyreicraprx2xwnico4tuqir3ozsxpz46qkcpox3obf5bagicqwurghpy",
            RootAfter: "bafyreihvay6pazw3dfa47u5d2tn3rd6pa57sr37bo5bqyvjuqc73ib65my",
            BlocksInProof:
            [
                "bafyreieazvzmba35p4phksumwfoklwe5o4ncmo7otud74idcyv4orrbzxi",
                "bafyreie4227qpa4vbtbpnsvuhp322b776vjuhxsidi5hxp2gawumr4m3de",
                "bafyreid44jgimksqqdratyste2moqu6zo4h6co2pknjppfoiplsqxtuxae",
                "bafyreiaerlvitye7fjjwodkshtbqqdsmfsdjtnlz4vs6y4trnddshsmd5a",
                "bafyreihvay6pazw3dfa47u5d2tn3rd6pa57sr37bo5bqyvjuqc73ib65my",
            ]),
        new(
            "two deep leafless split",
            Keys: ["A0/374913", "B0/601692", "D0/952776", "E0/670489"],
            Adds: ["C2/014073"],
            Dels: [],
            RootBefore: "bafyreialm5sgf7pijawbschsjpdevid5rss5ip3d4n4w6cc4mhu53sfl4i",
            RootAfter: "bafyreibxh4iztp5l2yshz3ectg2qjpeyprpw2gogao3pvceowpq3k3thya",
            BlocksInProof:
            [
                "bafyreih7dxytqtcjv3cfia3fi3wxofeip62teqkpynnkxisxqwfchfb4bu",
                "bafyreiaqbymlnvpklmogx75gozjl3y73gva43jbgwcrqu2pp5g5ejou5vm",
                "bafyreicfh3st5ghtnoqyyvznjv4lhfnvl7qsndempx35i4tcmoxakqbgrm",
                "bafyreieyjrrai6igjceyxzkajrxgxz37da2eufb33anvesb4ev6yzztauu",
                "bafyreibxh4iztp5l2yshz3ectg2qjpeyprpw2gogao3pvceowpq3k3thya",
            ]),
        new(
            "add on edge with neighbor two layers down",
            Keys: ["A0/374913", "B2/827649", "C0/451630"],
            Adds: ["D2/269196"],
            Dels: [],
            RootBefore: "bafyreigc6ay2qwfk7kuevvrczummpd64nknfo4yxpaooknfymzyb7u3ntq",
            RootAfter: "bafyreign6kxoll35r5f2ske6hjx7vg56aw3jn6r5hcopgrepzafpvohr2a",
            BlocksInProof:
            [
                "bafyreieazvzmba35p4phksumwfoklwe5o4ncmo7otud74idcyv4orrbzxi",
                "bafyreidicvcjgrpm5bmhm3ndh2ysqfhgzk4chwn3m4kuvwkenfusspb4uy",
                "bafyreign6kxoll35r5f2ske6hjx7vg56aw3jn6r5hcopgrepzafpvohr2a",
            ]),
        new(
            "merge and split in multi-op commit",
            Keys: ["A0/374913", "B2/827649", "D2/269196", "E0/670489"],
            Adds: ["C2/014073"],
            Dels: ["B2/827649", "D2/269196"],
            RootBefore: "bafyreiceld4icym4qjmdcn3dfgtxt7t66hdgyhvigessgmkvb56dx6amgi",
            RootAfter: "bafyreigkalika3taqauapfha556lo36zzcjoiifny5xeru6yis3nxw5ruq",
            BlocksInProof:
            [
                "bafyreid44jgimksqqdratyste2moqu6zo4h6co2pknjppfoiplsqxtuxae",
                "bafyreihytu6onh476trave25zuo63ziebkeong2755sc5nmf55uzdawgt4",
                "bafyreigkalika3taqauapfha556lo36zzcjoiifny5xeru6yis3nxw5ruq",
                "bafyreidnnkrdkcaswbflgtdsxm7nzs7p5f2rdous6wrlupzstuwqu5pfgm",
                "bafyreia2kq243hqq3volwlzkbzzphoeqauk54sc5h7vgogq4ei5fjizxvy",
            ]),
        new(
            "complex multi-op commit",
            Keys: ["B0/601692", "C2/014073", "D0/952776", "E2/819540", "F0/697858", "H0/131238"],
            Adds: ["A2/827942", "G2/611528"],
            Dels: ["C2/014073"],
            RootBefore: "bafyreigr3plnts7dax6yokvinbhcqpyicdfgg6npvvyx6okc5jo55slfqi",
            RootAfter: "bafyreiftrcrbhrwmi37u4egedlg56gk3jeh3tvmqvwgowoifuklfysyx54",
            BlocksInProof:
            [
                "bafyreih62n3gjbzzvlicuggpfydyzrp3ssyx7hdgtltd3sct3ribm3u73e",
                "bafyreihrjhuoynjvgteuefin5vwnqmupyfzvmytdobpstqt3mbawgw5qhm",
                "bafyreibevzst4gzkxo263syohlmq3lpxdvpjhlpyqx2ay3moh43lifydca",
                "bafyreifsdd7dv2neal7zjhyrsvndkaocelqlpgfxwo4utoq2g77klih37e",
                "bafyreid2wwyroodj2lxx2obikac74q77lsn6vqkoetlqqwwnr3criwlcvy",
                "bafyreie55b224oljhykpsxdjq4ajn2ysksud7qm347s6kn2ei6a775faum",
                "bafyreiftrcrbhrwmi37u4egedlg56gk3jeh3tvmqvwgowoifuklfysyx54",
            ]),
        new(
            "split with earlier leaves on same layer",
            Keys:
            [
                "app.bsky.feed.post/3lo3kqqljmfe2", "app.bsky.feed.post/3log4547dm6h2",
                "app.bsky.feed.post/3log45inogon2", "app.bsky.feed.post/3logaodrh74d2",
                "app.bsky.feed.post/3logteazog2n2", "app.bsky.feed.post/3lon5cqsbwrj2",
                "app.bsky.feed.repost/3l6sjhvqonco2",
            ],
            Adds: ["app.bsky.feed.post/3lon5dzeaihj2"],
            Dels: [],
            RootBefore: "bafyreigfcsro2up7qi7l3rxdpg7n6gjtteotkmgrrqztl5oy2tf4ncl4ji",
            RootAfter: "bafyreig33hsjiplaixvmccy65n7rn3in5nsbtcittzx6k3w5wjfhk2sg3a",
            BlocksInProof:
            [
                "bafyreig33hsjiplaixvmccy65n7rn3in5nsbtcittzx6k3w5wjfhk2sg3a",
                "bafyreih2rhjm3apcghihwfojv2em7noqkgt5qyjcnxux7do674m464oc3m",
                "bafyreiajhswkduap4zvqvfhth3skdgckmk2eb5gow7vv3gvj45f4fqwmxm",
            ]),
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void CommitProofFixture_RootsAndProofMatchReference(int index)
    {
        var fixture = CommitProofFixtures[index];

        var tree = MerkleSearchTree.Create();
        foreach (var key in fixture.Keys)
            tree.Add(key, Leaf);
        Assert.Equal(fixture.RootBefore, Root(tree));

        foreach (var key in fixture.Adds)
            tree.Add(key, Leaf);
        foreach (var key in fixture.Dels)
            tree.Delete(key);
        Assert.Equal(fixture.RootAfter, Root(tree));

        // A relay replays the commit backwards against the blocks it carries, so every block the
        // reference puts in the proof has to be there.
        var (proofRoot, proof) = tree.SerializeProof([.. fixture.Adds, .. fixture.Dels]);
        Assert.Equal(fixture.RootAfter, CidComputation.EncodeCidToString(proofRoot));
        foreach (var cid in fixture.BlocksInProof)
            Assert.True(proof.ContainsKey(cid), $"{fixture.Comment}: proof is missing {cid}");
    }

    // ── Trees built by @atproto/repo 0.10.14 ─────────────────

    /// <summary>
    /// The deterministic key set the reference trees were built from: a 64-bit LCG whose state
    /// is spelled out as a 13-character rkey, cycling through four collections.
    /// </summary>
    internal static List<string> GeneratedKeys(ulong seed, int count)
    {
        const string alphabet = "234567abcdefghijklmnopqrstuvwxyz";
        string[] collections = ["app.bsky.feed.post", "app.bsky.feed.like", "app.bsky.graph.follow", "com.example.record"];

        var keys = new List<string>(count);
        var state = seed;
        Span<char> rkey = stackalloc char[13];
        for (var i = 0; i < count; i++)
        {
            state = unchecked(state * 6364136223846793005UL + 1442695040888963407UL);
            var bits = state;
            for (var j = 0; j < rkey.Length; j++)
            {
                rkey[j] = alphabet[(int)(bits & 31)];
                bits >>= 5;
            }

            keys.Add($"{collections[i % collections.Length]}/{new string(rkey)}");
        }

        return keys;
    }

    /// <summary>The value of the <paramref name="index"/>th generated key: the CID of <c>{"i": index}</c>.</summary>
    internal static byte[] GeneratedValue(int index)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(1);
        writer.WriteTextString("i");
        writer.WriteInt32(index);
        writer.WriteEndMap();
        return CidComputation.ComputeBinaryForDagCbor(writer.Encode());
    }

    private static MerkleSearchTree GeneratedTree(ulong seed, int count)
    {
        var keys = GeneratedKeys(seed, count);
        return MerkleSearchTree.Create(keys.Select((key, i) => KeyValuePair.Create(key, GeneratedValue(i))));
    }

    /// <summary>SHA-256 over the sorted block CIDs, newline-joined: pins the whole block set.</summary>
    private static string BlockDigest(IEnumerable<string> cids)
    {
        var sorted = cids.Order(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', sorted))));
    }

    [Theory]
    [InlineData(1UL, 1, "app.bsky.feed.post/w52uz3lpjvpsa", "bafyreihdm2xrznzcjbnrggjrad6lape2ykltcj5oz6s3q4rlw557lwjzfu", 1, "204eeab3fc950b4896949b60093267370cf1f4e0f57236d787d96bc9e45b0ddf")]
    [InlineData(2UL, 2, "app.bsky.feed.post/dxz6dcav5fedg", "bafyreibps7x4h2t7yub3djo5womkud5uqbxlfizh22w3vu75ykv3jpvd7i", 2, "c715132672b362476c73cc7c37af4966511540de5be601faac3d4e1789252c45")]
    [InlineData(3UL, 3, "app.bsky.feed.post/qqzjmiv2suyt3", "bafyreiconk2rglg53ww724d5gfqy5wh5cx7sz3qkfabx4b3iz6oycqnbuq", 3, "325625aabe1d07e8e787e4485ec00377b4fb354d6bf4d52f33b1725976db0a25")]
    [InlineData(4UL, 10, "app.bsky.feed.post/5kzuvokageneb", "bafyreigiitb2qpmdfs7qj6pr7iuttzm5b2rn7asq7yx3ktrqqb365houhy", 3, "ff214f5094f626407d9d6196a533d3b911f5ad5ac5e00f74d5209a0e408e1da5")]
    [InlineData(5UL, 50, "app.bsky.feed.post/kdz77v7g2ubvg", "bafyreifu6trvtqrvnkfa244narjaddympypuxwnydbjjqkj5zqru2dqhhe", 17, "b1ee22b3cf6f93c7b7c774e27c92a5171c2dc0c90933f38ec95faa52a795ab2e")]
    [InlineData(6UL, 100, "app.bsky.feed.post/x4zki3vlodwf4", "bafyreiasvty3l2ibqadzu7aoitvbd7mnswj4lpw6lekmxglqz6csdd5rq4", 28, "9f6070906691bbe19c749c7ac9b460f0efcf441b05e9f7115df2934f88fe0794")]
    [InlineData(7UL, 257, "app.bsky.feed.post/ewyvrbkrctkwb", "bafyreigda7an7yqdtjue3ur77qfque6bpboubrjpakomldnma5qahmv2ri", 69, "5bfa8e53894c89b5bc4d5f5e67df37e93c2dae1d78314c7dac0fa685c6fa5c72")]
    [InlineData(8UL, 1000, "app.bsky.feed.post/rpya3i7xwc7hh", "bafyreib2yr6vw4h2u7n6jfw3fsoikwdp7ntgwfombrzgh4ops72nhgs7wm", 236, "1a6915f640eeecaa0226cf23055715c62bc8237b93b91118a6cde0cdcbefb2d2")]
    [InlineData(9UL, 5000, "app.bsky.feed.post/6jyleou4lstx4", "bafyreiacips47nn7z6mjgep7fnio2llvsdkfjdl4lc5ky4nb7ngch4gtka", 1356, "6e9fb1a9591b51e5a7fc2cc127a56dddc46f6169112e4414403dcb50022574e5")]
    public void Serialize_GeneratedTree_MatchesReferenceRootAndBlocks(
        ulong seed, int count, string firstKey, string root, int blockCount, string blockDigest)
    {
        Assert.Equal(firstKey, GeneratedKeys(seed, count)[0]);

        var (rootCid, blocks) = GeneratedTree(seed, count).Serialize();

        Assert.Equal(root, CidComputation.EncodeCidToString(rootCid));
        Assert.Equal(blockCount, blocks.Count);
        Assert.Equal(blockDigest, BlockDigest(blocks.Keys));
    }

    [Fact]
    public void Delete_EveryThirdKeyOfGeneratedTree_MatchesReference()
    {
        var keys = GeneratedKeys(11, 500);
        var tree = MerkleSearchTree.Create(keys.Select((key, i) => KeyValuePair.Create(key, GeneratedValue(i))));

        for (var i = 0; i < keys.Count; i += 3)
            tree.Delete(keys[i]);

        var (root, blocks) = tree.Serialize();
        Assert.Equal("bafyreifkb6ayacuo2w7riqqhcafmftvstqmzret5eql3uftjktenwgjpki", CidComputation.EncodeCidToString(root));
        Assert.Equal(93, blocks.Count);
        Assert.Equal("3d069cd538ac9947cb3c8ea7619cf9ab29bda0ffcde1404ea1c1483697f9465b", BlockDigest(blocks.Keys));
    }

    public static TheoryData<string, string[]> ReferenceCoveringProofs => new()
    {
        {
            "app.bsky.feed.post/lprwy5qxewx4d",
            [
                "bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4",
                "bafyreid5soaawg2lja3k2irffrtkc4tqoa5z35hvksjvtk6qavj5ab6qry",
                "bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve",
                "bafyreidhormwlyipxyuuzqc6mspskhovsiybzk76rtn5fnr264hhotls3u",
                "bafyreidq6zfiwkylcx7ghqm7eiuzaqsufbhy3wmahjr2qp5tvtrruvjc7a",
                "bafyreigxtswdsppzccgafh3gmpgrxf7wll6xi6c45umqzyb2bhjpdb57re",
            ]
        },
        {
            "app.bsky.feed.like/gfxt65roszhn5",
            [
                "bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4",
                "bafyreibhkc2dujycj3gwcxsoar2f7xc65mbzu2jov2ypdfuzdihn4ripdm",
                "bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve",
                "bafyreidg3hzwoptbgdauraeg5znwxjk4pqd4fzgdc34rtvsou262logdla",
                "bafyreidhormwlyipxyuuzqc6mspskhovsiybzk76rtn5fnr264hhotls3u",
                "bafyreifospfpqcuyu4a5jghocazxiqgpa6z6xwuzg6qrqvjuqpej3xgswq",
            ]
        },
        {
            "app.bsky.graph.follow/54dl4gzowy4si",
            [
                "bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4",
                "bafyreibqidsfaigyu672fl4k7fs5saibm5yjprwbfu4z5fuonp4vv5545q",
                "bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve",
                "bafyreidhormwlyipxyuuzqc6mspskhovsiybzk76rtn5fnr264hhotls3u",
                "bafyreieb2hfkas23uk57rmoe4ie6bllhkqig4ctcchebiwfdehwcx26nnq",
                "bafyreigxtswdsppzccgafh3gmpgrxf7wll6xi6c45umqzyb2bhjpdb57re",
            ]
        },
        {
            "app.bsky.feed.post/7jafnfb7olckg",
            [
                "bafyreibbzbgktynyemaziojna3ekz25gfdqt7wcarpop6z3dna3of4ovfu",
                "bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4",
                "bafyreiblprfhmvaayucklhnfjv5mqtqcpinn4rsf2u2zek4vnd42z34svi",
                "bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve",
                "bafyreidhormwlyipxyuuzqc6mspskhovsiybzk76rtn5fnr264hhotls3u",
                "bafyreihwv44qwy3fb43vdyzd5xvleni4psohh6fhisyydnbuyzopgfrwze",
            ]
        },
        {
            "com.example.record/m5mhcxbgsqxc5",
            [
                "bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4",
                "bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve",
                "bafyreidhormwlyipxyuuzqc6mspskhovsiybzk76rtn5fnr264hhotls3u",
                "bafyreifzczraqwwltddhhqtzayez3ggj42veierr3jklricgighnddvavq",
                "bafyreiglc6u3kfkez2ohkknkfw3npswnr5mpmpmjygjosjbdpwfxigx6ci",
                "bafyreihplmzyr4a5kx2jtuqg7bcfpdhbpu5mbkqdiilmyu3v5btwxl4zpy",
            ]
        },
        {
            // Absent, and before every key in the tree.
            "app.bsky.feed.like/2222222222222",
            [
                "bafyreia63p4zgpog5zar2fgsainugvepfozgtwz6fky5rg5dxmfb4go5p4",
                "bafyreiapmqhxlive6glurf3hxltg56wykn732mzx7nejrhnd27vgj2wnji",
                "bafyreiaua6eo3e2m53tyxdsudjajujwhv7mgwyhca6gsc26xwt5kt3wuu4",
                "bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4",
                "bafyreigmzkz73l34yr2abkaev7snxfibzhi2tny7356y7u6o5y7ginkwca",
                "bafyreihfhxoksnqb35ozxdhwjjwbqusacmzmqarqqltrpupnzj65onxdti",
            ]
        },
        {
            // Absent, and after every key in the tree.
            "com.example.record/zzzzzzzzzzzzz",
            [
                "bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4",
                "bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve",
                "bafyreidhormwlyipxyuuzqc6mspskhovsiybzk76rtn5fnr264hhotls3u",
                "bafyreifu7qchpvzivmlyp34ejqk37piy3ksf4fr3ag32sthnxhqmsfosju",
                "bafyreigzblvxtqjlt6zwcd3p5tf4od2tg4rduw37djpbkpovlfzj7jcfai",
                "bafyreihv4okbfesbusn4kwo24ya33anzzxxk4rnek7446cttazbgegab4m",
            ]
        },
        {
            // Absent, between two keys.
            "app.bsky.feed.post/aaaaaaaaaaaaa",
            [
                "bafyreibbzbgktynyemaziojna3ekz25gfdqt7wcarpop6z3dna3of4ovfu",
                "bafyreibfvbgr6icdhm4nd7xcqyjqgkmbro2za6tvtm7krghw5wm5lecwq4",
                "bafyreicztkzrp6yo4zfm53a5aaltk24vn5wbrepbxcgqp6qwmcqkwranyq",
                "bafyreid6erjndi5bsjevws6dtsmi76ag5mmcerfo6cfhb2vpv6wuygrvve",
                "bafyreidhormwlyipxyuuzqc6mspskhovsiybzk76rtn5fnr264hhotls3u",
                "bafyreiens3jc6j45nkcqhbxq3jjyzkffjv7pnb4qolb6bmtnpyrt26fulu",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(ReferenceCoveringProofs))]
    public void SerializeProof_GeneratedTree_MatchesReferenceCoveringProofExactly(string key, string[] expected)
    {
        var tree = GeneratedTree(42, 1000);

        var (_, proof) = tree.SerializeProof([key]);

        // getCoveringProof in the reference already includes the root for these keys, so the two
        // block sets must agree exactly.
        Assert.Equal(expected, proof.Keys.Order(StringComparer.Ordinal));
    }
}
