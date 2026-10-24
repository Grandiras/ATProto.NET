using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Crypto;
using ATProtoNet.Spaces;
using ATProtoNet.TestKit;

namespace ATProtoNet.Tests.Spaces;

/// <summary>Commit and record MACs, the two headers, and the shared secret behind them.</summary>
public sealed class SpaceMacTests
{
    // RFC 9180 appendix A.3.1, DHKEM(P-256, HKDF-SHA256): the requester's key share is pkEm, the author's KEM key skRm.
    private const string RfcSkRm = "f3ce7fdae57e1a310d87f1ebbde6f328be0a99cdbcadf4d6589cf29de4b8ffd2";
    private const string RfcPkEm = "04a92719c6195d5085104f469a8b9814d5838ff72b60501e2c4466e5e67b325ac98536d7b61a1af4b78e5b7f951c0900be863c403ce65c9bfcb9382657222d18c4";
    private const string RfcSharedSecret = "c0d26aeab536609a572b07695d933b589dcf363ff9d93c93adea537aeabb8cb8";

    // The CIDs of {text: "hello"} and {text: "world"}.
    private static readonly Cid CidA = Cid.Parse("bafyreidefdycgbfy3oglcb6ism3eqhyp5llsrpzxjsuac2gsy4mtrtx244");
    private static readonly Cid CidB = Cid.Parse("bafyreidpw4cbv6gr4ukh33z23pvvrpr3wi4gnpmi4doamlsl3sa4rgri2a");

    private static readonly SpaceCommit Commit = new(SpaceCommitTests.Repo, SpaceCommitTests.Space, SpaceCommitTests.Rev, SpaceCommitTests.Data);
    private static readonly SpaceRecordUri Uri = SpaceCommitTests.Space.Record(SpaceCommitTests.Repo, Nsid.Parse("app.bsky.feed.post"), RecordKey.Parse("1"));

    [Fact]
    [Trait("Spec", "SPC-55"), Trait("Spec", "SPC-56")]
    public void ComputeCommit_Rfc9180Secret_IsHmacOverTheLengthPrefixedContextThenData()
    {
        using var secret = RfcSecret(SpaceMacType.Repo);
        byte[] message = [.. "atproto-space-repo-v1"u8, .. Prefixed(Commit.Space.Value), .. Prefixed(Commit.Repo.Value), .. Prefixed(Commit.Rev), .. Commit.Data.Span];

        Assert.Equal(HMACSHA256.HashData(Convert.FromHexString(RfcSharedSecret), message), SpaceMac.ComputeCommit(secret, Commit));
    }

    [Fact]
    [Trait("Spec", "SPC-55"), Trait("Spec", "SPC-56")]
    public void ComputeRecord_Rfc9180Secret_IsHmacOverTheUriThenTheBinaryCid()
    {
        using var secret = RfcSecret(SpaceMacType.Record);
        byte[] message = [.. "atproto-space-record-v1"u8, .. Prefixed(Uri.Value), .. CidA.ToByteArray()];

        Assert.Equal(HMACSHA256.HashData(Convert.FromHexString(RfcSharedSecret), message), SpaceMac.ComputeRecord(secret, Uri, CidA));
    }

    [Fact]
    [Trait("Spec", "SPC-56")]
    public void VerifyCommit_SameSecret_TrueAndAnotherFalse()
    {
        using var secret = NewSecret();
        using var other = NewSecret();

        var mac = SpaceMac.ComputeCommit(secret, Commit);

        Assert.Equal(SharedSecret.MacLength, mac.Length);
        Assert.True(SpaceMac.VerifyCommit(secret, Commit, mac));
        Assert.False(SpaceMac.VerifyCommit(other, Commit, mac));
        Assert.False(SpaceMac.VerifyCommit(secret, Commit, mac[..31]));
        Assert.False(SpaceMac.VerifyCommit(secret, Commit, []));
    }

