using System.Security.Cryptography;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Identity;
using ATProtoNet.TestKit;
using static ATProtoNet.Tests.Identity.IdentityFixtures;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// Keys out of a DID document: the verification method types, the strict lookups, and the Spaces KEM key.
/// </summary>
/// <remarks>
/// plc.directory publishes <c>Multikey</c>, but older PLC releases and hand-written <c>did:web</c> documents publish the legacy
/// <c>Ecdsa…VerificationKey2019</c> types, whose <c>publicKeyMultibase</c> is a bare point rather than a multicodec-tagged one.
/// Both must resolve to the same <c>did:key</c>.
/// </remarks>
public sealed class DidDocumentKeyTests
{
    private const string Did = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    private static readonly byte[] Message = "legacy verification method"u8.ToArray();

    // ── Verification method types ────────────────────────────

    [Fact]
    [Trait("Spec", "IDN-16")]
    public void ToDidKey_Multikey_PassesTheMultibaseValueThrough() =>
        Assert.Equal(TestKeys.P256DidKey, new VerificationMethod($"{Did}#atproto", "Multikey", Did, TestKeys.P256Multibase).ToDidKey());

    [Fact]
    [Trait("Spec", "IDN-16")]
    public void ToDidKey_LegacyP256_CompressesTheUncompressedPoint()
    {
        // Both parities of Y: a compression that ignored it would still produce a well-formed did:key, just for the wrong
        // point half the time.
        var parities = new HashSet<int>();
        for (var attempt = 0; attempt < 64 && parities.Count < 2; attempt++)
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            parities.Add(ecdsa.ExportParameters(false).Q.Y![^1] & 1);
            AssertLegacyKeyResolves(ecdsa, KeyCurve.P256, "EcdsaSecp256r1VerificationKey2019");
        }
        Assert.Equal(2, parities.Count);
    }

    [Fact]
    [Trait("Spec", "IDN-16")]
    public void ToDidKey_LegacyK256_CompressesTheUncompressedPoint()
    {
        using var ecdsa = ECDsa.Create(ECCurve.CreateFromValue("1.3.132.0.10"));

        AssertLegacyKeyResolves(ecdsa, KeyCurve.K256, "EcdsaSecp256k1VerificationKey2019");
    }

    [Theory]
    [Trait("Spec", "IDN-16")]
    [InlineData("EcdsaSecp256r1VerificationKey2019", "zxdM8dSstjrpZaRUwBmDvjGXweKuEMVN95A9oJBFjkWMh", "did:key:zDnaembgSGUhZULN2Caob4HLJPaxBh92N7rtH21TErzqf8HQo")]
    [InlineData("EcdsaSecp256k1VerificationKey2019", "z25z9DTpsiYYJKGsWmSPJK2NFN8PcJtZig12K59UgW7q5t", "did:key:zQ3shqwJEJyMBsBXCWyCBpUBMqxcon9oHB7mCvx4sSpMdLJwc")]
    public void ToDidKey_LegacyCompressedPoint_IsTaggedWithItsCurve(string type, string multibase, string expected) =>
        Assert.Equal(expected, new VerificationMethod($"{Did}#atproto", type, Did, multibase).ToDidKey());

    [Fact]
    [Trait("Spec", "IDN-16")]
    public void ToDidKey_UnknownType_ReturnsNull() =>
        Assert.Null(new VerificationMethod($"{Did}#atproto", "Ed25519VerificationKey2020", Did, TestKeys.P256Multibase).ToDidKey());

    [Theory]
    [Trait("Spec", "IDN-16")]
    [InlineData(null)]
    [InlineData("")]
    public void ToDidKey_WithoutKeyMaterial_ReturnsNull(string? multibase) =>
        Assert.Null(new VerificationMethod($"{Did}#atproto", "Multikey", Did, multibase).ToDidKey());

    public static TheoryData<string, string> MalformedKeys => new()
    {
        { "EcdsaSecp256k1VerificationKey2019", "z" + Base58Btc.Encode(new byte[40]) },
        { "EcdsaSecp256r1VerificationKey2019", "z" + Base58Btc.Encode([0x04, .. new byte[64]]) },
        { "EcdsaSecp256r1VerificationKey2019", "f04" + new string('0', 128) },
        { "EcdsaSecp256r1VerificationKey2019", "z0OIl" },
        { "Multikey", "zNotAKey" },
        { "Multikey", "uAAAA" },
    };

    [Theory]
    [Trait("Spec", "IDN-16")]
    [MemberData(nameof(MalformedKeys))]
    public void ToDidKey_MalformedKeyMaterial_ThrowsFormatException(string type, string multibase) =>
        Assert.Throws<FormatException>(() => new VerificationMethod($"{Did}#atproto", type, Did, multibase).ToDidKey());

    // ── Document lookups ─────────────────────────────────────

    [Fact]
    [Trait("Spec", "IDN-16")]
    public void GetSigningKey_ReadsTheLegacyAtprotoEntry()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var key = SigningKey.ImportRawPrivateScalar(ecdsa.ExportParameters(true).D, KeyCurve.P256);

        var document = Document(Method("#atproto", "EcdsaSecp256r1VerificationKey2019", Uncompressed(ecdsa)));

        Assert.Equal(key.PublicKey.DidKey, document.GetSigningKey());
    }

    [Theory]
    [Trait("Spec", "IDN-13")]
    [InlineData("#atproto")]
    [InlineData($"{Did}#atproto")]
    public void GetSigningKey_MatchesBareAndDidQualifiedIds(string id) =>
        Assert.Equal(TestKeys.P256DidKey, Document(Method(id, "Multikey", TestKeys.P256Multibase)).GetSigningKey());

    [Fact]
    [Trait("Spec", "IDN-13")]
    public void TryGetVerificationKey_AcceptsAFragmentWithoutItsHash()
    {
        var document = Document(Method("#atproto_label", "Multikey", TestKeys.K256Multibase));

        Assert.Equal(TestKeys.K256DidKey, document.TryGetVerificationKey("atproto_label").Value);
        Assert.Equal(DidDocumentEntryStatus.Absent, document.TryGetVerificationKey("#atproto").Status);
        Assert.Null(document.GetSigningKey());
    }

    [Theory]
    [Trait("Spec", "IDN-14")]
    [InlineData("Multikey", TestKeys.K256Multibase, DidDocumentEntryStatus.Found)]
    [InlineData("Multikey", "zNotAKey", DidDocumentEntryStatus.Malformed)]
    [InlineData("Multikey", null, DidDocumentEntryStatus.Malformed)]
    [InlineData("JsonWebKey2020", TestKeys.K256Multibase, DidDocumentEntryStatus.Malformed)]
    public void TryGetVerificationKey_TellsAMalformedEntryFromAnAbsentOne(string type, string? multibase, DidDocumentEntryStatus expected)
    {
        var document = Document(Method("#atproto_label", type, multibase));

        var found = document.TryGetVerificationKey(DidDocument.LabelKeyId);

        Assert.Equal(expected, found.Status);
        Assert.Equal(expected == DidDocumentEntryStatus.Found ? TestKeys.K256DidKey : null, found.Value);
        Assert.Equal(DidDocumentEntryStatus.Absent, document.TryGetVerificationKey("#atproto").Status);
    }

    [Fact]
    [Trait("Spec", "IDN-14")]
    [Trait("Spec", "IDN-16")]
    public void GetSigningKey_KeyMaterialThatDoesNotDecode_ReturnsNullRatherThanThrowing()
    {
        // The document is anyone's to publish: a broken key reads as no usable key, which every verifier refuses, rather
        // than an exception each caller has to catch.
        var document = Document(Method("#atproto", "EcdsaSecp256k1VerificationKey2019", "z" + Base58Btc.Encode(new byte[40])));

        Assert.Null(document.GetSigningKey());
        Assert.Equal(DidDocumentEntryStatus.Malformed, document.TryGetVerificationKey("#atproto").Status);
    }

    // ── The Spaces KEM key ───────────────────────────────────

    [Fact]
    [Trait("Spec", "IDN-58")]
    public void TryGetKemKey_WellFormedEntry_IsFound()
    {
        using var kem = KemPrivateKey.Generate(KemAlgorithm.Default);
        var document = Document(Method($"{Did}#atproto_kem", "Multikey", kem.PublicKey.Multikey), Method($"{Did}#atproto", "Multikey", TestKeys.K256Multibase));

        var found = document.TryGetKemKey();

        Assert.Equal(DidDocumentEntryStatus.Found, found.Status);
        Assert.Equal(kem.PublicKey, found.Value);
    }

    [Theory]
    [Trait("Spec", "IDN-58")]
    [InlineData("#atproto_kem", Did, "Multikey", TestKeys.P256Multibase)]
    [InlineData($"{Did}#atproto_kem", "did:plc:yk4dd2qkboz2yv6tpubpc6co", "Multikey", TestKeys.P256Multibase)]
    [InlineData($"{Did}#atproto_kem", null, "Multikey", TestKeys.P256Multibase)]
    [InlineData($"{Did}#atproto_kem", Did, "EcdsaSecp256r1VerificationKey2019", TestKeys.P256Multibase)]
    [InlineData($"{Did}#atproto_kem", Did, "JsonWebKey2020", TestKeys.P256Multibase)]
    [InlineData($"{Did}#atproto_kem", Did, "Multikey", TestKeys.K256Multibase)]
    [InlineData($"{Did}#atproto_kem", Did, "Multikey", "zNotAKey")]
    [InlineData($"{Did}#atproto_kem", Did, "Multikey", "")]
    [InlineData($"{Did}#atproto_kem", Did, "Multikey", null)]
    public void TryGetKemKey_EntryThatIsNotAP256MultikeyOfThisDid_IsMalformed(string id, string? controller, string type, string? multibase)
    {
        var document = Document(Method(id, type, multibase, controller), Method($"{Did}#atproto", "Multikey", TestKeys.P256Multibase));

        Assert.Equal(new DidDocumentEntry<KemPublicKey>(DidDocumentEntryStatus.Malformed, null), document.TryGetKemKey());
    }

    [Fact]
    [Trait("Spec", "IDN-13")]
    [Trait("Spec", "IDN-58")]
    public void TryGetKemKey_FirstEntryMalformed_DoesNotConsultALaterOne()
    {
        var document = Document(Method("#atproto_kem", "Multikey", TestKeys.P256Multibase), Method($"{Did}#atproto_kem", "Multikey", TestKeys.P256Multibase));

        Assert.Equal(DidDocumentEntryStatus.Malformed, document.TryGetKemKey().Status);
    }

    [Theory]
    [Trait("Spec", "IDN-58")]
    [InlineData($"{Did}#atproto")]
    [InlineData("did:plc:yk4dd2qkboz2yv6tpubpc6co#atproto_kem")]
    public void TryGetKemKey_NoKemEntry_IsAbsentNeverTheSigningKey(string id)
    {
        // A P-256 signing key would parse as a KEM key; it must not stand in for one.
        var document = Document(Method(id, "Multikey", TestKeys.P256Multibase), Method($"{Did}#atproto", "Multikey", TestKeys.P256Multibase));

        Assert.Equal(new DidDocumentEntry<KemPublicKey>(DidDocumentEntryStatus.Absent, null), document.TryGetKemKey());
        Assert.NotNull(document.GetSigningKey());
    }

    private static void AssertLegacyKeyResolves(ECDsa ecdsa, KeyCurve curve, string type)
    {
        using var key = SigningKey.ImportRawPrivateScalar(ecdsa.ExportParameters(true).D, curve);
        var method = new VerificationMethod($"{Did}#atproto", type, Did, Uncompressed(ecdsa));

        Assert.Equal(key.PublicKey.DidKey, method.ToDidKey());
        // And the resolved key really verifies what the private half signed.
        Assert.True(method.ToDidKey()!.ToVerificationKey().Verify(Message, key.Sign(Message)));
    }

    // The legacy form: base58btc over a bare uncompressed point, no multicodec prefix.
    private static string Uncompressed(ECDsa ecdsa)
    {
        var q = ecdsa.ExportParameters(false).Q;
        return "z" + Base58Btc.Encode([0x04, .. q.X!, .. q.Y!]);
    }

    private static Dictionary<string, string?> Method(string id, string type, string? multibase, string? controller = Did) =>
        new() { ["id"] = id, ["type"] = type, ["controller"] = controller, ["publicKeyMultibase"] = multibase };

    // Absent fields are left out rather than written as null, which the parser refuses.
    private static DidDocument Document(params Dictionary<string, string?>[] methods) => Parse(JsonSerializer.Serialize(new
    {
        id = Did,
        verificationMethod = methods.Select(m => m.Where(p => p.Value is not null).ToDictionary()),
    }));
}
