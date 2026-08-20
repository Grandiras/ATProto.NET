using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

public class SpaceCommitTests
{
    private const string Space = "at://did:plc:ewvi7nxzyoun6zhxrhs64oiz/space/com.atmoboards.forum/default";
    private const string Author = "did:plc:z72i7hdynmk6r22z27h6tvur";
    private const string Rev = "3l6oveex3ii2l";

    private static byte[] CountingIkm()
    {
        var ikm = new byte[32];
        for (var i = 0; i < 32; i++)
            ikm[i] = (byte)i;
        return ikm;
    }

    // ── Context encoding ─────────────────────────────────────────

    [Fact]
    public void Encode_MatchesReferenceImplementation()
    {
        var context = new SpaceCommitContext(Space, Author, Rev);

        var encoded = context.Encode(CountingIkm());

        // Produced by the encodeCommitCtx in @atproto/space.
        Assert.Equal(
            "617470726f746f2d73706163652d76 31 0048 61743a2f2f6469643a706c633a65777669376e787a796f756e367a687872687336346f697a2f73706163652f636f6d2e61746d6f626f617264732e666f72756d2f64656661756c74 0020 6469643a706c633a7a373269376864796e6d6b367232327a3237683674767572 000d 336c366f76656578336969326c 0020 000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"
                .Replace(" ", ""),
            Convert.ToHexStringLower(encoded));
    }

    [Fact]
    public void Encode_LengthPrefixesAreBigEndian()
    {
        // The opposite byte order from the LtHash lanes, and the one thing most likely to be
        // silently "fixed" to little-endian by someone matching the surrounding code.
        var context = new SpaceCommitContext("at://did:plc:aaaaaaaaaaaaaaaaaaaaaaaa/space/a.b.c/x", Author, Rev);

        var encoded = context.Encode(CountingIkm());
        var tagLength = "atproto-space-v1".Length;

        Assert.Equal(0x00, encoded[tagLength]);
        Assert.Equal(context.Space.Length, encoded[tagLength + 1]);
    }

    [Fact]
    public void Encode_DiffersForEveryField()
    {
        var ikm = CountingIkm();
        var baseline = new SpaceCommitContext(Space, Author, Rev).Encode(ikm);

        Assert.NotEqual(baseline, new SpaceCommitContext(Space + "2", Author, Rev).Encode(ikm));
        Assert.NotEqual(baseline, new SpaceCommitContext(Space, Author + "2", Rev).Encode(ikm));
        Assert.NotEqual(baseline, new SpaceCommitContext(Space, Author, Rev + "2").Encode(ikm));
        Assert.NotEqual(baseline, new SpaceCommitContext(Space, Author, Rev).Encode(new byte[32]));
    }

    [Fact]
    public void Encode_IsUnambiguousAcrossFieldBoundaries()
    {
        // Without length prefixes, moving a character from one field to the next would produce
        // the same bytes. It must not.
        var ikm = CountingIkm();
        var left = new SpaceCommitContext("at://did:plc:aaaaaaaaaaaaaaaaaaaaaaaa/space/a.b.c/xy", Author, Rev);
        var right = new SpaceCommitContext("at://did:plc:aaaaaaaaaaaaaaaaaaaaaaaa/space/a.b.c/x", "y" + Author, Rev);

        Assert.NotEqual(left.Encode(ikm), right.Encode(ikm));
    }

    // ── MAC ──────────────────────────────────────────────────────

    [Fact]
    public void ComputeMac_MatchesReferenceImplementation()
    {
        var ikm = CountingIkm();
        var context = new SpaceCommitContext(Space, Author, Rev).Encode(ikm);
        var hash = new LtHash().Add(LtHashTests.SingleElement).Digest();

        var mac = SpaceCommitVerifier.ComputeMac(ikm, context, hash);

        Assert.Equal(
            "20c20805d3d29401f026b191e017fc59e64228a4d28016d9a3c554cd725e9d22",
            Convert.ToHexStringLower(mac));
    }

    // ── Sign / verify ────────────────────────────────────────────

    [Theory]
    [InlineData(KeyCurve.P256)]
    [InlineData(KeyCurve.K256)]
    public void Sign_ThenVerify_Succeeds(KeyCurve curve)
    {
        using var key = curve == KeyCurve.P256 ? AtProtoCrypto.GenerateP256Key() : AtProtoCrypto.GenerateK256Key();
        var repo = new SpaceRepoCommit().Add("com.example.note", "abc", TestCid);
        var context = new SpaceCommitContext(Space, Author, Rev);

        var commit = repo.Sign(context, key);

        Assert.True(SpaceCommitVerifier.Verify(commit, context, key.ToDidKey()));
        Assert.Equal(SignedSpaceCommit.CurrentVersion, commit.Ver);
        Assert.Equal(Rev, commit.Rev);
        Assert.Equal(32, commit.Hash.Length);
        Assert.Equal(32, commit.Ikm.Length);
        Assert.Equal(32, commit.Mac.Length);
        Assert.True(repo.Matches(commit));
    }

