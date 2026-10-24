using ATProtoNet.Crypto;
using ATProtoNet.Crypto.HttpSignatures;

namespace ATProtoNet.Spaces;

/// <summary>
/// A request for a space MAC (the <see cref="SpaceMac.WantMacHeader"/>): what it is for, and the requester's
/// fresh ephemeral P-256 key (<c>key_share</c>) from which both sides derive the shared secret.
/// </summary>
/// <remarks>
/// The secret is DHKEM(P-256, HKDF-SHA256) between the key share and the author's <c>#atproto_kem</c> key. A
/// requester makes a new one for every request, retries included. See proposal 0016, <i>Shared secret</i>.
/// </remarks>
public sealed class SpaceMacRequest
{
    /// <summary>A request with a given key share; <see cref="Create"/> makes one with a fresh key.</summary>
    /// <param name="type">What the MAC is for.</param>
    /// <param name="keyShare">The requester's ephemeral P-256 public key as a <c>did:key</c>.</param>
    public SpaceMacRequest(SpaceMacType type, string keyShare)
    {
        ArgumentNullException.ThrowIfNull(keyShare);
        (Type, KeyShare) = (type, keyShare);
    }

    /// <summary>What the MAC is for.</summary>
    public SpaceMacType Type { get; }

    /// <summary>The requester's ephemeral P-256 public key as a <c>did:key</c>, as received; <see cref="Accept"/> checks it.</summary>
    public string KeyShare { get; }

    /// <summary>Requester side: encapsulates a fresh secret to the author's KEM key.</summary>
    /// <param name="authorKey">The author's <c>#atproto_kem</c> key.</param>
    /// <param name="type">What the MAC is for.</param>
    /// <param name="secret">The shared secret, to verify the response with; the caller disposes it.</param>
    /// <returns>The request, to send as <see cref="ToHeaderValue"/>.</returns>
    public static SpaceMacRequest Create(KemPublicKey authorKey, SpaceMacType type, out SharedSecret secret)
    {
        ArgumentNullException.ThrowIfNull(authorKey);
        var (shared, ciphertext) = authorKey.Encapsulate();
        secret = shared;
        // The ciphertext is the ephemeral point, which the wire carries as a did:key.
        return new(type, VerificationKey.FromUncompressedPoint(KeyCurve.P256, ciphertext.Span).DidKey.Value);
    }

    /// <summary>Reads a request of the expected type and version.</summary>
    /// <param name="value">The <see cref="SpaceMac.WantMacHeader"/> value.</param>
    /// <param name="type">The type the endpoint serves.</param>
    /// <returns>The request.</returns>
    /// <exception cref="FormatException">The header is malformed, of another type or version, or has no <c>key_share</c> string.</exception>
    public static SpaceMacRequest Parse(string value, SpaceMacType type) =>
        SpaceMac.ParseFields(value, type).TryGetValue("key_share", out var share) && share.Value is string text
            ? new(type, text)
            : throw new FormatException("The MAC request has no key_share string.");

    /// <summary>The <see cref="SpaceMac.WantMacHeader"/> value: <c>type="…", ver=…, key_share="did:key:…"</c>.</summary>
    /// <returns>The header value.</returns>
    public string ToHeaderValue() => SpaceMac.Header(Type, new("key_share", new StructuredItem(KeyShare)));

    /// <summary>Host side: decapsulates the shared secret with the author's KEM key.</summary>
    /// <param name="authorKey">The author's KEM private key.</param>
    /// <returns>The shared secret; the caller disposes it.</returns>
    /// <exception cref="FormatException">The key share is not a P-256 <c>did:key</c>.</exception>
    public SharedSecret Accept(KemPrivateKey authorKey)
    {
        ArgumentNullException.ThrowIfNull(authorKey);
        return DidKey.TryParse(KeyShare, out var key) && key.Curve == KeyCurve.P256
            ? authorKey.Decapsulate(KemPublicKey.From(key).Point)
            : throw new FormatException("The key_share is not a P-256 did:key.");
    }
}
