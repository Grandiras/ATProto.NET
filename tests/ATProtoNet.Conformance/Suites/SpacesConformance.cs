using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// The Spaces primitives against the pinned reference implementation (<c>spaces/</c>): BLAKE3's
/// official vectors and long outputs, LtHash, RFC 9421 request signatures, RFC 8941 structured
/// fields and space tokens; the KEM, MACs and repository exports are in the other part.
/// </summary>
public abstract partial class SpacesConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    private const string Blake3File = "spaces/blake3_test_vectors.json";
    private const string SignatureFile = "spaces/http-signatures.json";

    private static readonly IReadOnlyDictionary<string, JsonNode> Blake3Cases =
        Vectors.Json(Blake3File)["cases"]!.AsArray().ToDictionary(c => $"{Blake3File}#{(int)c!["input_len"]!}", c => c!);
    private static readonly IReadOnlyDictionary<string, JsonNode> LongOutputCases = Vectors.Cases("spaces/blake3-long-output.json");
    private static readonly IReadOnlyDictionary<string, JsonNode> LtHashCases = Vectors.Cases("spaces/lthash.json");
    private static readonly JsonNode SignatureKeys = Vectors.Json(SignatureFile)["keys"]!;
    private static readonly IReadOnlyDictionary<string, JsonNode> SignatureCases = Vectors.Cases(SignatureFile);
    private static readonly IReadOnlyDictionary<string, JsonNode> SigningCases = Vectors.Cases(SignatureFile, "signing");
    private static readonly IReadOnlyDictionary<string, JsonNode> FieldCases = Vectors.Cases("spaces/structured-fields.json");
    private static readonly IReadOnlyDictionary<string, JsonNode> TokenCases = Vectors.Cases("spaces/space-tokens.json");

    /// <summary>The official BLAKE3 vectors, by ID.</summary>
    public static TheoryData<string> Blake3Vectors() => new(Blake3Cases.Keys);

    /// <summary>The long-output BLAKE3 vectors, by ID.</summary>
    public static TheoryData<string> LongOutputs() => new(LongOutputCases.Keys);

    /// <summary>The LtHash vectors, by ID.</summary>
    public static TheoryData<string> LtHashes() => new(LtHashCases.Keys);

    /// <summary>The signature verification vectors, by ID.</summary>
    public static TheoryData<string> Signatures() => new(SignatureCases.Keys);

    /// <summary>The signing vectors, by ID.</summary>
    public static TheoryData<string> Signing() => new(SigningCases.Keys);

    /// <summary>The structured-field vectors, by ID.</summary>
    public static TheoryData<string> Fields() => new(FieldCases.Keys);

    /// <summary>The space token vectors, by ID.</summary>
    public static TheoryData<string> Tokens() => new(TokenCases.Keys);

    /// <summary>Hashing the official input gives the official 131-byte output, and its first 32 bytes as the default digest.</summary>
    [Theory]
    [Trait("Spec", "SPC-10")]
    [MemberData(nameof(Blake3Vectors))]
    public void Blake3_OfficialVector_MatchesTheReference(string vectorId)
    {
        Vector(vectorId);
        var vector = Blake3Cases[vectorId];
        var expected = (string)vector["hash"]!;
        var input = Input((int)vector["input_len"]!);

        Assert.Equal(expected, Convert.ToHexStringLower(Adapter.Blake3(input, expected.Length / 2)));
        Assert.Equal(expected[..64], Convert.ToHexStringLower(Adapter.Blake3(input, 32)));
    }

    /// <summary>Outputs past the official vectors' length match an independent implementation.</summary>
    [Theory]
    [Trait("Spec", "SPC-10")]
    [MemberData(nameof(LongOutputs))]
    public void Blake3_LongOutput_MatchesTheReference(string vectorId)
    {
        Vector(vectorId);
        var vector = LongOutputCases[vectorId];

        var output = Adapter.Blake3(Input((int)vector["inputLength"]!), (int)vector["outputLength"]!);

        Assert.Equal((string)vector["sha256"]!, Convert.ToHexStringLower(SHA256.HashData(output)));
    }

    /// <summary>Adding and removing elements reaches the reference digest (and state, where pinned).</summary>
    [Theory]
    [Trait("Spec", "SPC-8")]
    [MemberData(nameof(LtHashes))]
    public void LtHash_Operations_ReachTheReferenceState(string vectorId)
    {
        Vector(vectorId);
        var vector = LtHashCases[vectorId];

        var (state, digest) = Adapter.LtHash([.. vector["ops"]!.AsArray().Select(o => ((string)o!["op"]! == "add", (string)o["element"]!))]);

        Assert.Equal((string)vector["digest"]!, Convert.ToHexStringLower(digest));
        Assert.Equal(2048, state.Length);
        if (vector["state"] is { } expected)
            Assert.Equal((string)expected!, Convert.ToHexStringLower(state));
    }

    /// <summary>A credentialed request signature is accepted with its key, or refused.</summary>
    [Theory]
    [Trait("Spec", "SPC-22"), Trait("Spec", "SPC-24")]
    [MemberData(nameof(Signatures))]
    public void VerifySpaceSignature_Vector_MatchesExpectation(string vectorId)
    {
        Vector(vectorId);
        var vector = SignatureCases[vectorId];

        var key = Adapter.VerifySpaceSignature((string?)vector["authorization"], (string?)vector["audience"],
            (string?)vector["signatureInput"], (string?)vector["signature"], (string)vector["keyId"]!);

        Assert.Equal((bool)vector["expect"]!["ok"]! ? (string)vector["expect"]!["key"]! : null, key);
    }

    /// <summary>Signing produces the reference signature input and a P-256 signature over the reference base; other keys are refused.</summary>
    [Theory]
    [Trait("Spec", "SPC-22"), Trait("Spec", "SPC-24")]
    [MemberData(nameof(Signing))]
    public void SignSpaceRequest_Vector_SignsTheReferenceBase(string vectorId)
    {
        const string Label = "atproto-space-credential=:";
        Vector(vectorId);
        var vector = SigningCases[vectorId];
        var curve = (string)vector["curve"]!;

        var signed = Adapter.SignSpaceRequest(curve, (string)SignatureKeys[curve]!["privateKey"]!, (string)vector["authorization"]!, (string)vector["audience"]!);

        if (vector["signatureInput"] is not { } input)
        {
            Assert.Null(signed);
            return;
        }

        Assert.NotNull(signed);
        Assert.Equal((string)input!, signed.Value.SignatureInput);
        Assert.StartsWith(Label, signed.Value.Signature, StringComparison.Ordinal);
        var signature = Convert.FromBase64String(signed.Value.Signature[Label.Length..^1]);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ecdsa.ImportParameters(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = Convert.FromHexString((string)SignatureKeys[curve]!["privateKey"]!) });
        Assert.True(ecdsa.VerifyData(Encoding.UTF8.GetBytes((string)vector["base"]!), signature, HashAlgorithmName.SHA256), "The signature does not cover the reference base.");
    }

    /// <summary>A dictionary member serializes canonically, or the field is refused.</summary>
    [Theory]
    [Trait("Spec", "SPC-22")]
    [MemberData(nameof(Fields))]
    public void ReadStructuredField_Vector_MatchesExpectation(string vectorId)
    {
        Vector(vectorId);
        var vector = FieldCases[vectorId];
        var expect = vector["expect"]!;

        var member = Adapter.ReadStructuredField((string)vector["field"]!, (string)vector["key"]!);

        if (!(bool)expect["parsed"]!)
        {
            Assert.Null(member);
            return;
        }

        Assert.NotNull(member);
        Assert.Equal((string?)expect["member"], member.Member);
        if (expect["duplicated"] is { } duplicated)
            Assert.Equal((bool)duplicated!, member.Duplicated);
    }

    /// <summary>A space token is accepted or refused at its <c>now</c>, with the reference error where the implementation names one.</summary>
    [Theory]
    [Trait("Spec", "SPC-19")]
    [MemberData(nameof(Tokens))]
    public void VerifySpaceToken_Vector_MatchesExpectation(string vectorId)
    {
        Vector(vectorId);
        var vector = TokenCases[vectorId];
        var expect = vector["expect"]!;

        var verdict = Adapter.VerifySpaceToken((string)vector["type"]!, (string)vector["token"]!, (string)vector["signingKey"]!,
            (string?)vector["audience"], (string?)vector["subject"], DateTimeOffset.FromUnixTimeSeconds((long)vector["now"]!));

        Assert.True((bool)expect["ok"]! == verdict.Accepted, $"Expected {expect.ToJsonString()}, got {verdict}.");
        if (!verdict.Accepted && verdict.Error is not null)
            Assert.Equal((string)expect["error"]!, verdict.Error);
    }

    private static byte[] Input(int length) => [.. Enumerable.Range(0, length).Select(i => (byte)(i % 251))];
}
