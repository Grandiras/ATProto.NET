using System.Buffers.Binary;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

/// <summary>
/// The expected digests here were produced by the reference implementation's own primitives
/// (<c>@noble/hashes</c> BLAKE3 + SHA-256, driven by the LtHash construction in
/// <c>@atproto/space</c>). A digest that diverges is a repo this SDK could not sync.
/// </summary>
public class LtHashTests
{
    private const string RecordA =
        "com.example.note/aaa/bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq";
    private const string RecordB =
        "com.example.note/bbb/bafyreihrjacrc7vmmyiuxka7uio7ximst76ippf6xuqa6c256olh3mxojq";
    private const string RecordC =
        "com.example.other/c/bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq";

    internal const string SingleElement =
        "app.bsky.feed.post/3l6oveex3ii2l/bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq";

    internal const string SingleElementDigestHex =
        "dec3e09573deb414bebcbd9b1e4043ec223d16b655761e3840deec7f6f723247";

    [Fact]
    public void Digest_EmptyRepo_MatchesReferenceImplementation()
    {
        // The empty state is all zeroes, so this is just sha256 over 2048 zero bytes.
        Assert.Equal(
            "e5a00aa9991ac8a5ee3109844d84a55583bd20572ad3ffcd42792f3c36b183ad",
            Convert.ToHexStringLower(new LtHash().Digest()));
    }

    [Fact]
    public void Digest_SingleElement_MatchesReferenceImplementation()
    {
        var hash = new LtHash().Add(SingleElement);

        Assert.Equal(SingleElementDigestHex, Convert.ToHexStringLower(hash.Digest()));
    }

    [Fact]
    public void GetState_SingleElement_MatchesReferenceImplementation()
    {
        // The digest alone would not catch a lane-ordering mistake that happened to survive
        // sha256; comparing raw state does.
        var state = new LtHash().Add(SingleElement).GetState();

        Assert.Equal(LtHash.StateBytes, state.Length);
        Assert.Equal(
            "d6c129c32e1e27f266d4afce37f6556c2f63e0f6a5668fe3333a8f9819166c51",
            Convert.ToHexStringLower(state.AsSpan(0, 32)));
    }

    [Fact]
    public void Digest_ThreeElements_MatchesReferenceImplementation()
    {
        var hash = new LtHash().Add(RecordA).Add(RecordB).Add(RecordC);

        Assert.Equal(
            "ec9e71c705e1d31fa1eb6a275bc5cd9a6c13576120af8bd31590481057be704f",
            Convert.ToHexStringLower(hash.Digest()));
    }

    [Fact]
    public void Digest_NonAsciiElement_MatchesReferenceImplementation()
    {
        // The spec encodes elements as UTF-8. Record paths are ASCII in practice, but a
        // UTF-16 or Latin-1 encoding would silently diverge only for non-ASCII input.
        var hash = new LtHash().Add(
            "com.example.nøte/é/bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq");

        Assert.Equal(
            "7341e706df6e3573151e0f72221a9007083ffc5e96d966c5f616d74d116a9476",
            Convert.ToHexStringLower(hash.Digest()));
    }

    [Fact]
    public void Add_InAnyOrder_ProducesTheSameDigest()
    {
        var forwards = new LtHash().Add(RecordA).Add(RecordB).Add(RecordC);
        var backwards = new LtHash().Add(RecordC).Add(RecordB).Add(RecordA);

        Assert.Equal(forwards, backwards);
        Assert.Equal(forwards.Digest(), backwards.Digest());
    }

    [Fact]
    public void Remove_UndoesAdd_ReturningToTheEmptyState()
    {
        var hash = new LtHash().Add(RecordA).Add(RecordB);
        hash.Remove(RecordA).Remove(RecordB);

        Assert.True(hash.IsEmpty);
        Assert.Equal(new LtHash().Digest(), hash.Digest());
    }

