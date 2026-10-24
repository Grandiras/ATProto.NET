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

/// <summary>A secret shared through a KEM; it is only ever an HMAC key, and it is zeroed on disposal.</summary>
/// <remarks>
/// The KEM output is already an HKDF result, so it keys HMAC-SHA256 directly; a consumer binds its context into the
/// message it MACs, as the Spaces commitment does. See <see href="https://www.rfc-editor.org/rfc/rfc2104"/>.
/// </remarks>
public sealed class SharedSecret : IDisposable
{
    private readonly byte[] _secret;
    private bool _disposed;

    internal SharedSecret(byte[] secret) => _secret = secret;

    /// <summary>The length of a MAC, in bytes.</summary>
    public const int MacLength = HMACSHA256.HashSizeInBytes;

    /// <summary>HMAC-SHA256 keyed by the secret over <paramref name="message"/>.</summary>
    /// <param name="message">The message, framed by the consumer (context, then data).</param>
    /// <param name="destination">Receives the <see cref="MacLength"/>-byte MAC.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <see cref="MacLength"/>.</exception>
    /// <exception cref="ObjectDisposedException">The secret is disposed.</exception>
    public void ComputeMac(ReadOnlySpan<byte> message, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, MacLength, nameof(destination));
        HMACSHA256.HashData(_secret, message, destination);
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
