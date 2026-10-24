using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// PLC operations: the pinned genesis vector built by the reference <c>@did-plc/lib</c>
/// (<c>identity/plc-genesis.json</c>). ECDSA signatures are randomized, so a fresh signature is checked
/// by its unsigned bytes and its verification, and DID and CID derivation on the reference's own bytes.
/// </summary>
public abstract class IdentityConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    private const string File = "identity/plc-genesis.json";

    private static JsonNode Genesis => Vectors.Json(File);

    private static string RotationKey(int index) => (string)Genesis["rotationKeys"]![index]!;

    /// <summary>The reference operation derives the reference DID and CID, covers the reference unsigned bytes, and verifies only against the key that signed it.</summary>
    [Fact]
    [Trait("Spec", "IDN-52"), Trait("Spec", "IDN-53"), Trait("Spec", "IDN-54")]
    public void ReadPlcOperation_ReferenceGenesis_DerivesItsDidAndCidAndVerifies()
    {
        Vector($"{File}#reference");
        var json = Genesis["signedOperation"]!.ToJsonString();

        var facts = Adapter.ReadPlcOperation(json, RotationKey(0));

        Assert.Equal((string)Genesis["did"]!, facts.Did);
        Assert.Equal((string)Genesis["cid"]!, facts.Cid);
        Assert.Equal((string)Genesis["unsignedHex"]!, Convert.ToHexStringLower(facts.UnsignedDrisl));
        Assert.True(facts.SignatureValid);
        Assert.False(Adapter.ReadPlcOperation(json, RotationKey(1)).SignatureValid);
    }

    /// <summary>The SDK's own genesis operation from the same inputs has the reference's fields and unsigned bytes, and its signature verifies.</summary>
    [Fact]
    [Trait("Spec", "IDN-50"), Trait("Spec", "IDN-51"), Trait("Spec", "IDN-53")]
    public void SignPlcGenesis_FixedInputs_EncodesTheReferenceUnsignedBytes()
    {
        Vector($"{File}#fresh signature");
        var g = Genesis;
        var curve = Enum.Parse<SigningCurve>((string)g["rotationCurve"]!);
        string[] rotationKeys = [.. g["rotationKeys"]!.AsArray().Select(k => (string)k!)];

        var json = Adapter.SignPlcGenesis(rotationKeys, (string)g["signingKey"]!, (string)g["handle"]!, (string)g["pds"]!, curve, Convert.FromHexString((string)g["rotationPrivateKeyHex"]!));
        var facts = Adapter.ReadPlcOperation(json, rotationKeys[0]);

        var signed = JsonNode.Parse(json)!.AsObject();
        Assert.True(signed.Remove("sig"));
        AssertJson(g["operation"]!.ToJsonString(), signed.ToJsonString());
        Assert.Equal((string)g["unsignedHex"]!, Convert.ToHexStringLower(facts.UnsignedDrisl));
        Assert.True(facts.SignatureValid);
        Assert.Matches("^did:plc:[a-z2-7]{24}$", facts.Did);
    }
}
