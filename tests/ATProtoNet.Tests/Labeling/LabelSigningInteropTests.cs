using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Labeling;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Labeling;

/// <summary>
/// Label signatures against the reference implementations: indigo's <c>atproto/labeling</c> test
/// vector, labels served by Bluesky's moderation service, and labels signed by
/// <c>@atproto/ozone</c>'s <c>signLabel</c> (with <c>@atproto/common</c>'s <c>cborEncode</c> and
/// <c>@atproto/crypto</c>).
/// </summary>
/// <remarks>
/// ECDSA signatures are randomized here and deterministic (RFC 6979) in the references, so a
/// signature this SDK makes cannot be pinned byte for byte. What is pinned is what both sides must
/// agree on: the signed bytes, and each side's signatures verifying on the other.
/// </remarks>
public sealed class LabelSigningInteropTests
{
    // indigo atproto/labeling/label_test.go, TestVerifyLabel.
    private const string IndigoKey = "did:key:zQ3shcnfWLQN1bY4d2patsEAYFzy4xp1zdckEvHsV7S4ocTnC";

    private const string IndigoLabel = """
        {
            "ver": 1,
            "src": "did:plc:n3timvoib5nau7gvwd6cshap",
            "uri": "did:plc:44ybard66vv44zksje25o7dz",
            "val": "bladerunner",
            "cts": "2024-10-23T17:51:19.128Z",
            "sig": { "$bytes": "uCRNA5mTzh078T5xZtkvLEt/O+z0gsKM3aqRI/lVAB8ZtbMnznwS/JwHopZE40JhNNDj80z8gsDLAp/hWqG5Pg" }
        }
        """;

    // @atproto/common cborEncode of the indigo label without its sig.
    private const string IndigoSigningBytes =
        "a5636374737818323032342d31302d32335431373a35313a31392e3132385a6373726378206469643a706c633a6e3374696d766f696235" +
        "6e617537677677643663736861706375726978206469643a706c633a343479626172643636767634347a6b736a6532356f37647a637661" +
        "6c6b626c61646572756e6e65726376657201";

    // Bluesky's moderation service: queryLabels on mod.bsky.app, verified against the
    // #atproto_label key did:plc:ar7c4by46qjdydhdevvrndac's document publishes.
    internal const string BlueskyModerationDid = "did:plc:ar7c4by46qjdydhdevvrndac";

    internal const string BlueskyModerationLabelKey = "did:key:zQ3shmV1BNcX17coaDbfen6zArEad6SCLT3jVWCbC6Y9iinTa";

    internal const string BlueskyModerationDocument = """
        {
            "@context": ["https://www.w3.org/ns/did/v1", "https://w3id.org/security/multikey/v1", "https://w3id.org/security/suites/secp256k1-2019/v1"],
            "id": "did:plc:ar7c4by46qjdydhdevvrndac",
            "alsoKnownAs": ["at://moderation.bsky.app"],
            "verificationMethod": [
                {
                    "id": "did:plc:ar7c4by46qjdydhdevvrndac#atproto",
                    "type": "Multikey",
                    "controller": "did:plc:ar7c4by46qjdydhdevvrndac",
                    "publicKeyMultibase": "zQ3shoG4QW9B3zvKSSiRwwc1De7MFQLNBT9A71gr12GKwMgHu"
                },
                {
                    "id": "did:plc:ar7c4by46qjdydhdevvrndac#atproto_label",
                    "type": "Multikey",
                    "controller": "did:plc:ar7c4by46qjdydhdevvrndac",
                    "publicKeyMultibase": "zQ3shmV1BNcX17coaDbfen6zArEad6SCLT3jVWCbC6Y9iinTa"
                }
            ],
            "service": [
                { "id": "#atproto_pds", "type": "AtprotoPersonalDataServer", "serviceEndpoint": "https://inkcap.us-east.host.bsky.network" },
                { "id": "#atproto_labeler", "type": "AtprotoLabeler", "serviceEndpoint": "https://mod.bsky.app" }
            ]
        }
        """;

    internal const string BlueskyModerationLabel = """
        {"ver":1,"src":"did:plc:ar7c4by46qjdydhdevvrndac","uri":"did:plc:qx7s5vdotumqa4h52ejojeby","val":"!takedown","cts":"2025-03-13T13:23:33.592Z","sig":{"$bytes":"Rc8wJ5NUsP3jx6idbILYW549QE+4ym8p0nfdWxDDbR1bZgf1tqO5MiBKxkFz//hqdbaJrujVUkLby+IejB/LiQ"}}
        """;

