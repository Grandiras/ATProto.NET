using ATProtoNet.Identity;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// The contract every identifier type shares: <see cref="IParsable{TSelf}"/>,
/// <see cref="ISpanParsable{TSelf}"/> and <see cref="IComparable{T}"/>, reachable from generic
/// code such as minimal-API parameter binding.
/// </summary>
public class IdentifierContractTests
{
    [Fact]
    public void Did_ImplementsParsingContract() =>
        AssertContract<Did>("did:plc:abc123", "did:plc:abc124", "did:plc:");

    [Fact]
    public void Handle_ImplementsParsingContract() =>
        AssertContract<Handle>("alice.bsky.social", "bob.bsky.social", "alice");

    [Fact]
    public void AtIdentifier_ImplementsParsingContract() =>
        AssertContract<AtIdentifier>("alice.bsky.social", "did:plc:abc123", "did:plc:");

    [Fact]
    public void Nsid_ImplementsParsingContract() =>
        AssertContract<Nsid>("app.bsky.feed.like", "app.bsky.feed.post", "app.bsky");

    [Fact]
    public void Tid_ImplementsParsingContract() =>
        AssertContract<Tid>("3jzfcijpj2z2a", "3jzfcijpj2z2b", "zzzzzzzzzzzzz");

    [Fact]
    public void RecordKey_ImplementsParsingContract() =>
        AssertContract<RecordKey>("3jzfcijpj2z2a", "self", "..");

    [Fact]
    public void Cid_ImplementsParsingContract() =>
        AssertContract<Cid>(
            "bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku",
            "bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm",
            "bafybeihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku");

    [Fact]
    public void AtUri_ImplementsParsingContract() =>
        AssertContract<AtUri>(
            "at://did:plc:abc123/app.bsky.feed.like/3jzfcijpj2z2a",
            "at://did:plc:abc123/app.bsky.feed.post/3jzfcijpj2z2a",
            "at://did:plc:abc123/");

    [Fact]
    public void SpaceUri_ImplementsParsingContract() =>
        AssertContract<SpaceUri>(
            "at://did:plc:abc123/space/com.example.forum/a",
            "at://did:plc:abc123/space/com.example.forum/b",
            "at://alice.bsky.social/space/com.example.forum/a");

    [Fact]
    public void SpaceRecordUri_ImplementsParsingContract() =>
        AssertContract<SpaceRecordUri>(
            "at://did:plc:abc123/space/com.example.forum/a/did:plc:def456/com.example.post/1",
            "at://did:plc:abc123/space/com.example.forum/a/did:plc:def456/com.example.post/2",
            "at://did:plc:abc123/space/com.example.forum/a");

    [Fact]
    public void ImplicitConversionsToString_NullInput_YieldNull()
    {
        Assert.Null((string?)(Did?)null);
        Assert.Null((string?)(Handle?)null);
        Assert.Null((string?)(AtIdentifier?)null);
        Assert.Null((string?)(Nsid?)null);
        Assert.Null((string?)(Tid?)null);
        Assert.Null((string?)(RecordKey?)null);
        Assert.Null((string?)(Cid?)null);
        Assert.Null((string?)(AtUri?)null);
        Assert.Null((string?)(SpaceUri?)null);
        Assert.Null((string?)(SpaceRecordUri?)null);
    }

    [Fact]
    public void ImplicitConversionsToAtIdentifier_NullInput_YieldNull()
    {
        Assert.Null((AtIdentifier?)(Did?)null);
        Assert.Null((AtIdentifier?)(Handle?)null);
    }

    [Fact]
    public void ExplicitConversionsFromString_ParseOrThrow()
    {
        Assert.Equal(Did.Parse("did:plc:abc123"), (Did)"did:plc:abc123");
        Assert.Equal(Handle.Parse("alice.bsky.social"), (Handle)"@Alice.Bsky.Social");
        Assert.Equal(AtIdentifier.Parse("did:plc:abc123"), (AtIdentifier)"did:plc:abc123");
        Assert.Equal(Nsid.Parse("app.bsky.feed.post"), (Nsid)"app.bsky.feed.post");
        Assert.Equal(RecordKey.Self, (RecordKey)"self");
        Assert.Equal(
            SpaceUri.Parse("at://did:plc:abc123/space/com.example.forum/a"),
            (SpaceUri)"at://did:plc:abc123/space/com.example.forum/a");

        Assert.ThrowsAny<ArgumentException>(() => (Nsid)"app.bsky");
        Assert.ThrowsAny<ArgumentException>(() => (RecordKey)"..");
        Assert.ThrowsAny<ArgumentException>(() => (AtIdentifier)"@");
        Assert.ThrowsAny<ArgumentException>(() => (SpaceRecordUri)"at://did:plc:abc123/space/com.example.forum/a");
    }

    [Fact]
    public void Parse_NullInput_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Did.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => Handle.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => AtIdentifier.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => Nsid.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => Tid.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => RecordKey.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => AtUri.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => SpaceUri.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => SpaceRecordUri.Parse(null!));
    }

    [Fact]
    public void Sort_UsesOrdinalOrder()
    {
        var handles = new[] { "b.test", "a.test", "c.test" }.Select(Handle.Parse).ToList();

        handles.Sort();

        Assert.Equal(["a.test", "b.test", "c.test"], handles.Select(h => h.Value));
    }

    /// <summary>
    /// Exercises the members generic code sees. <paramref name="lower"/> must sort before
    /// <paramref name="higher"/>, and both must already be in normalized form.
    /// </summary>
    private static void AssertContract<T>(string lower, string higher, string invalid)
        where T : class, ISpanParsable<T>, IComparable<T>, IEquatable<T>
    {
        var parsed = StringParsing<T>.Parse(lower);
        Assert.Equal(lower, parsed.ToString());

        Assert.True(StringParsing<T>.TryParse(lower, out var fromString));
        Assert.True(T.TryParse(lower.AsSpan(), provider: null, out var fromSpan));
        Assert.Equal(parsed, fromString);
        Assert.Equal(parsed, fromSpan);
        Assert.Equal(parsed, T.Parse(lower.AsSpan(), provider: null));
        Assert.Equal(0, parsed.CompareTo(fromSpan));

        var other = StringParsing<T>.Parse(higher);
        Assert.True(parsed.CompareTo(other) < 0);
        Assert.True(other.CompareTo(parsed) > 0);
        Assert.True(parsed.CompareTo(null) > 0);
        Assert.False(parsed.Equals(other));

        Assert.False(StringParsing<T>.TryParse(invalid, out _));
        Assert.False(StringParsing<T>.TryParse(null, out _));
        Assert.False(T.TryParse(invalid.AsSpan(), provider: null, out _));
        Assert.Throws<FormatException>(() => StringParsing<T>.Parse(invalid));
        Assert.Throws<FormatException>(() => T.Parse(invalid.AsSpan(), provider: null));
        Assert.Throws<ArgumentNullException>(() => StringParsing<T>.Parse(null!));
    }

    // Under an ISpanParsable<T> constraint, T.Parse(string, ...) binds to the span overload, so
    // the IParsable<T> members are reached through a constraint that offers only them.
    private static class StringParsing<T> where T : IParsable<T>
    {
        public static T Parse(string s) => T.Parse(s, provider: null);

        public static bool TryParse(string? s, out T? result) => T.TryParse(s, provider: null, out result);
    }
}
