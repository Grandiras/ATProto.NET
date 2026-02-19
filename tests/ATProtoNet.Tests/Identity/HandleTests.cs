using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

public class HandleTests
{
    [Theory]
    [InlineData("alice.bsky.social")]
    [InlineData("bob.example.com")]
    [InlineData("user123.test.dev")]
    [InlineData("my-handle.long-domain.co.uk")]
    public void Parse_ValidHandle_Succeeds(string value)
    {
        var handle = Handle.Parse(value);
        Assert.Equal(value.ToLowerInvariant(), handle.Value);
    }

    [Fact]
    public void Parse_NormalizesToLowercase()
    {
        var handle = Handle.Parse("Alice.Bsky.Social");
        Assert.Equal("alice.bsky.social", handle.Value);
    }

    [Fact]
    public void Parse_StripsLeadingAt()
    {
        var handle = Handle.Parse("@alice.bsky.social");
        Assert.Equal("alice.bsky.social", handle.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-handle")]    // Single segment
    [InlineData("abc")]             // No dots
    [InlineData(".bsky.social")]    // Leading dot
    [InlineData("alice.")]          // Trailing dot
    [InlineData("-bad.bsky.social")]  // Leading hyphen in label
    public void Parse_InvalidHandle_Throws(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => Handle.Parse(value));
    }

    [Fact]
    public void Parse_NullValue_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => Handle.Parse(null!));
    }

    [Fact]
    public void Parse_ExceedsMaxLength_Throws()
    {
        var longHandle = string.Join(".", Enumerable.Repeat("abcdefghij", 30)) + ".com";
        Assert.True(longHandle.Length > 253);
        Assert.Throws<ArgumentException>(() => Handle.Parse(longHandle));
    }

    [Theory]
    [InlineData("alice.bsky.social", true)]
    [InlineData("@alice.bsky.social", true)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_ReturnsExpected(string? value, bool expected)
    {
        var result = Handle.TryParse(value, out var handle);
        Assert.Equal(expected, result);

        if (expected)
            Assert.NotNull(handle);
        else
            Assert.Null(handle);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var a = Handle.Parse("alice.bsky.social");
        var b = Handle.Parse("ALICE.bsky.SOCIAL"); // Case insensitive

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        var a = Handle.Parse("alice.bsky.social");
        var b = Handle.Parse("bob.bsky.social");

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void ImplicitCast_ToString_Works()
    {
        var handle = Handle.Parse("alice.bsky.social");
        string value = handle;
        Assert.Equal("alice.bsky.social", value);
    }

    [Fact]
    public void ExplicitCast_FromString_Works()
    {
        var handle = (Handle)"alice.bsky.social";
        Assert.Equal("alice.bsky.social", handle.Value);
    }
}
