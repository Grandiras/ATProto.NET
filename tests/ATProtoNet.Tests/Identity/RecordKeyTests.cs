using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

public class RecordKeyTests
{
    [Theory]
    [InlineData("self")]
    [InlineData("3k2la")]
    [InlineData("my-record_key.123")]
    [InlineData("abc123")]
    public void Parse_ValidRecordKey_Succeeds(string value)
    {
        var rkey = RecordKey.Parse(value);
        Assert.Equal(value, rkey.Value);
    }

    [Theory]
    [InlineData(".")]     // Dot alone not allowed
    [InlineData("..")]    // Double dot not allowed
    [InlineData("")]
    [InlineData(" ")]
    public void Parse_InvalidRecordKey_Throws(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => RecordKey.Parse(value));
    }

    [Fact]
    public void Self_IsValidSingleton()
    {
        Assert.Equal("self", RecordKey.Self.Value);
    }

    [Fact]
    public void NewTid_GeneratesValidRecordKey()
    {
        var rkey = RecordKey.NewTid();

        Assert.Equal(13, rkey.Value.Length);
        Assert.True(RecordKey.TryParse(rkey.Value, out _));
    }

    [Theory]
    [InlineData("self", true)]
    [InlineData("abc123", true)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData(null, false)]
    public void TryParse_ReturnsExpected(string? value, bool expected)
    {
        var result = RecordKey.TryParse(value, out var rkey);
        Assert.Equal(expected, result);

        if (expected)
            Assert.NotNull(rkey);
        else
            Assert.Null(rkey);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var a = RecordKey.Parse("self");
        var b = RecordKey.Parse("self");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        var a = RecordKey.Parse("self");
        var b = RecordKey.Parse("abc123");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ImplicitCast_ToString_Works()
    {
        var rkey = RecordKey.Parse("self");
        string value = rkey;
        Assert.Equal("self", value);
    }
}
