namespace ATProtoNet.Tests.Syntax;

public class RecordKeyTests
{
    [Theory]
    [InlineData("@handle")]
    [InlineData("any+space")]
    [InlineData("number(3)")]
    [InlineData("dHJ1ZQ==")]
    [InlineData("a$b")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [Trait("Spec", "SYN-25")]
    public void Parse_CharacterOutsideTheAllowedSetOrDotSegment_Throws(string value) => Assert.Throws<FormatException>(() => RecordKey.Parse(value));

    [Theory]
    [InlineData("self")]
    [InlineData("a.b-c_d:e~f")]
    [InlineData("...")]
    [InlineData(".a")]
    [InlineData("MixedCase")]
    [Trait("Spec", "SYN-25")]
    public void Parse_AllowedCharacters_KeepTheirCase(string value) => Assert.Equal(value, RecordKey.Parse(value).Value);

    [Fact]
    [Trait("Spec", "SYN-25"), Trait("Spec", "SYN-9")]
    public void TryParse_LengthLimit_IsFiveHundredTwelve()
    {
        Assert.True(RecordKey.TryParse(new string('a', RecordKey.MaxLength), out _));
        Assert.False(RecordKey.TryParse(new string('a', RecordKey.MaxLength + 1), out _));
    }

    [Fact]
    [Trait("Spec", "SYN-26")]
    public void From_Tid_IsTheTidText()
    {
        var tid = Tid.Parse("3jzfcijpj2z2a");

        Assert.Equal("3jzfcijpj2z2a", RecordKey.From(tid).Value);
        Assert.Equal(RecordKey.Parse("3jzfcijpj2z2a"), RecordKey.From(tid));
    }

    [Fact]
    [Trait("Spec", "SYN-26")]
    public void From_SuccessiveTids_AreStrictlyIncreasingKeys()
    {
        var generator = new TidGenerator(1);
        var keys = Enumerable.Range(0, 1_000).Select(_ => RecordKey.From(generator.Next())).ToList();

        for (var i = 1; i < keys.Count; i++) Assert.True(keys[i].CompareTo(keys[i - 1]) > 0);
        Assert.Equal("self", RecordKey.Self.Value);
    }
}
