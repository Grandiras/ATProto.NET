using System.Globalization;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;
using ATProtoNet.Tests.Interop;

namespace ATProtoNet.Tests.Identity;

public class AtDatetimeTests
{
    private static readonly JsonSerializerOptions Options = AtProtoJsonDefaults.Options;

    public static TheoryData<string> Fixture(string fileName) => SyntaxFixtures.Lines(fileName);

    private sealed record Holder(AtDatetime When, AtDatetime? Maybe = null);

    // ── Interop fixtures ──

    [Theory]
    [MemberData(nameof(Fixture), "datetime_syntax_valid.txt")]
    public void Parse_ValidFixture_KeepsTheTextExactly(string value)
    {
        var parsed = AtDatetime.Parse(value);

        Assert.True(parsed.IsValid);
        Assert.Equal(value, parsed.ToString());
    }

    [Theory]
    [MemberData(nameof(Fixture), "datetime_syntax_invalid.txt")]
    [MemberData(nameof(Fixture), "datetime_parse_invalid.txt")]
    public void TryParse_InvalidFixture_Rejects(string value)
    {
        Assert.False(AtDatetime.TryParse(value, out _));
        Assert.ThrowsAny<ArgumentException>(() => AtDatetime.Parse(value));
    }

    [Theory]
    [MemberData(nameof(Fixture), "datetime_syntax_valid.txt")]
    [MemberData(nameof(Fixture), "datetime_syntax_invalid.txt")]
    [MemberData(nameof(Fixture), "datetime_parse_invalid.txt")]
    public void Json_AnyString_RoundTripsByteForByte(string value)
    {
        var json = JsonSerializer.Serialize(new { when = value });

        var holder = JsonSerializer.Deserialize<Holder>(json, Options)!;

        Assert.Equal(value, holder.When.ToString());
        Assert.Equal(json, JsonSerializer.Serialize(holder, Options));
    }

    [Theory]
    [MemberData(nameof(Fixture), "datetime_syntax_invalid.txt")]
    [MemberData(nameof(Fixture), "datetime_parse_invalid.txt")]
    public void Json_InvalidFixture_IsReadButNotValid(string value)
    {
        var holder = JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(new { when = value }), Options)!;

