using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Identity.Plc;
using ATProtoNet.TestKit;

namespace ATProtoNet.Tests.Identity.Plc;

public sealed class SignedPlcOperationTests
{
    private const string Pds = "https://pds.example.com";

    internal static SigningKey Rotation() => SigningKey.ImportRawPrivateScalar(Convert.FromHexString(TestKeys.K256PrivateKeyHex), KeyCurve.K256);

    internal static SigningKey Signing() => SigningKey.ImportRawPrivateScalar(Convert.FromHexString(TestKeys.P256PrivateKeyHex), KeyCurve.P256);

    internal static PlcUpdate Genesis(SigningKey rotation, SigningKey signing, string endpoint = Pds) => new()
    {
        RotationKeys = [rotation.PublicKey.DidKey],
        VerificationMethods = new Dictionary<string, DidKey> { ["atproto"] = signing.PublicKey.DidKey },
        AlsoKnownAs = ["at://alice.example.com"],
        Services = new Dictionary<string, PlcService> { ["atproto_pds"] = new("AtprotoPersonalDataServer", new Uri(endpoint)) },
    };

    private static JsonNode Json(SignedPlcOperation operation) => JsonNode.Parse(operation.ToJson().Span)!;

    [Fact]
    [Trait("Spec", "IDN-50")]
    public void Sign_Genesis_HasTheFieldsThePlcSpecRequires()
    {
        using var rotation = Rotation();
        using var signing = Signing();

        var json = Json(SignedPlcOperation.Sign(Genesis(rotation, signing), rotation));

        Assert.Equal("plc_operation", (string)json["type"]!);
        Assert.Equal(TestKeys.K256DidKey, (string)json["rotationKeys"]![0]!);
        Assert.Equal(TestKeys.P256DidKey, (string)json["verificationMethods"]!["atproto"]!);
        Assert.Equal("at://alice.example.com", (string)json["alsoKnownAs"]![0]!);
        Assert.Equal("AtprotoPersonalDataServer", (string)json["services"]!["atproto_pds"]!["type"]!);
        Assert.Equal(Pds, (string)json["services"]!["atproto_pds"]!["endpoint"]!);
        Assert.Null(json["prev"]);
        Assert.True(json.AsObject().ContainsKey("prev"));
    }

    [Fact]
    [Trait("Spec", "IDN-50")]
    public void Sign_EndpointWithTrailingSlash_IsWrittenWithout()
    {
        using var rotation = Rotation();
        using var signing = Signing();

        var json = Json(SignedPlcOperation.Sign(Genesis(rotation, signing, Pds + "/"), rotation));

        Assert.Equal(Pds, (string)json["services"]!["atproto_pds"]!["endpoint"]!);
    }

    [Theory]
    [Trait("Spec", "IDN-50")]
    [InlineData(0)]
    [InlineData(PlcUpdate.MaxRotationKeys + 1)]
    public void Sign_RotationKeyCountOutOfRange_Throws(int count)
    {
        using var rotation = Rotation();
        using var signing = Signing();
        var keys = Enumerable.Range(0, count).Select(_ => SigningKey.Generate(KeyCurve.P256).PublicKey.DidKey).ToList();

        Assert.Throws<ArgumentException>(() => SignedPlcOperation.Sign(Genesis(rotation, signing) with { RotationKeys = keys }, rotation));
    }

    public static TheoryData<string> BrokenLimits() =>
        ["duplicate rotation key", "verification methods", "verification method id", "alsoKnownAs", "alsoKnownAs entry", "duplicate alsoKnownAs",
         "services", "service id", "service type", "service endpoint", "relative endpoint", "operation size"];

