using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Data;
using ATProtoNet.Data.Car;

namespace ATProtoNet.Benchmarks;

// The MST nodes of a repository, built as the atproto repository spec describes, so the CAR holds the
// same nodes as 0.8's export. A stand-in until ATProtoNet.Repo (phase 2) has the real tree.
internal static class MstNodes
{
    public static (Cid Root, List<CarBlock> Nodes) Build(IEnumerable<(string Key, Cid Value)> entries)
    {
        var sorted = entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        var nodes = new List<CarBlock>();
        var root = Node(sorted, sorted.Count == 0 ? 0 : sorted.Max(e => Layer(e.Key)), nodes);
        return (root, nodes);
    }

    // The node at layer for these keys: its own keys are those of the layer, the runs between them subtrees.
    private static Cid Node(List<(string Key, Cid Value)> keys, int layer, List<CarBlock> nodes)
    {
        var own = keys.Where(k => Layer(k.Key) == layer).ToList();
        var runs = new List<List<(string, Cid)>> { new() };
        foreach (var key in keys)
            if (Layer(key.Key) == layer) runs.Add([]);
            else runs[^1].Add(key);
        Cid? Subtree(List<(string, Cid)> run) => run.Count == 0 ? null : Node(run, layer - 1, nodes);

        var output = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new DrislWriter(output);
        writer.WriteStartMap(2);
        writer.WriteKey("e");
        writer.WriteStartArray(own.Count);
        var previous = "";
        for (var i = 0; i < own.Count; i++)
        {
            var (key, value) = own[i];
            var prefix = 0;
            while (prefix < previous.Length && prefix < key.Length && previous[prefix] == key[prefix]) prefix++;
            writer.WriteStartMap(4);
            writer.WriteKey("k");
            writer.WriteBytes(Encoding.ASCII.GetBytes(key[prefix..]));
            writer.WriteKey("p");
            writer.WriteInt64(prefix);
            writer.WriteKey("t");
            Link(writer, Subtree(runs[i + 1]));
            writer.WriteKey("v");
            writer.WriteLink(value);
            writer.WriteEndMap();
            previous = key;
        }
        writer.WriteEndArray();
        writer.WriteKey("l");
        Link(writer, Subtree(runs[0]));
        writer.WriteEndMap();
        var bytes = output.WrittenSpan.ToArray();
        var cid = Cid.Compute(CidCodec.DagCbor, bytes);
        nodes.Add(new CarBlock(cid, bytes));
        return cid;
    }

    private static void Link(DrislWriter writer, Cid? cid)
    {
        if (cid is null) writer.WriteNull();
        else writer.WriteLink(cid);
    }

    // Leading zero bits of SHA-256(key), two per layer (fanout 4).
    private static int Layer(string key)
    {
        var layer = 0;
        foreach (var b in SHA256.HashData(Encoding.ASCII.GetBytes(key)))
        {
            if (b == 0) { layer += 4; continue; }
            layer += b < 4 ? 3 : b < 16 ? 2 : b < 64 ? 1 : 0;
            break;
        }
        return layer;
    }
}