    [Fact]
    public void Sign_TwiceOverTheSameState_ProducesDifferentCommits()
    {
        // A fresh ikm per commit is what makes a leaked commit deniable; reusing one would
        // turn every commit into a stable fingerprint of the repo's state.
        using var key = AtProtoCrypto.GenerateP256Key();
        var repo = new SpaceRepoCommit().Add("com.example.note", "abc", TestCid);
        var context = new SpaceCommitContext(Space, Author, Rev);

        var first = repo.Sign(context, key);
        var second = repo.Sign(context, key);

        Assert.Equal(first.Hash, second.Hash);
        Assert.NotEqual(first.Ikm, second.Ikm);
        Assert.NotEqual(first.Sig, second.Sig);
        Assert.NotEqual(first.Mac, second.Mac);
        Assert.True(SpaceCommitVerifier.Verify(second, context, key.ToDidKey()));
    }

    [Fact]
    public void Verify_WithADifferentSpace_Fails()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var context = new SpaceCommitContext(Space, Author, Rev);
        var commit = new SpaceRepoCommit().Sign(context, key);

        var otherSpace = context with { Space = Space.Replace("default", "other") };

        Assert.False(SpaceCommitVerifier.Verify(commit, otherSpace, key.ToDidKey()));
    }

    [Fact]
    public void Verify_WithADifferentAuthor_Fails()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var context = new SpaceCommitContext(Space, Author, Rev);
        var commit = new SpaceRepoCommit().Sign(context, key);

        Assert.False(SpaceCommitVerifier.Verify(
            commit, context with { Author = "did:plc:ewvi7nxzyoun6zhxrhs64oiz" }, key.ToDidKey()));
    }

    [Fact]
    public void Verify_WithADifferentKey_Fails()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        using var other = AtProtoCrypto.GenerateP256Key();
        var context = new SpaceCommitContext(Space, Author, Rev);
        var commit = new SpaceRepoCommit().Sign(context, key);

        Assert.False(SpaceCommitVerifier.Verify(commit, context, other.ToDidKey()));
    }

    [Fact]
    public void Verify_WithATamperedHash_Fails()
    {
        // The signature does not cover the hash, so only the MAC catches this — which is the
        // whole point of the construction.
        using var key = AtProtoCrypto.GenerateP256Key();
        var context = new SpaceCommitContext(Space, Author, Rev);
        var commit = new SpaceRepoCommit().Add("com.example.note", "abc", TestCid).Sign(context, key);

        var tampered = new SignedSpaceCommit
        {
            Ver = commit.Ver,
            Hash = new SpaceRepoCommit().Digest(),
            Ikm = commit.Ikm,
            Sig = commit.Sig,
            Mac = commit.Mac,
            Rev = commit.Rev,
        };

        Assert.False(SpaceCommitVerifier.Verify(tampered, context, key.ToDidKey()));
    }

    [Fact]
    public void Verify_WithARevMismatchBetweenCommitAndContext_Fails()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var context = new SpaceCommitContext(Space, Author, Rev);
        var commit = new SpaceRepoCommit().Sign(context, key);

        Assert.False(SpaceCommitVerifier.Verify(commit, context with { Rev = "3l6oveex3ii2m" }, key.ToDidKey()));
    }

    [Fact]
    public void Verify_WithAnUnknownVersion_Fails()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var context = new SpaceCommitContext(Space, Author, Rev);
        var commit = new SpaceRepoCommit().Sign(context, key);

        var future = new SignedSpaceCommit
        {
            Ver = 2,
            Hash = commit.Hash,
            Ikm = commit.Ikm,
            Sig = commit.Sig,
            Mac = commit.Mac,
            Rev = commit.Rev,
        };

        Assert.False(SpaceCommitVerifier.Verify(future, context, key.ToDidKey()));
    }

    [Fact]
    public void Verify_WithAMalformedDidKey_ReturnsFalseRatherThanThrowing()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var context = new SpaceCommitContext(Space, Author, Rev);
        var commit = new SpaceRepoCommit().Sign(context, key);

        Assert.False(SpaceCommitVerifier.Verify(commit, context, "did:key:not-a-key"));
    }

    // ── Repo state ───────────────────────────────────────────────

    [Fact]
    public void ApplyOp_Create_AddsTheRecord()
    {
        var repo = new SpaceRepoCommit();

        repo.ApplyOp(new SpaceRepoOp("com.example.note", "abc", TestCid, Prev: null));

        Assert.Equal(new SpaceRepoCommit().Add("com.example.note", "abc", TestCid).Digest(), repo.Digest());
    }

    [Fact]
    public void ApplyOp_Update_ReplacesTheRecord()
    {
        var repo = new SpaceRepoCommit().Add("com.example.note", "abc", TestCid);

        repo.ApplyOp(new SpaceRepoOp("com.example.note", "abc", OtherCid, Prev: TestCid));

        Assert.Equal(new SpaceRepoCommit().Add("com.example.note", "abc", OtherCid).Digest(), repo.Digest());
    }

    [Fact]
    public void ApplyOp_Delete_RemovesTheRecord()
    {
        var repo = new SpaceRepoCommit().Add("com.example.note", "abc", TestCid);

        repo.ApplyOp(new SpaceRepoOp("com.example.note", "abc", Cid: null, Prev: TestCid));

        Assert.True(repo.SetHash.IsEmpty);
    }

    [Fact]
    public void ApplyOps_InAnyOrder_ConvergesOnTheSameState()
    {
        // Self-healing sync rests on this: a syncer that receives operations out of order, or
        // replays one it already applied the inverse of, still lands on the right digest.
        var ops = new[]
        {
            new SpaceRepoOp("com.example.note", "a", TestCid, null),
            new SpaceRepoOp("com.example.note", "b", OtherCid, null),
            new SpaceRepoOp("com.example.note", "a", OtherCid, TestCid),
        };

        var forwards = new SpaceRepoCommit().ApplyOps(ops);
        var backwards = new SpaceRepoCommit().ApplyOps(ops.Reverse());

        Assert.Equal(forwards.Digest(), backwards.Digest());
    }

    [Fact]
    public void FromIndex_MatchesFromRecords()
    {
        var records = new[]
        {
            ("com.example.note", "a", TestCid),
            ("com.example.other", "b", OtherCid),
        };
        var index = records.Select(r =>
            new KeyValuePair<string, string>($"{r.Item1}/{r.Item2}", r.Item3));

        Assert.Equal(
            SpaceRepoCommit.FromRecords(records).Digest(),
            SpaceRepoCommit.FromIndex(index).Digest());
    }

    [Fact]
    public void FromState_RestoresAPersistedRepo()
    {
        var original = new SpaceRepoCommit().Add("com.example.note", "abc", TestCid);

        var restored = SpaceRepoCommit.FromState(original.SetHash.GetState());

        Assert.Equal(original.Digest(), restored.Digest());
    }

    // ── Serialization ────────────────────────────────────────────

    [Fact]
    public void ToDagCbor_RoundTrips()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var context = new SpaceCommitContext(Space, Author, Rev);
        var commit = new SpaceRepoCommit().Add("com.example.note", "abc", TestCid).Sign(context, key);

        var decoded = SignedSpaceCommit.FromDagCbor(commit.ToDagCbor());

        Assert.Equal(commit.Ver, decoded.Ver);
        Assert.Equal(commit.Hash, decoded.Hash);
        Assert.Equal(commit.Ikm, decoded.Ikm);
        Assert.Equal(commit.Sig, decoded.Sig);
        Assert.Equal(commit.Mac, decoded.Mac);
        Assert.Equal(commit.Rev, decoded.Rev);
        Assert.True(SpaceCommitVerifier.Verify(decoded, context, key.ToDidKey()));
    }

    [Fact]
    public void ToDagCbor_SortsKeysLengthFirst()
    {
        // hash (4) sorts after the three-letter keys, which plain bytewise ordering would not do.
        using var key = AtProtoCrypto.GenerateP256Key();
        var commit = new SpaceRepoCommit().Sign(new SpaceCommitContext(Space, Author, Rev), key);

        var cbor = commit.ToDagCbor();
        var text = System.Text.Encoding.ASCII.GetString(cbor);

        Assert.True(text.IndexOf("ver", StringComparison.Ordinal) < text.IndexOf("hash", StringComparison.Ordinal));
    }

    [Fact]
    public void FromDagCbor_Garbage_ThrowsVerificationException()
    {
        Assert.Throws<SpaceRepoVerificationException>(
            () => SignedSpaceCommit.FromDagCbor(new byte[] { 0xff, 0xff, 0xff }));
    }

    [Fact]
    public void JsonRoundTrip_UsesTheLexiconBytesRepresentation()
    {
        using var key = AtProtoCrypto.GenerateP256Key();
        var commit = new SpaceRepoCommit().Sign(new SpaceCommitContext(Space, Author, Rev), key);

        var json = JsonSerializer.Serialize(commit);

        Assert.Contains("\"$bytes\"", json, StringComparison.Ordinal);
        var decoded = JsonSerializer.Deserialize<SignedSpaceCommit>(json)!;
        Assert.Equal(commit.Hash, decoded.Hash);
        Assert.Equal(commit.Sig, decoded.Sig);
    }

    private const string TestCid = "bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq";
    private const string OtherCid = "bafyreihrjacrc7vmmyiuxka7uio7ximst76ippf6xuqa6c256olh3mxojq";
}