        Assert.False(holder.When.IsValid);
    }

    // ── Construction from code ──

    [Fact]
    public void FromDateTimeOffset_WritesTheCanonicalUtcForm()
    {
        var value = new DateTimeOffset(2026, 9, 24, 14, 30, 45, 123, TimeSpan.FromHours(2)).AddTicks(4567);

        var datetime = AtDatetime.FromDateTimeOffset(value);

        Assert.Equal("2026-09-24T12:30:45.123Z", datetime.ToString());
        Assert.True(datetime.IsValid);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 12, 30, 45, 123, TimeSpan.Zero), datetime.Value);
        Assert.Equal(AtDatetime.Parse("2026-09-24T12:30:45.123Z"), datetime);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void FromDateTime_UtcOrUnspecified_IsTakenAsUtc(DateTimeKind kind)
    {
        var datetime = AtDatetime.FromDateTime(new DateTime(2026, 9, 24, 12, 30, 45, 123, kind));

        Assert.Equal("2026-09-24T12:30:45.123Z", datetime.ToString());
    }

    [Fact]
    public void FromDateTime_Local_IsConvertedToUtc()
    {
        var local = new DateTime(2026, 9, 24, 12, 30, 45, 123, DateTimeKind.Local);

        var datetime = AtDatetime.FromDateTime(local);

        Assert.Equal(new DateTimeOffset(local).UtcDateTime, datetime.Value.UtcDateTime);
    }

    [Fact]
    public void FormatTimestamp_Unspecified_IsTakenAsUtcAsDocumented()
    {
        var formatted = AtProtoJsonDefaults.FormatTimestamp(
            new DateTime(2026, 9, 24, 12, 30, 45, 123, DateTimeKind.Unspecified));

        Assert.Equal("2026-09-24T12:30:45.123Z", formatted);
    }

    [Theory]
    [InlineData("th-TH")] // Thai Buddhist calendar: the year would be 2569
    [InlineData("ar-SA")]
    [InlineData("fi-FI")]
    public void Now_UnderAnyCulture_IsACanonicalGregorianUtcTimestamp(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var now = AtDatetime.Now();

            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", now.ToString());
            Assert.InRange(now.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ── Reading the instant ──

    [Fact]
    public void Value_KeepsTheWrittenOffsetAndTruncatesToTicks()
    {
        var datetime = AtDatetime.Parse("1985-04-12T23:20:50.123456789012-07:00");

        Assert.Equal(new DateTimeOffset(1985, 4, 12, 23, 20, 50, TimeSpan.FromHours(-7)).AddTicks(1234567), datetime.Value);
        Assert.Equal(TimeSpan.FromHours(-7), datetime.Value.Offset);
    }

    [Fact]
    public void Value_OffsetBeyondFourteenHours_IsNormalizedToUtc()
    {
        var datetime = AtDatetime.Parse("1985-04-12T23:20:50+20:00");

        Assert.True(datetime.IsValid);
        Assert.Equal(TimeSpan.Zero, datetime.Value.Offset);
        Assert.Equal(new DateTime(1985, 4, 12, 3, 20, 50, DateTimeKind.Utc), datetime.Value.UtcDateTime);
    }

    [Fact]
    public void Value_YearZero_IsValidButOutsideTheDateTimeOffsetRange()
    {
        var datetime = AtDatetime.Parse("0000-01-01T00:00:00.000Z");

        Assert.True(datetime.IsValid);
        Assert.False(datetime.TryGetValue(out _));
        Assert.Throws<InvalidOperationException>(() => datetime.Value);
    }

    [Theory]
    [InlineData("2023-04-01T10:00:00", "2023-04-01T10:00:00Z")]
    [InlineData("1985-04-12t23:20:50.123z", "1985-04-12T23:20:50.123Z")]
    [InlineData("1985-04-12 23:20:50.123+02:00", "1985-04-12T21:20:50.123Z")]
    public void Json_NearMissIso8601_IsNotValidButHasAnInstant(string text, string instant)
    {
        var holder = JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(new { when = text }), Options)!;

        Assert.False(holder.When.IsValid);
        Assert.True(holder.When.TryGetValue(out var value));
        Assert.Equal(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture), value);
        Assert.Equal(text, holder.When.ToString());
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("04/12/1985")]
    [InlineData("")]
    public void Json_Unreadable_KeepsTheTextWithoutAnInstant(string text)
    {
        var holder = JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(new { when = text }), Options)!;

        Assert.False(holder.When.IsValid);
        Assert.False(holder.When.TryGetValue(out _));
        Assert.Throws<InvalidOperationException>(() => holder.When.Value);
        Assert.Equal(text, holder.When.ToString());
    }

    [Fact]
    public void Json_NonStringToken_Throws()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Holder>("""{"when":1234}""", Options));
    }

    [Fact]
    public void Json_NullOptional_IsOmitted()
    {
        var json = JsonSerializer.Serialize(new Holder(AtDatetime.Parse("2024-01-01T00:00:00Z")), Options);

        Assert.Equal("""{"when":"2024-01-01T00:00:00Z"}""", json);
    }

    // ── default ──

    [Fact]
    public void Default_HasNoTextAndCannotBeSerialized()
    {
        var value = default(AtDatetime);

        Assert.Equal(string.Empty, value.ToString());
        Assert.False(value.IsValid);
        Assert.False(value.TryGetValue(out _));
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(new Holder(value), Options));
    }

    // ── Equality and ordering ──

    [Fact]
    public void Equality_IsOrdinalOnTheText()
    {
        var a = AtDatetime.Parse("1985-04-12T23:20:50Z");
        var b = AtDatetime.Parse("1985-04-12T23:20:50.000Z");

        Assert.NotEqual(a, b);
        Assert.Equal(a.Value, b.Value);
        Assert.Equal(a, AtDatetime.Parse("1985-04-12T23:20:50Z"));
        Assert.Equal(a.GetHashCode(), AtDatetime.Parse("1985-04-12T23:20:50Z").GetHashCode());
    }

    [Fact]
    public void CompareTo_OrdersByInstantAcrossOffsets()
    {
        var utc = AtDatetime.Parse("1985-04-12T23:20:50Z");
        var laterInPacificTime = AtDatetime.Parse("1985-04-12T16:20:51-07:00");
        var earlierInEuropeanTime = AtDatetime.Parse("1985-04-13T01:20:49+02:00");

        var sorted = new[] { laterInPacificTime, utc, earlierInEuropeanTime }.Order().ToArray();

        Assert.Equal(new[] { earlierInEuropeanTime, utc, laterInPacificTime }, sorted);
        Assert.True(utc < laterInPacificTime);
        Assert.True(utc >= earlierInEuropeanTime);
    }

    [Fact]
    public void CompareTo_ValuesWithoutAnInstant_SortFirst()
    {
        var unreadable = JsonSerializer.Deserialize<Holder>("""{"when":"soon"}""", Options)!.When;
        var valid = AtDatetime.Parse("0001-01-01T00:00:00Z");

        Assert.True(unreadable < valid);
        Assert.True(default(AtDatetime) < unreadable);
    }

    [Fact]
    public void CompareTo_SameInstantDifferentText_IsConsistentWithEquality()
    {
        var a = AtDatetime.Parse("1985-04-12T23:20:50Z");
        var b = AtDatetime.Parse("1985-04-12T23:20:50.000Z");

        Assert.NotEqual(0, a.CompareTo(b));
        Assert.Equal(-a.CompareTo(b), b.CompareTo(a));
    }

    // ── ISpanParsable ──

    [Fact]
    public void SpanParsable_ParsesThroughTheGenericContract()
    {
        Assert.Equal(AtDatetime.Parse("2024-01-15T12:00:00.000Z"), ParseGeneric<AtDatetime>("2024-01-15T12:00:00.000Z"));
        Assert.Throws<FormatException>(() => ParseGeneric<AtDatetime>("2024-01-15"));
        Assert.False(AtDatetimeSpanTryParse("2024-01-15"));
    }

    private static T ParseGeneric<T>(string value) where T : ISpanParsable<T> =>
        T.Parse(value.AsSpan(), CultureInfo.InvariantCulture);

    private static bool AtDatetimeSpanTryParse(string value) =>
        TrySpan<AtDatetime>(value.AsSpan());

    private static bool TrySpan<T>(ReadOnlySpan<char> value) where T : ISpanParsable<T> =>
        T.TryParse(value, CultureInfo.InvariantCulture, out _);
}
