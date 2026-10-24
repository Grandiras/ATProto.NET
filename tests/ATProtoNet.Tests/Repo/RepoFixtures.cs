using System.Buffers;
using System.Text;
using System.Text.Json.Nodes;
using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Data.Car;
using ATProtoNet.Repo;
using ATProtoNet.TestKit;

namespace ATProtoNet.Tests.Repo;

// Keys, values, hand-built blocks and CARs shared by the repository tests.
internal static class RepoFixtures
{
    /// <summary>The leaf value every reference vector maps its keys to.</summary>
    public static readonly Cid Leaf = Cid.Parse("bafyreie5cvv4h45feadgeuwhbcutmh6t2ceseocckahdoe6uat64zmz454");

    public static readonly Cid EmptyRoot = Cid.Parse("bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm");

    public static readonly Did Account = Did.Parse(TestIds.Did);

    private static readonly string VectorRoot = FindRoot(AppContext.BaseDirectory);

    /// <summary>A conformance-suite vector file, read in place so unit tests use the same pinned data.</summary>
    public static string VectorText(string path) => File.ReadAllText(Path.Combine(VectorRoot, "tests", "ATProtoNet.Conformance", "vectors", path));

    public static JsonNode Vector(string path) => JsonNode.Parse(VectorText(path))!;

