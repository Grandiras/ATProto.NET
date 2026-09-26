using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Crypto;
using ATProtoNet.Models;

namespace ATProtoNet.Labeling;

/// <summary>
/// The <see href="https://atproto.com/specs/label#signature-and-validation">label signature</see>
/// primitives: the bytes a label's signature covers, and checking a signature against a known key.
/// </summary>
/// <remarks>
/// <para>A label is signed over its DRISL (deterministic DAG-CBOR) encoding without the
/// <c>sig</c> field: SHA-256 of those bytes, signed with the labeler's <c>#atproto_label</c> key,
/// in the low-S <c>r || s</c> form repository commits use.</para>
/// <para>The encoding covers the Lexicon's fields exactly as the label carries them: an absent
/// optional field is omitted, a present one is encoded even when it holds its default
/// (<c>"neg": false</c>). Fields the Lexicon does not declare, which a <see cref="Label"/> keeps in
/// <see cref="LexObject.ExtensionData"/>, are not covered, as in the reference
/// implementations.</para>
/// <para><see cref="LabelSigner"/> signs, and <see cref="LabelVerifier"/> verifies against the
/// issuer's DID document.</para>
/// </remarks>
public static class LabelSigning
{
    /// <summary>
    /// The label format version this SDK signs and verifies: the only one the spec defines.
    /// </summary>
    public const int Version = 1;

    /// <summary>The Lexicon's maximum length of a label value, in UTF-8 bytes.</summary>
    public const int MaxValueLength = 128;

    /// <summary>
    /// The bytes a label's signature covers: its DRISL encoding without <c>sig</c>.
    /// </summary>
    /// <param name="label">The label. Its <see cref="Label.Sig"/>, if any, is ignored.</param>
    /// <returns>The encoded label.</returns>
    /// <exception cref="ArgumentException">
    /// The label lacks a field every label carries (<c>src</c>, <c>uri</c>, <c>val</c>,
    /// <c>cts</c>), or holds text that is not valid UTF-16.
    /// </exception>
    public static byte[] GetSigningBytes(Label label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return Encode(label, includeSignature: false);
    }

    /// <summary>
    /// Checks a label's signature against a known key, without resolving the issuer.
    /// </summary>
    /// <param name="label">The signed label.</param>
    /// <param name="didKey">The issuer's <c>#atproto_label</c> key as a <c>did:key</c>.</param>
    /// <returns>
    /// <see cref="LabelVerificationStatus.Valid"/>, or why not:
    /// <see cref="LabelVerificationStatus.Unsigned"/>,
    /// <see cref="LabelVerificationStatus.UnsupportedVersion"/>,
    /// <see cref="LabelVerificationStatus.Malformed"/> or
    /// <see cref="LabelVerificationStatus.InvalidSignature"/>.
    /// </returns>
    /// <exception cref="FormatException"><paramref name="didKey"/> is not a did:key.</exception>
    /// <exception cref="PlatformNotSupportedException">
    /// The key is a K-256 key and the platform lacks secp256k1.
    /// </exception>
    /// <remarks>
    /// A high-S signature is refused: a label is content-addressed data, like a commit, and only
    /// the low-S form is valid. Use <see cref="LabelVerifier"/> to verify against the key the
    /// issuer publishes.
    /// </remarks>
    public static LabelVerificationStatus Verify(Label label, string didKey)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(didKey);

        if (Prepare(label, out var bytes) is { } refused)
            return refused;

