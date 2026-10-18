using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// The JSON data model and DRISL-CBOR: the atproto-interop-tests <c>data-model/</c> suites and the
/// cases of <c>data-model/drisl-*.json</c>.
/// </summary>
public abstract class DataModelConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    /// <summary>One row per interop fixture: its JSON, its DRISL bytes and their CID.</summary>
    public static TheoryData<string, string, string, string> InteropFixtures()
    {
        var data = new TheoryData<string, string, string, string>();
        var fixtures = Vectors.Json("data-model/data-model-fixtures.json").AsArray();
        for (var i = 0; i < fixtures.Count; i++)
            data.Add($"data-model/data-model-fixtures.json#{i}", fixtures[i]!["json"]!.ToJsonString(), Base64((string)fixtures[i]!["cbor_base64"]!), (string)fixtures[i]!["cid"]!);
        return data;
    }

    /// <summary>A fixture encodes to the reference bytes, which hash to the reference CID.</summary>
    [Theory]
    [MemberData(nameof(InteropFixtures))]
    [Trait("Spec", "DAT-44"), Trait("Spec", "DAT-45")]
    public void EncodeDataModel_InteropFixture_MatchesReferenceBytesAndCid(string vectorId, string json, string cborHex, string cid)
    {
        Vector(vectorId);
        Assert.Equal(cborHex, Convert.ToHexStringLower(Adapter.EncodeDataModel(json) ?? []));
        Assert.Equal(cid, Adapter.ComputeCid(Convert.FromHexString(cborHex), CidCodec.DagCbor));
    }

    /// <summary>A fixture's bytes read back as its JSON.</summary>
    [Theory]
    [MemberData(nameof(InteropFixtures))]
    [Trait("Spec", "DAT-45")]
    public void DecodeDataModel_InteropFixture_MatchesReferenceJson(string vectorId, string json, string cborHex, string cid)
    {
        Vector(vectorId);
        _ = cid;
        AssertJson(json, Adapter.DecodeDataModel(Convert.FromHexString(cborHex), DrislMode.Lenient));
    }

    /// <summary>One row per entry of the interop valid and invalid data-model suites.</summary>
    public static TheoryData<string, string, bool> InteropSuites()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var (file, valid) in new[] { ("data-model-valid.json", true), ("data-model-invalid.json", false) })
            foreach (var entry in Vectors.Json($"data-model/{file}").AsArray())
                data.Add($"data-model/{file}#{entry!["note"]}", entry["json"]!.ToJsonString(), valid);
        return data;
    }

    /// <summary>A valid entry converts to the data model; an invalid one is refused.</summary>
    [Theory]
    [MemberData(nameof(InteropSuites))]
    [Trait("Spec", "DAT-1"), Trait("Spec", "DAT-47"), Trait("Spec", "DAT-51")]
    public void EncodeDataModel_InteropSuiteEntry_MatchesItsVerdict(string vectorId, string json, bool valid)
    {
        Vector(vectorId);
        Assert.Equal(valid, Adapter.EncodeDataModel(json) is not null);
    }

    /// <summary>Rows of a <c>drisl-*.json</c> file.</summary>
    public static TheoryData<string> Cases(string file) => Vectors.Ids($"data-model/{file}");

    /// <summary>JSON encodes to exactly the expected bytes, or is refused.</summary>
    [Theory]
    [MemberData(nameof(Cases), "drisl-encode.json")]
    [Trait("Spec", "DAT-1"), Trait("Spec", "DAT-2"), Trait("Spec", "DAT-40"), Trait("Spec", "DAT-41"), Trait("Spec", "DAT-46"), Trait("Spec", "DAT-48")]
    public void EncodeDataModel_Case_MatchesExpected(string vectorId)
    {
        Vector(vectorId);
        var c = Vectors.Case(vectorId);
        var cbor = Adapter.EncodeDataModel(Vectors.Text(c, "input"));
        if (c["rejected"] is not null)
        {
            Assert.Null(cbor);
            return;
        }

        Assert.Equal((string)c["output"]!, Convert.ToHexStringLower(cbor ?? []));
        if (c["cid"] is { } cid) Assert.Equal((string)cid!, Adapter.ComputeCid(cbor, CidCodec.DagCbor));
    }

    /// <summary>Bytes read leniently give the expected JSON, or are refused.</summary>
    [Theory]
    [MemberData(nameof(Cases), "drisl-decode.json")]
    [Trait("Spec", "DAT-26"), Trait("Spec", "DAT-34"), Trait("Spec", "DAT-45"), Trait("Spec", "DAT-46")]
    public void DecodeDataModel_LenientCase_MatchesExpected(string vectorId) => AssertDecode(vectorId, DrislMode.Lenient);

    /// <summary>Bytes read strictly give the expected JSON, or are refused.</summary>
    [Theory]
    [MemberData(nameof(Cases), "drisl-strict.json")]
    [Trait("Spec", "DAT-20"), Trait("Spec", "DAT-21"), Trait("Spec", "DAT-22"), Trait("Spec", "DAT-23"), Trait("Spec", "DAT-24"), Trait("Spec", "DAT-25")]
    public void DecodeDataModel_StrictCase_MatchesExpected(string vectorId) => AssertDecode(vectorId, DrislMode.Strict);

    private void AssertDecode(string vectorId, DrislMode mode)
    {
        Vector(vectorId);
        AssertDecode(Adapter, Vectors.Case(vectorId), mode);
    }

    /// <summary>Asserts a <c>drisl-decode.json</c>-shaped row: rejected, or read as the expected JSON.</summary>
    internal static void AssertDecode(IProtocolAdapter adapter, JsonNode c, DrislMode mode)
    {
        var json = adapter.DecodeDataModel(Vectors.Hex(c["input"]), mode);
        if (c["rejected"] is not null) Assert.Null(json);
        else AssertJson(Vectors.Text(c, "output"), json);
    }

    private static string Base64(string unpadded) =>
        Convert.ToHexStringLower(Convert.FromBase64String(unpadded.PadRight(unpadded.Length + (4 - unpadded.Length % 4) % 4, '=')));
}