    [Theory]
    [MemberData(nameof(BrokenLimits))]
    [Trait("Spec", "IDN-50")]
    public void Sign_LimitBroken_Throws(string limit)
    {
        using var rotation = Rotation();
        using var signing = Signing();
        var genesis = Genesis(rotation, signing);
        var key = signing.PublicKey.DidKey;
        var service = new PlcService("AtprotoPersonalDataServer", new Uri(Pds));
        Dictionary<string, TValue> Many<TValue>(int count, TValue value) => Enumerable.Range(0, count).ToDictionary(i => $"id{i}", _ => value);
        string[] Aka(int count, int length) => [.. Enumerable.Range(0, count).Select(i => $"at://{i}".PadRight(length, 'a'))];

        var broken = limit switch
        {
            "duplicate rotation key" => genesis with { RotationKeys = [rotation.PublicKey.DidKey, rotation.PublicKey.DidKey] },
            "verification methods" => genesis with { VerificationMethods = Many(PlcUpdate.MaxVerificationMethods + 1, key) },
            "verification method id" => genesis with { VerificationMethods = new Dictionary<string, DidKey> { [new string('a', PlcUpdate.MaxIdLength + 1)] = key } },
            "alsoKnownAs" => genesis with { AlsoKnownAs = Aka(PlcUpdate.MaxAlsoKnownAs + 1, 20) },
            "alsoKnownAs entry" => genesis with { AlsoKnownAs = Aka(1, PlcUpdate.MaxAlsoKnownAsLength + 1) },
            "duplicate alsoKnownAs" => genesis with { AlsoKnownAs = ["at://a.example.com", "at://a.example.com"] },
            "services" => genesis with { Services = Many(PlcUpdate.MaxServices + 1, service) },
            "service id" => genesis with { Services = new Dictionary<string, PlcService> { [""] = service } },
            "service type" => genesis with { Services = Many(1, service with { Type = new string('a', PlcService.MaxTypeLength + 1) }) },
            "service endpoint" => genesis with { Services = Many(1, service with { Endpoint = new Uri(Pds + "/" + new string('a', PlcService.MaxEndpointLength)) }) },
            "relative endpoint" => genesis with { Services = Many(1, service with { Endpoint = new Uri("/xrpc", UriKind.Relative) }) },
            _ => genesis with { AlsoKnownAs = Aka(PlcUpdate.MaxAlsoKnownAs, PlcUpdate.MaxAlsoKnownAsLength), Services = Many(PlcUpdate.MaxServices, service with { Endpoint = new Uri(Pds + "/" + new string('a', 200)) }) },
        };

        Assert.Throws<ArgumentException>(() => SignedPlcOperation.Sign(broken, rotation));
    }

    [Fact]
    [Trait("Spec", "IDN-51")]
    public void Sign_AddsBase64UrlSignatureWithoutPadding()
    {
        using var rotation = Rotation();
        using var signing = Signing();

        var signed = SignedPlcOperation.Sign(Genesis(rotation, signing), rotation);
        var sig = (string)Json(signed)["sig"]!;

        Assert.Equal(VerificationKey.SignatureLength, signed.Signature.Length);
        Assert.DoesNotContain('=', sig);
        Assert.DoesNotContain('+', sig);
        Assert.DoesNotContain('/', sig);
    }

    [Fact]
    [Trait("Spec", "IDN-51")]
    [Trait("Spec", "IDN-54")]
    public void Sign_SignatureVerifiesAgainstTheUnsignedOperationEncoding()
    {
        using var rotation = Rotation();
        using var signing = Signing();

        var signed = SignedPlcOperation.Sign(Genesis(rotation, signing), rotation);
        var unsigned = SignedDrisl.GetUnsignedBytes(DrislJson.ToDrisl(signed.ToJson().Span), out _);

        Assert.True(rotation.PublicKey.Verify(unsigned, signed.Signature.Span));
        Assert.True(signed.Verify(rotation.PublicKey.DidKey));
        Assert.False(signed.Verify(signing.PublicKey.DidKey));
    }

    [Fact]
    [Trait("Spec", "IDN-51")]
    public void Sign_DoesNotMutateTheInputOperation()
    {
        using var rotation = Rotation();
        using var signing = Signing();
        var genesis = Genesis(rotation, signing);
        var copy = genesis with { };

        var signed = SignedPlcOperation.Sign(genesis, rotation);

        Assert.Same(genesis, signed.Operation);
        Assert.Equal(copy, genesis);
    }

