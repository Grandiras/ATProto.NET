using System.Security.Cryptography;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// Tests for reading signing keys out of a DID document — <see cref="VerificationMethod.ToDidKey"/>,
/// <see cref="DidDocument.GetSigningKey"/> and the <see cref="SpaceAuthority"/> lookup over them.
/// </summary>
/// <remarks>
/// plc.directory publishes <c>Multikey</c>, but older PLC releases and hand-written
/// <c>did:web</c> documents publish the legacy <c>Ecdsa...VerificationKey2019</c> types, whose
/// <c>publicKeyMultibase</c> is a bare uncompressed point rather than a multicodec-tagged
/// compressed one. Both must resolve to the same <c>did:key</c>.
/// </remarks>
public sealed class DidDocumentKeyTests
{
    private const string Did = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";

    // ── Verification method types ────────────────────────────

    [Fact]
    public void ToDidKey_Multikey_PassesTheMultibaseValueThrough()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var method = new VerificationMethod
        {
            Id = $"{Did}#atproto",
            Type = "Multikey",
            PublicKeyMultibase = key.ToMultikey(),
        };

        Assert.Equal(key.ToDidKey(), method.ToDidKey());
    }

    [Fact]
    public void ToDidKey_LegacyP256_CompressesTheUncompressedPoint()
    {
        // Both parities of Y — a compression that ignored it would still produce a
        // well-formed did:key, just for the wrong point half the time.
        var seenParities = new HashSet<int>();

        for (var attempt = 0; attempt < 32 && seenParities.Count < 2; attempt++)
        {
            var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var key = new AtProtoKey(ecdsa, KeyCurve.P256);
            var q = ecdsa.ExportParameters(false).Q;
            seenParities.Add(q.Y![^1] & 1);

            var method = LegacyMethod("EcdsaSecp256r1VerificationKey2019", q);

            Assert.Equal(key.ToDidKey(), method.ToDidKey());

            // And the resolved key really verifies what the private half signed.
            var message = "legacy verification method"u8.ToArray();
            Assert.True(AtProtoCrypto.VerifySignature(method.ToDidKey()!, message, key.Sign(message)));
        }

        Assert.Equal(2, seenParities.Count);
    }

    [Fact]
    public void ToDidKey_LegacyK256_CompressesTheUncompressedPoint()
    {
        ECDsa ecdsa;
        try
        {
            ecdsa = ECDsa.Create(ECCurve.CreateFromValue("1.3.132.0.10"));
        }
        catch (PlatformNotSupportedException)
        {
            return; // secp256k1 unavailable on this platform — the P-256 case covers the logic.
        }

        using var key = new AtProtoKey(ecdsa, KeyCurve.K256);
        var method = LegacyMethod("EcdsaSecp256k1VerificationKey2019", ecdsa.ExportParameters(false).Q);

        Assert.Equal(key.ToDidKey(), method.ToDidKey());

        var message = "legacy k256 verification method"u8.ToArray();
        Assert.True(AtProtoCrypto.VerifySignature(method.ToDidKey()!, message, key.Sign(message)));
    }

    [Fact]
    public void ToDidKey_UnknownType_ReturnsNull()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var method = new VerificationMethod
        {
            Id = $"{Did}#atproto",
            Type = "Ed25519VerificationKey2020",
            PublicKeyMultibase = key.ToMultikey(),
        };

        Assert.Null(method.ToDidKey());
    }

    [Fact]
    public void ToDidKey_WithoutKeyMaterial_ReturnsNull()
    {
        Assert.Null(new VerificationMethod { Id = $"{Did}#atproto", Type = "Multikey" }.ToDidKey());
    }

    [Fact]
    public void ToDidKey_MalformedLegacyKeyMaterial_Throws()
    {
        var method = new VerificationMethod
        {
            Id = $"{Did}#atproto",
            Type = "EcdsaSecp256k1VerificationKey2019",
            PublicKeyMultibase = "z" + AtProtoCrypto.Base58Encode(new byte[40]),
        };

        Assert.Throws<FormatException>(() => method.ToDidKey());
    }

    // ── Document lookup ──────────────────────────────────────

    [Fact]
    public void GetSigningKey_ReadsTheLegacyAtprotoEntry()
    {
        var (document, expected) = LegacyDocument("#atproto");

        Assert.Equal(expected, document.GetSigningKey());
    }

    [Fact]
    public void GetVerificationKey_MatchesBareAndDidQualifiedIds()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var bare = new DidDocument
        {
            Id = Did,
            VerificationMethod = [new() { Id = "#atproto", Type = "Multikey", PublicKeyMultibase = key.ToMultikey() }],
        };
        var qualified = new DidDocument
        {
            Id = Did,
            VerificationMethod = [new() { Id = $"{Did}#atproto", Type = "Multikey", PublicKeyMultibase = key.ToMultikey() }],
        };

        Assert.Equal(key.ToDidKey(), bare.GetSigningKey());
        Assert.Equal(key.ToDidKey(), qualified.GetSigningKey());
    }

    [Fact]
    public void GetVerificationKey_AcceptsAFragmentWithoutItsHash()
    {
        var (document, expected) = LegacyDocument("#atproto_space");

        Assert.Equal(expected, document.GetVerificationKey("atproto_space"));
    }

    [Fact]
    public void GetVerificationKey_ReturnsNullWhenTheEntryIsAbsent()
    {
        var (document, _) = LegacyDocument("#atproto");

        Assert.Null(document.GetVerificationKey("#atproto_space"));
    }

    // ── Space authority lookup ───────────────────────────────

    [Fact]
    public void SpaceAuthorityGetSigningKey_FallsBackToALegacyAtprotoEntry()
    {
        var (document, expected) = LegacyDocument("#atproto");

        Assert.Equal(expected, SpaceAuthority.GetSigningKey(document));
    }

    [Fact]
    public void SpaceAuthorityGetSigningKey_PrefersTheSpaceEntry()
    {
        var (document, spaceKey) = LegacyDocument("#atproto_space");
        using var accountKey = AtProtoCrypto.GenerateP256Key();
        document.VerificationMethod.Add(new()
        {
            Id = $"{Did}#atproto",
            Type = "Multikey",
            PublicKeyMultibase = accountKey.ToMultikey(),
        });

        Assert.Equal(spaceKey, SpaceAuthority.GetSigningKey(document));
    }

    private static VerificationMethod LegacyMethod(string type, ECPoint q) => new()
    {
        Id = $"{Did}#atproto",
        Type = type,
        // The legacy form: base58btc over a bare uncompressed point, no multicodec prefix.
        PublicKeyMultibase = "z" + AtProtoCrypto.Base58Encode([0x04, .. q.X!, .. q.Y!]),
    };

    /// <summary>A document publishing one legacy P-256 entry, with the did:key it should resolve to.</summary>
    private static (DidDocument Document, string DidKey) LegacyDocument(string fragment)
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var key = new AtProtoKey(ecdsa, KeyCurve.P256);

        var method = LegacyMethod("EcdsaSecp256r1VerificationKey2019", ecdsa.ExportParameters(false).Q);
        method.Id = $"{Did}{fragment}";

        return (new DidDocument { Id = Did, VerificationMethod = [method] }, key.ToDidKey());
    }
}
