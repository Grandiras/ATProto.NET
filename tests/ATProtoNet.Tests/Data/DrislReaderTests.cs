using ATProtoNet.Data;

namespace ATProtoNet.Tests.Data;

public class DrislReaderTests
{
    private static readonly DrislReaderOptions Lenient = new() { Mode = DrislMode.Lenient };

    private const string Link = "d82a58250001711220c19a797fa1fd590cd2e5b42d1cf5f246e29b91684e2f87404b81dc345c7a56a0";

    [Fact]
    [Trait("Spec", "DAT-1")]
    [Trait("Spec", "DAT-3")]
    public void Read_EveryKind_ReadsBackWhatWasEncoded()
    {
        // {"a": [null, true, -2, "x", h'01', link], "bb": {}}
        var reader = Reader($"a2616186f6f52161784101{Link}626262a0");

        Assert.Equal(2, reader.ReadStartMap());
        Assert.Equal("a", reader.ReadKey());
        Assert.Equal(6, reader.ReadStartArray());
        reader.ReadNull();
        Assert.True(reader.ReadBoolean());
        Assert.Equal(-2, reader.ReadInt64());
        Assert.Equal("x", reader.ReadString());
        Assert.Equal([1], reader.ReadBytes().ToArray());
        Assert.Equal(DrislValueKind.Link, reader.PeekKind());
        Assert.Equal(Link[10..], Convert.ToHexStringLower(reader.ReadLink().ToByteArray()));
        Assert.Equal(DrislValueKind.End, reader.PeekKind());
        reader.ReadEndArray();
        Assert.Equal("bb", reader.ReadKey());
        Assert.Equal(0, reader.ReadStartMap());
        reader.ReadEndMap();
        reader.ReadEndMap();
        reader.ReadEnd();
    }

    [Theory]
    [Trait("Spec", "DAT-20")]
    [Trait("Spec", "DAT-21")]
    [Trait("Spec", "DAT-22")]
    [Trait("Spec", "DAT-23")]
    [Trait("Spec", "DAT-24")]
    [Trait("Spec", "DAT-25")]
    [InlineData("9f01ff")] // indefinite array
    [InlineData("7f6161ff")] // indefinite text
    [InlineData("1801")] // non-shortest integer
    [InlineData("b801616101")] // non-shortest map length
    [InlineData("a2616202616101")] // keys out of order
    [InlineData("a2616101616102")] // duplicate key
    [InlineData("c107")] // a tag other than 42
    [InlineData("d82a4101")] // tag 42 without the 0x00 prefix
    [InlineData("f93c00")] // half float
    [InlineData("fb40091eb851eb851f")] // double float
    [InlineData("f7")] // undefined
    [InlineData("f0")] // simple value 16
    [InlineData("62c328")] // invalid UTF-8
    [InlineData("0100")] // bytes after the value
    public void SkipValue_Strict_NonCanonicalInput_ThrowsFormatException(string hex) =>
        Assert.Throws<FormatException>(() => Skip(hex, null));

    [Theory]
    [Trait("Spec", "DAT-26")]
    [InlineData("9f01ff")]
    [InlineData("7f6161ff")]
    [InlineData("1801")]
    [InlineData("b801616101")]
    [InlineData("a2616202616101")]
    [InlineData("c107")]
    [InlineData("62c328")]
    public void SkipValue_Lenient_NonCanonicalInput_IsAccepted(string hex) => Skip(hex, Lenient);

    [Theory]
    [Trait("Spec", "DAT-26")]
    [InlineData("a2616101616102")] // duplicate key, in order
    [InlineData("a3616201616102616203")] // duplicate key after an unsorted one
    [InlineData("bf616101616102ff")] // duplicate key in an indefinite map
    [InlineData("fa3f800000")] // float
    [InlineData("a10101")] // integer key
    [InlineData("d82a6161")] // tag 42 around text
    public void SkipValue_Lenient_NotTheDataModel_ThrowsFormatException(string hex) =>
        Assert.Throws<FormatException>(() => Skip(hex, Lenient));

    [Fact]
    [Trait("Spec", "DAT-26")]
    public void ReadString_LenientInvalidUtf8_IsReplaced()
    {
        var reader = Reader("62c328", Lenient);

        Assert.Equal("�(", reader.ReadString());
    }

    [Fact]
    [Trait("Spec", "DAT-26")]
    public void ReadInt64_LenientChainOfUnknownTags_ReadsTheInnerValue()
    {
        var reader = Reader(string.Concat(Enumerable.Repeat("c1", 100_000)) + "07", Lenient);

        Assert.Equal(7, reader.ReadInt64());
        reader.ReadEnd();
    }

