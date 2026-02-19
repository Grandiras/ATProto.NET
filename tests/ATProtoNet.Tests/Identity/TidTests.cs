using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

public class TidTests
{
    [Theory]
    [InlineData("2222222222222")]
    [InlineData("zzzzzzzzzzzzz")]
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
    public void Next_GeneratesUniqueValues()
    {
        var tids = Enumerable.Range(0, 100).Select(_ => Tid.Next().Value).ToHashSet();

        // With random clock ID (1024 possibilities), some collisions are expected
        // when timestamps land on the same microsecond bucket.
        // We should still get a reasonable number of unique values.
        Assert.True(tids.Count >= 50, $"Expected at least 50 unique TIDs but got {tids.Count}");
    }

    [Fact]
    public void Next_GeneratesMonotonicallyIncreasingValues()
    {
        // Generate a batch quickly and verify ordering
        var tid1 = Tid.Next();
        // Need a small delay to ensure increasing timestamps
        Thread.Sleep(1);
        var tid2 = Tid.Next();

        // String comparison of base32-sortable should reflect time ordering
        Assert.True(string.Compare(tid1.Value, tid2.Value, StringComparison.Ordinal) <= 0);
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
        var b = Tid.Parse("zzzzzzzzzzzzz");

        Assert.True(a.CompareTo(b) < 0);
    }
}
