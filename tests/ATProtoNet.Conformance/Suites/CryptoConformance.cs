using System.Numerics;
using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// Signatures and <c>did:key</c>s: the atproto-interop-tests <c>crypto/</c> fixtures and the
/// scalar-range cases of <c>crypto/signature-scalar-range.json</c>.
/// </summary>
public abstract class CryptoConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    /// <summary>One row per signature fixture, keyed by its comment (or name).</summary>
    public static TheoryData<string> Signatures(string file) =>
        new(Vectors.Json($"crypto/{file}").AsArray().Select(f => $"crypto/{file}#{f!["comment"] ?? f["name"]}"));

    /// <summary>A fixture verifies exactly when the file says it is valid.</summary>
    [Theory]
    [MemberData(nameof(Signatures), "signature-fixtures.json")]
    [MemberData(nameof(Signatures), "signature-scalar-range.json")]
    [Trait("Spec", "CRY-10"), Trait("Spec", "CRY-11"), Trait("Spec", "CRY-13"), Trait("Spec", "CRY-14"), Trait("Spec", "CRY-15")]
    public void VerifySignature_Fixture_MatchesItsValidity(string vectorId)
    {
        Vector(vectorId);
        var (file, name) = (vectorId[..vectorId.IndexOf('#')], vectorId[(vectorId.IndexOf('#') + 1)..]);
        var f = Vectors.Json(file).AsArray().Single(f => (string?)(f!["comment"] ?? f["name"]) == name)!;

        var valid = Adapter.VerifySignature((string)f["publicKeyDid"]!, Base64((string)f["messageBase64"]!), Base64((string)f["signatureBase64"]!));

        Assert.Equal((bool)f["validSignature"]!, valid);
    }

    /// <summary>One row per W3C did:key fixture: curve, private key, did:key.</summary>
    public static TheoryData<string, SigningCurve, string, string> DidKeys()
    {
        var data = new TheoryData<string, SigningCurve, string, string>();
        foreach (var (file, curve) in new[] { ("w3c_didkey_P256.json", SigningCurve.P256), ("w3c_didkey_K256.json", SigningCurve.K256) })
        {
            var entries = Vectors.Json($"crypto/{file}").AsArray();
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i]!;
                var key = (string?)e["privateKeyBytesHex"] ?? Convert.ToHexStringLower(Base58((string)e["privateKeyBytesBase58"]!));
                data.Add($"crypto/{file}#{i}", curve, key, (string)e["publicDidKey"]!);
            }
        }
        return data;
    }

    /// <summary>A private key's public key formats as the published did:key.</summary>
    [Theory]
    [MemberData(nameof(DidKeys))]
    [Trait("Spec", "CRY-20"), Trait("Spec", "CRY-21"), Trait("Spec", "CRY-22")]
    public void DidKeyForPrivateKey_W3cFixture_MatchesPublishedDidKey(string vectorId, SigningCurve curve, string privateKeyHex, string didKey)
    {
        Vector(vectorId);
        Assert.Equal(didKey, Adapter.DidKeyForPrivateKey(curve, Convert.FromHexString(privateKeyHex)));
    }

    private static byte[] Base64(string unpadded) => Convert.FromBase64String(unpadded.PadRight(unpadded.Length + (4 - unpadded.Length % 4) % 4, '='));

    private static byte[] Base58(string text)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var value = text.Aggregate(BigInteger.Zero, (v, c) => v * 58 + alphabet.IndexOf(c));
        return [.. Enumerable.Repeat((byte)0, text.TakeWhile(c => c == '1').Count()), .. value.IsZero ? [] : value.ToByteArray(isUnsigned: true, isBigEndian: true)];
    }
}
