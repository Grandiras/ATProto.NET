using ATProtoNet.Crypto.HttpSignatures;

namespace ATProtoNet.Tests.Crypto.HttpSignatures;

/// <summary>
/// RFC 8941 parsing and serialization, strict as the RFC's failure rules. The dictionary cases of 0.8's tests are
/// conformance vectors (<c>spaces/structured-fields.json</c>); these cover items, lists and serialization.
/// </summary>
public sealed class StructuredFieldsTests
{
    [Theory]
    [InlineData("42", "42")]
    [InlineData("  42  ", "42")]
    [InlineData("-0", "0")]
    [InlineData("4.50", "4.5")]
    [InlineData("-999999999999.999", "-999999999999.999")]
    [InlineData("\"a \\\"quoted\\\\ string\"", "\"a \\\"quoted\\\\ string\"")]
    [InlineData("foo123/456", "foo123/456")]
    [InlineData("*tok:en", "*tok:en")]
    [InlineData(":cHJldGVuZCB0aGlzIGlzIGJpbmFyeSBjb250ZW50Lg==:", ":cHJldGVuZCB0aGlzIGlzIGJpbmFyeSBjb250ZW50Lg==:")]
    [InlineData("::", "::")]
    [InlineData("?0", "?0")]
    [InlineData("1;a;b=?0;c=\"x\"", "1;a;b=?0;c=\"x\"")]
    [Trait("Spec", "CRY-56")]
    public void ParseItem_Valid_SerializesCanonically(string field, string canonical) =>
        Assert.Equal(canonical, StructuredFields.Serialize(StructuredFields.ParseItem(field)));

    [Theory]
    [InlineData("")]
    [InlineData("\t42")]
    [InlineData("42\t")]
    [InlineData("4 2")]
    [InlineData("+1")]
    [InlineData("0x1")]
    [InlineData("1.2.3")]
    [InlineData("\"unterminated")]
    [InlineData("a=1")]
    [InlineData("(1 2)")]
    [InlineData("1;A=2")]
    [InlineData(":=aGVsbG8=:")]
    [InlineData(":a=GVsbG8=:")]
    [InlineData("é")]
    [Trait("Spec", "CRY-56")]
    public void ParseItem_Malformed_ThrowsFormatException(string field) =>
        Assert.Throws<FormatException>(() => StructuredFields.ParseItem(field));

    [Theory]
    [InlineData("", "")]
    [InlineData("1, 42", "1, 42")]
    [InlineData("1,42", "1, 42")]
    [InlineData("1 ,\t42", "1, 42")]
    [InlineData("(\"a\" b);x=1, (), c;y", "(\"a\" b);x=1, (), c;y")]
    [InlineData("( 1  2 )", "(1 2)")]
    [Trait("Spec", "CRY-56")]
    public void ParseList_Valid_SerializesCanonically(string field, string canonical) =>
        Assert.Equal(canonical, StructuredFields.SerializeList(StructuredFields.ParseList(field)));

    [Theory]
    [InlineData("1,")]
    [InlineData(",1")]
    [InlineData("1,,2")]
    [InlineData("1 2")]
    [InlineData("(1 2")]
    [InlineData("(1)(2)")]
    [Trait("Spec", "CRY-56")]
    public void ParseList_Malformed_ThrowsFormatException(string field) =>
        Assert.Throws<FormatException>(() => StructuredFields.ParseList(field));

    [Theory]
    [InlineData("a=1, b;x=2, c=?1, d=?0", "a=1, b;x=2, c, d=?0")]
    [InlineData("a=(1 2), b=\"s\"", "a=(1 2), b=\"s\"")]
    [Trait("Spec", "CRY-56")]
    public void ParseDictionary_Valid_SerializesCanonically(string field, string canonical) =>
        Assert.Equal(canonical, StructuredFields.Serialize(StructuredFields.ParseDictionary(field)));

    [Fact]
    [Trait("Spec", "CRY-56")]
    public void ParseDictionary_RepeatedKey_KeepsFirstPositionLastValueAndSaysSo()
    {
        var dictionary = StructuredFields.ParseDictionary("sig=1, other=2, sig=3");

        Assert.Equal(["sig", "other"], dictionary.Members.Select(m => m.Key));
        Assert.True(dictionary.TryGetValue("sig", out var sig));
        Assert.Equal(3L, ((StructuredItem)sig).Value.Value);
        Assert.True(dictionary.IsDuplicated("sig"));
        Assert.False(dictionary.IsDuplicated("other"));
    }

    [Fact]
    [Trait("Spec", "CRY-56")]
    public void Parse_FieldLongerThanTheBound_ThrowsFormatException()
    {
        static string Field(int members) => "1" + string.Concat(Enumerable.Repeat(", 1", members - 1));

        Assert.Equal(2731, StructuredFields.ParseList(Field(2731)).Count); // 8,191 characters
        Assert.Throws<FormatException>(() => StructuredFields.ParseList(Field(2732)));
    }

    [Fact]
    [Trait("Spec", "CRY-56")]
    public void Parse_Items_HaveTheirRfcTypes()
    {
        var list = StructuredFields.ParseList("1, 1.5, \"s\", t, :AQID:, ?1");

        Assert.Equal([typeof(long), typeof(decimal), typeof(string), typeof(StructuredToken), typeof(ReadOnlyMemory<byte>), typeof(bool)],
            list.Select(m => ((StructuredItem)m).Value.Value!.GetType()));
        Assert.Equal(new byte[] { 1, 2, 3 }, ((ReadOnlyMemory<byte>)((StructuredItem)list[4]).Value.Value!).ToArray());
    }

    [Theory]
    [InlineData(1.0005, "1.0")]
    [InlineData(1.0015, "1.002")]
    [InlineData(-2.5, "-2.5")]
    [Trait("Spec", "CRY-56")]
    public void Serialize_Decimal_RoundsHalfToEvenAtThreeDigits(double value, string expected) =>
        Assert.Equal(expected, StructuredFields.Serialize(new BareItem((decimal)value)));

    public static TheoryData<BareItem> Unserializable() =>
    [
        new(1_000_000_000_000_000L),
        new(long.MinValue),
        new(-1_000_000_000_000_000L),
        new(1_000_000_000_000m),
        new("line\nbreak"),
        new("café"),
        new(new StructuredToken("1abc")),
        new(new StructuredToken("a b")),
        new BareItem(),
    ];

    [Theory]
    [MemberData(nameof(Unserializable))]
    [Trait("Spec", "CRY-56")]
    public void Serialize_ValueOutsideRfc8941_ThrowsArgumentException(BareItem item) =>
        Assert.Throws<ArgumentException>(() => StructuredFields.Serialize(item));

    [Theory]
    [InlineData("A")]
    [InlineData("1a")]
    [InlineData("a b")]
    [InlineData("")]
    [Trait("Spec", "CRY-56")]
    public void Serialize_InvalidKey_ThrowsArgumentException(string key) =>
        Assert.Throws<ArgumentException>(() => StructuredFields.Serialize(new StructuredDictionary([new(key, new StructuredItem(1L))])));
}
