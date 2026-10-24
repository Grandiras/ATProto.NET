using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

public class MstKeyTests
{
    // mst.test.ts "allowable keys" of the reference implementation.
    public static TheoryData<string> RejectedKeys =>
    [
        "", "asdf", "nested/collection/asdf", "coll/", "/rkey", "coll/jalapeñoA", "coll/coöperative", "coll/abc💩",
        "coll/key$", "coll/key%", "coll/key(", "coll/key)", "coll/key+", "coll/key=", "coll/@handle", "coll/any space",
        "coll/#extra", "coll/any+space", "coll/number[3]", "coll/number(3)", "coll/dHJ1ZQ==", "coll/\"quote\"",
        "coll/" + new string('a', 1020),
    ];

    [Theory]
    [Trait("Spec", "REP-1")]
    [MemberData(nameof(RejectedKeys))]
    public void Parse_KeyTheReferenceRejects_ThrowsFormatException(string key)
    {
        Assert.False(MstKey.TryParse(key, out _));
        Assert.Throws<FormatException>(() => MstKey.Parse(key));
    }

    [Theory]
    [Trait("Spec", "REP-1")]
    [Trait("Spec", "REP-2")]
    [InlineData("coll/3jui7kd54zh2y")]
    [InlineData("coll/self")]
    [InlineData("coll/example.com")]
    [InlineData("com.example/rkey")]
    [InlineData("coll/~1.2-3_")]
    [InlineData("coll/dHJ1ZQ")]
    [InlineData("coll/pre:fix")]
    [InlineData("coll/_")]
    public void Parse_KeyTheReferenceAllows_RoundTripsAndIsStored(string key)
    {
        var parsed = MstKey.Parse(key);

        Assert.Equal(key, parsed.Value);
        Assert.Equal(RepoFixtures.Leaf, MerkleSearchTree.Empty().Insert(parsed, RepoFixtures.Leaf).Get(parsed));
    }

    [Fact]
    [Trait("Spec", "REP-1")]
    public void Parse_KeyOfExactly1024Characters_Succeeds()
    {
        var key = "coll/" + new string('a', 1019);

        Assert.Equal(MstKey.MaxLength, MstKey.Parse(key).Value.Length);
    }

    [Theory]
    [Trait("Spec", "REP-2")]
    [InlineData("coll/self")]                 // collection not an NSID
    [InlineData("com.example/rkey")]          // two segments are not an NSID
    [InlineData("com.example.record/..")]     // a reserved record key
    public void TryGetRecordPath_ValidKeyThatIsNotARecordPath_IsFalse(string key) =>
        Assert.False(MstKey.Parse(key).TryGetRecordPath(out _, out _));

    [Fact]
    [Trait("Spec", "REP-1")]
    public void For_RecordPath_RoundTripsThroughTryGetRecordPath()
    {
        var key = MstKey.For(Nsid.Parse("app.bsky.feed.post"), RecordKey.Parse("3jui7kd54zh2y"));

        Assert.Equal("app.bsky.feed.post/3jui7kd54zh2y", key.Value);
        Assert.True(key.TryGetRecordPath(out var collection, out var rkey));
        Assert.Equal("app.bsky.feed.post", collection.Value);
        Assert.Equal("3jui7kd54zh2y", rkey.Value);
    }

    [Fact]
    [Trait("Spec", "REP-3")]
    public void CompareTo_Keys_OrderByTheirAsciiBytes()
    {
        string[] ordered = ["B/1", "a./1", "a/1", "a~/1", "b/1"];

        Assert.Equal(ordered, ordered.Reverse().Select(MstKey.Parse).Order().Select(k => k.Value));
        Assert.True(MstKey.Parse("B/1") < MstKey.Parse("a/1"));
    }

    [Fact]
    public void Value_DefaultKey_ThrowsInvalidOperationException() =>
        Assert.Throws<InvalidOperationException>(() => default(MstKey).Value);

    // mst/key_heights.json rows that are valid MST keys; the rest run in the conformance suite.
    [Theory]
    [Trait("Spec", "REP-4")]
    [InlineData("app.bsky.feed.post/454397e440ec", 4)]
    [InlineData("app.bsky.feed.post/9adeb165882c", 8)]
    public void Layer_InteropKeyHeights_MatchTheExpectedLayer(string key, int layer) =>
        Assert.Equal(layer, MstKey.Parse(key).Layer);

    // mst/example_keys.txt: every key names its own layer in the digit after its letter.
    [Fact]
    [Trait("Spec", "REP-4")]
    public void Layer_InteropExampleKeys_MatchTheLayerTheirNameStates()
    {
        var keys = RepoFixtures.VectorText("mst/example_keys.txt").Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(156, keys.Length);
        Assert.All(keys, key => Assert.Equal(key[1] - '0', MstKey.Parse(key).Layer));
    }
}
