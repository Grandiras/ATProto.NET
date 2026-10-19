using System.Security.Cryptography;

namespace ATProtoNet.Crypto;

/// <summary>A key-encapsulation mechanism: exactly those the space commitment scheme names.</summary>
/// <remarks>
/// A KEM key is never a signing key: using the same material for ECDSA and key agreement is unsound, so a repository
/// host publishes a separate key (<c>#atproto_kem</c>). The set is closed; ML-KEM (FIPS 203) is expected to join it
/// as a new case. See <see href="https://www.rfc-editor.org/rfc/rfc9180#section-4.1"/>.
/// </remarks>
public closed record KemAlgorithm
{
    /// <summary>The algorithm the space commitment scheme uses today.</summary>
    public static KemAlgorithm Default { get; } = new EcdhP256();

    /// <summary>DHKEM(P-256, HKDF-SHA256) of RFC 9180: an ephemeral P-256 key and ECDH, published as a P-256 multikey.</summary>
    /// <remarks>The ciphertext is the 65-byte uncompressed ephemeral point and the shared secret 32 bytes.</remarks>
    public sealed record EcdhP256 : KemAlgorithm;
}

/// <summary>A fresh shared secret and the ciphertext that lets the key's holder recover it.</summary>
public sealed class KemEncapsulation(SharedSecret secret, ReadOnlyMemory<byte> ciphertext)
{
    /// <summary>The shared secret, owned by the caller.</summary>
    public SharedSecret Secret { get; } = secret;

    /// <summary>The encapsulation to send to the holder.</summary>
    public ReadOnlyMemory<byte> Ciphertext { get; } = ciphertext;

    /// <summary>Deconstructs the encapsulation.</summary>
    /// <param name="secret">The shared secret.</param>
    /// <param name="ciphertext">The ciphertext.</param>
    public void Deconstruct(out SharedSecret secret, out ReadOnlyMemory<byte> ciphertext) => (secret, ciphertext) = (Secret, Ciphertext);
}

/// <summary>A secret shared through a KEM; only HKDF reads it, and it is zeroed on disposal.</summary>
/// <remarks>
/// A shared secret is never a MAC key itself: consumers derive their keys with <see cref="DeriveKey"/>, bound to a
/// context string their spec defines. See <see href="https://www.rfc-editor.org/rfc/rfc5869"/>.
/// </remarks>
public sealed class SharedSecret : IDisposable
{
    private readonly byte[] _secret;
    private bool _disposed;

    internal SharedSecret(byte[] secret) => _secret = secret;

    /// <summary>Derives key material with HKDF-Expand (SHA-256) over <paramref name="info"/>.</summary>
    /// <param name="info">The context the key is bound to.</param>
    /// <param name="destination">The derived key, at most 8,160 bytes.</param>
    /// <exception cref="ObjectDisposedException">The secret is disposed.</exception>
    public void DeriveKey(ReadOnlySpan<byte> info, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        HKDF.Expand(HashAlgorithmName.SHA256, _secret, destination, info);
    }

    /// <summary>Zeroes the secret.</summary>
    public void Dispose()
    {
        _disposed = true;
        CryptographicOperations.ZeroMemory(_secret);
    }

    /// <summary>Never the secret.</summary>
    /// <returns><c>SharedSecret([redacted])</c>.</returns>
    public override string ToString() => "SharedSecret([redacted])";
}

// RFC 9180 §4.1 with DHKEM(P-256, HKDF-SHA256), kem_id 0x0010.
internal static class DhKem
{
    private static ReadOnlySpan<byte> Version => "HPKE-v1"u8;
    private static ReadOnlySpan<byte> SuiteId => [(byte)'K', (byte)'E', (byte)'M', 0x00, 0x10];

    // ExtractAndExpand(dh, enc || pkRm): the 32-byte shared secret.
    public static SharedSecret Derive(ReadOnlySpan<byte> dh, ReadOnlySpan<byte> enc, ReadOnlySpan<byte> recipient)
    {
        byte[] ikm = [.. Version, .. SuiteId, .. "eae_prk"u8, .. dh];
        byte[] info = [0, 32, .. Version, .. SuiteId, .. "shared_secret"u8, .. enc, .. recipient];
        Span<byte> prk = stackalloc byte[32];
        HKDF.Extract(HashAlgorithmName.SHA256, ikm, [], prk);
        var secret = new byte[32];
        HKDF.Expand(HashAlgorithmName.SHA256, prk, secret, info);
        CryptographicOperations.ZeroMemory(ikm);
        CryptographicOperations.ZeroMemory(prk);
        return new(secret);
    }

    // The ECDH x coordinate between a private key and the uncompressed point 0x04 || x || y.
    public static byte[] Agree(ECDiffieHellman key, ReadOnlySpan<byte> point)
    {
        using var peer = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new() { X = point[1..33].ToArray(), Y = point[33..].ToArray() },
        });
        return key.DeriveRawSecretAgreement(peer.PublicKey);
    }

    public static byte[] Serialize(ECDiffieHellman key)
    {
        var q = key.ExportParameters(false).Q;
        return [0x04, .. q.X!, .. q.Y!];
    }
}
