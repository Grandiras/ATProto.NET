using System.Formats.Cbor;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tests.Streaming;

public class FirehoseVerifierTests
{
    [Fact]
    public void VerifyCid_CommitWithNullBlocks_ReturnsFailure()
    {
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = null,
        };

        var result = FirehoseVerifier.VerifyCid(commit);
        Assert.False(result.IsValid);
        Assert.Contains("no blocks", result.Error);
    }

    [Fact]
    public void VerifyCid_CommitWithEmptyBlocks_ReturnsFailure()
    {
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = Array.Empty<byte>(),
        };

        var result = FirehoseVerifier.VerifyCid(commit);
        Assert.False(result.IsValid);
        Assert.Contains("no blocks", result.Error);
    }

    [Fact]
    public void VerifyCid_SyncEventWithNullBlocks_ReturnsFailure()
    {
        var syncEvent = new SyncEvent
        {
            Did = "did:plc:test",
            Rev = "abc123",
            Blocks = null,
        };

        var result = FirehoseVerifier.VerifyCid(syncEvent);
        Assert.False(result.IsValid);
        Assert.Contains("no blocks", result.Error);
    }

    [Fact]
    public void VerifyCid_CommitWithMalformedBlocks_ReturnsFailure()
    {
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = new byte[] { 0xFF, 0xFF, 0xFF },
        };

        var result = FirehoseVerifier.VerifyCid(commit);
        Assert.False(result.IsValid);
        Assert.Contains("error", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyCid_NullCommit_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FirehoseVerifier.VerifyCid((CommitEvent)null!));
    }

    [Fact]
    public void VerifyCid_NullSyncEvent_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FirehoseVerifier.VerifyCid((SyncEvent)null!));
    }

    [Fact]
    public void VerificationResult_Success_IsValid()
    {
        // We can test via the static VerifyCid which conditionally returns success
        // Just verify the ToString behavior on failure results
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = null,
        };

        var result = FirehoseVerifier.VerifyCid(commit);
        Assert.Contains("Invalid:", result.ToString());
    }

    [Fact]
    public async Task VerifySignature_NullCommit_ThrowsArgumentNullException()
    {
        using var verifier = new FirehoseVerifier();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            verifier.VerifySignatureAsync(null!));
    }

    [Fact]
    public async Task VerifySignature_NoBlocks_ReturnsFailure()
    {
        using var verifier = new FirehoseVerifier();
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = null,
        };

        var result = await verifier.VerifySignatureAsync(commit);
        Assert.False(result.IsValid);
        Assert.Contains("no blocks", result.Error);
    }

    [Fact]
    public async Task VerifySignature_EmptyBlocks_ReturnsFailure()
    {
        using var verifier = new FirehoseVerifier();
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = Array.Empty<byte>(),
        };

        var result = await verifier.VerifySignatureAsync(commit);
        Assert.False(result.IsValid);
        Assert.Contains("no blocks", result.Error);
    }

    [Fact]
    public async Task VerifySignature_MalformedBlocks_ReturnsFailure()
    {
        using var verifier = new FirehoseVerifier();
        var commit = new CommitEvent
        {
            Repo = "did:plc:test",
            Commit = "bafyreiabc",
            Rev = "abc123",
            Blocks = new byte[] { 0xFF, 0xFF, 0xFF },
        };

        var result = await verifier.VerifySignatureAsync(commit);
        Assert.False(result.IsValid);
        Assert.Contains("error", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var verifier = new FirehoseVerifier();
        verifier.Dispose();
        verifier.Dispose(); // Should not throw
    }

    // ── WriteMapHeader boundary coverage ──────────────────────
    //
    // Every commit-signature verification builds a fresh map header for
    // entryCount-1 via WriteMapHeader; getting the encoding wrong at a
    // 24/256/65536 boundary would produce a header whose entry count doesn't
    // match the spliced bytes, hashing to garbage. CBOR major type 5 (map):
    //   0..23   → 0xa0 + n               (1 byte)
    //   24..255 → 0xb8 0xNN              (2 bytes)
    //   256..65535 → 0xb9 0xNN 0xNN      (3 bytes)
    //   >=65536 → 0xba 0xNN 0xNN 0xNN 0xNN (5 bytes, big-endian uint32)

    [Theory]
    [InlineData(0, new byte[] { 0xa0 })]
    [InlineData(1, new byte[] { 0xa1 })]
    [InlineData(23, new byte[] { 0xb7 })]
    [InlineData(24, new byte[] { 0xb8, 24 })]
    [InlineData(255, new byte[] { 0xb8, 0xFF })]
    [InlineData(256, new byte[] { 0xb9, 0x01, 0x00 })]
    [InlineData(65535, new byte[] { 0xb9, 0xFF, 0xFF })]
    [InlineData(65536, new byte[] { 0xba, 0x00, 0x01, 0x00, 0x00 })]
    [InlineData(0x10203040, new byte[] { 0xba, 0x10, 0x20, 0x30, 0x40 })]
    public void WriteMapHeader_EncodesCanonicalLengthForBoundary(int count, byte[] expected)
    {
        using var ms = new MemoryStream();
        FirehoseVerifier.WriteMapHeader(ms, count);
        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public void WriteMapHeader_NegativeCount_Throws()
    {
        using var ms = new MemoryStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => FirehoseVerifier.WriteMapHeader(ms, -1));
    }

    // ── ExtractSignedView correctness ─────────────────────────
    //
    // Build a synthetic CBOR map and assert (a) the spliced bytes can be
    // re-parsed as a smaller map containing the same non-sig keys, and (b)
    // sigBytes equals what we put in. Validates the boundary that the entry
    // loop now runs inside the wider try/catch — malformed inputs return null
    // instead of throwing.

    [Fact]
    public void ExtractSignedView_SimpleMap_StripsSigPreservesOthers()
    {
        // Build {did: "did:plc:abc", sig: bytes(0xAB, 0xCD), rev: "tid"} — 3 entries.
        var writer = new CborWriter(CborConformanceMode.Strict);
        writer.WriteStartMap(3);
        writer.WriteTextString("did");
        writer.WriteTextString("did:plc:abc");
        writer.WriteTextString("sig");
        var expectedSig = new byte[] { 0xAB, 0xCD };
        writer.WriteByteString(expectedSig);
        writer.WriteTextString("rev");
        writer.WriteTextString("tid");
        writer.WriteEndMap();
        var commit = writer.Encode();

        var result = FirehoseVerifier.ExtractSignedView(commit);

        Assert.NotNull(result);
        Assert.Equal(expectedSig, result!.Value.SigBytes);

        // Re-parse the unsigned bytes: should be a 2-entry map with did + rev.
        var reader = new CborReader(result.Value.UnsignedBytes, CborConformanceMode.Strict);
        Assert.Equal(2, reader.ReadStartMap());
        Assert.Equal("did", reader.ReadTextString());
        Assert.Equal("did:plc:abc", reader.ReadTextString());
        Assert.Equal("rev", reader.ReadTextString());
        Assert.Equal("tid", reader.ReadTextString());
        reader.ReadEndMap();
    }

    [Fact]
    public void ExtractSignedView_NoSigField_ReturnsNull()
    {
        var writer = new CborWriter(CborConformanceMode.Strict);
        writer.WriteStartMap(1);
        writer.WriteTextString("did");
        writer.WriteTextString("did:plc:abc");
        writer.WriteEndMap();
        var commit = writer.Encode();

        Assert.Null(FirehoseVerifier.ExtractSignedView(commit));
    }

    [Fact]
    public void ExtractSignedView_EmptyInput_ReturnsNull()
    {
        Assert.Null(FirehoseVerifier.ExtractSignedView(Array.Empty<byte>()));
    }

    [Fact]
    public void ExtractSignedView_TruncatedMap_ReturnsNullNotThrow()
    {
        // Map header says 3 entries but only one is encoded — would throw
        // mid-loop without the widened try/catch.
        var writer = new CborWriter(CborConformanceMode.Strict);
        writer.WriteStartMap(1);
        writer.WriteTextString("did");
        writer.WriteTextString("did:plc:abc");
        writer.WriteEndMap();
        var partial = writer.Encode();
        // Hand-edit the header byte to claim 3 entries instead of 1.
        partial[0] = 0xa3;

        // Must NOT throw.
        var result = FirehoseVerifier.ExtractSignedView(partial);
        Assert.Null(result);
    }

    [Fact]
    public void ExtractSignedView_SigEncodedAsTextString_ReturnsNull()
    {
        // Hostile commit: sig is a text string instead of a byte string. We
        // peek the next type before ReadByteString and return null rather
        // than letting the framework throw a generic CborContentException.
        var writer = new CborWriter(CborConformanceMode.Strict);
        writer.WriteStartMap(1);
        writer.WriteTextString("sig");
        writer.WriteTextString("not bytes");
        writer.WriteEndMap();
        var commit = writer.Encode();

        Assert.Null(FirehoseVerifier.ExtractSignedView(commit));
    }

    [Fact]
    public void ExtractSignedView_LargeMap_CrossesByteHeaderBoundary()
    {
        // Build a 25-entry map (one over the 1-byte threshold) so the resulting
        // 24-entry stripped map exercises the 2-byte WriteMapHeader branch.
        var writer = new CborWriter(CborConformanceMode.Strict);
        writer.WriteStartMap(25);
        writer.WriteTextString("sig");
        writer.WriteByteString(new byte[] { 1, 2, 3 });
        for (var i = 0; i < 24; i++)
        {
            writer.WriteTextString($"k{i:D2}");
            writer.WriteInt32(i);
        }
        writer.WriteEndMap();
        var commit = writer.Encode();

        var result = FirehoseVerifier.ExtractSignedView(commit);
        Assert.NotNull(result);

        var reader = new CborReader(result!.Value.UnsignedBytes, CborConformanceMode.Strict);
        Assert.Equal(24, reader.ReadStartMap());
        for (var i = 0; i < 24; i++)
        {
            Assert.Equal($"k{i:D2}", reader.ReadTextString());
            Assert.Equal(i, reader.ReadInt32());
        }
        reader.ReadEndMap();
    }
}
