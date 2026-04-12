using ATProtoNet.Repo;

namespace ATProtoNet.Tests.Repo;

public class CidComputationTests
{
    [Fact]
    public void ComputeForDagCbor_ProducesValidCid()
    {
        var data = new byte[] { 0xA1, 0x61, 0x61, 0x01 }; // CBOR: { "a": 1 }
        var cid = CidComputation.ComputeForDagCbor(data);

        // CID should start with 'b' (base32lower multibase prefix)
        Assert.StartsWith("b", cid.Value);
        Assert.True(cid.Value.Length > 10);
    }

    [Fact]
    public void ComputeForRaw_ProducesValidCid()
    {
        var data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var cid = CidComputation.ComputeForRaw(data);

        Assert.StartsWith("b", cid.Value);
        Assert.True(cid.Value.Length > 10);
    }

    [Fact]
    public void ComputeForDagCbor_IsDeterministic()
    {
        var data = new byte[] { 0xA1, 0x61, 0x61, 0x01 };
        var cid1 = CidComputation.ComputeForDagCbor(data);
        var cid2 = CidComputation.ComputeForDagCbor(data);

        Assert.Equal(cid1, cid2);
    }

    [Fact]
    public void ComputeForDagCbor_DifferentData_DifferentCids()
    {
        var data1 = new byte[] { 0xA1, 0x61, 0x61, 0x01 }; // { "a": 1 }
        var data2 = new byte[] { 0xA1, 0x61, 0x61, 0x02 }; // { "a": 2 }
        var cid1 = CidComputation.ComputeForDagCbor(data1);
        var cid2 = CidComputation.ComputeForDagCbor(data2);

        Assert.NotEqual(cid1, cid2);
    }

    [Fact]
    public void CidStringRoundTrip()
    {
        var data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        var binaryBytes = CidComputation.ComputeBinaryForDagCbor(data);

        // Encode to string
        var cidString = CidComputation.EncodeCidToString(binaryBytes);
        Assert.StartsWith("b", cidString);

        // Decode back
        var decodedBytes = CidComputation.DecodeCidString(cidString);
        Assert.Equal(binaryBytes, decodedBytes);
    }

    [Fact]
    public void Verify_CorrectData_ReturnsTrue()
    {
        var data = new byte[] { 0xA1, 0x61, 0x61, 0x01 };
        var cid = CidComputation.ComputeForDagCbor(data);

        Assert.True(CidComputation.Verify(cid, data, isDagCbor: true));
    }

    [Fact]
    public void Verify_WrongData_ReturnsFalse()
    {
        var data = new byte[] { 0xA1, 0x61, 0x61, 0x01 };
        var cid = CidComputation.ComputeForDagCbor(data);

        var wrongData = new byte[] { 0xA1, 0x61, 0x61, 0x02 };
        Assert.False(CidComputation.Verify(cid, wrongData, isDagCbor: true));
    }

    [Fact]
    public void ComputeBinaryForDagCbor_HasCorrectFormat()
    {
        var data = new byte[] { 0xA1, 0x61, 0x61, 0x01 };
        var binary = CidComputation.ComputeBinaryForDagCbor(data);

        // CIDv1 structure: version(1) + codec(0x71) + hash_code(0x12) + hash_length(0x20) + hash(32)
        Assert.Equal(36, binary.Length);
        Assert.Equal(0x01, binary[0]); // CID version 1
        Assert.Equal(0x71, binary[1]); // DAG-CBOR codec
        Assert.Equal(0x12, binary[2]); // SHA-256 hash code
        Assert.Equal(0x20, binary[3]); // 32-byte hash
    }

    [Fact]
    public void ComputeBinaryForRaw_HasCorrectCodec()
    {
        var data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        var binary = CidComputation.ComputeBinaryForRaw(data);

        Assert.Equal(0x01, binary[0]); // CID version 1
        Assert.Equal(0x55, binary[1]); // Raw codec
    }
}
