using System.Security.Cryptography;
using System.Text.Json;
using ATProtoNet.Crypto;

namespace ATProtoNet.Tests.Crypto;

/// <summary>
/// <see cref="AtProtoCrypto"/> against the atproto interop crypto fixtures
/// (<c>Crypto/TestData</c>, vendored from bluesky-social/atproto-interop-tests).
/// </summary>
public sealed class AtProtoInteropCryptoTests
{
    public sealed record SignatureFixture(
        string Comment,
        string MessageBase64,
        string Algorithm,
        string PublicKeyDid,
        string PublicKeyMultibase,
        string SignatureBase64,
        bool ValidSignature,
        string[] Tags);

    private static readonly SignatureFixture[] s_signatureFixtures =
        ReadFixture<SignatureFixture[]>("signature-fixtures.json");

    // Keyed by comment, which is unique per fixture and reads well as the case name.
    public static TheoryData<string> SignatureFixtures() => new(s_signatureFixtures.Select(f => f.Comment));

    public static TheoryData<string, string> P256DidKeys()
    {
        var data = new TheoryData<string, string>();
        foreach (var entry in ReadFixture<JsonElement[]>("w3c_didkey_P256.json"))
        {
            var privateKey = AtProtoCrypto.Base58Decode(entry.GetProperty("privateKeyBytesBase58").GetString()!);
            data.Add(Convert.ToHexStringLower(privateKey), entry.GetProperty("publicDidKey").GetString()!);
        }
        return data;
    }

    public static TheoryData<string, string> K256DidKeys()
    {
        var data = new TheoryData<string, string>();
        foreach (var entry in ReadFixture<JsonElement[]>("w3c_didkey_K256.json"))
            data.Add(entry.GetProperty("privateKeyBytesHex").GetString()!, entry.GetProperty("publicDidKey").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(SignatureFixtures))]
    public void VerifySignature_InteropFixture_MatchesExpectedValidity(string comment)
    {
        var fixture = s_signatureFixtures.Single(f => f.Comment == comment);
        var message = FromUnpaddedBase64(fixture.MessageBase64);
        var signature = FromUnpaddedBase64(fixture.SignatureBase64);

        Assert.Equal(fixture.ValidSignature, AtProtoCrypto.VerifySignature(fixture.PublicKeyDid, message, signature));

        // Twice: the second call is served from the parsed-key cache.
        Assert.Equal(fixture.ValidSignature, AtProtoCrypto.VerifySignature(fixture.PublicKeyDid, message, signature));

        using var key = AtProtoCrypto.FromDidKey(fixture.PublicKeyDid);
        Assert.Equal(fixture.ValidSignature, key.Verify(message, signature));
    }

    [Theory]
    [MemberData(nameof(SignatureFixtures))]
    public void MultibaseToBytes_LegacyVerificationKey_IsTheSameKeyAsTheDidKey(string comment)
    {
        var fixture = s_signatureFixtures.Single(f => f.Comment == comment);

        // publicKeyMultibase is the legacy DID-document form: the bare compressed point,
        // with the curve named by the verification suite instead of a multicodec prefix.
        var curve = fixture.Algorithm == "ES256" ? KeyCurve.P256 : KeyCurve.K256;
        var compressed = AtProtoCrypto.MultibaseToBytes(fixture.PublicKeyMultibase);

        Assert.Equal(fixture.PublicKeyDid, AtProtoCrypto.FormatDidKey(compressed, curve));

        using var key = AtProtoCrypto.ImportCompressedPublicKey(compressed, curve);
        Assert.Equal(
            fixture.ValidSignature,
            key.Verify(FromUnpaddedBase64(fixture.MessageBase64), FromUnpaddedBase64(fixture.SignatureBase64)));
    }

    [Fact]
    public void SignatureFixtures_CoverHighSAndDerCases()
    {
        // Guards the vendored file: these are the cases the theories above exist for.
        var tags = s_signatureFixtures.SelectMany(f => f.Tags).ToHashSet();

        Assert.Contains("high-s", tags);
        Assert.Contains("der-encoded", tags);
    }

    [Theory]
    [MemberData(nameof(P256DidKeys))]
    public void ToDidKey_P256InteropFixture_MatchesPublishedDidKey(string privateKeyHex, string expectedDidKey)
        => AssertDidKeyFixture(KeyCurve.P256, ECCurve.NamedCurves.nistP256, privateKeyHex, expectedDidKey);

    [Theory]
    [MemberData(nameof(K256DidKeys))]
    public void ToDidKey_K256InteropFixture_MatchesPublishedDidKey(string privateKeyHex, string expectedDidKey)
        => AssertDidKeyFixture(KeyCurve.K256, ECCurve.CreateFromValue("1.3.132.0.10"), privateKeyHex, expectedDidKey);

    private static void AssertDidKeyFixture(KeyCurve curve, ECCurve ecCurve, string privateKeyHex, string expectedDidKey)
    {
        ECDsa ecdsa;
        try
        {
            ecdsa = ECDsa.Create(new ECParameters { Curve = ecCurve, D = Convert.FromHexString(privateKeyHex) });
        }
        catch (PlatformNotSupportedException ex)
        {
            Assert.Skip($"{curve} is not supported on this platform: {ex.Message}");
            return;
        }

        using var privateKey = new AtProtoKey(ecdsa, curve);

        Assert.Equal(expectedDidKey, privateKey.ToDidKey());

        using var parsed = AtProtoCrypto.FromDidKey(expectedDidKey);
        Assert.Equal(curve, parsed.Curve);
        Assert.Equal(privateKey.GetCompressedPublicKey(), parsed.GetCompressedPublicKey());

        var message = "interop"u8.ToArray();
        Assert.True(AtProtoCrypto.VerifySignature(expectedDidKey, message, privateKey.Sign(message)));
    }

    private static T ReadFixture<T>(string fileName)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Crypto", "TestData", fileName));
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static byte[] FromUnpaddedBase64(string value)
        => Convert.FromBase64String(value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '='));
}
