using System.Numerics;
using ATProtoNet.Crypto;
using ATProtoNet.Data;
using ATProtoNet.Repo;
using static ATProtoNet.Tests.Repo.RepoFixtures;

namespace ATProtoNet.Tests.Repo;

public class SignedCommitTests
{
    private static readonly Tid Rev = Tid.Parse("3ku2ipumwvw2a");

    private static RepoCommit Commit(Cid? prev = null) => new() { Did = Account, Rev = Rev, Data = Fake("mst-root"), Prev = prev };

    // The unsigned commit as the reference encodes it, built by hand.
    private static byte[] Unsigned(Cid? prev = null) =>
        Map(("did", Text(Account.Value)), ("rev", Text(Rev.Value)), ("data", Link(Fake("mst-root"))), ("prev", Link(prev)), ("version", Int(3)));

    // ── Signing ──

    [Fact]
    [Trait("Spec", "REP-25")]
    [Trait("Spec", "REP-31")]
    public void Sign_ThenVerify_IsValidOnlyForTheSigningKey()
    {
        using var k256 = K256();
        using var p256 = P256();
        using var other = SigningKey.Generate(KeyCurve.K256);

        var signed = Commit().Sign(k256);

        Assert.True(signed.Verify(k256.PublicKey));
        Assert.True(Commit().Sign(p256).Verify(p256.PublicKey));
        Assert.False(signed.Verify(p256.PublicKey));
        Assert.False(signed.Verify(other.PublicKey));
    }

    [Fact]
    [Trait("Spec", "REP-23")]
    [Trait("Spec", "REP-24")]
    [Trait("Spec", "REP-25")]
    public void Sign_Block_IsTheUnsignedEncodingWithSigAddedAndHashesToTheCid()
    {
        using var key = K256();

        var signed = Commit().Sign(key);

        Assert.Equal(Unsigned(), SignedDrisl.GetUnsignedBytes(signed.Block, out var sig));
        Assert.Equal(signed.Signature.ToArray(), ((DataModelBytes)sig!).Value.ToArray());
        Assert.Equal(VerificationKey.SignatureLength, signed.Signature.Length);
        Assert.True(key.PublicKey.Verify(Unsigned(), signed.Signature.Span));
        Assert.Equal(Cid.Compute(CidCodec.DagCbor, signed.Block.Span), signed.Cid);
        Assert.StartsWith("bafyrei", signed.Cid.Value);
    }

    [Fact]
    [Trait("Spec", "REP-23")]
    [Trait("Spec", "REP-24")]
    public void Sign_Block_DecodesWithTheCommitFieldsAndANullPrev()
    {
        using var key = K256();

        var map = (DataModelMap)DataModelValue.FromDrisl(Commit().Sign(key).Block);

        Assert.Equal(["did", "rev", "sig", "data", "prev", "version"], map.Keys);
        Assert.Equal(new DataModelString(Account.Value), map["did"]);
        Assert.Equal(new DataModelInteger(RepoCommit.Version), map["version"]);
        Assert.Equal(new DataModelLink(Fake("mst-root")), map["data"]);
        Assert.Equal(DataModelNull.Instance, map["prev"]);
    }

    [Fact]
    [Trait("Spec", "REP-24")]
    public void Sign_WithPrev_EncodesPrevAsALinkAndReadsItBack()
    {
        using var key = K256();

        var signed = Commit(Fake("previous-commit")).Sign(key);

        Assert.Equal(Unsigned(Fake("previous-commit")), SignedDrisl.GetUnsignedBytes(signed.Block, out _));
        Assert.Equal(Fake("previous-commit"), SignedCommit.Read(signed.Block).Commit.Prev);
        Assert.True(signed.Verify(key.PublicKey));
    }