    [Theory]
    [Trait("Spec", "SPC-56")]
    [InlineData("space")]
    [InlineData("repo")]
    [InlineData("rev")]
    [InlineData("data")]
    public void VerifyCommit_TamperedField_False(string field)
    {
        using var secret = NewSecret();
        var mac = SpaceMac.ComputeCommit(secret, Commit);
        var tampered = field switch
        {
            "space" => new SpaceCommit(Commit.Repo, SpaceUri.Parse("at://did:example:space/space/app.bsky.group/other"), Tid.Parse(Commit.Rev), Commit.Data.Span),
            "repo" => new SpaceCommit(Did.Parse("did:example:bob"), Commit.Space, Tid.Parse(Commit.Rev), Commit.Data.Span),
            "rev" => new SpaceCommit(Commit.Repo, Commit.Space, Tid.Parse("3kbcq3p7adzzz"), Commit.Data.Span),
            _ => new SpaceCommit(Commit.Repo, Commit.Space, Tid.Parse(Commit.Rev), LtHash.Empty().Digest()),
        };

        Assert.False(SpaceMac.VerifyCommit(secret, tampered, mac));
    }

    [Fact]
    [Trait("Spec", "SPC-56")]
    public void VerifyCommit_UnknownVersion_False()
    {
        using var secret = NewSecret();
        var future = SpaceCommit.FromStarMetadata(SpaceCommitTests.Metadata(ver: "2"));

        Assert.False(SpaceMac.VerifyCommit(secret, future, SpaceMac.ComputeCommit(secret, future)));
    }

    [Fact]
    [Trait("Spec", "SPC-56")]
    public void VerifyRecord_SameSecret_TrueAndAnotherUriCidOrSecretFalse()
    {
        using var secret = NewSecret();
        using var other = NewSecret();
        var mac = SpaceMac.ComputeRecord(secret, Uri, CidA);

        Assert.True(SpaceMac.VerifyRecord(secret, Uri, CidA, mac));
        Assert.False(SpaceMac.VerifyRecord(other, Uri, CidA, mac));
        Assert.False(SpaceMac.VerifyRecord(secret, SpaceRecordUri.Parse($"{Uri}2"), CidA, mac));
        Assert.False(SpaceMac.VerifyRecord(secret, Uri, CidB, mac));
    }

    [Fact]
    [Trait("Spec", "SPC-56")]
    public void ComputeRecord_SameSecret_IsDomainSeparatedFromACommitMac()
    {
        using var secret = NewSecret();

        Assert.NotEqual(SpaceMac.ComputeCommit(secret, Commit), SpaceMac.ComputeRecord(secret, Uri, CidA));
    }

    [Fact]
    [Trait("Spec", "SPC-55"), Trait("Spec", "SPC-56")]
    public void Constants_AreTheProtocolNames()
    {
        Assert.Equal(("Atproto-Space-Want-Mac", "Atproto-Space-Mac"), (SpaceMac.WantMacHeader, SpaceMac.MacHeader));
        Assert.Equal(("atproto-space-repo", "atproto-space-record"), (SpaceMac.TypeName(SpaceMacType.Repo), SpaceMac.TypeName(SpaceMacType.Record)));
        Assert.Equal((1, 1), (SpaceMac.Version(SpaceMacType.Repo), SpaceMac.Version(SpaceMacType.Record)));
    }

    [Fact]
    [Trait("Spec", "SPC-56")]
    public void FormatHeader_RepoMac_IsTheReferenceString()
    {
        var mac = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

        Assert.Equal("type=\"atproto-space-repo\", ver=1, mac=:AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=:", SpaceMac.FormatHeader(SpaceMacType.Repo, mac));
    }

    [Theory]
    [Trait("Spec", "SPC-56")]
    [InlineData(SpaceMacType.Repo)]
    [InlineData(SpaceMacType.Record)]
    public void ParseHeader_FormattedHeader_RoundTrips(SpaceMacType type)
    {
        var mac = RandomNumberGenerator.GetBytes(32);

        Assert.Equal(mac, SpaceMac.ParseHeader(SpaceMac.FormatHeader(type, mac), type));
    }

