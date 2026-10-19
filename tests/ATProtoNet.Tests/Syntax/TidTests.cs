namespace ATProtoNet.Tests.Syntax;

public class TidTests
{
    [Theory]
    [InlineData("jzzzzzzzzzzzz", true)]
    [InlineData("kjzfcijpj2z2a", false)]
    [InlineData("3jzfcijpj2z2", false)]
    [InlineData("3jzfcijpj2z2aa", false)]
    [InlineData("3JZFCIJPJ2Z2A", false)]
    [InlineData("3jzfcijpj2z1a", false)]
    [Trait("Spec", "SYN-27")]
    public void TryParse_Structure_FollowsTheAlphabetAndTheHighBit(string value, bool valid) => Assert.Equal(valid, Tid.TryParse(value, out _));

    [Fact]
    [Trait("Spec", "SYN-27"), Trait("Spec", "SYN-28")]
    public void Parse_LargestTid_RoundTripsThroughItsRawValue()
    {
        var largest = Tid.Parse("jzzzzzzzzzzzz");

        Assert.Equal("jzzzzzzzzzzzz", Tid.FromInt64(largest.ToInt64()).Value);
        Assert.Equal(-1, largest.ToInt64());
        Assert.True(largest.CompareTo(Tid.Parse("3jzfcijpj2z2a")) > 0);
    }

    [Fact]
    [Trait("Spec", "SYN-28")]
    public void FromInt64_ToInt64_RoundTrip()
    {
        var tid = Tid.Parse("3jzfcijpj2z2a");

        Assert.Equal(1_728_652_679_052_295_174, tid.ToInt64());
        Assert.Equal(tid, Tid.FromInt64(tid.ToInt64()));
        Assert.Equal(1_688_137_381_887_007, tid.TimestampMicroseconds);
        Assert.Equal(6, tid.ClockId);
    }

    [Fact]
    [Trait("Spec", "SYN-28")]
    public void Order_OfTextAndOfValue_Agree()
    {
        var random = new Random(28);
        var values = Enumerable.Range(0, 500).Select(_ => random.NextInt64()).Append(0).Append(long.MaxValue).Append(-1).OrderBy(v => (ulong)v).ToList();
        var byValue = values.Select(Tid.FromInt64).ToList();

        Assert.Equal(byValue, byValue.OrderBy(t => t).ToList());
        Assert.Equal(byValue.Select(t => t.Value), byValue.Select(t => t.Value).Order(StringComparer.Ordinal));
        Assert.All(byValue, t => Assert.Equal(t, Tid.Parse(t.Value)));
    }

    [Fact]
    [Trait("Spec", "SYN-29")]
    public void Default_IsTidZero()
    {
        Assert.Equal("2222222222222", default(Tid).Value);
        Assert.Equal(0, default(Tid).ToInt64());
        Assert.Equal(default, Tid.Parse("2222222222222"));
    }

    [Fact]
    [Trait("Spec", "SYN-5")]
    public void Convert_ToString_IsTheText()
    {
        string text = Tid.Parse("3jzfcijpj2z2a");

        Assert.Equal("3jzfcijpj2z2a", text);
    }

    [Fact]
    [Trait("Spec", "SYN-35")]
    public void Next_RapidCalls_StrictlyIncreasing()
    {
        // Regression: Next() used to add a random clock id to a millisecond timestamp per call, so values minted within a millisecond came out of order.
        var tids = Enumerable.Range(0, 20_000).Select(_ => Tid.Next()).ToList();

        for (var i = 1; i < tids.Count; i++) Assert.True(tids[i].CompareTo(tids[i - 1]) > 0, $"{tids[i]} did not follow {tids[i - 1]}");
        Assert.Equal(TidGenerator.Default.ClockId, tids[0].ClockId);
    }
}
