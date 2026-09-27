using System.Globalization;
using ATProtoNet.Identity;

namespace ATProtoNet.Tests.Serialization;

public class TimestampFormatTests
{
    [Theory]
    [InlineData("th-TH")] // Thai Buddhist calendar: the year would be 2569
    [InlineData("ja-JP")]
    [InlineData("fi-FI")]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    public void FromDateTime_IgnoresTheCurrentCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var formatted = AtDatetime.FromDateTime(
                new DateTime(2026, 9, 24, 12, 30, 45, 123, DateTimeKind.Utc)).ToString();

            Assert.Equal("2026-09-24T12:30:45.123Z", formatted);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