    [Theory]
    [Trait("Spec", "SPC-56")]
    [InlineData("???")]
    [InlineData("")]
    [InlineData("type=\"atproto-space-repo\", ver=1")] // no mac
    [InlineData("type=\"atproto-space-repo\", ver=1, mac=\"abc\"")] // not a byte sequence
    [InlineData("type=\"atproto-space-repo\", ver=1, mac=(:AAAA:)")] // an inner list
    [InlineData("type=\"atproto-space-record\", ver=1, mac=:AAAA:")] // another type
    [InlineData("type=atproto-space-repo, ver=1, mac=:AAAA:")] // a token, not a string
    [InlineData("type=\"atproto-space-repo\", ver=2, mac=:AAAA:")] // another version
    [InlineData("type=\"atproto-space-repo\", ver=\"1\", mac=:AAAA:")]
    [InlineData("ver=1, mac=:AAAA:")] // no type
    [InlineData("type=\"atproto-space-repo\", mac=:AAAA:")] // no version
    public void ParseHeader_Malformed_ThrowsFormatException(string value) =>
        Assert.Throws<FormatException>(() => SpaceMac.ParseHeader(value, SpaceMacType.Repo));

    [Fact]
    [Trait("Spec", "SPC-55")]
    public void ToHeaderValue_ParsedRequest_IsTheReferenceShape()
    {
        var value = $"type=\"atproto-space-repo\", ver=1, key_share=\"{TestKeys.P256DidKey}\"";

        var request = SpaceMacRequest.Parse(value, SpaceMacType.Repo);

        Assert.Equal((SpaceMacType.Repo, TestKeys.P256DidKey), (request.Type, request.KeyShare));
        Assert.Equal(value, request.ToHeaderValue());
    }

    [Theory]
    [Trait("Spec", "SPC-55")]
    [InlineData("???")]
    [InlineData("type=\"atproto-space-repo\", ver=1")] // no key_share
    [InlineData("type=\"atproto-space-repo\", ver=1, key_share=:AAAA:")] // not a string
    [InlineData("type=\"atproto-space-record\", ver=1, key_share=\"" + TestKeys.P256DidKey + "\"")] // another type
    [InlineData("type=\"atproto-space-repo\", ver=2, key_share=\"" + TestKeys.P256DidKey + "\"")] // another version
    public void Parse_MalformedRequest_ThrowsFormatException(string value) =>
        Assert.Throws<FormatException>(() => SpaceMacRequest.Parse(value, SpaceMacType.Repo));

    [Theory]
    [Trait("Spec", "SPC-55"), Trait("Spec", "SPC-56")]
    [InlineData("did:key:nope")]
    [InlineData("not a did")]
    [InlineData(TestKeys.K256DidKey)] // not P-256
    public void Accept_KeyShareNotAP256DidKey_ThrowsFormatException(string keyShare)
    {
        using var author = KemPrivateKey.Generate(KemAlgorithm.Default);
        var request = SpaceMacRequest.Parse($"type=\"atproto-space-repo\", ver=1, key_share=\"{keyShare}\"", SpaceMacType.Repo);

        Assert.Equal(keyShare, request.KeyShare);
        Assert.Throws<FormatException>(() => request.Accept(author));
    }