    [Fact]
    public void Remove_BeforeAdd_StillCommutes()
    {
        // Lanes wrap mod 2^16, so subtracting first and adding later must land in the same
        // place — this is what lets a syncer apply oplog entries out of order.
        var subtractFirst = new LtHash().Remove(RecordA).Add(RecordB).Add(RecordA);
        var addOnly = new LtHash().Add(RecordB);

        Assert.Equal(addOnly, subtractFirst);
    }

    [Fact]
    public void Add_SameElementTwice_DoesNotCollapseToOne()
    {
        // The construction is a multiset sum, not a set union; a repo cannot hold the same
        // path twice, but the arithmetic must not silently absorb a double-add either.
        var once = new LtHash().Add(RecordA);
        var twice = new LtHash().Add(RecordA).Add(RecordA);

        Assert.NotEqual(once, twice);
        Assert.Equal(once, twice.Remove(RecordA));
    }

    [Fact]
    public void Constructor_RoundTripsAPersistedState()
    {
        var original = new LtHash().Add(RecordA).Add(RecordB);

        var restored = new LtHash(original.GetState());

        Assert.Equal(original, restored);
        Assert.Equal(original.Digest(), restored.Digest());
    }

    [Fact]
    public void Constructor_EmptySpan_YieldsAnEmptyRepo()
    {
        Assert.True(new LtHash(ReadOnlySpan<byte>.Empty).IsEmpty);
    }

    [Fact]
    public void Constructor_WrongLengthState_Throws()
    {
        Assert.Throws<ArgumentException>(() => new LtHash(new byte[100]));
    }

    [Fact]
    public void Clone_IsIndependentOfTheOriginal()
    {
        var original = new LtHash().Add(RecordA);
        var clone = original.Clone();

        clone.Add(RecordB);

        Assert.NotEqual(original, clone);
        Assert.Equal(new LtHash().Add(RecordA), original);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(90)]
    [InlineData(600)]  // past the stack buffer for the UTF-8 bytes
    [InlineData(1500)] // more than one BLAKE3 chunk
    public void AddAndRemove_ElementOfAnyLength_MatchesLaneByLaneArithmetic(int length)
    {
        var element = new string('x', length) + "/bafyreicnt42y6vo6pfpvyro234ac4o6ijug6adwwrh7awflgrqlt4zibxq";
        var start = new LtHash().Add(RecordA).Add(RecordB);
        var startState = start.GetState();

        // The spec's construction, one lane at a time on the scalar BLAKE3 path.
        var expanded = new byte[LtHash.StateBytes];
        ATProtoNet.Crypto.Blake3.HashExtended(System.Text.Encoding.UTF8.GetBytes(element), expanded, parallelism: 1);
        var added = new byte[LtHash.StateBytes];
        var removed = new byte[LtHash.StateBytes];
        for (var i = 0; i < LtHash.Lanes; i++)
        {
            var lane = BinaryPrimitives.ReadUInt16LittleEndian(startState.AsSpan(i * 2));
            var delta = BinaryPrimitives.ReadUInt16LittleEndian(expanded.AsSpan(i * 2));
            BinaryPrimitives.WriteUInt16LittleEndian(added.AsSpan(i * 2), unchecked((ushort)(lane + delta)));
            BinaryPrimitives.WriteUInt16LittleEndian(removed.AsSpan(i * 2), unchecked((ushort)(lane - delta)));
        }

        Assert.Equal(added, start.Clone().Add(element).GetState());
        Assert.Equal(removed, start.Clone().Remove(element).GetState());
    }

    [Fact]
    public void WriteState_LargerDestination_WritesOnlyTheState()
    {
        var hash = new LtHash().Add(RecordA);
        var destination = new byte[LtHash.StateBytes + 3];
        destination.AsSpan().Fill(0xAA);

        hash.WriteState(destination);

        Assert.Equal(hash.GetState(), destination[..LtHash.StateBytes]);
        Assert.All(destination[LtHash.StateBytes..], b => Assert.Equal(0xAA, b));
    }

    [Fact]
    public void IsEmpty_AfterAdd_IsFalse()
    {
        Assert.False(new LtHash().Add(RecordA).IsEmpty);
    }
}
