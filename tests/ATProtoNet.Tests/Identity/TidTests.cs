using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

public class TidTests
{
    [Theory]
    [InlineData("2222222222222")]
    [InlineData("jzzzzzzzzzzzz")]        // Largest TID: the high bit stays clear
    [InlineData("abcdefghijklm")]
    public void Parse_ValidTid_Succeeds(string value)
    {
        var tid = Tid.Parse(value);
        Assert.Equal(value, tid.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("short")]                // Too short
    [InlineData("22222222222221")]       // 14 chars (too long)
    [InlineData("0000000000000")]        // '0' not in base32-sortable
    [InlineData("1111111111111")]        // '1' not in base32-sortable
    [InlineData("AAAAAAAAAAAAA")]        // Uppercase not allowed
    [InlineData("kjzfcijpj2z2a")]        // First character sets the high bit
    [InlineData("zzzzzzzzzzzzz")]
    public void Parse_InvalidTid_Throws(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => Tid.Parse(value));
    }

    [Fact]
    public void Next_GeneratesValidTid()
    {
        var tid = Tid.Next();

        Assert.Equal(13, tid.Value.Length);
        Assert.True(Tid.TryParse(tid.Value, out _));
    }

    [Fact]
    public void Next_RapidCalls_StrictlyIncreasing()
    {
        // Regression: Next() used to add a random clock id to a millisecond timestamp per call,
        // so values minted within one millisecond came out of order and could repeat.
        var tids = Enumerable.Range(0, 20_000).Select(_ => Tid.Next()).ToList();

        for (var i = 1; i < tids.Count; i++)
            Assert.True(tids[i].CompareTo(tids[i - 1]) > 0, $"{tids[i]} did not follow {tids[i - 1]}");
    }

    [Fact]
    public void NextString_ReturnsValidTidString()
    {
        var value = Tid.NextString();

        Assert.Equal(13, value.Length);
        Assert.True(Tid.TryParse(value, out _));
    }

    [Theory]
    [InlineData("2222222222222", true)]
    [InlineData("invalid", false)]
    [InlineData(null, false)]
    public void TryParse_ReturnsExpected(string? value, bool expected)
    {
        var result = Tid.TryParse(value, out var tid);
        Assert.Equal(expected, result);

        if (expected)
            Assert.NotNull(tid);
        else
            Assert.Null(tid);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var a = Tid.Parse("abcdefghijklm");
        var b = Tid.Parse("abcdefghijklm");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void CompareTo_OrdersCorrectly()
    {
        var a = Tid.Parse("2222222222222");
        var b = Tid.Parse("jzzzzzzzzzzzz");

        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
        Assert.True(a.CompareTo(null) > 0);
    }

    [Fact]
    public void FromInt64_ToInt64_RoundTrip()
    {
        var tid = Tid.Parse("3jzfcijpj2z2a");

        Assert.Equal(1_728_652_679_052_295_174, tid.ToInt64());
        Assert.Equal(tid, Tid.FromInt64(tid.ToInt64()));
    }

    [Fact]
    public void ExplicitConversion_FromString_Parses()
    {
        Assert.Equal(Tid.Parse("3jzfcijpj2z2a"), (Tid)"3jzfcijpj2z2a");
        Assert.ThrowsAny<ArgumentException>(() => (Tid)"zzzzzzzzzzzzz");
    }
}