    [Fact]
    public void Parse_NullHeader_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SpaceMacRequest.Parse(null!, SpaceMacType.Repo));
        Assert.Throws<ArgumentNullException>(() => SpaceMac.ParseHeader(null!, SpaceMacType.Repo));
    }

    [Theory]
    [Trait("Spec", "SPC-54"), Trait("Spec", "SPC-55"), Trait("Spec", "SPC-56")]
    [InlineData(SpaceMacType.Repo)]
    [InlineData(SpaceMacType.Record)]
    public void Create_ThroughTheHeaders_HostAndRequesterAgreeOnTheMac(SpaceMacType type)
    {
        using var author = KemPrivateKey.Generate(KemAlgorithm.Default);

        // Requester: a request to the author's published #atproto_kem key.
        var want = SpaceMacRequest.Create(KemPublicKey.FromMultikey(author.PublicKey.Multikey), type, out var requesterSecret);
        using var _ = requesterSecret;
        // Host: decapsulate, MAC, answer.
        using var hostSecret = SpaceMacRequest.Parse(want.ToHeaderValue(), type).Accept(author);
        var answer = SpaceMac.FormatHeader(type, type is SpaceMacType.Repo ? SpaceMac.ComputeCommit(hostSecret, Commit) : SpaceMac.ComputeRecord(hostSecret, Uri, CidA));
        // Requester: verify.
        var mac = SpaceMac.ParseHeader(answer, type);

        Assert.Equal(KeyCurve.P256, DidKey.Parse(want.KeyShare).Curve);
        Assert.True(type is SpaceMacType.Repo ? SpaceMac.VerifyCommit(requesterSecret, Commit, mac) : SpaceMac.VerifyRecord(requesterSecret, Uri, CidA, mac));
    }

    [Fact]
    [Trait("Spec", "SPC-55")]
    public void Create_EveryRequest_UsesAFreshKeyShareAndSecret()
    {
        using var author = KemPrivateKey.Generate(KemAlgorithm.Default);

        var first = SpaceMacRequest.Create(author.PublicKey, SpaceMacType.Repo, out var firstSecret);
        var second = SpaceMacRequest.Create(author.PublicKey, SpaceMacType.Repo, out var secondSecret);
        using var a = firstSecret;
        using var b = secondSecret;

        Assert.NotEqual(first.KeyShare, second.KeyShare);
        Assert.NotEqual(SpaceMac.ComputeCommit(firstSecret, Commit), SpaceMac.ComputeCommit(secondSecret, Commit));
    }

    [Fact]
    [Trait("Spec", "SPC-54"), Trait("Spec", "SPC-57")]
    public void Accept_AnotherAuthorKey_ProducesAMacThatFailsVerification()
    {
        // A host answering with a key other than the published one (rotated, or not the author's) fails closed.
        using var author = KemPrivateKey.Generate(KemAlgorithm.Default);
        using var other = KemPrivateKey.Generate(KemAlgorithm.Default);
        var request = SpaceMacRequest.Create(author.PublicKey, SpaceMacType.Repo, out var requesterSecret);
        using var _ = requesterSecret;

        using var wrong = request.Accept(other);

        Assert.False(SpaceMac.VerifyCommit(requesterSecret, Commit, SpaceMac.ComputeCommit(wrong, Commit)));
    }

    [Fact]
    [Trait("Spec", "SPC-58")]
    public void ComputeCommit_ByTheRequester_IsIndistinguishableFromTheHosts()
    {
        // Deniability: the requester holds the same secret, so it can MAC any commit it likes; a MAC it shows a third
        // party proves nothing about the author.
        using var author = KemPrivateKey.Generate(KemAlgorithm.Default);
        var request = SpaceMacRequest.Create(author.PublicKey, SpaceMacType.Repo, out var requesterSecret);
        using var _ = requesterSecret;
        using var hostSecret = request.Accept(author);
        var forged = new SpaceCommit(Commit.Repo, Commit.Space, Tid.Parse("3kbcq3p7adzzz"), LtHash.Empty().Digest());

        Assert.Equal(SpaceMac.ComputeCommit(hostSecret, forged), SpaceMac.ComputeCommit(requesterSecret, forged));
    }

    private static SharedSecret RfcSecret(SpaceMacType type)
    {
        using var ecdh = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = Convert.FromHexString(RfcSkRm) });
        using var author = KemPrivateKey.ImportPkcs8(ecdh.ExportPkcs8PrivateKey(), KemAlgorithm.Default);
        var keyShare = VerificationKey.FromUncompressedPoint(KeyCurve.P256, Convert.FromHexString(RfcPkEm)).DidKey;
        return SpaceMacRequest.Parse($"type=\"{SpaceMac.TypeName(type)}\", ver=1, key_share=\"{keyShare}\"", type).Accept(author);
    }

    private static SharedSecret NewSecret()
    {
        using var key = KemPrivateKey.Generate(KemAlgorithm.Default);
        return key.PublicKey.Encapsulate().Secret;
    }

    private static byte[] Prefixed(string field)
    {
        var bytes = Encoding.UTF8.GetBytes(field);
        var prefixed = new byte[2 + bytes.Length];
        BinaryPrimitives.WriteUInt16BigEndian(prefixed, (ushort)bytes.Length);
        bytes.CopyTo(prefixed, 2);
        return prefixed;
    }
}