    [Fact]
    [Trait("Spec", "REP-25")]
    public void Sign_SameCommitTwice_SignsTheSameBytes()
    {
        using var key = K256();

        Assert.Equal(SignedDrisl.GetUnsignedBytes(Commit().Sign(key).Block, out _), SignedDrisl.GetUnsignedBytes(Commit().Sign(key).Block, out _));
        Assert.Equal(Commit(), SignedCommit.Read(Commit().Sign(key).Block).Commit);
    }

    [Fact]
    public void Sign_NullKey_ThrowsArgumentNullException() => Assert.Throws<ArgumentNullException>(() => Commit().Sign(null!));

    // ── Verifying received blocks ──

    [Theory]
    [Trait("Spec", "REP-26")]
    [Trait("Spec", "REP-28")]
    [InlineData(0)]
    [InlineData(18)] // 24 entries with sig, 23 without: the map header shrinks from two bytes to one
    public void Verify_FieldsTheCommitDoesNotKnow_AreSkippedButSigned(int extra)
    {
        using var key = K256();
        var unsigned = new DataModelMap([
            new("did", new DataModelString(Account.Value)), new("rev", new DataModelString(Rev.Value)), new("data", new DataModelLink(Fake("mst-root"))),
            new("prev", DataModelNull.Instance), new("version", new DataModelInteger(3)),
            .. Enumerable.Range(0, extra).Select(i => KeyValuePair.Create<string, DataModelValue>($"k{i:D2}", new DataModelInteger(i))),
        ]);
        var block = SignedObject.Sign(unsigned, key);

        var commit = SignedCommit.Read(block);

        // The verifier splices sig out of the block as received; re-encoding the commit would drop the extra fields.
        Assert.Equal(unsigned.ToDrisl(), SignedDrisl.GetUnsignedBytes(block, out _));
        Assert.True(commit.Verify(key.PublicKey));
        Assert.Equal(Commit(), commit.Commit);
        if (extra == 0) return;
        var tampered = block.AsSpan().LastIndexOf(Text("k17"));
        Assert.False(SignedCommit.Read(Flip(block, tampered + Text("k17").Length)).Verify(key.PublicKey));
    }

    [Fact]
    [Trait("Spec", "REP-31")]
    public void Verify_SignatureAlteredHighSOrShort_IsFalse()
    {
        using var key = K256();
        var signed = Commit().Sign(key);
        var signature = signed.Signature.ToArray();
        var n = BigInteger.Parse("0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141", System.Globalization.NumberStyles.HexNumber);
        var highS = (n - new BigInteger(signature.AsSpan(32), isUnsigned: true, isBigEndian: true)).ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] flipped = Flip(signature, 10), high = [.. signature[..32], .. highS], shortened = signature[..63];