    internal const string BlueskyModerationNegation = """
        {"ver":1,"src":"did:plc:ar7c4by46qjdydhdevvrndac","uri":"did:plc:jhp75pzoruahzoiyyjujcv63","val":"!takedown","neg":true,"cts":"2025-03-13T13:54:41.618Z","sig":{"$bytes":"yOzNIKIe/zY5OKCySwjAIO9cQfQmWUc1cX4jxTh7KQI7TZWS9uIoVJzaMn4omMiTvPHBAgEPGGxMXIBF9bpMMA"}}
        """;

    private const string BlueskyModerationNegationSigningBytes =
        "a6636374737818323032352d30332d31335431333a35343a34312e3631385a636e6567f56373726378206469643a706c633a6172376334" +
        "62793436716a6479646864657676726e6461636375726978206469643a706c633a6a68703735707a6f727561687a6f6979796a756a6376" +
        "36336376616c692174616b65646f776e6376657201";

    // Keys the @atproto/ozone vectors were signed with, and every optional field set.
    private const string K256PrivateKey = "9085d2bef69286a6cbb51623c8fa258629945cd55ca705cc4e66700396894e0c";
    private const string K256DidKey = "did:key:zQ3shokFTS3brHcDQrn82RUDfCZESWL1ZdCEJwekUDPQiYBme";
    private const string P256PrivateKey = "d8e0c7a1f6c1b1f1c0e4d7b3a2f9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b3a291";
    private const string P256DidKey = "did:key:zDnaepfg2wiYzJMG65JaGtFztRv8CBpcerNEoNsWe5WbgcvrG";

    private const string OzoneLabeler = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    private const string OzoneSigningBytes =
        "a863636964783b626166797265696678796b71686564373273323663723469363472787672746f666571726c79336a34766a7a626b766f" +
        "33636b6b6a62786a717471636374737818323032342d31302d32335431373a35313a31392e3132385a636578707818323032352d30372d" +
        "32385432333a35333a31392e3830345a636e6567f56373726378206469643a706c633a65777669376e787a796f756e367a687872687336" +
        "346f697a63757269784661743a2f2f6469643a706c633a65777669376e787a796f756e367a687872687336346f697a2f6170702e62736b" +
        "792e666565642e706f73742f336c366f76656578336969326c6376616c647370616d6376657201";

    private const string OzoneK256Signature =
        "B6And1ZqjHses9lTKJIUIgVHkjuBeo8JovlaVCpFANVcjjY4MJGOrJL500jHCSXoL56ToOfK2+a9TqVZ0nr4Rg";

    private const string OzoneP256Signature =
        "5+BoFN8IR8YyD7qsRx2eWDEabbLXS51kjuzfOk0JYgQ53liXV+93QNmcGDwdTebo6kZBpldd8NAgj9UuIk+rbQ";

    // signLabel with neg: false, which ozone drops before encoding.
    private const string OzoneNotNegatedSigningBytes =
        "a5636374737818323032342d31302d32335431373a35313a31392e3132385a6373726378206469643a706c633a65777669376e787a796f" +
        "756e367a687872687336346f697a6375726978206469643a706c633a343479626172643636767634347a6b736a6532356f37647a637661" +
        "6c64676f6f646376657201";

    private const string OzoneNotNegatedK256Signature =
        "bXk5FxYpB22bpBZA0DbF7wlZ+gF0rn7I3nauaNkl6V55CmewyPLQ2NpAIxa1FWnuuygFc6IQzOaWl84BbY1Q4w";

    private const string OzoneNotNegatedP256Signature =
        "KWqP19ovQgMZNQMRX9bhnDdSjRqV06u69XGhXl18Y4E2shvFSs2JkAv7yp+F4qJ5sVxis6JC+EQYund9ssQxfw";

    internal static Label ParseLabel(string json) =>
        JsonSerializer.Deserialize<Label>(json, AtProtoJsonDefaults.Options)!;

    private static Label OzoneLabel(byte[]? signature = null) => new()
    {
        Version = 1,
        Src = Did.Parse(OzoneLabeler),
        Uri = "at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/app.bsky.feed.post/3l6oveex3ii2l",
        Cid = Cid.Parse("bafyreifxykqhed72s26cr4i64rxvrtofeqrly3j4vjzbkvo3ckkjbxjqtq"),
        Val = "spam",
        Neg = true,
        Cts = AtDatetime.Parse("2024-10-23T17:51:19.128Z"),
        Exp = AtDatetime.Parse("2025-07-28T23:53:19.804Z"),
        Sig = signature,
    };

    private static Label OzoneNotNegatedLabel(byte[]? signature = null, bool? neg = null) => new()
    {
        Version = 1,
        Src = Did.Parse(OzoneLabeler),
        Uri = "did:plc:44ybard66vv44zksje25o7dz",
        Val = "good",
        Neg = neg,
        Cts = AtDatetime.Parse("2024-10-23T17:51:19.128Z"),
        Sig = signature,
    };

    // ── The signed bytes ───────────────────────────────────────

