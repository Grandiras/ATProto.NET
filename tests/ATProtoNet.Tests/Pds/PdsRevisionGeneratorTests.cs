using ATProtoNet.Identity;
using ATProtoNet.Pds;

namespace ATProtoNet.Tests.Pds;

public sealed class PdsRevisionGeneratorTests
{
    [Fact]
    public void Next_ProducesAValidTid()
    {
        Assert.True(Tid.TryParse(new PdsRevisionGenerator().Next(), out _));
    }

    [Fact]
    public void Next_RapidSuccession_IsStrictlyIncreasing()
    {
        // Tid.Next() cannot guarantee this — it resolves to the millisecond and randomizes the
        // clock id, so a burst of commits inside one millisecond would sort arbitrarily.
        var generator = new PdsRevisionGenerator();
        var revs = Enumerable.Range(0, 1000).Select(_ => generator.Next()).ToList();

        Assert.Equal(revs.Order(StringComparer.Ordinal), revs);
        Assert.Equal(revs.Count, revs.Distinct().Count());
    }

    [Fact]
    public void Next_RespectsAPreviousRevisionFloor()
    {
        // Simulates a restart: the generator has issued nothing, but the stored head already
        // carries a revision far in the future.
        var future = Tid.FromInt64((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000) * 1000L << 10);
        var next = new PdsRevisionGenerator().Next(future.Value);

        Assert.True(string.CompareOrdinal(next, future.Value) > 0);
    }

    [Fact]
    public void Next_UnparseableFloor_IsIgnored()
    {
        Assert.True(Tid.TryParse(new PdsRevisionGenerator().Next("not-a-tid"), out _));
    }

    [Fact]
    public void Next_NullFloor_IsIgnored()
    {
        Assert.True(Tid.TryParse(new PdsRevisionGenerator().Next(null), out _));
    }

    [Fact]
    public async Task Next_ConcurrentCallers_NeverCollideOrGoBackwards()
    {
        var generator = new PdsRevisionGenerator();
        var results = new System.Collections.Concurrent.ConcurrentBag<string>();

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++) results.Add(generator.Next());
        })));

        var all = results.ToList();
        Assert.Equal(3200, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Next_AdvancesWithWallClockWhenNotClamping()
    {
        var generator = new PdsRevisionGenerator();
        var first = Tid.Parse(generator.Next()).ToInt64();

        Thread.Sleep(20);

        var second = Tid.Parse(generator.Next()).ToInt64();

        // Far more than the +1 the clamp would contribute, so the timestamp really did move.
        Assert.True(second - first > 1000);
    }
}
