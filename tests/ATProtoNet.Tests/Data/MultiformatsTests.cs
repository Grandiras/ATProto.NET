using System.Buffers;
using ATProtoNet.Data;

namespace ATProtoNet.Tests.Data;

public class MultiformatsTests
{
    [Theory]
    [Trait("Spec", "DAT-70")]
    [InlineData("", "")]
    [InlineData("66", "my")]
    [InlineData("666f6f626172", "mzxw6ytboi")]
    public void Base32_Rfc4648Vector_RoundTripsLowerCaseUnpadded(string hex, string text)
    {
        var bytes = Convert.FromHexString(hex);

        Assert.Equal(text, Base32.Encode(bytes));
        Assert.Equal(bytes, Base32.Decode(text));
    }

    [Theory]
    [Trait("Spec", "DAT-71")]
    [InlineData("MZXW6YTBOI")] // upper case
    [InlineData("my======")] // padding
    [InlineData("m1")] // outside the alphabet
    [InlineData("mz")] // non-zero spare bits
    [InlineData("m")] // a character left over
    public void Base32_Decode_NonCanonical_ThrowsFormatException(string text)
    {
        Assert.Throws<FormatException>(() => Base32.Decode(text));
        Assert.False(Base32.TryDecode(text, new byte[8], out _));
    }

    [Theory]
    [Trait("Spec", "DAT-70")]
    [Trait("Spec", "DAT-71")]
    [InlineData("", "")]
    [InlineData("00", "1")]
    [InlineData("0000ff", "115Q")]
    [InlineData("626262", "a3gV")]
    public void Base58Btc_Vector_RoundTripsAndPreservesLeadingZeros(string hex, string text)
    {
        var bytes = Convert.FromHexString(hex);

        Assert.Equal(text, Base58Btc.Encode(bytes));
        Assert.Equal(bytes, Base58Btc.Decode(text));
    }

    [Theory]
    [Trait("Spec", "DAT-71")]
    [InlineData("0OIl")]
    [InlineData("abc+")]
    public void Base58Btc_Decode_OutsideTheAlphabet_ThrowsFormatException(string text) =>
        Assert.Throws<FormatException>(() => Base58Btc.Decode(text));

    [Fact]
    [Trait("Spec", "DAT-71")]
    public void Base58Btc_TryDecode_ValueLongerThanDestination_ReturnsFalse()
    {
        Assert.False(Base58Btc.TryDecode("115Q", new byte[2], out _));
        Assert.True(Base58Btc.TryDecode("115Q", new byte[3], out var written));
        Assert.Equal(3, written);
    }

    [Theory]
    [Trait("Spec", "DAT-56")]
    [Trait("Spec", "DAT-70")]
    [InlineData(0UL, "00")]
    [InlineData(127UL, "7f")]
    [InlineData(128UL, "8001")]
    [InlineData(300UL, "ac02")]
    [InlineData(UnsignedVarint.MaxValue, "ffffffffffffffff7f")]
    public void UnsignedVarint_Value_RoundTripsMinimally(ulong value, string hex)
    {
        var buffer = new byte[UnsignedVarint.MaxLength];

        Assert.Equal(hex, Convert.ToHexStringLower(buffer[..UnsignedVarint.Write(value, buffer)]));
        Assert.Equal(hex.Length / 2, UnsignedVarint.GetLength(value));
        Assert.Equal(value, UnsignedVarint.Read(Convert.FromHexString(hex), out var read));
        Assert.Equal(hex.Length / 2, read);
    }

    [Theory]
    [Trait("Spec", "DAT-56")]
    [InlineData("80", OperationStatus.NeedMoreData)] // truncated
    [InlineData("8000", OperationStatus.InvalidData)] // not minimal
    [InlineData("ffffffffffffffffff01", OperationStatus.InvalidData)] // over nine bytes
    public void UnsignedVarint_TryRead_MalformedVarint_IsRefused(string hex, OperationStatus status)
    {
        Assert.Equal(status, UnsignedVarint.TryRead(Convert.FromHexString(hex), out _, out _));
        Assert.Throws<FormatException>(() => UnsignedVarint.Read(Convert.FromHexString(hex), out _));
    }
}
