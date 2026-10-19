using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace ATProtoNet.Crypto;

/// <summary>A key-encapsulation public key, as a repository host publishes it in a DID document (<c>#atproto_kem</c>).</summary>
/// <remarks>It is never a signing key, and nothing converts between the two (see <see cref="KemAlgorithm"/>).</remarks>
public sealed class KemPublicKey : IEquatable<KemPublicKey>
{
    // The SEC1 uncompressed point, 0x04 || x || y: the RFC 9180 serialization.
    private readonly byte[] _point;

    private KemPublicKey(KemAlgorithm algorithm, string multikey, byte[] point) => (Algorithm, Multikey, _point) = (algorithm, multikey, point);

    /// <summary>The key's algorithm.</summary>
    public KemAlgorithm Algorithm { get; }

    /// <summary>The multikey, <c>z…</c>, as the DID document's <c>publicKeyMultibase</c> carries it.</summary>
    public string Multikey { get; }

    /// <summary>Parses a multikey.</summary>
    /// <param name="multikey">The multikey.</param>
    /// <returns>The key.</returns>
    /// <exception cref="FormatException">The multikey is malformed or of an algorithm outside <see cref="KemAlgorithm"/>.</exception>
    public static KemPublicKey FromMultikey(string multikey)
    {
        var key = DidKey.FromMultikey(multikey);
        return key.Curve == KeyCurve.P256 ? From(key) : throw new FormatException("The multikey is not a key of a supported KEM algorithm.");
    }

    /// <summary>Generates a fresh shared secret and its encapsulation to this key.</summary>
    /// <returns>The secret, owned by the caller, and the ciphertext for the key's holder.</returns>
    public KemEncapsulation Encapsulate()
    {
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var dh = DhKem.Agree(ephemeral, _point);
        var enc = DhKem.Serialize(ephemeral);
        try
        {
            return new(DhKem.Derive(dh, enc, _point), enc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dh);
        }
    }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] KemPublicKey? other) => other is not null && Algorithm == other.Algorithm && Multikey == other.Multikey;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as KemPublicKey);

    /// <inheritdoc />
    public override int GetHashCode() => Multikey.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Multikey;

    internal static KemPublicKey From(DidKey key) => new(KemAlgorithm.Default, key.Multikey, [0x04, .. key.Point]);

    internal ReadOnlySpan<byte> Point => _point;
}

/// <summary>A key-encapsulation private key: its holder decapsulates the secrets encapsulated to its public key.</summary>
/// <remarks>Whoever creates it disposes it. <see cref="ToString"/> shows only the public key.</remarks>
public sealed class KemPrivateKey : IDisposable
{
    private readonly ECDiffieHellman _key;

    private KemPrivateKey(ECDiffieHellman key)
    {
        _key = key;
        var q = key.ExportParameters(false).Q;
        PublicKey = KemPublicKey.From(DidKey.FromPoint(Curve.P256, [.. q.X!, .. q.Y!]));
    }

    /// <summary>The public key to publish.</summary>
    public KemPublicKey PublicKey { get; }

    /// <summary>Generates a key with the platform's CSPRNG.</summary>
    /// <param name="algorithm">The algorithm.</param>
    /// <returns>The key, owned by the caller.</returns>
    public static KemPrivateKey Generate(KemAlgorithm algorithm) => algorithm switch
    {
        KemAlgorithm.EcdhP256 => new(ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256)),
        null => throw new ArgumentNullException(nameof(algorithm)),
    };

    /// <summary>Imports a PKCS#8 private key.</summary>
    /// <param name="pkcs8">The DER-encoded PKCS#8 <c>PrivateKeyInfo</c>.</param>
    /// <param name="algorithm">The algorithm the key must be for.</param>
    /// <returns>The key, owned by the caller.</returns>
    /// <exception cref="FormatException">The bytes are not a PKCS#8 key of <paramref name="algorithm"/>.</exception>
    public static KemPrivateKey ImportPkcs8(ReadOnlySpan<byte> pkcs8, KemAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(algorithm);
        var key = ECDiffieHellman.Create();
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out var read);
            if (read == pkcs8.Length && key.ExportParameters(false).Curve.Oid?.Value == Curve.P256.Oid) return new(key);
        }
        catch (CryptographicException)
        {
        }
        key.Dispose();
        throw new FormatException("The bytes are not a PKCS#8 P-256 private key.");
    }

    /// <summary>Recovers the shared secret of an encapsulation to this key.</summary>
    /// <param name="ciphertext">The encapsulation's ciphertext.</param>
    /// <returns>The shared secret, owned by the caller.</returns>
    /// <exception cref="FormatException">The ciphertext is not an encapsulation of this algorithm.</exception>
    /// <exception cref="ObjectDisposedException">The key is disposed.</exception>
    public SharedSecret Decapsulate(ReadOnlySpan<byte> ciphertext)
    {
        if (ciphertext is not [0x04, ..] || ciphertext.Length != VerificationKey.UncompressedPointLength)
            throw new FormatException("The ciphertext is not a 65-byte uncompressed P-256 point.");
        Curve.P256.CheckOnCurve(ciphertext[1..]);
        byte[] dh;
        lock (_key) dh = DhKem.Agree(_key, ciphertext);
        try
        {
            return DhKem.Derive(dh, ciphertext, PublicKey.Point);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dh);
        }
    }

    /// <summary>Exports the private key as unencrypted PKCS#8.</summary>
    /// <returns>The DER-encoded <c>PrivateKeyInfo</c>; secret.</returns>
    /// <exception cref="ObjectDisposedException">The key is disposed.</exception>
    public byte[] ExportPkcs8()
    {
        lock (_key) return _key.ExportPkcs8PrivateKey();
    }

    /// <summary>Releases the private key.</summary>
    public void Dispose()
    {
        lock (_key) _key.Dispose();
    }

    /// <summary>The public key only.</summary>
    /// <returns>For example <c>KemPrivateKey(zDn…)</c>.</returns>
    public override string ToString() => $"KemPrivateKey({PublicKey.Multikey})";
}
