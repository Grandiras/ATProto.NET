using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Syntax;

public class AtDatetimeTests
{
    private sealed record Holder(AtDatetime When, AtDatetime? Maybe = null);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private static AtDatetime Read(string text) => JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(new { when = text }), Options)!.When;

    [Theory]
    [InlineData("1985-04-12T23:20:50.123Z")]
    [InlineData("1985-04-12T23:20:50Z")]
    [InlineData("1985-04-12T23:20:50.123456789012Z")]
    [InlineData("1985-04-12T23:20:50.123+01:45")]
    [InlineData("0000-01-01T00:00:00.000Z")]
    [InlineData("2024-02-29T12:00:00Z")]
    [Trait("Spec", "SYN-41"), Trait("Spec", "SYN-43")]
    public void Parse_Valid_KeepsTheTextExactly(string value)
    {
        var parsed = AtDatetime.Parse(value);

        Assert.True(parsed.IsValid);
        Assert.Equal(value, parsed.Value);
        Assert.Equal(value, parsed.ToString());
    }

    [Theory]
    [InlineData("1985-04-12T23:20:50.123z")]
    [InlineData("1985-04-12t23:20:50.123Z")]
    [InlineData("1985-04-12 23:20:50.123Z")]
    [InlineData("1985-04-12T23:20:50.123")]
    [InlineData("1985-04-12T23:20:50.Z")]
    [InlineData("1985-04-12T23:20:50.123456789012345678901Z")]
    [InlineData("1985-04-12T23:20:50.123-00:00")]
    [InlineData("1985-04-12T23:20:50.123+0000")]
    [InlineData("1985-02-29T12:00:00Z")]
    [InlineData("2023-02-29T12:00:00Z")]
    [InlineData("1985-04-12T23:20:60Z")]
    [InlineData("1985-04-12T24:00:00Z")]
    [InlineData("1985-13-12T23:20:50Z")]
    [InlineData("0000-01-01T00:00:00+01:00")]
    [InlineData("1985-04-12T23:20:50+24:00")]
    [InlineData("１985-04-12T23:20:50Z")]
    [Trait("Spec", "SYN-41"), Trait("Spec", "SYN-1")]
    public void TryParse_Invalid_Rejects(string value)
    {
        Assert.False(AtDatetime.TryParse(value, out _));
        Assert.Throws<FormatException>(() => AtDatetime.Parse(value));
    }

    [Fact]
    [Trait("Spec", "SYN-41"), Trait("Spec", "SYN-9")]
    public void TryParse_FractionDigits_AreOneToTwenty()
    {
        Assert.True(AtDatetime.TryParse("1985-04-12T23:20:50." + new string('1', AtDatetime.MaxFractionDigits) + "Z", out _));
        Assert.False(AtDatetime.TryParse("1985-04-12T23:20:50." + new string('1', AtDatetime.MaxFractionDigits + 1) + "Z", out _));
        Assert.False(AtDatetime.TryParse("1985-04-12T23:20:50Z" + new string(' ', AtDatetime.MaxLength), out _));
    }

    [Fact]
    [Trait("Spec", "SYN-42")]
    public void YearZero_IsValidButOutsideTheDateTimeOffsetRange()
    {
        var datetime = AtDatetime.Parse("0000-01-01T00:00:00.000Z");

        Assert.True(datetime.IsValid);
        Assert.False(datetime.TryGetInstant(out _));
    }

    [Fact]
    [Trait("Spec", "SYN-44")]
    public void From_DateTimeOffset_WritesTheCanonicalUtcForm()
    {
        var value = new DateTimeOffset(2026, 9, 24, 14, 30, 45, 123, TimeSpan.FromHours(2)).AddTicks(4567);

        var datetime = AtDatetime.From(value);

        Assert.Equal("2026-09-24T12:30:45.123Z", datetime.Value);
        Assert.True(datetime.IsValid);
        Assert.True(datetime.TryGetInstant(out var instant));
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 12, 30, 45, 123, TimeSpan.Zero), instant);
        Assert.Equal(AtDatetime.Parse("2026-09-24T12:30:45.123Z"), datetime);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [Trait("Spec", "SYN-44")]
    public void From_DateTimeUtcOrUnspecified_IsTakenAsUtc(DateTimeKind kind) =>
        Assert.Equal("2026-09-24T12:30:45.123Z", AtDatetime.From(new DateTime(2026, 9, 24, 12, 30, 45, 123, kind)).Value);

    [Fact]
    [Trait("Spec", "SYN-44")]
    public void From_DateTimeLocal_IsConvertedToUtc()
    {
        var local = new DateTime(2026, 9, 24, 12, 30, 45, 123, DateTimeKind.Local);

        Assert.True(AtDatetime.From(local).TryGetInstant(out var instant));
        Assert.Equal(new DateTimeOffset(local).UtcDateTime, instant.UtcDateTime);
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("fi-FI")]
    [Trait("Spec", "SYN-44")]
    public void Now_UnderAnyCulture_IsACanonicalGregorianUtcTimestampOfTheInjectedClock(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var now = AtDatetime.Now(new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)));

            Assert.Equal("2026-10-01T12:00:00.000Z", now.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    [Trait("Spec", "SYN-45")]
    public void Producers_OnlyYieldValidValues()
    {
        Assert.True(AtDatetime.Now(new FakeTimeProvider()).IsValid);
        Assert.True(AtDatetime.From(DateTimeOffset.UnixEpoch).IsValid);
        Assert.True(AtDatetime.Parse("1985-04-12T23:20:50Z").IsValid);
        Assert.True(AtDatetime.TryParse("1985-04-12T23:20:50Z"u8, out var fromBytes) && fromBytes.IsValid);
    }

    [Theory]
    [InlineData("1985-04-12T23:20:50.123456789012-07:00", -420)]
    [InlineData("1985-04-12T23:20:50.123+05:30", 330)]
    [InlineData("1985-04-12T23:20:50-00:30", -30)]
    [InlineData("1985-04-12T23:20:50.1234567+14:00", 840)]
    [InlineData("1985-04-12T23:20:50Z", 0)]
    [Trait("Spec", "SYN-47")]
    public void TryGetInstant_Valid_KeepsTheWrittenOffset(string text, int offsetMinutes)
    {
        Assert.True(AtDatetime.Parse(text).TryGetInstant(out var instant));
        Assert.Equal(TimeSpan.FromMinutes(offsetMinutes), instant.Offset);
        Assert.Equal(DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).UtcTicks / 10, instant.UtcTicks / 10);
    }

    [Fact]
    [Trait("Spec", "SYN-47")]
    public void TryGetInstant_OffsetBeyondFourteenHours_IsNormalizedToUtc()
    {
        var datetime = AtDatetime.Parse("1985-04-12T23:20:50+20:00");

        Assert.True(datetime.IsValid);
        Assert.True(datetime.TryGetInstant(out var instant));
        Assert.Equal(TimeSpan.Zero, instant.Offset);
        Assert.Equal(new DateTime(1985, 4, 12, 3, 20, 50, DateTimeKind.Utc), instant.UtcDateTime);
    }

    [Theory]
    [InlineData("1985-04-12T23:20:50.123z")]
    [InlineData("1985-04-12 23:20:50.123+02:00")]
    [InlineData("not a date")]
    [InlineData("04/12/1985")]
    [InlineData("")]
    [Trait("Spec", "SYN-43")]
    public void Json_AnyString_RoundTripsByteForByte(string value)
    {
        var json = JsonSerializer.Serialize(new { when = value });

        var holder = JsonSerializer.Deserialize<Holder>(json, Options)!;

        Assert.Equal(value, holder.When.Value);
        Assert.Equal(json, JsonSerializer.Serialize(holder, Options));
    }

    [Theory]
    [InlineData("2023-04-01T10:00:00", "2023-04-01T10:00:00Z", 0)]
    [InlineData("1985-04-12t23:20:50.123z", "1985-04-12T23:20:50.123Z", 0)]
    [InlineData("1985-04-12 23:20:50.123+02:00", "1985-04-12T21:20:50.123Z", 120)]
    [InlineData("1985-04-12T23:20:50.123-0530", "1985-04-13T04:50:50.123Z", -330)]
    [Trait("Spec", "SYN-46")]
    public void Json_NearMissIso8601_IsNotValidButHasAnInstantAndItsOffset(string text, string instant, int offsetMinutes)
    {
        var datetime = Read(text);

        Assert.False(datetime.IsValid);
        Assert.True(datetime.TryGetInstant(out var value));
        Assert.Equal(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture), value);
        Assert.Equal(TimeSpan.FromMinutes(offsetMinutes), value.Offset);
        Assert.Equal(text, datetime.Value);
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("04/12/1985")]
    [InlineData("")]
    [Trait("Spec", "SYN-46")]
    public void Json_Unreadable_KeepsTheTextWithoutAnInstant(string text)
    {
        var datetime = Read(text);

        Assert.False(datetime.IsValid);
        Assert.False(datetime.TryGetInstant(out _));
        Assert.Equal(text, datetime.Value);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("true")]
    [InlineData("{}")]
    [Trait("Spec", "SYN-46")]
    public void Json_NonStringToken_Throws(string token) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Holder>($$"""{"when":{{token}}}""", Options));

    [Fact]
    [Trait("Spec", "SYN-72")]
    public void Json_NullOptional_IsOmitted()
    {
        Assert.Equal("""{"when":"2024-01-01T00:00:00Z"}""", JsonSerializer.Serialize(new Holder(AtDatetime.Parse("2024-01-01T00:00:00Z")), Options));
        Assert.Null(JsonSerializer.Deserialize<Holder>("""{"when":"2024-01-01T00:00:00Z","maybe":null}""", Options)!.Maybe);
    }

    [Fact]
    [Trait("Spec", "SYN-48")]
    public void Equality_IsOrdinalOnTheText()
    {
        var a = AtDatetime.Parse("1985-04-12T23:20:50Z");
        var b = AtDatetime.Parse("1985-04-12T23:20:50.000Z");

        Assert.NotEqual(a, b);
        Assert.True(a.TryGetInstant(out var instantA) && b.TryGetInstant(out var instantB) && instantA == instantB);
        Assert.Equal(a, AtDatetime.Parse("1985-04-12T23:20:50Z"));
        Assert.Equal(a.GetHashCode(), AtDatetime.Parse("1985-04-12T23:20:50Z").GetHashCode());
    }

    [Fact]
    [Trait("Spec", "SYN-48")]
    public void CompareTo_OrdersByInstantAcrossOffsets()
    {
        var utc = AtDatetime.Parse("1985-04-12T23:20:50Z");
        var laterInPacificTime = AtDatetime.Parse("1985-04-12T16:20:51-07:00");
        var earlierInEuropeanTime = AtDatetime.Parse("1985-04-13T01:20:49+02:00");

        Assert.Equal([earlierInEuropeanTime, utc, laterInPacificTime], new[] { laterInPacificTime, utc, earlierInEuropeanTime }.Order());
    }

    [Fact]
    [Trait("Spec", "SYN-48")]
    public void CompareTo_ValuesWithoutAnInstant_SortFirst()
    {
        var unreadable = Read("soon");
        var valid = AtDatetime.Parse("0001-01-01T00:00:00Z");

        Assert.True(unreadable.CompareTo(valid) < 0);
        Assert.True(valid.CompareTo(unreadable) > 0);
    }

    [Fact]
    [Trait("Spec", "SYN-48")]
    public void CompareTo_SameInstantDifferentText_IsConsistentWithEquality()
    {
        var a = AtDatetime.Parse("1985-04-12T23:20:50Z");
        var b = AtDatetime.Parse("1985-04-12T23:20:50.000Z");

        Assert.NotEqual(0, a.CompareTo(b));
        Assert.Equal(-a.CompareTo(b), b.CompareTo(a));
    }
}
