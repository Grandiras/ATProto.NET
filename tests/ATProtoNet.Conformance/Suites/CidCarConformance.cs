using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

/// <summary>CID computation and CARv1 reading and writing: <c>cid-car/*.json</c>.</summary>
public abstract class CidCarConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    /// <summary>Rows of a <c>cid-car/*.json</c> file.</summary>
    public static TheoryData<string> Cases(string file) => Vectors.Ids($"cid-car/{file}");

    /// <summary>The CID of the data under the codec is the expected one.</summary>
    [Theory]
    [MemberData(nameof(Cases), "cid.json")]
    [Trait("Spec", "DAT-10"), Trait("Spec", "DAT-11")]
    public void ComputeCid_Case_MatchesExpected(string vectorId)
    {
        Vector(vectorId);
        var c = Vectors.Case(vectorId);
        var codec = (string)c["codec"]! == "raw" ? CidCodec.Raw : CidCodec.DagCbor;
        Assert.Equal((string)c["cid"]!, Adapter.ComputeCid(Vectors.Hex(c["data"]), codec));
    }

    /// <summary>A CAR reads as the expected roots and blocks, or is refused.</summary>
    [Theory]
    [MemberData(nameof(Cases), "car-read.json")]
    [Trait("Spec", "DAT-55"), Trait("Spec", "DAT-56"), Trait("Spec", "DAT-57"), Trait("Spec", "DAT-58"), Trait("Spec", "DAT-61")]
    public void ReadCar_Case_MatchesExpected(string vectorId)
    {
        Vector(vectorId);
        AssertRead(Adapter, Vectors.Case(vectorId));
    }

    /// <summary>Roots and blocks write as exactly the expected CAR, which reads back as them.</summary>
    [Theory]
    [MemberData(nameof(Cases), "car-write.json")]
    [Trait("Spec", "DAT-62")]
    public void WriteCar_Case_MatchesExpectedAndReadsBack(string vectorId)
    {
        Vector(vectorId);
        var c = Vectors.Case(vectorId);
        var roots = Roots(c);
        var blocks = Blocks(c);

        var car = Adapter.WriteCar(roots, blocks);

        Assert.Equal((string)c["car"]!, Convert.ToHexStringLower(car));
        var read = Adapter.ReadCar(car, verifyBlockCids: true);
        Assert.NotNull(read);
        Assert.Equal(roots, read.Roots);
        Assert.Equal(blocks.Select(Show), read.Blocks.Select(Show));
    }

    /// <summary>Asserts a <c>car-read.json</c>-shaped row: rejected, or the expected roots and blocks.</summary>
    internal static void AssertRead(IProtocolAdapter adapter, JsonNode c)
    {
        var read = adapter.ReadCar(Vectors.Hex(c["car"]), (bool?)c["verifyBlockCids"] ?? false);
        if (c["rejected"] is not null)
        {
            Assert.Null(read);
            return;
        }

        Assert.NotNull(read);
        Assert.Equal(Roots(c), read.Roots);
        Assert.Equal(Blocks(c).Select(Show), read.Blocks.Select(Show));
    }

    private static string[] Roots(JsonNode c) => [.. c["roots"]!.AsArray().Select(r => (string)r!)];

    private static CarBlockData[] Blocks(JsonNode c) =>
        [.. c["blocks"]!.AsArray().Select(b => new CarBlockData((string)b![0]!, Convert.FromHexString((string)b[1]!)))];

    private static string Show(CarBlockData block) => $"{block.Cid} {Convert.ToHexStringLower(block.Data)}";
}
