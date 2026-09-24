using System.Globalization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Serialization;

public class TimestampFormatTests
{
    [Theory]
    [InlineData("th-TH")] // Thai Buddhist calendar: the year would be 2569
    [InlineData("ja-JP")]
    [InlineData("fi-FI")]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    public void FormatTimestamp_IgnoresTheCurrentCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var formatted = AtProtoJsonDefaults.FormatTimestamp(
                new DateTime(2026, 9, 24, 12, 30, 45, 123, DateTimeKind.Utc));

            Assert.Equal("2026-09-24T12:30:45.123Z", formatted);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void NowTimestamp_UnderThaiCulture_IsAGregorianUtcTimestamp()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        try
        {
            var now = AtProtoJsonDefaults.NowTimestamp();

            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", now);
            Assert.InRange(
                DateTimeOffset.Parse(now, CultureInfo.InvariantCulture),
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(1));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
