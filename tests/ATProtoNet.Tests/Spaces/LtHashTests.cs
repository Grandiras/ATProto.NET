using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Crypto;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

/// <summary>
/// The homomorphic set hash. The expected digests are the reference implementation's (<c>@atproto/space</c>
/// tests and the vectors 0.8 generated with its primitives); a digest that diverges is a repository this SDK
/// could not sync.
/// </summary>
public sealed class LtHashTests
{
    private const string CidA = "bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq";
    private const string CidB = "bafyreihrjacrc7vmmyiuxka7uio7ximst76ippf6xuqa6c256olh3mxojq";

    private static readonly (string Collection, string Rkey, string Cid) RecordA = ("com.example.note", "aaa", CidA);
    private static readonly (string Collection, string Rkey, string Cid) RecordB = ("com.example.note", "bbb", CidB);
    private static readonly (string Collection, string Rkey, string Cid) RecordC = ("com.example.other", "c", CidA);

    [Fact]
    [Trait("Spec", "SPC-8")]
    public void Empty_State_IsZeroesAndDigestIsTheReferenceVector()
    {
        var hash = LtHash.Empty();

        Assert.True(hash.IsEmpty);
        Assert.Equal(new byte[LtHash.StateLength], hash.GetState());
        Assert.Equal("e5a00aa9991ac8a5ee3109844d84a55583bd20572ad3ffcd42792f3c36b183ad", Convert.ToHexStringLower(hash.Digest()));
    }

    [Fact]
    [Trait("Spec", "SPC-7"), Trait("Spec", "SPC-8")]
    public void Add_SingleRecord_MatchesTheReferenceStateAndDigest()
    {
        var hash = With(("app.bsky.feed.post", "3l6oveex3ii2l", CidA));

        // The digest alone would not catch a lane-ordering mistake that survived SHA-256; the raw state does.
        Assert.Equal("d6c129c32e1e27f266d4afce37f6556c2f63e0f6a5668fe3333a8f9819166c51", Convert.ToHexStringLower(hash.GetState().AsSpan(0, 32)));
        Assert.Equal("dec3e09573deb414bebcbd9b1e4043ec223d16b655761e3840deec7f6f723247", Convert.ToHexStringLower(hash.Digest()));
    }

    [Fact]
    [Trait("Spec", "SPC-7"), Trait("Spec", "SPC-8")]
    public void Add_ThreeRecords_MatchesTheReferenceDigest() =>
        Assert.Equal("ec9e71c705e1d31fa1eb6a275bc5cd9a6c13576120af8bd31590481057be704f", Convert.ToHexStringLower(With(RecordA, RecordB, RecordC).Digest()));

    [Fact]
    [Trait("Spec", "SPC-7"), Trait("Spec", "SPC-8")]
    public void Construction_ReferenceSnapshotElements_MatchesTheSnapshotDigest()
    {
        // The reference's snapshot ("one", "two") is not a record element, so it pins the construction (BLAKE3 XOF to
        // 2,048 bytes, u16 lanes summed) that Add_RecordElement_MatchesTheConstruction holds LtHash to.
        var state = Construction(new byte[LtHash.StateLength], "one", add: true);
        state = Construction(state, "two", add: true);

        Assert.Equal("ae05cb6d224379d9710c290c8529945c5b0e0fde9ead30b9699057ce701c63e7", Convert.ToHexStringLower(SHA256.HashData(state)));
    }

    [Theory]
    [Trait("Spec", "SPC-7"), Trait("Spec", "SPC-8")]
    [InlineData("com.example.note", "aaa")]
    [InlineData("app.bsky.feed.post", "3l6oveex3ii2l")]
    [InlineData("com.example.note", "self")]
    [InlineData("com.example.note", 512)] // the longest record key: more than one BLAKE3 chunk of element
    public void Add_RecordElement_MatchesTheConstruction(string collection, object rkey)
    {
        var key = rkey is int length ? new string('k', length) : (string)rkey;
        var start = With(RecordA, RecordB);
        var element = $"{collection}/{key}/{CidB}";

        var added = start.Clone();
        added.Add(Nsid.Parse(collection), RecordKey.Parse(key), Cid.Parse(CidB));
        var removed = start.Clone();
        removed.Remove(Nsid.Parse(collection), RecordKey.Parse(key), Cid.Parse(CidB));

        Assert.Equal(Construction(start.GetState(), element, add: true), added.GetState());
        Assert.Equal(Construction(start.GetState(), element, add: false), removed.GetState());
    }

    [Fact]
    [Trait("Spec", "SPC-8")]
    public void Add_InAnyOrder_GivesTheSameState() => Assert.Equal(With(RecordA, RecordB, RecordC).GetState(), With(RecordC, RecordB, RecordA).GetState());

    [Fact]
    [Trait("Spec", "SPC-8")]
    public void Remove_EverythingAdded_RestoresTheEmptyState()
    {
        var hash = With(RecordA, RecordB);

        Remove(hash, RecordA);
        Remove(hash, RecordB);

        Assert.True(hash.IsEmpty);
        Assert.Equal(LtHash.Empty().Digest(), hash.Digest());
    }

