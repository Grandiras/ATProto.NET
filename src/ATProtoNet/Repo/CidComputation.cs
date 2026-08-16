using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using ATProtoNet.Identity;

namespace ATProtoNet.Repo;

/// <summary>
/// CID (Content Identifier) computation and encoding utilities for AT Protocol.
/// Supports CIDv1 with SHA-256 hashing and both DAG-CBOR (0x71) and raw (0x55) codecs.
/// </summary>
public static class CidComputation
{
    /// <summary>CID version 1.</summary>
    private const byte CidVersion = 0x01;

    /// <summary>DRISL/DAG-CBOR multicodec (0x71).</summary>
    private const byte DagCborCodec = 0x71;

    /// <summary>Raw binary multicodec (0x55).</summary>
    private const byte RawCodec = 0x55;

    /// <summary>SHA-256 multihash function code (0x12).</summary>
    private const byte Sha256Code = 0x12;

    /// <summary>SHA-256 digest length (32 bytes = 0x20).</summary>
    private const byte Sha256Length = 0x20;

    /// <summary>
    /// Computes a CID for DRISL-CBOR encoded data.
    /// Uses CIDv1 with SHA-256 hash and dag-cbor (0x71) codec.
    /// </summary>
    /// <param name="dagCborBytes">The DRISL-CBOR encoded bytes.</param>
    /// <returns>The CID as a base32lower-encoded string with 'b' prefix.</returns>
    public static Cid ComputeForDagCbor(ReadOnlySpan<byte> dagCborBytes)
    {
        return ComputeCid(dagCborBytes, DagCborCodec);
    }

    /// <summary>
    /// Computes a CID for raw binary data (e.g., blobs).
    /// Uses CIDv1 with SHA-256 hash and raw (0x55) codec.
    /// </summary>
    /// <param name="rawBytes">The raw binary data.</param>
    /// <returns>The CID as a base32lower-encoded string with 'b' prefix.</returns>
    public static Cid ComputeForRaw(ReadOnlySpan<byte> rawBytes)
    {
        return ComputeCid(rawBytes, RawCodec);
    }

    /// <summary>
    /// Computes the binary CID bytes for DRISL-CBOR encoded data.
    /// </summary>
    /// <param name="dagCborBytes">The DRISL-CBOR encoded bytes.</param>
    /// <returns>The raw binary CID bytes (version + codec + multihash).</returns>
    public static byte[] ComputeBinaryForDagCbor(ReadOnlySpan<byte> dagCborBytes)
    {
        return ComputeBinaryCid(dagCborBytes, DagCborCodec);
    }

    /// <summary>
    /// Computes the binary CID bytes for raw binary data.
    /// </summary>
    /// <param name="rawBytes">The raw binary data.</param>
    /// <returns>The raw binary CID bytes (version + codec + multihash).</returns>
    public static byte[] ComputeBinaryForRaw(ReadOnlySpan<byte> rawBytes)
    {
        return ComputeBinaryCid(rawBytes, RawCodec);
    }

    /// <summary>
    /// Decodes a base32-encoded CID string (with 'b' prefix) to binary bytes.
    /// </summary>
    /// <param name="cidString">The CID string (e.g., "bafyrei...").</param>
    /// <returns>The raw binary CID bytes.</returns>
    public static byte[] DecodeCidString(string cidString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cidString);

        if (cidString.StartsWith('b'))
        {
            // Base32lower encoding (RFC 4648, no padding)
            return Base32Lower.Decode(cidString.AsSpan(1));
        }

        if (cidString.StartsWith('z'))
        {
            // Base58btc encoding — used for CIDv0 (legacy)
            throw new NotSupportedException(
                "CIDv0 (base58btc) strings are not supported for encoding. Use CIDv1 with base32lower.");
        }

