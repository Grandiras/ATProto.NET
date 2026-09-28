using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using ATProtoNet.Repo;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>The content type a <see cref="Cid"/> declares for the data it addresses.</summary>
public enum CidCodec
{
    /// <summary>Raw bytes (multicodec <c>0x55</c>): the codec of blob CIDs.</summary>
    Raw = 0x55,

    /// <summary>DRISL, also known as DAG-CBOR (multicodec <c>0x71</c>): records, commits and MST nodes.</summary>
    DagCbor = 0x71,
}

/// <summary>Represents a Content Identifier (CID) used to reference content-addressed data.</summary>
/// <remarks>
/// <para>Only the CID form the atproto data model blesses is accepted: CIDv1 with the
/// <see cref="CidCodec.DagCbor"/> or <see cref="CidCodec.Raw"/> codec and a 32-byte SHA-256
/// digest, written in base32 lower case with the <c>b</c> multibase prefix. Other CID
/// versions, codecs, hashes and encodings — including legacy CIDv0 <c>Qm…</c> strings — fail
/// to parse.</para>
/// <para>Equality and ordering are ordinal on <see cref="Value"/>. The string form is canonical,
/// so two CIDs are equal exactly when their bytes are.</para>
/// </remarks>
[JsonConverter(typeof(IdentifierJsonConverter<Cid>))]
public sealed record Cid : IIdentifier<Cid>
{
    // version, codec, hash function, digest length, then the SHA-256 digest
    private const int BinaryLength = 4 + DigestLength;
    private const int DigestLength = 32;

    // The 'b' multibase prefix plus 36 bytes in unpadded base32 (ceil(288 / 5) = 58 chars).
    private const int StringLength = 1 + (BinaryLength * 8 + 4) / 5;

    private const byte CidVersion1 = 0x01;
    private const byte Sha256 = 0x12;

    // The binary form, held in the object rather than in an array of its own.
    private readonly Binary _binary;

    // The string form, when the CID was parsed from one; otherwise encoded on first use. A CID read off
    // the wire is mostly compared or written back, and its binary form is what both need.
    private string? _value;

    // The digest as an array, for Digest, made on first use.
    private byte[]? _digest;

    /// <summary>The CID string value.</summary>
    public string Value => _value ??= Base32Lower.EncodeWithPrefix('b', AsSpan());

    /// <summary>The codec of the addressed content.</summary>
    public CidCodec Codec => (CidCodec)_binary[1];

    /// <summary>The 32-byte SHA-256 digest of the addressed content.</summary>
    public ReadOnlyMemory<byte> Digest => _digest ??= AsSpan()[4..].ToArray();

    private Cid(string? value, ReadOnlySpan<byte> bytes)
    {
        _value = value;
        bytes.CopyTo(_binary);
    }

    [InlineArray(BinaryLength)]
    private struct Binary
    {
        private byte _element;
    }

    /// <summary>Creates a CID from its string form with validation.</summary>
    /// <param name="value">The CID string, e.g. <c>bafyrei…</c>.</param>
    /// <returns>A validated CID.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid atproto CID.</exception>
    public static Cid Parse(string value) =>
        TryParse(value, out var cid) ? cid : throw IIdentifier<Cid>.InvalidValue(value, "CID");

    /// <summary>Attempts to create a CID from its string form without throwing.</summary>
    /// <param name="value">The CID string, e.g. <c>bafyrei…</c>.</param>
    /// <param name="cid">The parsed CID on success.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid atproto CID.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out Cid? cid) =>
        TryCreate(value, value, out cid);

    static bool IIdentifier<Cid>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Cid? result) =>
        TryCreate(span, text, out result);

    internal static bool TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Cid? result)
    {
        Span<byte> bytes = stackalloc byte[BinaryLength];
        result = TryDecode(span, bytes) ? new Cid(text, bytes) : null;
        return result is not null;
    }

    static bool IIdentifier<Cid>.TryCreate(ReadOnlySpan<byte> utf8, [NotNullWhen(true)] out Cid? result)
    {
        Span<byte> bytes = stackalloc byte[BinaryLength];
        result = TryDecode(utf8, bytes) ? new Cid(null, bytes) : null;
        return result is not null;
    }

    // Decodes the string form of a CID this type accepts into bytes, which holds BinaryLength bytes.
    private static bool TryDecode<TChar>(ReadOnlySpan<TChar> text, Span<byte> bytes)
        where TChar : unmanaged, IBinaryInteger<TChar>
        => text.Length == StringLength && uint.CreateTruncating(text[0]) == 'b'
            && Base32Lower.TryDecode(text[1..], bytes) && IsValid(bytes);

    // Creates a CID from its binary form, as ToBytes returns it.
    //
    // Throws ArgumentException: The bytes are not a valid atproto CID.
    internal static Cid FromBytes(ReadOnlySpan<byte> bytes) => IsValid(bytes)
        ? new Cid(null, bytes)
        : throw IIdentifier<Cid>.InvalidValue(Convert.ToHexStringLower(bytes), "binary CID");

    // Whether bytes is the binary form of a CID this type accepts.
    internal static bool IsValid(ReadOnlySpan<byte> bytes) =>
        bytes.Length == BinaryLength
        && bytes[0] == CidVersion1
        && bytes[1] is ((byte)CidCodec.Raw or (byte)CidCodec.DagCbor)
        && bytes[2] == Sha256
        && bytes[3] == DigestLength;

    /// <summary>Returns the binary form of the CID: version, codec, multihash header and digest.</summary>
    /// <returns>A new 36-byte array.</returns>
    public byte[] ToBytes() => AsSpan().ToArray();

    // The binary form, without the copy ToBytes makes.
    internal ReadOnlySpan<byte> AsSpan() => _binary;

    /// <summary>Implicitly converts a <see cref="Cid"/> to its <see cref="string"/> representation.</summary>
    /// <param name="cid">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> CID.</returns>
    [return: NotNullIfNotNull(nameof(cid))]
    public static implicit operator string?(Cid? cid) => cid?.Value;

    /// <summary>Explicitly converts a <see cref="string"/> to its <see cref="Cid"/> representation.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid <see cref="Cid"/>.</exception>
    public static explicit operator Cid(string value) => Parse(value);

    // The string form is canonical, so comparing the binary forms is comparing the strings.

    /// <inheritdoc />
    public bool Equals(Cid? other) => other is not null && AsSpan().SequenceEqual(other.AsSpan());

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // Seeded per process, as string hashing is: a parsed CID's digest is whatever its sender chose.
        var hash = new HashCode();
        hash.AddBytes(AsSpan());
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public int CompareTo(Cid? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
