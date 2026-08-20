using System.Security.Cryptography;
using ATProtoNet.Crypto;

namespace ATProtoNet.Tests.Crypto;

/// <summary>Tests for <see cref="AtProtoCrypto"/> and <see cref="AtProtoKey"/>.</summary>
public sealed class AtProtoCryptoTests
{
    // ── P-256 Key Generation ─────────────────────────────────

    [Fact]
    public void GenerateP256Key_CreatesValidKey()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        Assert.Equal(KeyCurve.P256, key.Curve);
    }

    [Fact]
    public void GenerateP256Key_ProducesCompressedPublicKey()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var compressed = key.GetCompressedPublicKey();
        Assert.Equal(33, compressed.Length);
        Assert.True(compressed[0] is 0x02 or 0x03);
    }

    [Fact]
    public void GenerateP256Key_UniqueKeysEachTime()
    {
        using var key1 = AtProtoCrypto.GenerateP256Key();
        using var key2 = AtProtoCrypto.GenerateP256Key();
        Assert.NotEqual(key1.GetCompressedPublicKey(), key2.GetCompressedPublicKey());
    }

    // ── Signing and Verification ─────────────────────────────

    [Fact]
    public void P256_SignAndVerify_RoundTrips()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var message = "Hello, AT Protocol!"u8.ToArray();

        var signature = key.Sign(message);

        Assert.NotEmpty(signature);
        Assert.True(key.Verify(message, signature));
    }

    [Fact]
    public void P256_Verify_RejectsTamperedMessage()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var message = "Original message"u8.ToArray();
        var signature = key.Sign(message);

        var tamperedMessage = "Tampered message"u8.ToArray();
        Assert.False(key.Verify(tamperedMessage, signature));
    }

    [Fact]
    public void P256_Verify_RejectsTamperedSignature()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var message = "Test message"u8.ToArray();
        var signature = key.Sign(message);

        // Flip a bit in the signature
        var tampered = (byte[])signature.Clone();
        tampered[5] ^= 0xFF;
        Assert.False(key.Verify(message, tampered));
    }

    [Fact]
    public void P256_Verify_RejectsWrongKey()
    {
        using var key1 = AtProtoCrypto.GenerateP256Key();
        using var key2 = AtProtoCrypto.GenerateP256Key();

        var message = "Test message"u8.ToArray();
        var signature = key1.Sign(message);

        Assert.False(key2.Verify(message, signature));
    }

    // ── Multikey / did:key Encoding ──────────────────────────

    [Fact]
    public void P256_ToDidKey_ProducesCorrectFormat()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var didKey = key.ToDidKey();

        Assert.StartsWith("did:key:z", didKey);
    }

    [Fact]
    public void P256_ToMultikey_ProducesCorrectPrefix()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var multikey = key.ToMultikey();

        Assert.StartsWith("z", multikey);
    }

    [Fact]
    public void P256_DidKey_RoundTrips()
    {
        using var original = AtProtoCrypto.GenerateP256Key();
        var didKey = original.ToDidKey();

        using var parsed = AtProtoCrypto.FromDidKey(didKey);

        Assert.Equal(KeyCurve.P256, parsed.Curve);
        Assert.Equal(original.GetCompressedPublicKey(), parsed.GetCompressedPublicKey());
    }

    [Fact]
    public void P256_Multikey_RoundTrips()
    {
        using var original = AtProtoCrypto.GenerateP256Key();
        var multikey = original.ToMultikey();

        using var parsed = AtProtoCrypto.FromMultikey(multikey);

        Assert.Equal(KeyCurve.P256, parsed.Curve);
        Assert.Equal(original.GetCompressedPublicKey(), parsed.GetCompressedPublicKey());
    }

    [Fact]
    public void VerifySignature_StaticHelper_Works()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var didKey = key.ToDidKey();
        var message = "Hello"u8.ToArray();
        var signature = key.Sign(message);

        Assert.True(AtProtoCrypto.VerifySignature(didKey, message, signature));
    }

    // ── Key Import/Export ────────────────────────────────────

    [Fact]
    public void P256_ExportImportPrivateKey_Preserves()
    {
        using var original = AtProtoCrypto.GenerateP256Key();
        var exported = original.ExportPrivateKey();

        using var imported = AtProtoCrypto.ImportPrivateKey(exported, KeyCurve.P256);

        var message = "Export test"u8.ToArray();
        var signature = original.Sign(message);

        Assert.True(imported.Verify(message, signature));
    }

    [Fact]
    public void P256_ImportCompressedPublicKey_CanVerify()
    {
        using var signer = AtProtoCrypto.GenerateP256Key();
        var compressed = signer.GetCompressedPublicKey();

        using var verifier = AtProtoCrypto.ImportCompressedPublicKey(compressed, KeyCurve.P256);

        var message = "Verify test"u8.ToArray();
        var signature = signer.Sign(message);

        Assert.True(verifier.Verify(message, signature));
    }

    // ── Error Cases ──────────────────────────────────────────

    [Fact]
    public void FromDidKey_RejectsInvalidPrefix()
    {
        Assert.Throws<FormatException>(() => AtProtoCrypto.FromDidKey("did:web:example.com"));
    }

    [Fact]
    public void FromMultikey_RejectsEmptyString()
    {
        Assert.Throws<FormatException>(() => AtProtoCrypto.FromMultikey(""));
    }

    [Fact]
    public void FromMultikey_RejectsWrongPrefix()
    {
        Assert.Throws<FormatException>(() => AtProtoCrypto.FromMultikey("abc"));
    }

    [Fact]
    public void ImportCompressedPublicKey_RejectsWrongLength()
    {
        Assert.Throws<ArgumentException>(() =>
            AtProtoCrypto.ImportCompressedPublicKey(new byte[32], KeyCurve.P256));
    }

    [Fact]
    public void Sign_ThrowsWhenDisposed()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        key.Dispose();

        Assert.Throws<ObjectDisposedException>(() => key.Sign("test"u8.ToArray()));
    }

    [Fact]
    public void Verify_ThrowsWhenDisposed()
    {
        var key = AtProtoCrypto.GenerateP256Key();
        key.Dispose();

        Assert.Throws<ObjectDisposedException>(() => key.Verify("test"u8, new byte[64]));
    }

    // ── Base58 ───────────────────────────────────────────────

    [Fact]
    public void Base58_RoundTrips()
    {
        var original = new byte[] { 0x00, 0x01, 0x42, 0xFF };
        var encoded = AtProtoCrypto.Base58Encode(original);
        var decoded = AtProtoCrypto.Base58Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Base58_LeadingZeros_Preserved()
    {
        var data = new byte[] { 0x00, 0x00, 0x01 };
        var encoded = AtProtoCrypto.Base58Encode(data);

        Assert.StartsWith("11", encoded); // Leading zeros become '1's
        var decoded = AtProtoCrypto.Base58Decode(encoded);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void Base58Decode_RejectsInvalidCharacters()
    {
        Assert.Throws<FormatException>(() => AtProtoCrypto.Base58Decode("invalid0OIl"));
    }

    // ── Point Compression / did:key Formatting ───────────────

    [Fact]
    public void CompressPublicKey_PassesThroughAlreadyCompressedPoint()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var compressed = key.GetCompressedPublicKey();

        Assert.Equal(compressed, AtProtoCrypto.CompressPublicKey(compressed));
    }

    [Fact]
    public void CompressPublicKey_RejectsUnknownPointEncoding()
    {
        Assert.Throws<FormatException>(() => AtProtoCrypto.CompressPublicKey(new byte[64]));
        Assert.Throws<FormatException>(() => AtProtoCrypto.CompressPublicKey(new byte[65]));
        Assert.Throws<FormatException>(() => AtProtoCrypto.CompressPublicKey([]));
    }

    [Fact]
    public void FormatDidKey_UncompressedPoint_MatchesTheKeysOwnDidKey()
    {
        // Both parities of Y, since compression encodes exactly that bit.
        var seenParities = new HashSet<int>();

        for (var attempt = 0; attempt < 32 && seenParities.Count < 2; attempt++)
        {
            var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var key = new AtProtoKey(ecdsa, KeyCurve.P256);

            var q = ecdsa.ExportParameters(false).Q;
            seenParities.Add(q.Y![^1] & 1);
            byte[] uncompressed = [0x04, .. q.X!, .. q.Y!];

            Assert.Equal(key.ToDidKey(), AtProtoCrypto.FormatDidKey(uncompressed, KeyCurve.P256));
        }

        Assert.Equal(2, seenParities.Count);
    }

    [Fact]
    public void FormatDidKey_CompressedPoint_MatchesTheKeysOwnDidKey()
    {
        using var key = AtProtoCrypto.GenerateP256Key();

        Assert.Equal(
            key.ToDidKey(),
            AtProtoCrypto.FormatDidKey(key.GetCompressedPublicKey(), KeyCurve.P256));
    }

    [Fact]
    public void MultibaseToBytes_DecodesBase58btc()
    {
        var bytes = "multibase round trip"u8.ToArray();
        var encoded = "z" + AtProtoCrypto.Base58Encode(bytes);

        Assert.Equal(bytes, AtProtoCrypto.MultibaseToBytes(encoded));
    }

    [Fact]
    public void MultibaseToBytes_RejectsUnsupportedPrefix()
    {
        Assert.Throws<FormatException>(() => AtProtoCrypto.MultibaseToBytes("mZm9v"));
        Assert.Throws<FormatException>(() => AtProtoCrypto.MultibaseToBytes(""));
    }

    // ── K-256 (if platform supports it) ──────────────────────

    [Fact]
    public void K256_GenerateAndSign_IfSupported()
    {
        try
        {
            using var key = AtProtoCrypto.GenerateK256Key();
            Assert.Equal(KeyCurve.K256, key.Curve);

            var message = "K-256 test"u8.ToArray();
            var signature = key.Sign(message);
            Assert.True(key.Verify(message, signature));

            var didKey = key.ToDidKey();
            Assert.StartsWith("did:key:z", didKey);

            // Round-trip
            using var parsed = AtProtoCrypto.FromDidKey(didKey);
            Assert.Equal(KeyCurve.K256, parsed.Curve);
            Assert.True(parsed.Verify(message, signature));
        }
        catch (PlatformNotSupportedException)
        {
            // K-256 not available on this platform — skip
        }
    }

    // ── Security: Low-S Normalization ────────────────────────

    [Fact]
    public void P256_Sign_ProducesLowSSignature()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var message = "Low-S test"u8.ToArray();

        // Sign multiple times and verify all produce low-S
        for (var i = 0; i < 20; i++)
        {
            var signature = key.Sign(message);
            Assert.True(AtProtoCrypto.IsLowS(signature, KeyCurve.P256),
                $"Signature {i} should be in low-S form.");
        }
    }

    [Fact]
    public void P256_Verify_RejectsHighSSignature()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var message = "High-S test"u8.ToArray();
        var signature = key.Sign(message);

        // Craft a high-S version: S' = order - S
        var halfLen = signature.Length / 2;
        var order = new System.Numerics.BigInteger(
            new byte[] {
                0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xBC, 0xE6, 0xFA, 0xAD, 0xA7, 0x17, 0x9E, 0x84,
                0xF3, 0xB9, 0xCA, 0xC2, 0xFC, 0x63, 0x25, 0x51
            }, true, true);

        var sValue = new System.Numerics.BigInteger(
            signature.AsSpan(halfLen).ToArray(), true, true);
        var highS = order - sValue;
        var highSBytes = highS.ToByteArray(true, true);

        var highSSig = (byte[])signature.Clone();
        Array.Clear(highSSig, halfLen, halfLen);
        highSBytes.CopyTo(highSSig, halfLen + (halfLen - highSBytes.Length));

        // The high-S signature should be rejected
        Assert.False(key.Verify(message, highSSig));
    }

    // ── Security: ImportPrivateKey Curve Validation ──────────

    [Fact]
    public void ImportPrivateKey_RejectsCurveMismatch()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var exported = key.ExportPrivateKey();

        // Try to import P-256 key as K-256 — should throw
        Assert.Throws<ArgumentException>(() =>
            AtProtoCrypto.ImportPrivateKey(exported, KeyCurve.K256));
    }

    [Fact]
    public void ImportPrivateKey_AcceptsCorrectCurve()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var exported = key.ExportPrivateKey();

        using var reimported = AtProtoCrypto.ImportPrivateKey(exported, KeyCurve.P256);
        Assert.Equal(KeyCurve.P256, reimported.Curve);

        // Verify signing still works
        var message = "Reimport test"u8.ToArray();
        var sig = reimported.Sign(message);
        Assert.True(reimported.Verify(message, sig));
    }
}
