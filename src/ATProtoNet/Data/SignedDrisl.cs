using System.Text;

namespace ATProtoNet.Data;

/// <summary>The encoding side of signed DRISL maps: the bytes a signature covers, and the signed map built from them.</summary>
/// <remarks>
/// A signature covers the canonical DRISL of the map without its signature entry, for repository
/// commits, labels, PLC operations and space commits alike; this is the SDK's one implementation of it.
/// Signing and verifying are <c>ATProtoNet.Crypto</c>'s. See
/// <see href="https://atproto.com/specs/repository#commit-objects"/> and
/// <see href="https://atproto.com/specs/label#signature-and-validation"/>.
/// </remarks>
public static class SignedDrisl
{
    /// <summary>The bytes the signature of a received signed map covers, and the signature.</summary>
    /// <remarks>
    /// The signature entry is cut out and the map header decremented; every other byte stays as received,
    /// so integer widths and key order are the signer's. The map must pass strict reading, so no duplicate
    /// key can show a verifier a field the signature does not cover.
    /// </remarks>
    /// <param name="signedDrisl">The signed map, nothing after it.</param>
    /// <param name="signature">The value under <paramref name="signatureKey"/>, or <see langword="null"/> when the map has none (the bytes are then the whole map).</param>
    /// <param name="signatureKey">The key of the signature entry.</param>
    /// <returns>The unsigned bytes.</returns>
    /// <exception cref="FormatException">The bytes are not a strict DRISL map.</exception>
    public static byte[] GetUnsignedBytes(ReadOnlyMemory<byte> signedDrisl, out DataModelValue? signature, string signatureKey = "sig")
    {
        ArgumentNullException.ThrowIfNull(signatureKey);
        var keyUtf8 = Encoding.UTF8.GetBytes(signatureKey);
        var reader = new DrislReader(signedDrisl);
        var count = reader.ReadStartMap();
        var body = reader.Position;
        int cut = -1, cutEnd = -1;
        signature = null;
        while (reader.PeekKind() != DrislValueKind.End)
        {
            var start = reader.Position;
            if (reader.ReadKeyUtf8().SequenceEqual(keyUtf8))
            {
                signature = DataModelValue.Read(reader);
                (cut, cutEnd) = (start, reader.Position);
            }
            else reader.SkipValue();
        }
        var end = reader.Position;
        reader.ReadEndMap();
        reader.ReadEnd();
        if (cut < 0) return signedDrisl.ToArray();

        var span = signedDrisl.Span;
        Span<byte> header = stackalloc byte[9];
        var headerLength = DrislEncoding.WriteHeader(header, DrislEncoding.Map, (ulong)(count - 1));
        return [.. header[..headerLength], .. span[body..cut], .. span[cutEnd..end]];
    }

    /// <summary>The signed map: the unsigned map with the signature entry added at its place in key order.</summary>
    /// <param name="unsignedDrisl">The unsigned map, as signed.</param>
    /// <param name="signature">The signature: a byte string, or the text encodings that use one (PLC operations).</param>
    /// <param name="signatureKey">The key of the signature entry.</param>
    /// <returns>The signed map, canonical when the unsigned map is.</returns>
    /// <exception cref="FormatException">The bytes are not a strict DRISL map, or already have the key.</exception>
    public static byte[] AddSignature(ReadOnlyMemory<byte> unsignedDrisl, DataModelValue signature, string signatureKey = "sig")
    {
        ArgumentNullException.ThrowIfNull(signature);
        var added = new DataModelMap([new(signatureKey, signature)]).ToDrisl().AsSpan(1);
        var keyUtf8 = Encoding.UTF8.GetBytes(signatureKey);

        var reader = new DrislReader(unsignedDrisl);
        var count = reader.ReadStartMap();
        var body = reader.Position;
        var at = -1;
        while (reader.PeekKind() != DrislValueKind.End)
        {
            var start = reader.Position;
            var existing = reader.ReadKeyUtf8();
            var order = DrislEncoding.CompareKeys(existing, keyUtf8);
            if (order == 0) throw new FormatException($"The map already has a \"{signatureKey}\" entry.");
            if (order > 0 && at < 0) at = start;
            reader.SkipValue();
        }
        var end = reader.Position;
        reader.ReadEndMap();
        reader.ReadEnd();
        if (at < 0) at = end;

        var span = unsignedDrisl.Span;
        Span<byte> header = stackalloc byte[9];
        var headerLength = DrislEncoding.WriteHeader(header, DrislEncoding.Map, (ulong)(count + 1));
        return [.. header[..headerLength], .. span[body..at], .. added, .. span[at..end]];
    }
}
