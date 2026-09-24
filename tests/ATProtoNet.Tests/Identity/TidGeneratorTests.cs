using System.Collections.Concurrent;
using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Identity;

public class TidGeneratorTests
{
    [Fact]
    public void Next_SpecExampleInstantAndClockId_ProducesSpecExampleTid()
    {
        // 3jzfcijpj2z2a, the TID spec's example, is 2023-06-30T15:03:01.887007Z with clock id 6.
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch.AddTicks(1_688_137_381_887_007 * 10));
        var generator = new TidGenerator(clockId: 6, timeProvider: clock);

        Assert.Equal("3jzfcijpj2z2a", generator.Next().Value);
    }

    [Fact]
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
    public void Next_ConcurrentCallers_EachStrictlyIncreasingAndAllDistinct()
    {
        const int callers = 8;
        const int callsPerCaller = 12_500;
        var generator = new TidGenerator();
        var sequences = new ConcurrentBag<long[]>();

        Parallel.For(0, callers, new ParallelOptions { MaxDegreeOfParallelism = callers }, _ =>
        {
            var values = new long[callsPerCaller];
            for (var i = 0; i < callsPerCaller; i++)
                values[i] = generator.Next().ToInt64();
            sequences.Add(values);
        });

        foreach (var values in sequences)
        {
            for (var i = 1; i < values.Length; i++)
                Assert.True(values[i] > values[i - 1], $"caller sequence decreased at {i}");
        }

        var all = sequences.SelectMany(values => values).ToList();
        Assert.Equal(callers * callsPerCaller, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Next_FrozenClock_AdvancesOneMicrosecondPerCall()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        var generator = new TidGenerator(clockId: 42, timeProvider: clock);

        var first = generator.Next().ToInt64();
        var second = generator.Next().ToInt64();
        var third = generator.Next().ToInt64();

        Assert.Equal(1L << 10, second - first);
        Assert.Equal(1L << 10, third - second);
    }

    [Fact]
    public void Next_ClockStepsBackwards_StillIncreases()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        var generator = new TidGenerator(timeProvider: clock);
        var before = generator.Next();

        clock.Now -= TimeSpan.FromSeconds(5);
        var after = generator.Next();

        Assert.True(after.CompareTo(before) > 0);
    }

    [Fact]
    public void Next_ClockAdvances_EncodesClockTimeAndClockId()
    {
        var instant = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero).AddTicks(1230);
        var clock = new ManualTimeProvider(instant);
        var generator = new TidGenerator(clockId: 1023, timeProvider: clock);

        var value = generator.Next().ToInt64();

        Assert.Equal((instant - DateTimeOffset.UnixEpoch).Ticks / 10, value >> 10);
        Assert.Equal(1023, value & 0x3FF);
    }

    [Fact]
    public void Next_EveryTid_CarriesTheGeneratorsClockId()
    {
        var generator = new TidGenerator();

        for (var i = 0; i < 100; i++)
            Assert.Equal(generator.ClockId, generator.Next().ToInt64() & 0x3FF);
    }

    [Fact]
    public void Constructor_NoClockId_DrawsOneInRange()
    {
        var generator = new TidGenerator();

        Assert.InRange(generator.ClockId, 0, TidGenerator.MaxClockId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1024)]
    public void Constructor_ClockIdOutOfRange_Throws(int clockId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TidGenerator(clockId));
    }

    [Fact]
    public void Next_TimestampBeyond53Bits_Throws()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.MaxValue);
        var generator = new TidGenerator(timeProvider: clock);

        Assert.Throws<InvalidOperationException>(() => generator.Next());
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