    [Fact]
    public void GetSigningBytes_IndigoVector_MatchesTheReferenceEncoding()
        => Assert.Equal(IndigoSigningBytes, Convert.ToHexStringLower(LabelSigning.GetSigningBytes(ParseLabel(IndigoLabel))));

    [Fact]
    public void GetSigningBytes_NegatedLabel_MatchesTheReferenceEncoding()
        => Assert.Equal(
            BlueskyModerationNegationSigningBytes,
            Convert.ToHexStringLower(LabelSigning.GetSigningBytes(ParseLabel(BlueskyModerationNegation))));

    [Fact]
    public void GetSigningBytes_EveryOptionalField_MatchesTheReferenceEncoding()
        => Assert.Equal(OzoneSigningBytes, Convert.ToHexStringLower(LabelSigning.GetSigningBytes(OzoneLabel())));

    [Fact]
    public void GetSigningBytes_IgnoresTheSignatureAndUndeclaredFields()
    {
        var label = ParseLabel(IndigoLabel.Replace("\"ver\": 1,", "\"ver\": 1, \"$type\": \"com.atproto.label.defs#label\", \"extra\": 7,"));

        Assert.NotNull(label.ExtensionData);
        Assert.Equal(IndigoSigningBytes, Convert.ToHexStringLower(LabelSigning.GetSigningBytes(label)));
    }

    [Fact]
    public void GetSigningBytes_ExplicitNegFalse_IsEncodedAsCarried()
    {
        // indigo encodes a present neg: false; only ozone's signer drops it before signing.
        var bytes = Convert.ToHexStringLower(LabelSigning.GetSigningBytes(OzoneNotNegatedLabel(neg: false)));

        Assert.Contains("636e6567f4", bytes, StringComparison.Ordinal);
        Assert.NotEqual(OzoneNotNegatedSigningBytes, bytes);
    }

    // ── Verifying reference signatures ─────────────────────────

    [Fact]
    public void Verify_IndigoVector_IsValid()
        => Assert.Equal(LabelVerificationStatus.Valid, LabelSigning.Verify(ParseLabel(IndigoLabel), IndigoKey));

    [Fact]
    public void Verify_IndigoVectorWithAChangedValue_IsInvalid()
    {
        var label = ParseLabel(IndigoLabel.Replace("bladerunner", "wrong"));

        Assert.Equal(LabelVerificationStatus.InvalidSignature, LabelSigning.Verify(label, IndigoKey));
    }

    [Theory]
    [InlineData(BlueskyModerationLabel)]
    [InlineData(BlueskyModerationNegation)]
    public void Verify_BlueskyModerationLabel_IsValid(string json)
        => Assert.Equal(LabelVerificationStatus.Valid, LabelSigning.Verify(ParseLabel(json), BlueskyModerationLabelKey));

    [Fact]
    public void Verify_BlueskyModerationLabel_AgainstTheAccountKey_IsInvalid()
    {
        // The labeler's #atproto key is a different key: a label must verify against #atproto_label.
        const string accountKey = "did:key:zQ3shoG4QW9B3zvKSSiRwwc1De7MFQLNBT9A71gr12GKwMgHu";

        Assert.Equal(
            LabelVerificationStatus.InvalidSignature,
            LabelSigning.Verify(ParseLabel(BlueskyModerationLabel), accountKey));
    }

    [Theory]
    [InlineData(K256DidKey, OzoneK256Signature)]
    [InlineData(P256DidKey, OzoneP256Signature)]
    public void Verify_OzoneSignedLabel_IsValid(string didKey, string signature)
        => Assert.Equal(LabelVerificationStatus.Valid, LabelSigning.Verify(OzoneLabel(FromBase64(signature)), didKey));

    [Theory]
    [InlineData(K256DidKey, OzoneNotNegatedK256Signature)]
    [InlineData(P256DidKey, OzoneNotNegatedP256Signature)]
    public void Verify_OzoneSignedLabelWithoutNeg_IsValid(string didKey, string signature)
        => Assert.Equal(
            LabelVerificationStatus.Valid,
            LabelSigning.Verify(OzoneNotNegatedLabel(FromBase64(signature)), didKey));

    [Theory]
    [InlineData(KeyCurve.K256, K256DidKey, OzoneK256Signature)]
    [InlineData(KeyCurve.P256, P256DidKey, OzoneP256Signature)]
    public void Verify_HighSFormOfAValidSignature_IsInvalid(KeyCurve curve, string didKey, string signature)
    {
        var highS = ToHighS(FromBase64(signature), curve);

        Assert.Equal(LabelVerificationStatus.InvalidSignature, LabelSigning.Verify(OzoneLabel(highS), didKey));
    }

