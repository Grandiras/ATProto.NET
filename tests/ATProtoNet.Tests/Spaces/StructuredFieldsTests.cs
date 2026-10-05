using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

/// <summary>
/// The RFC 8941 subset behind <see cref="SpaceHttpSignature"/>: parsing a dictionary and serializing
/// an inner list. Parsing is strict, since a field that is read differently by two parties is a way
/// to smuggle a signature past one of them.
/// </summary>
public class StructuredFieldsTests
{
    private static StructuredFields.InnerList InnerList(string field, string key = "sig") =>
        Assert.IsType<StructuredFields.InnerList>(StructuredFields.ParseDictionary(field).Get(key));

    private static string Canonical(string field) => StructuredFields.Serialize(InnerList(field));

    [Theory]
    [InlineData("sig=(\"a\" \"b\")", "(\"a\" \"b\")")]
    [InlineData("sig=(  \"a\"   \"b\"  )", "(\"a\" \"b\")")]
    [InlineData("sig=()", "()")]
    [InlineData("sig=(\"a\");keyid=\"x\"", "(\"a\");keyid=\"x\"")]
    [InlineData("sig=(\"a\");  keyid=\"x\"", "(\"a\");keyid=\"x\"")]
    [InlineData("sig=(\"a\");alg=\"p\";keyid=\"x\"", "(\"a\");alg=\"p\";keyid=\"x\"")]
    [InlineData("sig=(\"a\";sf \"b\")", "(\"a\";sf \"b\")")]
    [InlineData("sig=(\"a\");n=1;d=1.50;t=tok;b=:YWJj:;f;g=?0;h=?1", "(\"a\");n=1;d=1.5;t=tok;b=:YWJj:;f;g=?0;h")]
    [InlineData("sig=(\"a\");d=-0.0", "(\"a\");d=0.0")]
    [InlineData("sig=(\"a\");n=-42", "(\"a\");n=-42")]
    [InlineData("sig=(\"a\");s=\"q\\\"\\\\\"", "(\"a\");s=\"q\\\"\\\\\"")]
    [InlineData("sig=(\"a\");b=:YWJjZA:", "(\"a\");b=:YWJjZA==:")] // padding is optional on input, canonical on output
    [InlineData("sig=(\"a\");k=1;k=2", "(\"a\");k=2")]            // the last value wins, in the first position
    [InlineData("sig=(\"a\");k=1;j=1;k=2", "(\"a\");k=2;j=1")]
    public void Serialize_ParsedInnerList_IsCanonical(string field, string expected) =>
        Assert.Equal(expected, Canonical(field));

    [Fact]
    public void ParseDictionary_Members_AreFoundByKeyAmongOthers()
    {
        var dictionary = StructuredFields.ParseDictionary("a=1, b=\"two\" ,\tc=(\"x\"), d, sig=(\"a\")");

        Assert.Equal(1L, Assert.IsType<StructuredFields.Item>(dictionary.Get("a")).Value);
        Assert.Equal("two", Assert.IsType<StructuredFields.Item>(dictionary.Get("b")).Value);
        Assert.IsType<StructuredFields.InnerList>(dictionary.Get("c"));
        Assert.Equal(true, Assert.IsType<StructuredFields.Item>(dictionary.Get("d")).Value);
        Assert.IsType<StructuredFields.InnerList>(dictionary.Get("sig"));
        Assert.Null(dictionary.Get("missing"));
    }

    [Fact]
    public void ParseDictionary_ByteSequence_DecodesItsBytes()
    {
        var item = Assert.IsType<StructuredFields.Item>(StructuredFields.ParseDictionary("sig=:AQID:").Get("sig"));

        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<byte[]>(item.Value));
    }

    [Fact]
    public void ParseDictionary_Empty_HasNoMembers() =>
        Assert.Null(StructuredFields.ParseDictionary(string.Empty).Get("sig"));

    [Fact]
    public void ParseDictionary_RepeatedKey_KeepsTheLastAndSaysSo()
    {
        var dictionary = StructuredFields.ParseDictionary("sig=1, other=2, sig=3");

        Assert.Equal(3L, Assert.IsType<StructuredFields.Item>(dictionary.Get("sig")).Value);
        Assert.True(dictionary.Duplicated("sig"));
        Assert.False(dictionary.Duplicated("other"));
    }

    [Theory]
    [InlineData("sig=")]
    [InlineData("sig=1,")]                  // a trailing comma
    [InlineData(",sig=1")]
    [InlineData("sig=1 sig2=2")]            // no comma between members
    [InlineData("Sig=1")]                   // keys are lower case
    [InlineData("1sig=1")]
    [InlineData("sig=(\"a\"")]              // unterminated inner list
    [InlineData("sig=(\"a\"\"b\")")]        // no space between members
    [InlineData("sig=(\"a\";)")]            // a parameter with no key
    [InlineData("sig=\"unterminated")]
    [InlineData("sig=\"bad\\escape\"")]
    [InlineData("sig=\"tab\there\"")]
    [InlineData("sig=\"café\"")]
    [InlineData("sig=?2")]
    [InlineData("sig=?")]
    [InlineData("sig=:YWJj")]               // unterminated byte sequence
    [InlineData("sig=:YW!j:")]
    [InlineData("sig=:Y:")]                 // one base64 digit is not a byte
    [InlineData("sig=1.")]
    [InlineData("sig=1.2345")]              // at most three fractional digits
    [InlineData("sig=1234567890123456")]    // at most fifteen integer digits
    [InlineData("sig=1234567890123.5")]     // at most twelve integer digits in a decimal
    [InlineData("sig=-")]
    [InlineData("sig=@1618884473")]         // a date item, RFC 9651
    [InlineData("sig=%\"display\"")]        // a display string, RFC 9651
    [InlineData("sig=(\"a\");k=")]
    [InlineData("sig=1;")]
    public void ParseDictionary_Malformed_Throws(string field) =>
        Assert.Throws<FormatException>(() => StructuredFields.ParseDictionary(field));

    [Fact]
    public void ParseDictionary_LargestIntegerAndDecimal_Parse()
    {
        Assert.Equal(
            999999999999999L,
            Assert.IsType<StructuredFields.Item>(StructuredFields.ParseDictionary("n=999999999999999").Get("n")).Value);
        Assert.Equal(
            999999999999.999m,
            Assert.IsType<StructuredFields.Item>(StructuredFields.ParseDictionary("n=999999999999.999").Get("n")).Value);
    }
}
