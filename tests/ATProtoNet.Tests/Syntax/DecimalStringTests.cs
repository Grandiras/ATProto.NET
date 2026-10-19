using System.Globalization;
using System.Text.Json;
using ATProtoNet.Data;

namespace ATProtoNet.Tests.Syntax;

public class DecimalStringTests
{
    private sealed record Place(DecimalString Latitude, DecimalString? Altitude = null);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("52.520")]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("-0.5")]
    [InlineData("10")]
    [InlineData("1e5")]
    [InlineData("1.5E+10")]
    [InlineData("1.5e-10")]
    [Trait("Spec", "SYN-82")]
    public void Parse_JsonNumberGrammar_Accepts(string value) => Assert.Equal(value, DecimalString.Parse(value).Value);

    [Theory]
    [InlineData("")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1,000")]
    [InlineData("52,52")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("0x10")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("--1")]
    [InlineData("١٢")]
    [Trait("Spec", "SYN-82")]
    public void TryParse_OutsideTheGrammar_Rejects(string value)
    {
        Assert.False(DecimalString.TryParse(value, out _));
        Assert.Throws<FormatException>(() => DecimalString.Parse(value));
    }

    [Fact]
    [Trait("Spec", "SYN-82"), Trait("Spec", "SYN-9")]
    public void TryParse_LengthLimit_IsSixtyFourCharacters()
    {
        Assert.True(DecimalString.TryParse(new string('1', DecimalString.MaxLength), out _));
        Assert.False(DecimalString.TryParse(new string('1', DecimalString.MaxLength + 1), out _));
    }

    [Fact]
    [Trait("Spec", "SYN-83")]
    public void Json_Text_RoundTripsByteForByte()
    {
        const string json = """{"latitude":"52.520","altitude":"1e3"}""";

        var place = JsonSerializer.Deserialize<Place>(json, Options)!;

        Assert.Equal("52.520", place.Latitude.Value);
        Assert.Equal(json, JsonSerializer.Serialize(place, Options));
    }

    [Theory]
    [InlineData(52.52, "52.52")]
    [InlineData(1e21, "1E+21")]
    [InlineData(-0.0001, "-0.0001")]
    [InlineData(0.1 + 0.2, "0.30000000000000004")]
    [Trait("Spec", "SYN-84")]
    public void From_Double_WritesTheShortestRoundTripText(double value, string text)
    {
        var decimalString = DecimalString.From(value);

        Assert.Equal(text, decimalString.Value);
        Assert.True(decimalString.IsValid);
        Assert.True(decimalString.TryGetDouble(out var back));
        Assert.Equal(value, back);
    }

    [Fact]
    [Trait("Spec", "SYN-84")]
    public void From_Decimal_KeepsTheScale()
    {
        Assert.Equal("52.520", DecimalString.From(52.520m).Value);
        Assert.Equal("-1", DecimalString.From(-1m).Value);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [Trait("Spec", "SYN-84")]
    public void From_NonFiniteDouble_Throws(double value) => Assert.Throws<ArgumentOutOfRangeException>(() => DecimalString.From(value));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    [Trait("Spec", "SYN-84"), Trait("Spec", "SYN-86")]
    public void FromAndTryGet_UnderAnyCulture_MatchTheInvariantCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            Assert.Equal("52.52", DecimalString.From(52.52).Value);
            Assert.Equal("52.520", DecimalString.From(52.520m).Value);
            Assert.True(DecimalString.Parse("52.520").TryGetDouble(out var d));
            Assert.Equal(52.52, d);
            Assert.True(DecimalString.Parse("52.520").TryGetDecimal(out var m));
            Assert.Equal(52.520m, m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    [Trait("Spec", "SYN-86")]
    public void TryGet_InvalidOrOutOfRange_ReturnsFalse()
    {
        var invalid = JsonSerializer.Deserialize<Place>("""{"latitude":"52,52"}""", Options)!.Latitude;

        Assert.False(invalid.TryGetDouble(out _));
        Assert.False(invalid.TryGetDecimal(out _));
        Assert.False(DecimalString.Parse("1e999").TryGetDouble(out _));
        Assert.False(DecimalString.Parse("1e30").TryGetDecimal(out _));
        Assert.True(DecimalString.Parse("1e30").TryGetDouble(out var big));
        Assert.Equal(1e30, big);
    }

    [Theory]
    [InlineData("52,52")]
    [InlineData("NaN")]
    [InlineData("")]
    [Trait("Spec", "SYN-85")]
    public void Json_InvalidText_IsKeptWithIsValidFalse(string text)
    {
        var place = JsonSerializer.Deserialize<Place>(JsonSerializer.Serialize(new { latitude = text }), Options)!;

        Assert.False(place.Latitude.IsValid);
        Assert.Equal(text, place.Latitude.Value);
    }

    [Theory]
    [InlineData("52.52")]
    [InlineData("true")]
    [Trait("Spec", "SYN-85")]
    public void Json_NonStringToken_Throws(string token) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Place>($$"""{"latitude":{{token}}}""", Options));

    [Fact]
    [Trait("Spec", "SYN-87")]
    public void Equality_IsOrdinalOnTheText()
    {
        Assert.NotEqual(DecimalString.Parse("52.52"), DecimalString.Parse("52.520"));
        Assert.Equal(DecimalString.Parse("52.52"), DecimalString.Parse("52.52"));
        Assert.Equal(DecimalString.Parse("52.52").GetHashCode(), DecimalString.Parse("52.52").GetHashCode());
    }
}