    [Fact(Timeout = 10_000)]
    [Trait("Spec", "DAT-26")]
    public async Task SkipValue_LenientUnsortedMapsNested60Deep_TakesLinearTime()
    {
        // {"b": {"b": … 0 …, "a": 0}, "a": 0}: every map is unsorted. Re-reading earlier keys once the order
        // broke made this exponential (13 s at depth 26).
        var hex = string.Concat(Enumerable.Repeat("a26162", 60)) + "00" + string.Concat(Enumerable.Repeat("616100", 60));

        await Task.Run(() => Skip(hex, Lenient), TestContext.Current.CancellationToken);
    }

    [Theory]
    [Trait("Spec", "DAT-26")]
    [InlineData("a2616101780161 02")] // "a", then "a" with a non-shortest length
    [InlineData("a2786101617801610102")] // both non-shortest
    public void SkipValue_LenientDuplicateKeyInAnotherSpelling_ThrowsFormatException(string hex) =>
        Assert.Throws<FormatException>(() => Skip(hex.Replace(" ", ""), Lenient));

    [Theory]
    [Trait("Spec", "DAT-30")]
    [Trait("Spec", "DAT-31")]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void SkipValue_NestedArrays_LimitedTo64Levels(int depth, bool accepted)
    {
        var hex = string.Concat(Enumerable.Repeat("81", depth)) + "01";

        if (accepted) Skip(hex, null);
        else Assert.Equal("MaxDepth", Assert.Throws<DataLimitExceededException>(() => Skip(hex, null)).Limit);
    }

    [Theory]
    [Trait("Spec", "DAT-30")]
    [InlineData(63, true)]
    [InlineData(64, false)]
    public void ReadLink_InsideNestedArrays_CountsItsJsonWrapper(int depth, bool accepted)
    {
        var hex = string.Concat(Enumerable.Repeat("81", depth)) + Link;

        if (accepted) Skip(hex, null);
        else Assert.Throws<DataLimitExceededException>(() => Skip(hex, null));
    }

    [Fact]
    [Trait("Spec", "DAT-30")]
    public void ReadStartArray_LowerMaxDepth_IsEnforced() =>
        Assert.Throws<DataLimitExceededException>(() => Skip("818101", new() { MaxDepth = 1 }));

    [Theory]
    [Trait("Spec", "DAT-32")]
    [InlineData("81")]
    [InlineData("a16161")]
    public void SkipValue_100kLevels_ThrowsFormatExceptionNotStackOverflow(string level) =>
        Assert.Throws<DataLimitExceededException>(() => Skip(string.Concat(Enumerable.Repeat(level, 100_000)) + "01", Lenient));

    [Theory]
    [Trait("Spec", "DAT-33")]
    [InlineData("7b000000010000000061")] // text of 2^32 bytes
    [InlineData("5b7fffffffffffffff00")] // bytes of 2^63 - 1
    [InlineData("9b7fffffffffffffff01")] // array of 2^63 - 1 items
    [InlineData("ba80000000616101")] // map of 2^31 entries
    public void SkipValue_DeclaredLengthBeyondTheInput_ThrowsFormatException(string hex) =>
        Assert.Throws<FormatException>(() => Skip(hex, Lenient));

    [Theory]
    [Trait("Spec", "DAT-34")]
    [InlineData("")]
    [InlineData("62")]
    [InlineData("82")]
    [InlineData("ff")]
    [InlineData("1c")]
    [InlineData("1bffffffffffffffff")]
    [InlineData("3b8000000000000000")]
    [InlineData("bf6161ff")]
    public void SkipValue_Malformed_ThrowsOnlyFormatException(string hex)
    {
        Assert.Throws<FormatException>(() => Skip(hex, null));
        Assert.Throws<FormatException>(() => Skip(hex, Lenient));
    }

    [Fact]
    [Trait("Spec", "DAT-35")]
    public void ReadKey_AfterAFailedRead_NextReaderIsUnaffected()
    {
        Assert.Throws<FormatException>(() => Skip("a2616202616101", null));

        var reader = Reader("a1616101");
        reader.ReadStartMap();
        Assert.Equal("a", reader.ReadKey());
    }

    [Fact]
    [Trait("Spec", "DAT-34")]
    public void ReadString_WrongKind_ThrowsFormatException() =>
        Assert.Throws<FormatException>(() => Reader("01").ReadString());

    [Fact]
    public void ReadInt64_AtKeyPosition_ThrowsInvalidOperationException()
    {
        var reader = Reader("a1616101");
        reader.ReadStartMap();

        Assert.Throws<InvalidOperationException>(() => reader.ReadInt64());
    }

    private static DrislReader Reader(string hex, DrislReaderOptions? options = null) => new(Convert.FromHexString(hex), options);

    private static void Skip(string hex, DrislReaderOptions? options)
    {
        var reader = Reader(hex, options);
        reader.SkipValue();
        reader.ReadEnd();
    }
}
