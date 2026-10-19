using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>The content type a <see cref="Cid"/> declares for the data it addresses: the set atproto blesses.</summary>
public enum CidCodec : byte
{
    /// <summary>Raw bytes (multicodec <c>0x55</c>): blobs.</summary>
    Raw = 0x55,

    /// <summary>DRISL (multicodec <c>0x71</c>, DAG-CBOR): records, commits and MST nodes.</summary>
    DagCbor = 0x71,
}

/// <summary>A content identifier: CIDv1, a blessed codec and a SHA-256 digest.</summary>
/// <remarks>
/// Only the form the atproto data model blesses exists: 36 binary bytes, written as <c>b</c> and
/// unpadded lower-case base32. Equality and hashing use the bytes; ordering is ordinal on
/// <see cref="Value"/>. See <see href="https://atproto.com/specs/data-model#link-and-cid-formats"/>.
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<Cid>))]
public sealed class Cid : IAtIdentifier<Cid>
{
    /// <summary>The length of a binary CID: version, codec, hash function, digest length and the digest.</summary>
    public const int BinaryLength = 4 + DigestLength;

    /// <summary>The length of a CID's text: <c>b</c> and 58 base32 characters.</summary>
    public const int StringLength = 59;

    private const int DigestLength = 32;
    private const byte Version1 = 0x01, Sha256 = 0x12, CidV0Multihash = 0x12;

    private readonly Binary _binary;
    private string? _value;

    private Cid(ReadOnlySpan<byte> binary, string? value)
    {
        binary.CopyTo(_binary);
        _value = value;
    }

    /// <inheritdoc />
    public static int MaxLength => StringLength;

    /// <summary>The canonical text, <c>b…</c>.</summary>
    public string Value => _value ??= string.Create(StringLength, _binary, static (chars, binary) =>
    {
        chars[0] = 'b';
        Base32Lower.Encode(binary, chars[1..]);
    });

    /// <summary>The codec of the addressed content.</summary>
    public CidCodec Codec => (CidCodec)_binary[1];

    /// <summary>The 32-byte SHA-256 digest of the addressed content.</summary>
    public ReadOnlySpan<byte> Digest => Bytes[4..];

    private ReadOnlySpan<byte> Bytes => _binary;

    /// <summary>The CID of SHA-256 over <paramref name="data"/> with <paramref name="codec"/>.</summary>
    /// <param name="codec">The codec the data is in.</param>
    /// <param name="data">The addressed bytes.</param>
    /// <returns>The computed CID.</returns>
    public static Cid Compute(CidCodec codec, ReadOnlySpan<byte> data)
    {
        Span<byte> binary = stackalloc byte[BinaryLength];
        Write(codec, data, binary);
        return new(binary, null);
    }

    /// <summary>Reads a binary CID.</summary>
    /// <param name="binary">Exactly the 36 bytes of a blessed CID.</param>
    /// <returns>The CID.</returns>
    /// <exception cref="FormatException">The bytes are not a blessed CID (a CIDv0 is reported as such).</exception>
    public static Cid FromBytes(ReadOnlySpan<byte> binary) => TryFromBytes(binary, out var cid) ? cid
        : throw new FormatException(binary is [CidV0Multihash, ..] ? "CIDv0 is not supported; atproto uses CIDv1." : "The bytes are not a blessed atproto CID (CIDv1, dag-cbor or raw, sha2-256).");

    /// <summary>Reads a binary CID without throwing.</summary>
    /// <param name="binary">Exactly the 36 bytes of a blessed CID.</param>
    /// <param name="cid">The CID, when the bytes are one.</param>
    /// <returns>Whether <paramref name="binary"/> is a blessed CID.</returns>
    public static bool TryFromBytes(ReadOnlySpan<byte> binary, [NotNullWhen(true)] out Cid? cid)
    {
        cid = IsBlessed(binary) ? new(binary, null) : null;
        return cid is not null;
    }

    /// <summary>Writes the 36 binary bytes into <paramref name="destination"/>.</summary>
    /// <param name="destination">At least <see cref="BinaryLength"/> bytes.</param>
    public void WriteBytes(Span<byte> destination) => Bytes.CopyTo(destination);

    /// <summary>The 36 binary bytes, in a new array.</summary>
    /// <returns>The binary CID.</returns>
    public byte[] ToByteArray() => Bytes.ToArray();

    /// <summary>Whether <paramref name="data"/> hashes to this CID under its own codec.</summary>
    /// <param name="data">The bytes to check.</param>
    /// <returns>Whether the CID addresses <paramref name="data"/>.</returns>
    public bool Matches(ReadOnlySpan<byte> data)
    {
        Span<byte> binary = stackalloc byte[BinaryLength];
        Write(Codec, data, binary);
        return binary.SequenceEqual(Bytes);
    }

    /// <inheritdoc />
    public static Cid Parse(string s) => Identifiers.Parse<Cid>(s);

    /// <inheritdoc />
    public static Cid Parse(ReadOnlySpan<char> s) => Identifiers.Parse<Cid>(s);

    /// <inheritdoc />
    public static Cid Parse(ReadOnlySpan<byte> utf8Text) => Identifiers.Parse<Cid>(utf8Text);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, [NotNullWhen(true)] out Cid? result) =>
        TryParse(s.AsSpan(), s, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, [NotNullWhen(true)] out Cid? result) => TryParse(s, null, out result);

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out Cid? result) => TryParse(utf8Text, null, out result);

    private static bool TryParse<TChar>(ReadOnlySpan<TChar> text, string? value, [NotNullWhen(true)] out Cid? result)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        Span<byte> binary = stackalloc byte[BinaryLength];
        result = text.Length == StringLength && uint.CreateTruncating(text[0]) == 'b'
            && Base32Lower.TryDecode(text[1..], binary) && IsBlessed(binary) ? new(binary, value) : null;
        return result is not null;
    }

    private static bool IsBlessed(ReadOnlySpan<byte> binary) =>
        binary is [Version1, (byte)CidCodec.DagCbor or (byte)CidCodec.Raw, Sha256, DigestLength, ..] && binary.Length == BinaryLength;

    private static void Write(CidCodec codec, ReadOnlySpan<byte> data, Span<byte> binary)
    {
        binary[0] = Version1;
        binary[1] = (byte)codec;
        binary[2] = Sha256;
        binary[3] = DigestLength;
        SHA256.HashData(data, binary[4..]);
    }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] Cid? other) => other is not null && Bytes.SequenceEqual(other.Bytes);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as Cid);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Bytes);
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public int CompareTo(Cid? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>The canonical text of <paramref name="cid"/>.</summary>
    /// <param name="cid">The CID.</param>
    [return: NotNullIfNotNull(nameof(cid))]
    public static implicit operator string?(Cid? cid) => cid?.Value;

    /// <summary>Whether two CIDs are the same.</summary>
    public static bool operator ==(Cid? left, Cid? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two CIDs differ.</summary>
    public static bool operator !=(Cid? left, Cid? right) => !(left == right);

    [InlineArray(BinaryLength)]
    private struct Binary
    {
        private byte _element;
    }
}