    [Fact]
    public void Verify_NoOrEmptySignature_ReportsUnsigned()
    {
        Assert.Equal(LabelVerificationStatus.Unsigned, LabelSigning.Verify(OzoneLabel(), K256DidKey));
        Assert.Equal(LabelVerificationStatus.Unsigned, LabelSigning.Verify(OzoneLabel([]), K256DidKey));
    }

    [Theory]
    [InlineData("\"ver\": 1,", "")]
    [InlineData("\"ver\": 1,", "\"ver\": 2,")]
    [InlineData("\"ver\": 1,", "\"ver\": 0,")]
    public void Verify_VersionOtherThanOne_ReportsUnsupportedVersion(string from, string to)
    {
        var label = ParseLabel(IndigoLabel.Replace(from, to));

        Assert.Equal(LabelVerificationStatus.UnsupportedVersion, LabelSigning.Verify(label, IndigoKey));
    }

    [Fact]
    public void Verify_MalformedDidKey_Throws()
        => Assert.Throws<FormatException>(() => LabelSigning.Verify(ParseLabel(IndigoLabel), "did:key:zNotAKey"));

    // ── Signing, checked against the reference bytes ───────────

    [Theory]
    [InlineData(KeyCurve.K256, K256PrivateKey, K256DidKey)]
    [InlineData(KeyCurve.P256, P256PrivateKey, P256DidKey)]
    public void Sign_ReferenceKey_SignsTheReferenceBytesWithALowSSignature(KeyCurve curve, string privateKey, string didKey)
    {
        using var key = ImportKey(curve, privateKey);
        Assert.Equal(didKey, key.ToDidKey());
        var signer = new LabelSigner(Did.Parse(OzoneLabeler), key);

        var signed = signer.Sign(OzoneLabel());

        Assert.Equal(OzoneSigningBytes, Convert.ToHexStringLower(LabelSigning.GetSigningBytes(signed)));
        Assert.True(AtProtoCrypto.IsLowS(signed.Sig, curve));
        Assert.Equal(LabelVerificationStatus.Valid, LabelSigning.Verify(signed, didKey));
    }

    [Theory]
    [InlineData(KeyCurve.K256, K256PrivateKey)]
    [InlineData(KeyCurve.P256, P256PrivateKey)]
    public void Sign_NegFalse_DropsItAsTheReferenceSignerDoes(KeyCurve curve, string privateKey)
    {
        using var key = ImportKey(curve, privateKey);
        var signer = new LabelSigner(Did.Parse(OzoneLabeler), key);

        var signed = signer.Sign(OzoneNotNegatedLabel(neg: false));

        Assert.Null(signed.Neg);
        Assert.Equal(OzoneNotNegatedSigningBytes, Convert.ToHexStringLower(LabelSigning.GetSigningBytes(signed)));
    }

    [Fact]
    public void Sign_SignedLabel_SurvivesAJsonRoundTrip()
    {
        using var key = ImportKey(KeyCurve.P256, P256PrivateKey);
        var signed = new LabelSigner(Did.Parse(OzoneLabeler), key).Sign(OzoneLabel());

        var json = JsonSerializer.Serialize(signed, AtProtoJsonDefaults.Options);
        var parsed = ParseLabel(json);

        Assert.Contains("\"sig\":{\"$bytes\":", json, StringComparison.Ordinal);
        Assert.Equal(LabelVerificationStatus.Valid, LabelSigning.Verify(parsed, P256DidKey));
    }

    internal static AtProtoKey ImportKey(KeyCurve curve, string privateKeyHex)
    {
        var ecCurve = curve == KeyCurve.P256 ? ECCurve.NamedCurves.nistP256 : ECCurve.CreateFromValue("1.3.132.0.10");
        try
        {
            return new AtProtoKey(
                ECDsa.Create(new ECParameters { Curve = ecCurve, D = Convert.FromHexString(privateKeyHex) }),
                curve);
        }
        catch (PlatformNotSupportedException ex)
        {
            Assert.Skip($"{curve} is not supported on this platform: {ex.Message}");
            throw;
        }
    }

    private static byte[] FromBase64(string value)
        => Convert.FromBase64String(value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '='));

    /// <summary>The other valid form of an ECDSA signature: <c>s</c> replaced by <c>n − s</c>.</summary>
    private static byte[] ToHighS(byte[] signature, KeyCurve curve)
    {
        var order = BigInteger.Parse(
            curve == KeyCurve.P256
                ? "0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551"
                : "0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141",
            System.Globalization.NumberStyles.HexNumber);

        var s = new BigInteger(signature.AsSpan(32), isUnsigned: true, isBigEndian: true);
        var highS = (order - s).ToByteArray(isUnsigned: true, isBigEndian: true);

        var result = (byte[])signature.Clone();
        Array.Clear(result, 32, 32);
        highS.CopyTo(result, 64 - highS.Length);
        return result;
    }
}
