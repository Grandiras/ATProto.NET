using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ATProtoNet.Tests.Data;

public class CidBinaryTests
{
    private static readonly byte[] Hello = Encoding.ASCII.GetBytes("hello");

    [Theory]
    [Trait("Spec", "DAT-10")]
    [InlineData(CidCodec.DagCbor, "01711220")]
    [InlineData(CidCodec.Raw, "01551220")]
    public void ToByteArray_ComputedCid_IsVersionCodecSha256AndDigest(CidCodec codec, string prefix)
    {
        var binary = Cid.Compute(codec, Hello).ToByteArray();

        Assert.Equal(Cid.BinaryLength, binary.Length);
        Assert.Equal(prefix, Convert.ToHexStringLower(binary[..4]));
        Assert.Equal(SHA256.HashData(Hello), binary[4..]);
    }

    [Theory]
    [Trait("Spec", "DAT-10")]
    [InlineData("02711220")] // CIDv2
    [InlineData("01701220")] // dag-pb
    [InlineData("01711320")] // sha2-512 multihash code
    [InlineData("01711210")] // 16-byte digest
    public void FromBytes_NotABlessedCid_ThrowsFormatException(string prefix)
    {
        var binary = Convert.FromHexString(prefix.PadRight(Cid.BinaryLength * 2, '0'));

        Assert.Throws<FormatException>(() => Cid.FromBytes(binary));
        Assert.False(Cid.TryFromBytes(binary, out _));
    }

    [Theory]
    [Trait("Spec", "DAT-10")]
    [InlineData(35)]
    [InlineData(37)]
    public void FromBytes_WrongLength_ThrowsFormatException(int length)
    {
        var binary = new byte[Cid.BinaryLength + 1];
        Cid.Compute(CidCodec.Raw, Hello).WriteBytes(binary);

        Assert.Throws<FormatException>(() => Cid.FromBytes(binary.AsSpan(0, length)));
    }

    [Fact]
    [Trait("Spec", "DAT-11")]
    public void Matches_DataAndOtherData_RecomputesWithItsOwnCodec()
    {
        var cid = Cid.Compute(CidCodec.Raw, Hello);

        Assert.True(cid.Matches(Hello));
        Assert.False(cid.Matches(Encoding.ASCII.GetBytes("hellO")));
        Assert.False(Cid.Compute(CidCodec.DagCbor, Hello).Equals(cid));
    }

    [Fact]
    [Trait("Spec", "DAT-12")]
    public void FromBytes_CidV0_IsReportedAsCidV0()
    {
        var binary = (byte[])[0x12, 0x20, .. SHA256.HashData(Hello), 0, 0];

        var error = Assert.Throws<FormatException>(() => Cid.FromBytes(binary));
        Assert.Contains("CIDv0", error.Message);
    }

    [Fact]
    [Trait("Spec", "DAT-13")]
    public void Equals_SameCidParsedAndComputed_IsEqualWithTheSameHashCode()
    {
        var computed = Cid.Compute(CidCodec.DagCbor, Hello);
        var parsed = Cid.Parse(computed.Value);
        var fromBytes = Cid.FromBytes(computed.ToByteArray());

        Assert.Equal(computed, parsed);
        Assert.Equal(computed, fromBytes);
        Assert.Equal(computed.GetHashCode(), parsed.GetHashCode());
        Assert.True(computed == fromBytes);
    }

    [Fact]
    [Trait("Spec", "DAT-14")]
    public void PublicApi_NoCidAsBytesOrString_OutsideCidItself()
    {
        // Byte arrays and spans that are CIDs appear only on Cid; elsewhere a CID is a Cid.
        var offenders = typeof(Cid).Assembly.GetExportedTypes().Where(t => t != typeof(Cid))
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.Name.Contains("Cid", StringComparison.Ordinal) && Type(m) is { } type && (type == typeof(byte[]) || type == typeof(string)))
            .Select(m => $"{m.DeclaringType}.{m.Name}");
        Assert.Empty(offenders);

        static Type? Type(MemberInfo member) => member switch
        {
            PropertyInfo p => p.PropertyType,
            FieldInfo f => f.FieldType,
            MethodInfo m => m.ReturnType,
            _ => null,
        };
    }

    [Fact]
    [Trait("Spec", "DAT-15")]
    public void FromBytes_AndTryFromBytes_ArePublic()
    {
        Assert.NotNull(typeof(Cid).GetMethod(nameof(Cid.FromBytes), BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(Cid).GetMethod(nameof(Cid.TryFromBytes), BindingFlags.Public | BindingFlags.Static));
        Assert.True(Cid.TryFromBytes(Cid.Compute(CidCodec.Raw, Hello).ToByteArray(), out var cid));
        Assert.Equal(CidCodec.Raw, cid.Codec);
    }
}