    [Fact]
    [Trait("Spec", "IDN-49")]
    public void Sign_LegacyCreate_Throws()
    {
        using var rotation = Rotation();
        var create = new PlcLegacyCreate { SigningKey = rotation.PublicKey.DidKey, RecoveryKey = rotation.PublicKey.DidKey, Handle = "alice.example.com", Service = Pds };

        Assert.Throws<ArgumentException>(() => SignedPlcOperation.Sign(create, rotation));
    }

    [Fact]
    [Trait("Spec", "IDN-49")]
    public void Sign_Tombstone_NamesItsPrevAndNeedsOne()
    {
        using var rotation = Rotation();
        using var signing = Signing();
        var genesis = SignedPlcOperation.Sign(Genesis(rotation, signing), rotation);

        var json = Json(SignedPlcOperation.Sign(new PlcTombstone { Prev = genesis.Cid }, rotation));

        Assert.Equal("plc_tombstone", (string)json["type"]!);
        Assert.Equal(genesis.Cid.Value, (string)json["prev"]!);
        Assert.Equal(3, json.AsObject().Count);
        Assert.Throws<ArgumentException>(() => SignedPlcOperation.Sign(new PlcTombstone(), rotation));
    }

    [Fact]
    [Trait("Spec", "IDN-50")]
    public void Sign_GenesisByAKeyItDoesNotList_ThrowsButAnUpdateMay()
    {
        using var rotation = Rotation();
        using var signing = Signing();
        var genesis = Genesis(rotation, signing);

        Assert.Throws<ArgumentException>(() => SignedPlcOperation.Sign(genesis, signing));
        var update = SignedPlcOperation.Sign(genesis with { Prev = SignedPlcOperation.Sign(genesis, rotation).Cid }, signing);
        Assert.True(update.Verify(signing.PublicKey.DidKey));
    }

    [Fact]
    [Trait("Spec", "IDN-52")]
    public void Sign_DerivesAWellFormedDidPlcAndTheCidOfTheSignedEncoding()
    {
        using var rotation = Rotation();
        using var signing = Signing();

        var signed = SignedPlcOperation.Sign(Genesis(rotation, signing), rotation);
        var drisl = DrislJson.ToDrisl(signed.ToJson().Span);
        var did = signed.DeriveDid();

        Assert.Equal("plc", did.Method);
        Assert.Equal("did:plc:" + Base32.Encode(SHA256.HashData(drisl))[..24], did.Value);
        Assert.Equal(Cid.Compute(CidCodec.DagCbor, drisl), signed.Cid);
    }

    [Fact]
    [Trait("Spec", "IDN-52")]
    public void DeriveDid_IsDeterministicForTheSameSignedOperation()
    {
        using var rotation = Rotation();
        using var signing = Signing();

        var signed = SignedPlcOperation.Sign(Genesis(rotation, signing), rotation);
        var parsed = SignedPlcOperation.Parse(signed.ToJson().Span);

        Assert.Equal(signed.DeriveDid(), parsed.DeriveDid());
        Assert.Equal(signed.Cid, parsed.Cid);
    }

    [Fact]
    [Trait("Spec", "IDN-52")]
    public void DeriveDid_NotAGenesisOperation_Throws()
    {
        using var rotation = Rotation();

        var tombstone = SignedPlcOperation.Sign(new PlcTombstone { Prev = Cid.Compute(CidCodec.DagCbor, [1]) }, rotation);

        Assert.Throws<InvalidOperationException>(tombstone.DeriveDid);
    }

    [Fact]
    [Trait("Spec", "IDN-52")]
    public void Sign_DifferentAccountsGetDifferentDids()
    {
        using var rotationA = SigningKey.Generate(KeyCurve.P256);
        using var rotationB = SigningKey.Generate(KeyCurve.K256);
        using var signing = Signing();

        var a = SignedPlcOperation.Sign(Genesis(rotationA, signing), rotationA).DeriveDid();
        var b = SignedPlcOperation.Sign(Genesis(rotationB, signing), rotationB).DeriveDid();

        Assert.NotEqual(a, b);
    }
}
