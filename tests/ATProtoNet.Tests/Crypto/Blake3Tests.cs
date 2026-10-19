using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Crypto;

namespace ATProtoNet.Tests.Crypto;

/// <summary>
/// BLAKE3 against the official test vectors (<c>spaces/blake3_test_vectors.json</c>: plain and keyed, 131 bytes of
/// extended output for inputs of <c>i % 251</c>) and longer outputs from an independent implementation.
/// </summary>
public sealed class Blake3Tests
{
    private static readonly System.Text.Json.Nodes.JsonNode Official = CryptoVectors.Json("spaces/blake3_test_vectors.json");

    public static TheoryData<int> InputLengths() => [.. Official["cases"]!.AsArray().Select(c => (int)c!["input_len"]!)];

    public static TheoryData<int, int, string> LongOutputs() =>
        [.. CryptoVectors.Json("spaces/blake3-long-output.json")["cases"]!.AsArray()
            .Select(c => ((int)c!["inputLength"]!, (int)c["outputLength"]!, (string)c["sha256"]!))];

    [Theory]
    [MemberData(nameof(InputLengths))]
    [Trait("Spec", "CRY-48"), Trait("Spec", "SPC-10")]
    public void Hash_OfficialVector_MatchesTheReference(int length)
    {
        var expected = Case(length, "hash");
        var output = new byte[expected.Length / 2];

        Blake3.Hash(Input(length), output);

        Assert.Equal(expected, Convert.ToHexStringLower(output));
    }

    [Theory]
    [MemberData(nameof(InputLengths))]
    [Trait("Spec", "CRY-48")]
    public void HashKeyed_OfficialVector_MatchesTheReference(int length)
    {
        var expected = Case(length, "keyed_hash");
        var output = new byte[expected.Length / 2];

        Blake3.HashKeyed(Key, Input(length), output);

        Assert.Equal(expected, Convert.ToHexStringLower(output));
    }

    [Theory]
    [MemberData(nameof(InputLengths))]
    [Trait("Spec", "CRY-48")]
    public void Hasher_InputInUnevenPieces_MatchesOneShot(int length)
    {
        var input = Input(length);
        var plain = Blake3.CreateHasher();
        var keyed = Blake3.CreateKeyedHasher(Key);
        for (var (at, piece) = (0, 1); at < length; at += piece, piece = piece * 3 % 1031 + 1)
        {
            plain.AppendData(input.AsSpan(at, Math.Min(piece, length - at)));
            keyed.AppendData(input.AsSpan(at, Math.Min(piece, length - at)));
        }
        var (fromPlain, fromKeyed) = (new byte[131], new byte[131]);

        plain.GetCurrentHash(fromPlain);
        keyed.GetCurrentHash(fromKeyed);

        Assert.Equal(Case(length, "hash"), Convert.ToHexStringLower(fromPlain));
        Assert.Equal(Case(length, "keyed_hash"), Convert.ToHexStringLower(fromKeyed));
    }

    [Fact]
    [Trait("Spec", "CRY-48")]
    public void GetCurrentHash_MoreInputAfterwards_ContinuesTheSameHash()
    {
        var hasher = Blake3.CreateHasher();
        hasher.AppendData(Input(1023));
        var early = new byte[32];
        hasher.GetCurrentHash(early);
        hasher.AppendData(Input(2049)[1023..]);
        var late = new byte[32];

        hasher.GetCurrentHash(late);

        Assert.Equal(Case(1023, "hash")[..64], Convert.ToHexStringLower(early));
        Assert.Equal(Case(2049, "hash")[..64], Convert.ToHexStringLower(late));
    }

    [Theory]
    [MemberData(nameof(LongOutputs))]
    [Trait("Spec", "CRY-48"), Trait("Spec", "CRY-49")]
    public void Hash_LongOutput_MatchesAnIndependentImplementation(int inputLength, int outputLength, string sha256)
    {
        // Lengths past the official 131 bytes reach the 8- and 4-block vector paths and a partial last block.
        var output = new byte[outputLength];

        Blake3.Hash(Input(inputLength), output);

        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(output)));
    }

    [Fact]
    [Trait("Spec", "CRY-48")]
    public void Hash_ShorterOutput_IsAPrefixOfTheLonger()
    {
        var (shortOutput, longOutput) = (new byte[45], new byte[1000]);

        Blake3.Hash("atproto"u8, shortOutput);
        Blake3.Hash("atproto"u8, longOutput);

        Assert.Equal(shortOutput, longOutput[..45]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    [Trait("Spec", "CRY-48")]
    public void HashKeyed_KeyNot32Bytes_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => Blake3.HashKeyed(new byte[length], [], new byte[32]));
        Assert.Throws<ArgumentException>(() => Blake3.CreateKeyedHasher(new byte[length]));
    }

    private static byte[] Key => Encoding.ASCII.GetBytes((string)Official["key"]!);

    private static string Case(int length, string member) =>
        (string)Official["cases"]!.AsArray().Single(c => (int)c!["input_len"]! == length)![member]!;

    private static byte[] Input(int length) => [.. Enumerable.Range(0, length).Select(i => (byte)(i % 251))];
}
