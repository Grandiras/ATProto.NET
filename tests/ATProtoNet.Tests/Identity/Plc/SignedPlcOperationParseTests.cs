using System.Buffers;
using System.Text;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Identity.Plc;
using ATProtoNet.TestKit;
using static ATProtoNet.Tests.Identity.Plc.SignedPlcOperationTests;

namespace ATProtoNet.Tests.Identity.Plc;

public sealed class SignedPlcOperationParseTests
{
    private const string Cid = "bafyreiga4x3hesaf54i5g3fvpffbgzrmvgyzpcf5dokiitdoqcf3f2ipem";
    private const string Sig = "L1NJAj0nJ-MLk5R6C9Nfaqil7TOkXY4j9zWlG5E16q84Q9cBkLz3jo_QMqeYCIL4S0iblzYemNkL-Qz4X2EcFw";

    // Signs any map the way the directory's clients do, so Parse can be fed shapes Sign refuses to build.
    private static byte[] SignJson(string unsignedJson, SigningKey key)
    {
        var drisl = SignedObject.Sign((DataModelMap)DataModelValue.FromJson(Encoding.UTF8.GetBytes(unsignedJson)), key, SignatureEncoding.Base64Url);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) DrislJson.WriteJson(drisl, writer);
        return buffer.WrittenSpan.ToArray();
    }

    [Fact]
    [Trait("Spec", "IDN-49")]
    public void Parse_SignedUpdate_RoundTripsTheTypedFields()
    {
        using var rotation = Rotation();
        using var signing = Signing();
        var genesis = Genesis(rotation, signing);
        var signed = SignedPlcOperation.Sign(genesis with { Prev = SignedPlcOperation.Sign(genesis, rotation).Cid }, rotation);

        var parsed = SignedPlcOperation.Parse(signed.ToJson().Span);

        var update = Assert.IsType<PlcUpdate>(parsed.Operation);
        Assert.Equal(genesis.RotationKeys, update.RotationKeys);
        Assert.Equal(genesis.VerificationMethods, update.VerificationMethods);
        Assert.Equal(genesis.AlsoKnownAs, update.AlsoKnownAs);
        Assert.Equal(genesis.Services, update.Services);
        Assert.Equal(signed.Operation.Prev, update.Prev);
        Assert.Equal(signed.Signature.ToArray(), parsed.Signature.ToArray());
        Assert.Equal(signed.Cid, parsed.Cid);
        Assert.True(parsed.Verify(rotation.PublicKey.DidKey));
    }

    [Fact]
    [Trait("Spec", "IDN-59")]
    public void Parse_KemVerificationMethod_IsAnOrdinaryEntry()
    {
        using var rotation = Rotation();
        using var signing = Signing();
        using var kem = SigningKey.Generate(KeyCurve.P256);
        var genesis = Genesis(rotation, signing) with
        {
            VerificationMethods = new Dictionary<string, DidKey> { ["atproto"] = signing.PublicKey.DidKey, ["atproto_kem"] = kem.PublicKey.DidKey },
        };

        var parsed = SignedPlcOperation.Parse(SignedPlcOperation.Sign(genesis, rotation).ToJson().Span);

        Assert.Equal(kem.PublicKey.DidKey, Assert.IsType<PlcUpdate>(parsed.Operation).VerificationMethods["atproto_kem"]);
    }

    [Fact]
    [Trait("Spec", "IDN-49")]
    public void Parse_Tombstone_ReadsItsPrev()
    {
        using var rotation = Rotation();

        var parsed = SignedPlcOperation.Parse(SignJson($$"""{"type":"plc_tombstone","prev":"{{Cid}}"}""", rotation));

        Assert.Equal(ATProtoNet.Cid.Parse(Cid), Assert.IsType<PlcTombstone>(parsed.Operation).Prev);
        Assert.True(parsed.Verify(rotation.PublicKey.DidKey));
    }

    [Fact]
    [Trait("Spec", "IDN-49")]
    [Trait("Spec", "IDN-52")]
    public void Parse_LegacyCreate_ReadsItsFieldsVerifiesAndDerivesItsDid()
    {
        using var signing = Rotation();
        var json = SignJson($$"""
            {"type":"create","signingKey":"{{TestKeys.K256DidKey}}","recoveryKey":"{{TestKeys.P256DidKey}}","handle":"alice.example.com","service":"https://pds.example.com","prev":null}
            """, signing);

        var parsed = SignedPlcOperation.Parse(json);

        var create = Assert.IsType<PlcLegacyCreate>(parsed.Operation);
        Assert.Equal((TestKeys.K256DidKey, TestKeys.P256DidKey, "alice.example.com", "https://pds.example.com"), (create.SigningKey.Value, create.RecoveryKey.Value, create.Handle, create.Service));
        Assert.Null(create.Prev);
        Assert.True(parsed.Verify(signing.PublicKey.DidKey));
        Assert.Equal("plc", parsed.DeriveDid().Method);
    }

    public static TheoryData<string> Malformed() =>
    [
        "not json",
        "[]",
        $$$"""{"type":"plc_tombstone","prev":"{{{Cid}}}"}""",
        $$$"""{"type":"plc_teleport","prev":"{{{Cid}}}","sig":"{{{Sig}}}"}""",
        $$$"""{"type":"plc_tombstone","prev":"{{{Cid}}}","sig":"{{{Sig}}}","extra":1}""",
        $$$"""{"type":"plc_tombstone","prev":"not a cid","sig":"{{{Sig}}}"}""",
        $$$"""{"type":"plc_tombstone","prev":null,"sig":"{{{Sig}}}"}""",
        $$$"""{"type":"plc_tombstone","prev":"{{{Cid}}}","sig":"{{{Sig}}}=="}""",
        $$$"""{"type":"plc_tombstone","prev":"{{{Cid}}}","sig":"{{{Sig}}}","sig":"{{{Sig}}}"}""",
        $$$"""{"type":"plc_operation","rotationKeys":"{{{TestKeys.K256DidKey}}}","verificationMethods":{},"alsoKnownAs":[],"services":{},"prev":null,"sig":"{{{Sig}}}"}""",
        $$$"""{"type":"plc_operation","rotationKeys":["did:key:nope"],"verificationMethods":{},"alsoKnownAs":[],"services":{},"prev":null,"sig":"{{{Sig}}}"}""",
        $$$"""{"type":"plc_operation","rotationKeys":[],"verificationMethods":{},"alsoKnownAs":[1],"services":{},"prev":null,"sig":"{{{Sig}}}"}""",
        $$$"""{"type":"plc_operation","rotationKeys":[],"verificationMethods":{},"alsoKnownAs":[],"services":{"atproto_pds":{"type":"x","endpoint":"/relative"}},"prev":null,"sig":"{{{Sig}}}"}""",
        $$$"""{"type":"plc_operation","rotationKeys":[],"verificationMethods":{},"alsoKnownAs":[],"services":{"atproto_pds":{"type":"x","endpoint":"https://a.example","more":1}},"prev":null,"sig":"{{{Sig}}}"}""",
        $$$"""{"type":"plc_operation","rotationKeys":[],"verificationMethods":{},"alsoKnownAs":[],"services":{},"sig":"{{{Sig}}}"}""",
        $$$"""{"type":"create","signingKey":"{{{TestKeys.K256DidKey}}}","recoveryKey":"{{{TestKeys.K256DidKey}}}","handle":"a","service":"b","prev":"{{{Cid}}}","sig":"{{{Sig}}}"}""",
    ];

    [Theory]
    [MemberData(nameof(Malformed))]
    [Trait("Spec", "IDN-49")]
    public void Parse_MalformedOperation_ThrowsFormatException(string json) =>
        Assert.Throws<FormatException>(() => SignedPlcOperation.Parse(Encoding.UTF8.GetBytes(json)));
}
