using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Syntax;

public class TidGeneratorTests
{
    [Fact]
    [Trait("Spec", "SYN-34")]
    public void Next_SpecExampleInstantAndClockId_ProducesSpecExampleTid()
    {
        // 3jzfcijpj2z2a, the TID spec's example, is 2023-06-30T15:03:01.887007Z with clock id 6.
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddTicks(1_688_137_381_887_007 * 10));

        Assert.Equal("3jzfcijpj2z2a", new TidGenerator(clockId: 6, timeProvider: clock).Next().Value);
    }

    [Fact]
    [Trait("Spec", "SYN-30")]
    public void Next_100kSequentialCalls_StrictlyIncreasing()
    {
        var generator = new TidGenerator();
        var previous = generator.Next();

        for (var i = 0; i < 100_000; i++)
        {
            var next = generator.Next();
            Assert.True(next.CompareTo(previous) > 0, $"{next} did not follow {previous} (call {i})");
            previous = next;
        }
    }

    [Fact]
    [Trait("Spec", "SYN-30")]
    public void Next_ConcurrentCallers_EachStrictlyIncreasingAndAllDistinct()
    {
        const int callers = 8, callsPerCaller = 12_500;
        var generator = new TidGenerator();
        var sequences = new ConcurrentBag<long[]>();

        Parallel.For(0, callers, new ParallelOptions { MaxDegreeOfParallelism = callers }, _ =>
        {
            var values = new long[callsPerCaller];
            for (var i = 0; i < callsPerCaller; i++) values[i] = generator.Next().ToInt64();
            sequences.Add(values);
        });

        foreach (var values in sequences)
            for (var i = 1; i < values.Length; i++) Assert.True(values[i] > values[i - 1], $"caller sequence decreased at {i}");
        var all = sequences.SelectMany(values => values).ToList();
        Assert.Equal(callers * callsPerCaller, all.Distinct().Count());
    }

    [Fact]
    [Trait("Spec", "SYN-31")]
    public void Next_FrozenClock_AdvancesOneMicrosecondPerCall()
    {
        var generator = new TidGenerator(clockId: 42, timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero)));

        var first = generator.Next().ToInt64();
        var second = generator.Next().ToInt64();
        var third = generator.Next().ToInt64();

        Assert.Equal(1L << 10, second - first);
        Assert.Equal(1L << 10, third - second);
    }

    [Fact]
    [Trait("Spec", "SYN-31")]
    public void Next_ClockStepsBackwards_StillIncreases()
    {
        var start = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        var generator = new TidGenerator(timeProvider: new ScriptedClock(start, start.AddSeconds(-5)));

        var before = generator.Next();
        var after = generator.Next();

        Assert.True(after.CompareTo(before) > 0);
    }

    [Fact]
    [Trait("Spec", "SYN-32"), Trait("Spec", "SYN-33")]
    public void Next_ClockAdvances_EncodesClockTimeAndClockId()
    {
        var instant = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero).AddTicks(1230);
        var generator = new TidGenerator(clockId: 1023, timeProvider: new FakeTimeProvider(instant));

        var tid = generator.Next();

        Assert.Equal((instant - DateTimeOffset.UnixEpoch).Ticks / 10, tid.TimestampMicroseconds);
        Assert.Equal(1023, tid.ClockId);
    }

    [Fact]
    [Trait("Spec", "SYN-32")]
    public void Next_EveryTid_CarriesTheGeneratorsClockId()
    {
        var generator = new TidGenerator();

        Assert.InRange(generator.ClockId, 0, TidGenerator.MaxClockId);
        for (var i = 0; i < 100; i++) Assert.Equal(generator.ClockId, generator.Next().ClockId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1024)]
    [Trait("Spec", "SYN-32")]
    public void Constructor_ClockIdOutOfRange_Throws(int clockId) => Assert.Throws<ArgumentOutOfRangeException>(() => new TidGenerator(clockId));

    [Fact]
    [Trait("Spec", "SYN-33")]
    public void Next_ClockBeforeTheEpoch_CountsAsTheEpoch()
    {
        var generator = new TidGenerator(5, new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddDays(-1)));

        Assert.Equal(0, generator.Next().TimestampMicroseconds);
    }

    [Fact]
    [Trait("Spec", "SYN-33")]
    public void Next_TimestampBeyond53Bits_Throws()
    {
        var generator = new TidGenerator(timeProvider: new FakeTimeProvider(DateTimeOffset.MaxValue));

        Assert.Throws<InvalidOperationException>(() => generator.Next());
    }

    [Fact]
    [Trait("Spec", "SYN-35")]
    public void Default_IsOneInstanceBehindTidNext()
    {
        Assert.Same(TidGenerator.Default, TidGenerator.Default);
        Assert.Equal(TidGenerator.Default.ClockId, Tid.Next().ClockId);
    }

    private sealed class ScriptedClock(params DateTimeOffset[] instants) : TimeProvider
    {
        private int _next;

        public override DateTimeOffset GetUtcNow() => instants[_next++];
    }
}
