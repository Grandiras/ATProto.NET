using System.Buffers.Text;
using ATProtoNet.Data;

namespace ATProtoNet.Crypto;

/// <summary>How a signed object carries its signature.</summary>
public enum SignatureEncoding
{
    /// <summary>A DRISL byte string: repository commits, labels and space commits.</summary>
    Bytes,

    /// <summary>An unpadded base64url string: PLC operations.</summary>
    Base64Url,
}

/// <summary>Signs and verifies DRISL maps that carry their own signature: the SDK's one path for them.</summary>
/// <remarks>
/// The signature covers the canonical DRISL of the map without its signature entry
/// (<see cref="SignedDrisl"/>). See <see href="https://atproto.com/specs/repository#commit-objects"/>.
/// </remarks>
public static class SignedObject
{
    /// <summary>Signs <paramref name="unsigned"/> and returns the signed map as canonical DRISL.</summary>
    /// <param name="unsigned">The map to sign, without a <paramref name="signatureKey"/> entry.</param>
    /// <param name="key">The signing key.</param>
    /// <param name="encoding">How the signature is written.</param>
    /// <param name="signatureKey">The key of the signature entry.</param>
    /// <returns>The signed map.</returns>
    /// <exception cref="FormatException"><paramref name="unsigned"/> already has a <paramref name="signatureKey"/> entry.</exception>
    public static byte[] Sign(DataModelMap unsigned, SigningKey key, SignatureEncoding encoding = SignatureEncoding.Bytes, string signatureKey = "sig")
    {
        ArgumentNullException.ThrowIfNull(unsigned);
        ArgumentNullException.ThrowIfNull(key);
        var bytes = unsigned.ToDrisl();
        var signature = key.Sign(bytes);
        DataModelValue value = encoding is SignatureEncoding.Base64Url ? new DataModelString(Base64Url.EncodeToString(signature)) : new DataModelBytes(signature);
        return SignedDrisl.AddSignature(bytes, value, signatureKey);
    }

    /// <summary>Whether <paramref name="signedDrisl"/> carries a valid low-S signature by <paramref name="key"/>.</summary>
    /// <param name="signedDrisl">The signed map as received, nothing after it.</param>
    /// <param name="key">The key that should have signed it.</param>
    /// <param name="encoding">How the signature is written.</param>
    /// <param name="signatureKey">The key of the signature entry.</param>
    /// <returns><see langword="false"/> also for a missing, empty or wrongly typed signature.</returns>
    /// <exception cref="FormatException">The bytes are not a strict DRISL map.</exception>
    public static bool Verify(ReadOnlyMemory<byte> signedDrisl, VerificationKey key, SignatureEncoding encoding = SignatureEncoding.Bytes, string signatureKey = "sig")
    {
        ArgumentNullException.ThrowIfNull(key);
        var unsigned = SignedDrisl.GetUnsignedBytes(signedDrisl, out var value, signatureKey);
        ReadOnlySpan<byte> signature = (encoding, value) switch
        {
            (SignatureEncoding.Bytes, DataModelBytes bytes) => bytes.Value.Span,
            // Unpadded and strict, so a signed operation has one encoding and one CID.
            (SignatureEncoding.Base64Url, DataModelString text) when StrictBase64.TryDecode(text.Value.AsSpan(), url: true, allowPadding: false, out var decoded) => decoded,
            _ => [],
        };
        return !signature.IsEmpty && key.Verify(unsigned, signature);
    }
}
