using System.Numerics;
using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// Label signatures and <c>subscribeLabels</c> frames against the reference implementations
/// (<c>labels/label-vectors.json</c>): indigo's vector, Bluesky's moderation labels, Ozone's
/// <c>signLabel</c> output, issuer documents, and <c>@atproto/common</c>'s frame encoding.
/// </summary>
public abstract class LabelsConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    private const string File = "labels/label-vectors.json";

    private static readonly JsonNode Keys = Vectors.Json(File)["keys"]!;
    private static readonly IReadOnlyDictionary<string, JsonNode> SignatureCases = Vectors.Cases(File, "signatures");
    private static readonly IReadOnlyDictionary<string, JsonNode> IssuerCases = Vectors.Cases(File, "issuers");
    private static readonly IReadOnlyDictionary<string, JsonNode> SigningCases = Vectors.Cases(File, "signing");
    private static readonly IReadOnlyDictionary<string, JsonNode> FrameCases = Vectors.Cases(File, "frames");

    /// <summary>The signature vectors, by ID.</summary>
    public static TheoryData<string> Signatures() => new(SignatureCases.Keys);

    /// <summary>The issuer-document vectors, by ID.</summary>
    public static TheoryData<string> Issuers() => new(IssuerCases.Keys);

    /// <summary>The signing vectors, by ID.</summary>
    public static TheoryData<string> Signing() => new(SigningCases.Keys);

    /// <summary>The frame vectors, by ID.</summary>
    public static TheoryData<string> Frames() => new(FrameCases.Keys);

    /// <summary>A label's signed bytes match the reference encoding, and its signature gets the expected verdict.</summary>
    [Theory]
    [Trait("Spec", "LBL-16")]
    [MemberData(nameof(Signatures))]
    public void VerifyLabel_ReferenceVector_MatchesExpectation(string vectorId)
    {
        Vector(vectorId);
        var vector = SignatureCases[vectorId];
        var label = vector["label"]!.ToJsonString();

        if (vector["signingBytes"] is { } bytes)
            Assert.Equal((string)bytes!, Convert.ToHexStringLower(Adapter.LabelSigningBytes(label)));
        Assert.Equal(Status(vector), Adapter.VerifyLabel(label, (string)vector["key"]!));
    }

    /// <summary>A label verifies against its issuer's <c>#atproto_label</c> key, refetching the document once.</summary>
    [Theory]
    [Trait("Spec", "LBL-12")]
    [MemberData(nameof(Issuers))]
    public void VerifyLabelAgainstIssuer_Vector_MatchesExpectation(string vectorId)
    {
        Vector(vectorId);
        var vector = IssuerCases[vectorId];

        var status = Adapter.VerifyLabelAgainstIssuer(
            vector["label"]!.ToJsonString(), vector["document"]?.ToJsonString(), vector["freshDocument"]?.ToJsonString());

        Assert.Equal(Status(vector), status);
    }

    /// <summary>Signing with a reference key signs the reference bytes, low-S, and the result verifies.</summary>
    [Theory]
    [Trait("Spec", "LBL-5")]
    [MemberData(nameof(Signing))]
    public void SignLabel_ReferenceKey_SignsTheReferenceBytes(string vectorId)
    {
        Vector(vectorId);
        var vector = SigningCases[vectorId];
        var key = Keys[(string)vector["key"]!]!;

        var signed = Adapter.SignLabel(vector["label"]!.ToJsonString(), (string)key["curve"]!, (string)key["privateKey"]!);

        Assert.Equal((string)vector["signingBytes"]!, Convert.ToHexStringLower(Adapter.LabelSigningBytes(signed)));
        Assert.Equal(LabelStatus.Valid, Adapter.VerifyLabel(signed, (string)key["didKey"]!));
        var sig = Convert.FromBase64String(Padded((string)JsonNode.Parse(signed)!["sig"]!["$bytes"]!));
        Assert.Equal(64, sig.Length);
        Assert.True(new BigInteger(sig.AsSpan(32), isUnsigned: true, isBigEndian: true) <= HalfOrder((string)key["curve"]!), "The signature is not low-S.");
    }

    /// <summary>A frame encodes to the reference bytes, or is refused.</summary>
    [Theory]
    [Trait("Spec", "LBL-17")]
    [MemberData(nameof(Frames))]
    public void EncodeLabelFrame_Vector_MatchesTheReferenceBytes(string vectorId)
    {
        Vector(vectorId);
        var vector = FrameCases[vectorId];

        var frame = Adapter.EncodeLabelFrame(vector["message"]!.ToJsonString());

        Assert.Equal((string?)vector["frame"], frame is null ? null : Convert.ToHexStringLower(frame));
    }

    private static LabelStatus Status(JsonNode vector) => Enum.Parse<LabelStatus>((string)vector["expect"]!, ignoreCase: true);

    private static string Padded(string base64) => base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');

    private static BigInteger HalfOrder(string curve) => BigInteger.Parse(curve == "P-256"
        ? "0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551"
        : "0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141", System.Globalization.NumberStyles.HexNumber) / 2;
}
