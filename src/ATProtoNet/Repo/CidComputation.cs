using System.Buffers;
using System.Numerics;
using System.Security.Cryptography;
using ATProtoNet.Identity;

namespace ATProtoNet.Repo;

/// <summary>
/// CID (Content Identifier) computation and encoding utilities for AT Protocol.
/// Supports CIDv1 with SHA-256 hashing and both DAG-CBOR (0x71) and raw (0x55) codecs.
/// </summary>
public static class CidComputation
{
    // CID version 1.
    private const byte CidVersion = 0x01;

    // DRISL/DAG-CBOR multicodec (0x71).
    private const byte DagCborCodec = 0x71;

    // Raw binary multicodec (0x55).
    private const byte RawCodec = 0x55;

    // SHA-256 multihash function code (0x12).
    private const byte Sha256Code = 0x12;

    // SHA-256 digest length (32 bytes = 0x20).
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

    /// <summary>Computes the binary CID bytes for DRISL-CBOR encoded data.</summary>
    /// <param name="dagCborBytes">The DRISL-CBOR encoded bytes.</param>
    /// <returns>The raw binary CID bytes (version + codec + multihash).</returns>
    public static byte[] ComputeBinaryForDagCbor(ReadOnlySpan<byte> dagCborBytes)
    {
        return ComputeBinaryCid(dagCborBytes, DagCborCodec);
    }

    /// <summary>Computes the binary CID bytes for raw binary data.</summary>
    /// <param name="rawBytes">The raw binary data.</param>
    /// <returns>The raw binary CID bytes (version + codec + multihash).</returns>
    public static byte[] ComputeBinaryForRaw(ReadOnlySpan<byte> rawBytes)
    {
        return ComputeBinaryCid(rawBytes, RawCodec);
    }

    /// <summary>Decodes a base32-encoded CID string (with 'b' prefix) to binary bytes.</summary>
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

    /// <summary>Decodes a base32-encoded CID string without throwing on malformed input.</summary>
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

    /// <summary>Encodes binary CID bytes to a base32lower string with 'b' prefix.</summary>
    /// <param name="cidBytes">The raw binary CID bytes.</param>
    /// <returns>The base32lower-encoded CID string.</returns>
    public static string EncodeCidToString(ReadOnlySpan<byte> cidBytes)
    {
        // Written in one pass: "b" + Base32Lower.Encode(...) built the base32 in a char[],
        // copied that into a string, then allocated a third string for the concatenation.
        return Base32Lower.EncodeWithPrefix('b', cidBytes);
    }

    /// <summary>Verifies that a CID matches the expected hash for the given data and codec.</summary>
    /// <param name="cid">The CID to verify.</param>
    /// <param name="data">The data that was supposedly CID-referenced.</param>
    /// <param name="isDagCbor">Whether the data is DAG-CBOR encoded (true) or raw (false).</param>
    /// <returns><c>true</c> if the CID matches; otherwise <c>false</c>.</returns>
    public static bool Verify(Cid cid, ReadOnlySpan<byte> data, bool isDagCbor = true)
    {
        Span<byte> expected = stackalloc byte[BinaryCidLength];
        WriteBinaryCid(data, isDagCbor ? DagCborCodec : RawCodec, expected);
        return cid.AsSpan().SequenceEqual(expected);
    }

    private static Cid ComputeCid(ReadOnlySpan<byte> data, byte codec) => Cid.FromBytes(ComputeBinaryCid(data, codec));

    private static byte[] ComputeBinaryCid(ReadOnlySpan<byte> data, byte codec)
    {
        var cidBytes = new byte[BinaryCidLength];
        WriteBinaryCid(data, codec, cidBytes);
        return cidBytes;
    }

    // The length of a binary CID: version(1) + codec(1) + multihash_code(1) + multihash_length(1) + hash(32).
    internal const int BinaryCidLength = 4 + 32;

    // Writes the binary CIDv1 of data under codec into destination, which holds BinaryCidLength bytes.
    // Each header field is an unsigned varint, but every value used here fits in one byte.
    internal static void WriteBinaryCid(ReadOnlySpan<byte> data, byte codec, Span<byte> destination)
    {
        destination[0] = CidVersion;
        destination[1] = codec;
        destination[2] = Sha256Code;
        destination[3] = Sha256Length;
        SHA256.HashData(data, destination.Slice(4, 32));
    }
}

// Base32 lower-case encoding/decoding (RFC 4648) without padding. Used for CID string encoding in AT
// Protocol.
internal static class Base32Lower
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static string Encode(ReadOnlySpan<byte> data)
        => data.IsEmpty ? string.Empty : EncodeWithPrefix(null, data);

    // Encodes data as base32lower, optionally preceded by a single multibase prefix character.
    //
    // A CID encodes to 59 characters including its prefix, so the whole result is built on the stack and
    // the returned string is the only allocation. The prefix is folded in here rather than concatenated by
    // the caller, which would allocate a second string.
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

    // Longest result built on the stack; a prefixed CID needs 59 characters.
    private const int MaxStackChars = 128;

    // Number of base32 characters byteCount bytes encode to.
    private static int EncodedLength(int byteCount) => (byteCount * 8 + 4) / 5;

    // Decodes canonical base32 (see TryDecode).
    //
    // Throws FormatException: The text is not canonical unpadded lower-case base32.
    public static byte[] Decode(ReadOnlySpan<char> chars)
    {
        var bytes = new byte[chars.Length * 5 / 8];
        return TryDecode(chars, bytes) ? bytes : throw new FormatException("The text is not canonical unpadded lower-case base32.");
    }

    // Decodes unpadded base32 in lower case into bytes, which must hold chars.Length * 5 / 8 bytes. Only
    // the canonical encoding is accepted: no character left over and the unused bits of the last one zero,
    // so each value has one string form.
    public static bool TryDecode<TChar>(ReadOnlySpan<TChar> chars, Span<byte> bytes)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        // Eight characters are exactly five bytes, so whole groups decode without carrying bits over. An
        // invalid character's value has bits above the low five set, which the OR of all of them keeps.
        var at = 0;
        var i = 0;
        uint seen = 0;
        for (; i + 8 <= chars.Length; i += 8)
        {
            ulong group = 0;
            for (var k = 0; k < 8; k++)
            {
                var value = Value(chars[i + k]);
                seen |= value;
                group = (group << 5) | value;
            }

            bytes[at] = (byte)(group >> 32);
            bytes[at + 1] = (byte)(group >> 24);
            bytes[at + 2] = (byte)(group >> 16);
            bytes[at + 3] = (byte)(group >> 8);
            bytes[at + 4] = (byte)group;
            at += 5;
        }

        if (seen > 31)
            return false;

        int buffer = 0, bits = 0;
        for (; i < chars.Length; i++)
        {
            var value = Value(chars[i]);
            if (value > 31)
                return false;

            buffer = ((buffer << 5) | (int)value) & 0xFFF;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes[at++] = (byte)(buffer >> bits);
            }
        }

        return bits < 5 && (buffer & ((1 << bits) - 1)) == 0;
    }

    // The value of a base32 character, or 255 for any other.
    private static uint Value<TChar>(TChar unit)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        var c = uint.CreateTruncating(unit);
        return c < 128 ? DecodeTable[(int)c] : 255u;
    }

    private static ReadOnlySpan<byte> DecodeTable =>
    [
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        // '2'-'7'
        255, 255, 26, 27, 28, 29, 30, 31, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        // 'a'-'z'
        255, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
        15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 255, 255, 255, 255, 255,
    ];
}