        return AtProtoCrypto.VerifySignature(didKey, bytes, label.Sig!)
            ? LabelVerificationStatus.Valid
            : LabelVerificationStatus.InvalidSignature;
    }

    /// <summary>
    /// Checks what can be checked without a key, and encodes the signed bytes.
    /// </summary>
    /// <returns>Why the label cannot verify, or <see langword="null"/> when it is worth a key.</returns>
    internal static LabelVerificationStatus? Prepare(Label label, out byte[] bytes)
    {
        bytes = [];

        if (label.Sig is not { Length: > 0 })
            return LabelVerificationStatus.Unsigned;

        // The signed bytes include the version, but what a later version's fields mean is not
        // this SDK's to guess; a label without one predates versioning or was never meant to be
        // transferred, since the spec requires ver on every signed label.
        if (label.Version != Version)
            return LabelVerificationStatus.UnsupportedVersion;

        try
        {
            bytes = Encode(label, includeSignature: false);
            return null;
        }
        catch (ArgumentException)
        {
            return LabelVerificationStatus.Malformed;
        }
    }

    /// <summary>Verifies a signature, reading key material this platform cannot use as a failure.</summary>
    internal static bool VerifyWith(string didKey, byte[] bytes, byte[] signature)
    {
        try
        {
            return AtProtoCrypto.VerifySignature(didKey, bytes, signature);
        }
        catch (Exception ex) when (
            ex is ArgumentException or FormatException or NotSupportedException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Encodes a label as DRISL, with or without its signature.
    /// </summary>
    /// <exception cref="ArgumentException">See <see cref="GetSigningBytes"/>.</exception>
    internal static byte[] Encode(Label label, bool includeSignature)
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        Write(writer, label, includeSignature);
        return writer.Encode();
    }

    /// <summary>
    /// Writes a label as a DRISL map, with or without its signature.
    /// </summary>
    /// <exception cref="ArgumentException">See <see cref="GetSigningBytes"/>.</exception>
    internal static void Write(CborWriter writer, Label label, bool includeSignature)
    {
        // Required by the Lexicon, and by the model; a label built around the model's contract
        // (null!) has no encoding.
        if (label.Src is null || label.Uri is null || label.Val is null || label.Cts.ToString().Length == 0)
            throw new ArgumentException("A label needs its src, uri, val and cts.", nameof(label));

        var signature = includeSignature ? label.Sig : null;

        var count = 4
            + (label.Cid is null ? 0 : 1)
            + (label.Exp is null ? 0 : 1)
            + (label.Neg is null ? 0 : 1)
            + (signature is null ? 0 : 1)
            + (label.Version is null ? 0 : 1);

        // DRISL orders keys by length, then bytewise; every label key is three bytes long, so
        // this is plain alphabetical order.
        writer.WriteStartMap(count);

        // A Lexicon "cid" string, not a tag-42 link: the label names a version of its subject.
        if (label.Cid is { } cid)
            WriteText(writer, "cid", cid.Value);

        WriteText(writer, "cts", label.Cts.ToString());

        if (label.Exp is { } exp)
            WriteText(writer, "exp", exp.ToString());

        if (label.Neg is { } neg)
        {
            writer.WriteTextString("neg");
            writer.WriteBoolean(neg);
        }

        if (signature is not null)
        {
            writer.WriteTextString("sig");
            writer.WriteByteString(signature);
        }

        WriteText(writer, "src", label.Src.Value);
        WriteText(writer, "uri", label.Uri);
        WriteText(writer, "val", label.Val);

        if (label.Version is { } version)
        {
            writer.WriteTextString("ver");
            writer.WriteInt64(version);
        }

        writer.WriteEndMap();
    }

    private static void WriteText(CborWriter writer, string key, string value)
    {
        writer.WriteTextString(key);
        try
        {
            writer.WriteTextString(value);
        }
        catch (EncoderFallbackException ex)
        {
            // A lone surrogate, which JSON escapes can carry into a string but UTF-8 cannot hold.
            throw new ArgumentException($"The label's '{key}' is not valid text.", nameof(value), ex);
        }
    }

    /// <summary>The UTF-8 length of a label value, as the Lexicon's <c>maxLength</c> counts it.</summary>
    internal static int ValueLength(string value) => Encoding.UTF8.GetByteCount(value);
}