        Assert.True(key.PublicKey.Verify(Unsigned(), high, SignaturePolicy.AcceptHighS));
        Assert.All(new[] { flipped, high, shortened }, sig => Assert.False(SignedCommit.Read(SignedDrisl.AddSignature(Unsigned(), new DataModelBytes(sig))).Verify(key.PublicKey)));
    }

    [Fact]
    [Trait("Spec", "REP-28")]
    public void Read_HandBuiltCommit_ReadsItsFields()
    {
        var commit = SignedCommit.Read(Block());

        Assert.Equal(Account, commit.Commit.Did);
        Assert.Equal(Tid.Parse("3jzfcijpj2z2a"), commit.Commit.Rev);
        Assert.Equal(Fake("data"), commit.Commit.Data);
        Assert.Null(commit.Commit.Prev);
        Assert.Equal([0xAB, 0xCD], commit.Signature.ToArray());
        Assert.Equal(Block(), commit.Block.ToArray());
    }

    [Fact]
    [Trait("Spec", "REP-28")]
    public void Read_NullOrLinkPrev_IsAccepted()
    {
        Assert.Null(SignedCommit.Read(Block(("prev", Null))).Commit.Prev);
        Assert.Equal(Fake("p"), SignedCommit.Read(Block(("prev", Link(Fake("p"))))).Commit.Prev);
    }

    // A CID link that is not a CID: a SHA-256 multihash header with no digest behind it.
    private static readonly byte[] NotACid = [0x01, 0x71, 0x12, 0x20, 0x00];

    public static TheoryData<string, byte[], RepoVerificationError> NotWellFormed => new()
    {
        { "no sig", Block(("sig", null)), RepoVerificationError.MalformedCommit },
        { "sig as text", Block(("sig", Text("not bytes"))), RepoVerificationError.MalformedCommit },
        { "empty sig", Block(("sig", Bytes([]))), RepoVerificationError.MalformedCommit },
        { "version 2", Block(("version", Int(2))), RepoVerificationError.UnsupportedVersion },
        { "version as text", Block(("version", Text("3"))), RepoVerificationError.MalformedCommit },
        { "no did", Block(("did", null)), RepoVerificationError.MalformedCommit },
        { "no rev", Block(("rev", null)), RepoVerificationError.MalformedCommit },
        { "no data", Block(("data", null)), RepoVerificationError.MalformedCommit },
        { "no version", Block(("version", null)), RepoVerificationError.MalformedCommit },
        { "did not a DID", Block(("did", Text("alice.test"))), RepoVerificationError.MalformedCommit },
        { "rev not a TID", Block(("rev", Text("not-a-tid"))), RepoVerificationError.MalformedCommit },
        // #180: a signed commit whose data is not a CID used to throw out of the verifier.
        { "data not a CID", Block(("data", Link(NotACid))), RepoVerificationError.MalformedCommit },
        { "data null", Block(("data", Null)), RepoVerificationError.MalformedCommit },
        { "prev not a CID", Block(("prev", Link(NotACid))), RepoVerificationError.MalformedCommit },
        // A duplicate could show the verifier a field other than the one signed.
        { "duplicate key", Map([.. Fields(), ("did", Text("did:plc:someoneelse"))]), RepoVerificationError.MalformedCommit },
        // The block's CID covers trailing bytes, the signature does not.
        { "trailing bytes", [.. Block(), 0x00], RepoVerificationError.MalformedCommit },
        { "truncated", Block()[..20], RepoVerificationError.MalformedCommit },
        { "not a map", [0x80], RepoVerificationError.MalformedCommit },
        { "indefinite map", [0xBF, 0xFF], RepoVerificationError.MalformedCommit },
        { "empty", [], RepoVerificationError.MalformedCommit },
    };

    [Theory]
    [Trait("Spec", "REP-27")]
    [Trait("Spec", "REP-28")]
    [Trait("Spec", "REP-29")]
    [Trait("Spec", "REP-30")]
    [MemberData(nameof(NotWellFormed))]
    public void Read_NotAWellFormedSignedCommit_ThrowsRepoVerificationException(string reason, byte[] block, RepoVerificationError error)
    {
        var ex = Record.Exception(() => SignedCommit.Read(block));

        Assert.True(ex is RepoVerificationException e && e.Error == error, $"{reason}: {ex?.ToString() ?? "no exception"}");
    }

    // A commit map with a stand-in signature; a null value removes the field.
    private static byte[] Block(params (string Key, byte[]? Value)[] changes) => Map([.. Fields(changes)]);

    private static List<(string Key, byte[] Value)> Fields(params (string Key, byte[]? Value)[] changes)
    {
        var fields = new Dictionary<string, byte[]>
        {
            ["did"] = Text(Account.Value), ["rev"] = Text("3jzfcijpj2z2a"), ["data"] = Link(Fake("data")), ["version"] = Int(3), ["sig"] = Bytes([0xAB, 0xCD]),
        };
        foreach (var (key, value) in changes)
            if (value is null) fields.Remove(key);
            else fields[key] = value;
        return [.. fields.Select(f => (f.Key, f.Value))];
    }
}
