using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Serialization;

public class LexBase64Tests
{
    [Theory]
    [InlineData("", new byte[0])]
    [InlineData("AA", new byte[] { 0x00 })]
    [InlineData("AA==", new byte[] { 0x00 })]
    [InlineData("AAE", new byte[] { 0x00, 0x01 })]
    [InlineData("AAE=", new byte[] { 0x00, 0x01 })]
    [InlineData("AAEC", new byte[] { 0x00, 0x01, 0x02 })]
    [InlineData("3q2+7w", new byte[] { 0xde, 0xad, 0xbe, 0xef })]
    public void Decode_PaddedOrUnpadded_ReturnsBytes(string base64, byte[] expected)
        => Assert.Equal(expected, LexBase64.Decode(base64));

    [Theory]
    [InlineData("A")]
    [InlineData("not base64!")]
    public void Decode_Invalid_ThrowsFormatException(string base64)
        => Assert.Throws<FormatException>(() => LexBase64.Decode(base64));
}
