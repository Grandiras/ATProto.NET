using System.Text;
using ATProtoNet.Data;

namespace ATProtoNet.Tests.Data;

public class SignedDrislTests
{
    private static readonly DataModelBytes Signature = new(new byte[] { 1, 2, 3 });

    [Fact]
    [Trait("Spec", "DAT-65")]
    [Trait("Spec", "DAT-67")]
    public void AddSignature_ThenGetUnsignedBytes_AreTheCanonicalMapWithoutSig()
    {
        var unsigned = Drisl("""{"did":"did:plc:abc","rev":"3k2a","version":3,"data":{"$link":"bafyreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}""");

        var signed = SignedDrisl.AddSignature(unsigned, Signature);

        Assert.Equal(Drisl("""{"did":"did:plc:abc","rev":"3k2a","sig":{"$bytes":"AQID"},"version":3,"data":{"$link":"bafyreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}"""), signed);
        Assert.Equal(unsigned, SignedDrisl.GetUnsignedBytes(signed, out var signature));
        Assert.Equal(Signature, signature);
    }

    [Fact]
    [Trait("Spec", "DAT-66")]
    public void GetUnsignedBytes_ReceivedBytes_SplicesWithoutReencoding()
    {
        // 24 entries before the cut (a two-byte header), 23 after (one byte): the header shrinks, nothing else changes.
        var entries = string.Join(",", Enumerable.Range(0, 23).Select(i => $"\"k{i:00}\":{i}"));
        var unsigned = Drisl($"{{{entries}}}");
        var signed = SignedDrisl.AddSignature(unsigned, Signature, "s");

        Assert.Equal(0xb8, signed[0]);
        Assert.Equal(unsigned, SignedDrisl.GetUnsignedBytes(signed, out _, "s"));
    }

    [Theory]
    [Trait("Spec", "DAT-66")]
    [InlineData("a2617401617301")] // keys out of order
    [InlineData("a263736967430102036373696701")] // duplicate sig
    [InlineData("a16373696743010203f6")] // bytes after the map
    public void GetUnsignedBytes_NotStrict_ThrowsFormatException(string hex) =>
        Assert.Throws<FormatException>(() => SignedDrisl.GetUnsignedBytes(Convert.FromHexString(hex), out _));

    [Fact]
    [Trait("Spec", "DAT-66")]
    public void GetUnsignedBytes_NoSignatureEntry_ReturnsTheMapAndNoSignature()
    {
        var map = Convert.FromHexString("a1636b657901");

        Assert.Equal(map, SignedDrisl.GetUnsignedBytes(map, out var signature));
        Assert.Null(signature);
    }

    [Fact]
    [Trait("Spec", "DAT-65")]
    public void AddSignature_KeyAlreadyThere_ThrowsFormatException() =>
        Assert.Throws<FormatException>(() => SignedDrisl.AddSignature(Convert.FromHexString("a163736967f6"), Signature));

    private static byte[] Drisl(string json) => DrislJson.ToDrisl(Encoding.UTF8.GetBytes(json));
}