    private static string FindRoot(string directory) =>
        File.Exists(Path.Combine(directory, "ATProto.NET.slnx")) ? directory
        : FindRoot(Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar)) ?? throw new DirectoryNotFoundException("No ATProto.NET.slnx above the tests."));

    public static Cid Fake(string label) => Cid.Compute(CidCodec.DagCbor, Encoding.UTF8.GetBytes(label));

    public static Cid CidOf(byte[] block) => Cid.Compute(CidCodec.DagCbor, block);

    public static MstKey Key(string key) => MstKey.Parse(key);

    public static MerkleSearchTree Tree(params (string Key, Cid Value)[] entries) =>
        MerkleSearchTree.Build(entries.Select(e => KeyValuePair.Create(Key(e.Key), e.Value)));

    public static SigningKey K256() => SigningKey.ImportRawPrivateScalar(Convert.FromHexString(TestKeys.K256PrivateKeyHex), KeyCurve.K256);

    public static SigningKey P256() => SigningKey.ImportRawPrivateScalar(Convert.FromHexString(TestKeys.P256PrivateKeyHex), KeyCurve.P256);

    public static byte[] RecordBlock(string text) =>
        new DataModelMap([new("$type", new DataModelString("com.example.record")), new("text", new DataModelString(text))]).ToDrisl();

    public static MemoryBlockStore Store(params byte[][] blocks)
    {
        var store = new MemoryBlockStore();
        foreach (var block in blocks) store.Put(CidOf(block), block);
        return store;
    }

    public static MemoryBlockStore Blocks(MerkleSearchTree tree)
    {
        var store = new MemoryBlockStore();
        tree.WriteBlocks(store);
        return store;
    }

    public static MemoryBlockStore Proof(MerkleSearchTree tree, params string[] keys)
    {
        var store = new MemoryBlockStore();
        tree.WriteCoveringProof(keys.Select(Key), store);
        return store;
    }

    // The keys the reference trees of @atproto/repo 0.10.14 were built from: a 64-bit LCG whose
    // state is spelled as a 13-character rkey, cycling through four collections.
    public static List<MstKey> GeneratedKeys(ulong seed, int count)
    {
        const string alphabet = "234567abcdefghijklmnopqrstuvwxyz";
        string[] collections = ["app.bsky.feed.post", "app.bsky.feed.like", "app.bsky.graph.follow", "com.example.record"];
        var keys = new List<MstKey>(count);
        var state = seed;
        var rkey = new char[13];
        for (var i = 0; i < count; i++)
        {
            state = unchecked(state * 6364136223846793005UL + 1442695040888963407UL);
            var bits = state;
            for (var j = 0; j < rkey.Length; j++, bits >>= 5) rkey[j] = alphabet[(int)(bits & 31)];
            keys.Add(Key($"{collections[i % collections.Length]}/{new string(rkey)}"));
        }
        return keys;
    }

    /// <summary>The value of the i-th generated key: the CID of <c>{"i": i}</c>.</summary>
    public static Cid GeneratedValue(int index) => CidOf(new DataModelMap([new("i", new DataModelInteger(index))]).ToDrisl());

    public static MerkleSearchTree GeneratedTree(ulong seed, int count) =>
        MerkleSearchTree.Build(GeneratedKeys(seed, count).Select((k, i) => KeyValuePair.Create(k, GeneratedValue(i))));

    /// <summary>Keys of <c>com.example.record</c> on one layer, ascending.</summary>
    public static string[] KeysOnLayer(int layer, int count) =>
        [.. Enumerable.Range(0, 100_000).Select(i => $"com.example.record/k{i:D6}").Where(k => Key(k).Layer == layer).Take(count)];

    // ── Hand-built DRISL, for blocks the SDK would never write ──

    public static byte[] Map(params (string Key, byte[] Value)[] entries)
    {
        // Canonical key order (shorter first, then bytewise), stable so a repeated key stays repeated.
        var sorted = entries.OrderBy(e => e.Key.Length).ThenBy(e => e.Key, StringComparer.Ordinal);
        return [.. Header(5, (ulong)entries.Length), .. sorted.SelectMany(e => (byte[])[.. Text(e.Key), .. e.Value])];
    }

    public static byte[] Array(params byte[][] items) => [.. Header(4, (ulong)items.Length), .. items.SelectMany(i => i)];

    public static byte[] Text(string value) => [.. Header(3, (ulong)Encoding.UTF8.GetByteCount(value)), .. Encoding.UTF8.GetBytes(value)];

    public static byte[] Bytes(ReadOnlySpan<byte> value) => [.. Header(2, (ulong)value.Length), .. value];

    public static byte[] Int(long value) => value < 0 ? Header(1, (ulong)(-1 - value)) : Header(0, (ulong)value);

    public static byte[] UInt(ulong value) => Header(0, value);

    public static readonly byte[] Null = [0xF6];

    public static byte[] Link(Cid? cid) => cid is null ? Null : Link(cid.ToByteArray());

    public static byte[] Link(byte[] binary) => [0xD8, 0x2A, .. Bytes([0x00, .. binary])];

    private static byte[] Header(int major, ulong value) => value switch
    {
        < 24 => [(byte)(major << 5 | (int)value)],
        <= byte.MaxValue => [(byte)(major << 5 | 24), (byte)value],
        <= ushort.MaxValue => [(byte)(major << 5 | 25), (byte)(value >> 8), (byte)value],
        <= uint.MaxValue => [(byte)(major << 5 | 26), .. BigEndian(value, 4)],
        _ => [(byte)(major << 5 | 27), .. BigEndian(value, 8)],
    };

    private static byte[] BigEndian(ulong value, int length) => [.. Enumerable.Range(0, length).Select(i => (byte)(value >> (8 * (length - 1 - i))))];

    /// <summary>An MST node entry as written: prefix length, key suffix, value and right subtree.</summary>
    public static byte[] Entry(byte[] prefix, ReadOnlySpan<byte> suffix, Cid value, Cid? right) =>
        Map(("k", Bytes(suffix)), ("p", prefix), ("t", Link(right)), ("v", Link(value)));

    public static byte[] RawNode(Cid? left, params byte[][] entries) => Map(("e", Array(entries)), ("l", Link(left)));

    /// <summary>A node holding keys as given, prefix-compressed as the writer does, but without the tree's rules.</summary>
    public static byte[] Node(Cid? left, params (string Key, Cid Value, Cid? Right)[] entries)
    {
        var previous = "";
        var encoded = new byte[entries.Length][];
        for (var i = 0; i < entries.Length; i++)
        {
            var (key, value, right) = entries[i];
            var prefix = previous.AsSpan().CommonPrefixLength(key);
            encoded[i] = Entry(Int(prefix), Encoding.ASCII.GetBytes(key[prefix..]), value, right);
            previous = key;
        }
        return RawNode(left, encoded);
    }

    // ── Commits and CARs ──

    public static byte[] Car(Cid[] roots, IEnumerable<CarBlock> blocks)
    {
        var output = new ArrayBufferWriter<byte>();
        CarWriter.Write(output, roots, blocks);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>A CAR whose root is a commit of <see cref="Account"/> over <paramref name="data"/>, with the blocks given.</summary>
    public static byte[] SignedCar(SigningKey key, Cid data, params byte[][] blocks)
    {
        var commit = new RepoCommit { Did = Account, Rev = Tid.Parse("3m7fmkxcn5d2a"), Data = data }.Sign(key);
        return Car([commit.Cid], [new(commit.Cid, commit.Block), .. blocks.Select(b => new CarBlock(CidOf(b), b))]);
    }

    /// <summary>The CAR with each block mapped (null drops it), and optionally new roots.</summary>
    public static byte[] Rewrite(byte[] car, Func<CarBlock, CarBlock?> map, Cid[]? roots = null)
    {
        var file = CarReader.Read(car);
        return Car(roots ?? [.. file.Header.Roots], file.Blocks.Select(map).OfType<CarBlock>().ToList());
    }

    public static byte[] Flip(ReadOnlyMemory<byte> data, int index)
    {
        var copy = data.ToArray();
        copy[index] ^= 0x01;
        return copy;
    }
}
