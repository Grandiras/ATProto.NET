using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// The Merkle Search Tree: the atproto-interop-tests <c>mst/</c> files and the reference trees of
/// <c>mst/mst-*.json</c>.
/// </summary>
public abstract class MstConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    /// <summary>One row per key of key_heights.json (by index) and example_keys.txt (by line).</summary>
    public static TheoryData<string, string, int> KeyHeights()
    {
        var data = new TheoryData<string, string, int>();
        var heights = Vectors.Json("mst/key_heights.json").AsArray();
        for (var i = 0; i < heights.Count; i++)
            data.Add($"mst/key_heights.json#{i}", (string)heights[i]!["key"]!, (int)heights[i]!["height"]!);
        foreach (var key in Vectors.Lines("mst/example_keys.txt"))
            data.Add($"mst/example_keys.txt#{key}", key, key[1] - '0');
        return data;
    }

    /// <summary>A key sits on the layer the fixture names.</summary>
    [Theory]
    [MemberData(nameof(KeyHeights))]
    [Trait("Spec", "REP-4")]
    public void MstKeyHeight_InteropKey_MatchesItsHeight(string vectorId, string key, int height)
    {
        Vector(vectorId);
        Assert.Equal(height, Adapter.MstKeyHeight(key));
    }

    /// <summary>One row per pair of common_prefix.json.</summary>
    public static TheoryData<string, string, string, int> CommonPrefixes() =>
        new(Vectors.Json("mst/common_prefix.json").AsArray().Select((p, i) => ($"mst/common_prefix.json#{i}", (string)p!["left"]!, (string)p["right"]!, (int)p["len"]!)));

    /// <summary>Two keys share the prefix the fixture names.</summary>
    [Theory]
    [MemberData(nameof(CommonPrefixes))]
    [Trait("Spec", "REP-5")]
    public void MstCommonPrefixLength_InteropPair_MatchesTheFixture(string vectorId, string left, string right, int length)
    {
        Vector(vectorId);
        Assert.Equal(length, Adapter.MstCommonPrefixLength(left, right));
    }

    /// <summary>Rows of an <c>mst/*.json</c> array file.</summary>
    public static TheoryData<string> Cases(string file) => Vectors.Ids($"mst/{file}");

    /// <summary>A key is storable exactly when the reference implementation allows it.</summary>
    [Theory]
    [MemberData(nameof(Cases), "mst-keys.json")]
    [Trait("Spec", "REP-1"), Trait("Spec", "REP-2")]
    public void IsValidMstKey_ReferenceKey_MatchesTheReference(string vectorId)
    {
        Vector(vectorId);
        var c = Vectors.Case(vectorId);
        Assert.Equal((bool)c["valid"]!, Adapter.IsValidMstKey((string)c["key"]!));
    }

    /// <summary>One row per edit sequence of mst-edits.json.</summary>
    public static TheoryData<string> EditSequences() =>
        new(Vectors.Json("mst/mst-edits.json")["sequences"]!.AsArray().Select(s => $"mst/mst-edits.json#{s!["name"]}"));

    /// <summary>After every step, the tree has the reference root (and blocks).</summary>
    [Theory]
    [MemberData(nameof(EditSequences))]
    [Trait("Spec", "REP-6"), Trait("Spec", "REP-8"), Trait("Spec", "REP-9")]
    public void MstEdit_ReferenceSequence_MatchesTheReferenceAfterEachStep(string vectorId)
    {
        Vector(vectorId);
        var file = Vectors.Json("mst/mst-edits.json");
        var leaf = (string)file["leaf"]!;
        var sequence = file["sequences"]!.AsArray().Single(s => vectorId.EndsWith("#" + (string)s!["name"]!, StringComparison.Ordinal))!;
        var operations = new List<MstOperation>();

        foreach (var step in sequence["steps"]!.AsArray())
        {
            operations.AddRange(step!["ops"]!.AsArray().Select(o => (string)o!["op"]! == "insert"
                ? new MstOperation((string)o["key"]!, null, leaf)
                : new MstOperation((string)o["key"]!, leaf, null)));
            var tree = Adapter.MstEdit([], operations);
            Assert.Equal((string)step["root"]!, tree.Root);
            if (step["blocks"] is JsonObject blocks)
                Assert.Equal(blocks.Select(b => $"{b.Key} {b.Value}").Order(), tree.Blocks.Select(b => $"{b.Key} {Convert.ToHexStringLower(b.Value)}").Order());
        }
    }

    /// <summary>One row per generated tree of mst-generated.json.</summary>
    public static TheoryData<string> GeneratedTrees() =>
        new(Vectors.Json("mst/mst-generated.json")["trees"]!.AsArray().Select(t => $"mst/mst-generated.json#{t!["name"]}"));

    /// <summary>A generated tree has the reference root and blocks, or its covering proof the reference blocks.</summary>
    [Theory]
    [MemberData(nameof(GeneratedTrees))]
    [Trait("Spec", "REP-9"), Trait("Spec", "REP-21")]
    public void MstEdit_GeneratedTree_MatchesTheReference(string vectorId)
    {
        Vector(vectorId);
        var t = Vectors.Json("mst/mst-generated.json")["trees"]!.AsArray().Single(t => vectorId.EndsWith("#" + (string)t!["name"]!, StringComparison.Ordinal))!;
        var entries = GeneratedEntries((ulong)t["seed"]!, (int)t["count"]!);
        if (t["firstKey"] is { } firstKey) Assert.Equal((string)firstKey!, entries[0].Key);

        if (t["key"] is { } key)
        {
            var proof = Adapter.MstCoveringProof(entries, [(string)key!]);
            Assert.Equal(t["blocks"]!.AsArray().Select(b => (string)b!), proof.Blocks.Keys.Order(StringComparer.Ordinal));
            return;
        }

        var every = (int?)t["deleteEvery"] ?? 0;
        var deletes = every == 0 ? [] : entries.Where((_, i) => i % every == 0).Select(e => new MstOperation(e.Key, e.Value, null)).ToList();
        var tree = Adapter.MstEdit(entries, deletes);
        Assert.Equal((string)t["root"]!, tree.Root);
        Assert.Equal((int)t["blockCount"]!, tree.Blocks.Count);
        Assert.Equal((string)t["blockDigest"]!, Digest(tree.Blocks.Keys));
    }

    /// <summary>Blocks load as exactly the expected entries, or are refused.</summary>
    [Theory]
    [MemberData(nameof(Cases), "mst-load.json")]
    [Trait("Spec", "REP-10"), Trait("Spec", "REP-11"), Trait("Spec", "REP-12"), Trait("Spec", "REP-13"), Trait("Spec", "REP-14"), Trait("Spec", "REP-16")]
    public void MstLoad_Case_MatchesExpected(string vectorId)
    {
        Vector(vectorId);
        AssertLoad(Adapter, Vectors.Case(vectorId));
    }

    /// <summary>Asserts an <c>mst-load.json</c>-shaped row: rejected, or loaded as the expected entries.</summary>
    internal static void AssertLoad(IProtocolAdapter adapter, JsonNode c)
    {
        var blocks = c["blocks"]!.AsObject().ToDictionary(b => b.Key, b => Convert.FromHexString((string)b.Value!));
        var entries = adapter.MstLoad((string)c["root"]!, blocks);
        if (c["rejected"] is not null)
        {
            Assert.Null(entries);
            return;
        }

        Assert.NotNull(entries);
        Assert.Equal(c["entries"]!.AsArray().Select(e => $"{e![0]} {e[1]}"), entries.Select(e => $"{e.Key} {e.Value}"));
    }

    /// <summary>
    /// The keys of the reference trees: a 64-bit LCG whose state is spelled as a 13-character rkey,
    /// cycling through four collections; the value of the i-th key is the CID of <c>{"i": i}</c>.
    /// </summary>
    private List<KeyValuePair<string, string>> GeneratedEntries(ulong seed, int count)
    {
        const string alphabet = "234567abcdefghijklmnopqrstuvwxyz";
        string[] collections = ["app.bsky.feed.post", "app.bsky.feed.like", "app.bsky.graph.follow", "com.example.record"];
        var entries = new List<KeyValuePair<string, string>>(count);
        var state = seed;
        var rkey = new char[13];
        for (var i = 0; i < count; i++)
        {
            state = unchecked(state * 6364136223846793005UL + 1442695040888963407UL);
            var bits = state;
            for (var j = 0; j < rkey.Length; j++, bits >>= 5)
                rkey[j] = alphabet[(int)(bits & 31)];
            byte[] value = i < 24 ? [0xA1, 0x61, 0x69, (byte)i] : i < 256 ? [0xA1, 0x61, 0x69, 0x18, (byte)i] : [0xA1, 0x61, 0x69, 0x19, (byte)(i >> 8), (byte)i];
            entries.Add(new($"{collections[i % collections.Length]}/{new string(rkey)}", Adapter.ComputeCid(value, CidCodec.DagCbor)));
        }
        return entries;
    }

    private static string Digest(IEnumerable<string> cids) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', cids.Order(StringComparer.Ordinal)))));
}
