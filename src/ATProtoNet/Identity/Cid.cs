using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ATProtoNet.Serialization;

namespace ATProtoNet.Identity;

/// <summary>
/// The content type a <see cref="Cid"/> declares for the data it addresses.
/// </summary>
public enum CidCodec
{
    /// <summary>Raw bytes (multicodec <c>0x55</c>): the codec of blob CIDs.</summary>
    Raw = 0x55,

    /// <summary>DRISL, also known as DAG-CBOR (multicodec <c>0x71</c>): records, commits and MST nodes.</summary>
    DagCbor = 0x71,
}

/// <summary>
/// Represents a Content Identifier (CID) used to reference content-addressed data.
/// </summary>
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

    private readonly byte[] _bytes;

    /// <summary>
    /// The CID string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The codec of the addressed content.
    /// </summary>
    public CidCodec Codec => (CidCodec)_bytes[1];

    /// <summary>
    /// The 32-byte SHA-256 digest of the addressed content.
    /// </summary>
    public ReadOnlyMemory<byte> Digest => _bytes.AsMemory(4);

    private Cid(string value, byte[] bytes)
    {
        Value = value;
        _bytes = bytes;
    }

    /// <summary>
    /// Creates a CID from its string form with validation.
    /// </summary>
    /// <param name="value">The CID string, e.g. <c>bafyrei…</c>.</param>
    /// <returns>A validated CID.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid atproto CID.</exception>
    public static Cid Parse(string value) =>
        TryParse(value, out var cid) ? cid : throw IIdentifier<Cid>.InvalidValue(value, "CID");

    /// <summary>
    /// Attempts to create a CID from its string form without throwing.
    /// </summary>
    /// <param name="value">The CID string, e.g. <c>bafyrei…</c>.</param>
    /// <param name="cid">The parsed CID on success.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid atproto CID.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out Cid? cid) =>
        TryCreate(value, value, out cid);

    static bool IIdentifier<Cid>.TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Cid? result) =>
        TryCreate(span, text, out result);

    internal static bool TryCreate(ReadOnlySpan<char> span, string? text, [NotNullWhen(true)] out Cid? result)
    {
        result = null;
        if (span.Length != StringLength || span[0] != 'b')
            return false;

        Span<byte> bytes = stackalloc byte[BinaryLength];
        if (!TryDecodeBase32Lower(span[1..], bytes))
            return false;

        if (bytes[0] != CidVersion1
            || bytes[1] is not ((byte)CidCodec.Raw or (byte)CidCodec.DagCbor)
            || bytes[2] != Sha256
            || bytes[3] != DigestLength)
            return false;

        result = new Cid(text ?? span.ToString(), bytes.ToArray());
        return true;
    }

    /// <summary>
    /// Decodes unpadded RFC 4648 base32 in lower case, requiring the canonical encoding: the
    /// unused bits of the final character must be zero, so each CID has one string form.
    /// </summary>
    private static bool TryDecodeBase32Lower(ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        int buffer = 0, bits = 0, at = 0;
        foreach (var c in chars)
        {
            int value = c switch
            {
                >= 'a' and <= 'z' => c - 'a',
                >= '2' and <= '7' => c - '2' + 26,
                _ => -1,
            };
            if (value < 0)
                return false;

            buffer = ((buffer << 5) | value) & 0xFFF;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes[at++] = (byte)(buffer >> bits);
            }
        }

        return (buffer & ((1 << bits) - 1)) == 0;
    }

    /// <summary>
    /// Returns the binary form of the CID: version, codec, multihash header and digest.
    /// </summary>
    /// <returns>A new 36-byte array.</returns>
    public byte[] ToBytes() => (byte[])_bytes.Clone();

    /// <summary>
    /// Implicitly converts a <see cref="Cid"/> to its <see cref="string"/> representation.
    /// </summary>
    /// <param name="cid">The value to convert.</param>
    /// <returns>The converted value, or <see langword="null"/> for a <see langword="null"/> CID.</returns>
    [return: NotNullIfNotNull(nameof(cid))]
    public static implicit operator string?(Cid? cid) => cid?.Value;

    /// <summary>
    /// Explicitly converts a <see cref="string"/> to its <see cref="Cid"/> representation.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is not a valid <see cref="Cid"/>.</exception>
    public static explicit operator Cid(string value) => Parse(value);

    /// <inheritdoc />
    public bool Equals(Cid? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public int CompareTo(Cid? other) => string.CompareOrdinal(Value, other?.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