    [Fact]
    [Trait("Spec", "SPC-8")]
    public void Remove_BeforeAdd_StillCommutes()
    {
        // Lanes wrap modulo 2^16, so subtracting first lands where adding only would: a syncer may apply
        // operations in any order.
        var hash = LtHash.Empty();
        Remove(hash, RecordA);
        Add(hash, RecordB);
        Add(hash, RecordA);

        Assert.Equal(With(RecordB).GetState(), hash.GetState());
    }

    [Fact]
    [Trait("Spec", "SPC-8")]
    public void Add_SameRecordTwice_DoesNotCollapse()
    {
        var twice = With(RecordA, RecordA);

        Assert.NotEqual(With(RecordA).GetState(), twice.GetState());
        Remove(twice, RecordA);
        Assert.Equal(With(RecordA).GetState(), twice.GetState());
    }

    [Theory]
    [Trait("Spec", "SPC-7")]
    [InlineData("com.example.note", "bbb", CidA)] // the same CID at another path
    [InlineData("com.example.note", "aaa", CidB)] // another CID at the same path
    [InlineData("com.example.notes", "aaa", CidA)] // another collection
    public void Add_RecordsDifferingInOneField_GiveDifferentStates(string collection, string rkey, string cid) =>
        Assert.NotEqual(With(RecordA).GetState(), With((collection, rkey, cid)).GetState());

    [Fact]
    [Trait("Spec", "SPC-8")]
    public void FromState_PersistedState_ResumesWithoutAliasing()
    {
        var original = With(RecordA, RecordB);
        var state = original.GetState();

        var resumed = LtHash.FromState(state);
        state[0] ^= 0xFF;
        var handedOut = resumed.GetState();
        handedOut[1] ^= 0xFF;

        Assert.Equal(original.GetState(), resumed.GetState());
        Assert.Equal(original.Digest(), resumed.Digest());
    }

    [Theory]
    [Trait("Spec", "SPC-8")]
    [InlineData(0)] // unlike the reference's nullish state, empty is not an empty repository: that is LtHash.Empty()
    [InlineData(32)]
    [InlineData(LtHash.StateLength - 1)]
    [InlineData(LtHash.StateLength + 1)]
    public void FromState_WrongLength_ThrowsArgumentException(int length) =>
        Assert.Throws<ArgumentException>(() => LtHash.FromState(new byte[length]));

    [Fact]
    [Trait("Spec", "SPC-9")]
    public void Clone_ThenAdd_LeavesTheOriginal()
    {
        var original = With(RecordA);

        var staged = original.Clone();
        Add(staged, RecordB);

        Assert.Equal(With(RecordA).GetState(), original.GetState());
        Assert.Equal(With(RecordA, RecordB).GetState(), staged.GetState());
    }

    [Fact]
    [Trait("Spec", "SPC-9")]
    public void Equality_IsReferenceEquality()
    {
        // 0.8's LtHash hashed its mutable state, so one mutated inside a dictionary was lost there.
        var a = With(RecordA);
        var b = With(RecordA);

        Assert.Equal(typeof(object), typeof(LtHash).GetMethod(nameof(Equals), [typeof(object)])!.DeclaringType);
        Assert.Equal(typeof(object), typeof(LtHash).GetMethod(nameof(GetHashCode), Type.EmptyTypes)!.DeclaringType);
        Assert.False(a.Equals(b));
        Assert.DoesNotContain(typeof(LtHash).GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEquatable<>));
    }

    [Fact]
    public void Add_NullArgument_ThrowsArgumentNullException()
    {
        var hash = LtHash.Empty();

        Assert.Throws<ArgumentNullException>(() => hash.Add(null!, RecordKey.Parse("a"), Cid.Parse(CidA)));
        Assert.Throws<ArgumentNullException>(() => hash.Remove(Nsid.Parse("com.example.note"), null!, Cid.Parse(CidA)));
        Assert.Throws<ArgumentNullException>(() => hash.Add(Nsid.Parse("com.example.note"), RecordKey.Parse("a"), null!));
        Assert.True(hash.IsEmpty);
    }

    private static LtHash With(params (string Collection, string Rkey, string Cid)[] records)
    {
        var hash = LtHash.Empty();
        foreach (var record in records) Add(hash, record);
        return hash;
    }

    private static void Add(LtHash hash, (string Collection, string Rkey, string Cid) r) => hash.Add(Nsid.Parse(r.Collection), RecordKey.Parse(r.Rkey), Cid.Parse(r.Cid));

    private static void Remove(LtHash hash, (string Collection, string Rkey, string Cid) r) => hash.Remove(Nsid.Parse(r.Collection), RecordKey.Parse(r.Rkey), Cid.Parse(r.Cid));

    // The spec's construction, lane by lane: the UTF-8 element expanded with BLAKE3 to 2,048 bytes, as 1,024
    // little-endian u16 lanes added to or subtracted from the state modulo 2^16.
    private static byte[] Construction(byte[] state, string element, bool add)
    {
        var expanded = new byte[LtHash.StateLength];
        Blake3.Hash(Encoding.UTF8.GetBytes(element), expanded);
        var result = new byte[LtHash.StateLength];
        for (var i = 0; i < LtHash.StateLength; i += 2)
        {
            var (lane, delta) = (BinaryPrimitives.ReadUInt16LittleEndian(state.AsSpan(i)), BinaryPrimitives.ReadUInt16LittleEndian(expanded.AsSpan(i)));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i), unchecked((ushort)(add ? lane + delta : lane - delta)));
        }
        return result;
    }
}
