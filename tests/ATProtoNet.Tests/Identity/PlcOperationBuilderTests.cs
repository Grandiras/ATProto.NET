using System.Text.Json.Nodes;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Identity;

public sealed class PlcOperationBuilderTests
{
    private const string Handle = "alice.example.com";
    private const string PdsEndpoint = "https://pds.example.com";

    private static JsonObject Genesis(AtProtoKey rotationKey, AtProtoKey signingKey) =>
        PlcOperationBuilder.CreateGenesisOperation(
            [rotationKey.ToDidKey()], signingKey.ToDidKey(), Handle, PdsEndpoint);

    // ── Genesis operation shape ──────────────────────────────

    [Fact]
    public void CreateGenesisOperation_HasTheFieldsThePlcSpecRequires()
    {
        using var rotation = AtProtoCrypto.GenerateP256Key();
        using var signing = AtProtoCrypto.GenerateP256Key();

        var op = Genesis(rotation, signing);

        Assert.Equal("plc_operation", op["type"]!.GetValue<string>());
        Assert.Equal(rotation.ToDidKey(), op["rotationKeys"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal(signing.ToDidKey(), op["verificationMethods"]!["atproto"]!.GetValue<string>());
        Assert.Equal($"at://{Handle}", op["alsoKnownAs"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("AtprotoPersonalDataServer", op["services"]!["atproto_pds"]!["type"]!.GetValue<string>());
        Assert.Equal(PdsEndpoint, op["services"]!["atproto_pds"]!["endpoint"]!.GetValue<string>());
        Assert.Null(op["prev"]);
    }

    [Fact]
    public void CreateGenesisOperation_TrimsTrailingSlashFromEndpoint()
    {
        using var rotation = AtProtoCrypto.GenerateP256Key();
        using var signing = AtProtoCrypto.GenerateP256Key();

        var op = PlcOperationBuilder.CreateGenesisOperation(
            [rotation.ToDidKey()], signing.ToDidKey(), Handle, "https://pds.example.com/");

        Assert.Equal("https://pds.example.com", op["services"]!["atproto_pds"]!["endpoint"]!.GetValue<string>());
    }

    [Fact]
    public void CreateGenesisOperation_NoRotationKeys_Throws()
    {
        using var signing = AtProtoCrypto.GenerateP256Key();
        Assert.Throws<ArgumentException>(() =>
            PlcOperationBuilder.CreateGenesisOperation([], signing.ToDidKey(), Handle, PdsEndpoint));
    }

    [Fact]
    public void CreateGenesisOperation_MoreThanFiveRotationKeys_Throws()
    {
        using var signing = AtProtoCrypto.GenerateP256Key();
        var keys = Enumerable.Range(0, 6).Select(_ => "did:key:zQ3shokFTS3brHcDQrn82RUDfCZESWL1ZdCEJwekUDPQiYBme").ToList();

        Assert.Throws<ArgumentException>(() =>
            PlcOperationBuilder.CreateGenesisOperation(keys, signing.ToDidKey(), Handle, PdsEndpoint));
    }

    // ── Signing and DID derivation ───────────────────────────

    [Fact]
    public void Sign_AddsBase64UrlSignatureWithoutPadding()
    {
        using var rotation = AtProtoCrypto.GenerateP256Key();
        using var signing = AtProtoCrypto.GenerateP256Key();

        var signed = PlcOperationBuilder.Sign(Genesis(rotation, signing), rotation);
        var sig = signed.Operation["sig"]!.GetValue<string>();

        Assert.DoesNotContain('=', sig);
        Assert.DoesNotContain('+', sig);
        Assert.DoesNotContain('/', sig);
    }

    [Fact]
    public void Sign_SignatureVerifiesAgainstTheUnsignedOperationEncoding()
    {
        using var rotation = AtProtoCrypto.GenerateP256Key();
        using var signing = AtProtoCrypto.GenerateP256Key();

        var unsigned = Genesis(rotation, signing);
        var unsignedCbor = DagCborEncoder.Encode(
            System.Text.Json.JsonSerializer.SerializeToElement(unsigned));

        var signed = PlcOperationBuilder.Sign(unsigned, rotation);
        var sigBytes = DecodeBase64Url(signed.Operation["sig"]!.GetValue<string>());

        Assert.True(rotation.Verify(unsignedCbor, sigBytes));
    }

    [Fact]
    public void Sign_DoesNotMutateTheInputOperation()
    {
        using var rotation = AtProtoCrypto.GenerateP256Key();
        using var signing = AtProtoCrypto.GenerateP256Key();

        var unsigned = Genesis(rotation, signing);
        PlcOperationBuilder.Sign(unsigned, rotation);

        Assert.False(unsigned.ContainsKey("sig"));
    }

    [Fact]
    public void Sign_OperationAlreadySigned_Throws()
    {
        using var rotation = AtProtoCrypto.GenerateP256Key();
        using var signing = AtProtoCrypto.GenerateP256Key();

        var signed = PlcOperationBuilder.Sign(Genesis(rotation, signing), rotation);

        Assert.Throws<ArgumentException>(() => PlcOperationBuilder.Sign(signed.Operation, rotation));
    }

    [Fact]
    public void Sign_DerivesAWellFormedDidPlc()
    {
        using var rotation = AtProtoCrypto.GenerateP256Key();
        using var signing = AtProtoCrypto.GenerateP256Key();

        var did = PlcOperationBuilder.Sign(Genesis(rotation, signing), rotation).Did;

        Assert.StartsWith("did:plc:", did);
        Assert.Equal(24, did["did:plc:".Length..].Length);
        Assert.True(Did.TryParse(did, out _));
    }

    [Fact]
    public void DeriveDid_IsDeterministicForTheSameSignedOperation()
    {
        using var rotation = AtProtoCrypto.GenerateP256Key();
        using var signing = AtProtoCrypto.GenerateP256Key();

        var signed = PlcOperationBuilder.Sign(Genesis(rotation, signing), rotation);

        Assert.Equal(signed.Did, PlcOperationBuilder.DeriveDid(signed.Cbor));
    }

    [Fact]
    public void Sign_DifferentAccountsGetDifferentDids()
    {
        using var rotationA = AtProtoCrypto.GenerateP256Key();
        using var signingA = AtProtoCrypto.GenerateP256Key();
        using var rotationB = AtProtoCrypto.GenerateP256Key();
        using var signingB = AtProtoCrypto.GenerateP256Key();

        var a = PlcOperationBuilder.Sign(Genesis(rotationA, signingA), rotationA).Did;
        var b = PlcOperationBuilder.Sign(Genesis(rotationB, signingB), rotationB).Did;

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ToJson_RendersASubmittableBody()
    {
        using var rotation = AtProtoCrypto.GenerateP256Key();
        using var signing = AtProtoCrypto.GenerateP256Key();

        var json = PlcOperationBuilder.Sign(Genesis(rotation, signing), rotation).ToJson();
        var parsed = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("plc_operation", parsed["type"]!.GetValue<string>());
        Assert.True(parsed.ContainsKey("sig"));
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }
}
