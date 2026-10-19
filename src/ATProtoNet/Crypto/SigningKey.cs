using System.Security.Cryptography;

namespace ATProtoNet.Crypto;

/// <summary>A P-256 or K-256 private key that signs with SHA-256 and ECDSA; safe for concurrent signing.</summary>
/// <remarks>
/// Signatures are the 64-byte <c>r || s</c>, always low-S (<see href="https://atproto.com/specs/cryptography"/>).
/// Whoever creates a key disposes it; no SDK component disposes a key it is given. <see cref="ToString"/> shows
/// only the curve and public key; private material leaves only through <see cref="ExportPkcs8"/>.
/// </remarks>
public sealed class SigningKey : IDisposable
{
    private readonly ECDsa _ecdsa;
    private bool _disposed;

    private SigningKey(ECDsa ecdsa, Curve curve)
    {
        _ecdsa = ecdsa;
        var q = ecdsa.ExportParameters(false).Q;
        PublicKey = new(DidKey.FromPoint(curve, [.. q.X!, .. q.Y!]));
    }

    /// <summary>The key's curve.</summary>
    public KeyCurve Curve => PublicKey.Curve;

    /// <summary>The key's public half.</summary>
    public VerificationKey PublicKey { get; }

    /// <summary>Generates a key with the platform's CSPRNG.</summary>
    /// <param name="curve">The curve.</param>
    /// <returns>The new key, owned by the caller.</returns>
    /// <exception cref="PlatformNotSupportedException">The platform cannot sign on <paramref name="curve"/> (K-256 without OpenSSL).</exception>
    public static SigningKey Generate(KeyCurve curve)
    {
        var info = Crypto.Curve.Of(curve);
        return Create(info, () => ECDsa.Create(info.EcCurve));
    }

    /// <summary>Imports a PKCS#8 private key.</summary>
    /// <param name="pkcs8">The DER-encoded PKCS#8 <c>PrivateKeyInfo</c>.</param>
    /// <param name="curve">The curve the key must be on.</param>
    /// <returns>The key, owned by the caller.</returns>
    /// <exception cref="FormatException">The bytes are not a PKCS#8 EC key, or its curve is not <paramref name="curve"/>.</exception>
    /// <exception cref="PlatformNotSupportedException">The platform cannot sign on <paramref name="curve"/>.</exception>
    public static SigningKey ImportPkcs8(ReadOnlySpan<byte> pkcs8, KeyCurve curve)
    {
        var info = Crypto.Curve.Of(curve);
        RequirePlatform(info);
        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportPkcs8PrivateKey(pkcs8, out var read);
            if (read == pkcs8.Length && ecdsa.ExportParameters(false).Curve.Oid?.Value == info.Oid) return new(ecdsa, info);
        }
        catch (CryptographicException)
        {
        }
        ecdsa.Dispose();
        throw new FormatException($"The bytes are not a PKCS#8 {curve} private key.");
    }

    /// <summary>Imports a raw private scalar, as PDS configuration carries rotation keys in hex.</summary>
    /// <param name="scalar">The 32-byte big-endian scalar <c>d</c>, with <c>1 ≤ d &lt; n</c>.</param>
    /// <param name="curve">The curve.</param>
    /// <returns>The key, owned by the caller.</returns>
    /// <exception cref="FormatException">The scalar is not 32 bytes in <c>[1, n − 1]</c>.</exception>
    /// <exception cref="PlatformNotSupportedException">The platform cannot sign on <paramref name="curve"/>.</exception>
    public static SigningKey ImportRawPrivateScalar(ReadOnlySpan<byte> scalar, KeyCurve curve)
    {
        var info = Crypto.Curve.Of(curve);
        if (scalar.Length != Crypto.Curve.FieldLength || !info.InRange(scalar))
            throw new FormatException($"A private scalar is {Crypto.Curve.FieldLength} bytes in [1, n - 1].");
        var d = scalar.ToArray();
        try
        {
            return Create(info, () => ECDsa.Create(new ECParameters { Curve = info.EcCurve, D = d }));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(d);
        }
    }

    /// <summary>Signs <paramref name="message"/>.</summary>
    /// <param name="message">The bytes to sign; they are hashed with SHA-256 here.</param>
    /// <returns>The 64-byte low-S <c>r || s</c>.</returns>
    /// <exception cref="ObjectDisposedException">The key is disposed.</exception>
    public byte[] Sign(ReadOnlySpan<byte> message)
    {
        var signature = new byte[VerificationKey.SignatureLength];
        Sign(message, signature);
        return signature;
    }

    /// <summary>Signs <paramref name="message"/> into <paramref name="destination"/>.</summary>
    /// <param name="message">The bytes to sign; they are hashed with SHA-256 here.</param>
    /// <param name="destination">At least <see cref="VerificationKey.SignatureLength"/> bytes.</param>
    /// <returns>The number of bytes written, always <see cref="VerificationKey.SignatureLength"/>.</returns>
    /// <exception cref="ObjectDisposedException">The key is disposed.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public int Sign(ReadOnlySpan<byte> message, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, VerificationKey.SignatureLength, nameof(destination));
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(message, hash);
        var signature = destination[..VerificationKey.SignatureLength];
        // ECDsa is not documented as safe for concurrent use.
        lock (_ecdsa)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _ecdsa.SignHash(hash, signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        var curve = PublicKey.DidKey.CurveInfo;
        if (!curve.IsLowS(signature[32..])) curve.Negate(signature[32..]);
        return VerificationKey.SignatureLength;
    }

    /// <summary>Exports the private key as unencrypted PKCS#8.</summary>
    /// <returns>The DER-encoded <c>PrivateKeyInfo</c>; secret.</returns>
    /// <exception cref="ObjectDisposedException">The key is disposed.</exception>
    public byte[] ExportPkcs8()
    {
        lock (_ecdsa)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _ecdsa.ExportPkcs8PrivateKey();
        }
    }

    /// <summary>Releases the private key.</summary>
    public void Dispose()
    {
        lock (_ecdsa)
        {
            _disposed = true;
            _ecdsa.Dispose();
        }
    }

    /// <summary>The curve and public key; never the private key.</summary>
    /// <returns>For example <c>SigningKey(P256, did:key:zDn…)</c>.</returns>
    public override string ToString() => $"SigningKey({Curve}, {PublicKey.DidKey})";

    private static SigningKey Create(Curve curve, Func<ECDsa> create)
    {
        RequirePlatform(curve);
        var ecdsa = create();
        try
        {
            return new(ecdsa, curve);
        }
        catch
        {
            ecdsa.Dispose();
            throw;
        }
    }

    private static void RequirePlatform(Curve curve)
    {
        if (!curve.HasPlatformEcdsa)
            throw new PlatformNotSupportedException($"This platform's ECDSA has no {curve.JwkName}, so it cannot sign with {curve.Id} keys (verification still works). Linux with OpenSSL supports it.");
    }
}