        throw new ArgumentException($"Unsupported CID multibase prefix: '{cidString[0]}'", nameof(cidString));
    }

    /// <summary>
    /// Decodes a base32-encoded CID string without throwing on malformed input.
    /// </summary>
    /// <param name="cidString">The CID string (e.g., "bafyrei...").</param>
    /// <param name="cidBytes">The raw binary CID bytes on success.</param>
    /// <returns><c>true</c> if the string decoded successfully.</returns>
    public static bool TryDecodeCidString(
        string? cidString, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? cidBytes)
    {
        cidBytes = null;
        if (string.IsNullOrWhiteSpace(cidString) || !cidString.StartsWith('b'))
            return false;

        try
        {
            cidBytes = Base32Lower.Decode(cidString.AsSpan(1));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Encodes binary CID bytes to a base32lower string with 'b' prefix.
    /// </summary>
    /// <param name="cidBytes">The raw binary CID bytes.</param>
    /// <returns>The base32lower-encoded CID string.</returns>
    public static string EncodeCidToString(ReadOnlySpan<byte> cidBytes)
    {
        // Written in one pass: "b" + Base32Lower.Encode(...) built the base32 in a char[],
        // copied that into a string, then allocated a third string for the concatenation.
        return Base32Lower.EncodeWithPrefix('b', cidBytes);
    }

    /// <summary>
    /// Verifies that a CID matches the expected hash for the given data and codec.
    /// </summary>
    /// <param name="cid">The CID to verify.</param>
    /// <param name="data">The data that was supposedly CID-referenced.</param>
    /// <param name="isDagCbor">Whether the data is DAG-CBOR encoded (true) or raw (false).</param>
    /// <returns><c>true</c> if the CID matches; otherwise <c>false</c>.</returns>
    public static bool Verify(Cid cid, ReadOnlySpan<byte> data, bool isDagCbor = true)
    {
        var expected = isDagCbor ? ComputeForDagCbor(data) : ComputeForRaw(data);
        return cid.Value == expected.Value;
    }

    private static Cid ComputeCid(ReadOnlySpan<byte> data, byte codec)
    {
        var binaryBytes = ComputeBinaryCid(data, codec);
        return Cid.Parse(EncodeCidToString(binaryBytes));
    }

    private static byte[] ComputeBinaryCid(ReadOnlySpan<byte> data, byte codec)
    {
        // Hash the data with SHA-256
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);

        // CID binary: version(1) + codec(1) + multihash_code(1) + multihash_length(1) + hash(32)
        // All as unsigned varints (but these values all fit in 1 byte)
        var cidBytes = new byte[1 + 1 + 1 + 1 + 32];
        cidBytes[0] = CidVersion;
        cidBytes[1] = codec;
        cidBytes[2] = Sha256Code;
        cidBytes[3] = Sha256Length;
        hash.CopyTo(cidBytes.AsSpan(4));

        return cidBytes;
    }
}

/// <summary>
/// Base32 lower-case encoding/decoding (RFC 4648) without padding.
/// Used for CID string encoding in AT Protocol.
/// </summary>
internal static class Base32Lower
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static string Encode(ReadOnlySpan<byte> data)
        => data.IsEmpty ? string.Empty : EncodeWithPrefix(null, data);

    /// <summary>
    /// Encodes <paramref name="data"/> as base32lower, optionally preceded by a single
    /// multibase <paramref name="prefix"/> character.
    /// </summary>
    /// <remarks>
    /// A CID encodes to 59 characters including its prefix, so the whole result is built on
    /// the stack and the returned string is the only allocation. The prefix is folded in here
    /// rather than concatenated by the caller, which would allocate a second string.
    /// </remarks>
    public static string EncodeWithPrefix(char? prefix, ReadOnlySpan<byte> data)
    {
        var length = (prefix is null ? 0 : 1) + EncodedLength(data.Length);
        if (length == 0) return string.Empty;

        char[]? rented = length > MaxStackChars ? ArrayPool<char>.Shared.Rent(length) : null;
        Span<char> chars = rented ?? stackalloc char[MaxStackChars];

        try
        {
            var at = 0;
            if (prefix is { } p) chars[at++] = p;

            int buffer = 0;
            int bitsLeft = 0;

            foreach (var b in data)
            {
                buffer = (buffer << 8) | b;
                bitsLeft += 8;

                while (bitsLeft >= 5)
                {
                    bitsLeft -= 5;
                    chars[at++] = Alphabet[(buffer >> bitsLeft) & 0x1F];
                }
            }

            if (bitsLeft > 0)
                chars[at++] = Alphabet[(buffer << (5 - bitsLeft)) & 0x1F];

            return new string(chars[..at]);
        }
        finally
        {
            if (rented is not null) ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>Longest result built on the stack; a prefixed CID needs 59 characters.</summary>
    private const int MaxStackChars = 128;

    /// <summary>Number of base32 characters <paramref name="byteCount"/> bytes encode to.</summary>
    private static int EncodedLength(int byteCount) => (byteCount * 8 + 4) / 5;

    public static byte[] Decode(ReadOnlySpan<char> encoded)
    {
        if (encoded.IsEmpty) return [];

        // Strip any padding
        int length = encoded.Length;
        while (length > 0 && encoded[length - 1] == '=')
            length--;

        var result = new byte[length * 5 / 8];
        int resultIndex = 0;
        int buffer = 0;
        int bitsLeft = 0;

        for (int i = 0; i < length; i++)
        {
            var c = encoded[i];
            int value = c switch
            {
                >= 'a' and <= 'z' => c - 'a',
                >= 'A' and <= 'Z' => c - 'A',
                >= '2' and <= '7' => c - '2' + 26,
                _ => throw new FormatException($"Invalid base32 character: '{c}'"),
            };

            buffer = (buffer << 5) | value;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                result[resultIndex++] = (byte)(buffer >> bitsLeft);
            }
        }

        return result[..resultIndex];
    }
}
