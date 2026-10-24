using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using ATProtoNet.Crypto;
using ATProtoNet.Spaces;

namespace ATProtoNet.Conformance.Next;

public sealed partial class NextAdapter
{
    /// <inheritdoc />
    public byte[]? KemDecapsulate(string authorPrivateKeyHex, string keyShare)
    {
        using var key = KemKey(authorPrivateKeyHex);
        try
        {
            using var secret = new SpaceMacRequest(SpaceMacType.Repo, keyShare).Accept(key);
            return [.. SecretOf(secret)];
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public (string KeyShare, byte[] Secret)? KemEncapsulate(string authorKey)
    {
        if (!DidKey.TryParse(authorKey, out var did)) return null;
        KemPublicKey key;
        try
        {
            key = KemPublicKey.FromMultikey(did.Multikey);
        }
        catch (FormatException)
        {
            return null;
        }
        var request = SpaceMacRequest.Create(key, SpaceMacType.Repo, out var secret);
        using (secret) return (request.KeyShare, [.. SecretOf(secret)]);
    }

    /// <inheritdoc />
    public byte[]? SpaceMac(byte[] sharedSecret, string type, IReadOnlyList<string> fields, byte[] data)
    {
        using var secret = NewSecret([.. sharedSecret]);
        try
        {
            // The vectors frame arbitrary strings, which the typed ComputeCommit and ComputeRecord never pass.
            return (byte[])typeof(global::ATProtoNet.Spaces.SpaceMac).GetMethod("Compute", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [secret, MacType(type), fields.ToArray(), data])!;
        }
        catch (TargetInvocationException e) when (e.InnerException is OverflowException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string FormatWantMac(string type, string keyShare) => new SpaceMacRequest(MacType(type), keyShare).ToHeaderValue();

    /// <inheritdoc />
    public string FormatMac(string type, byte[] mac) => global::ATProtoNet.Spaces.SpaceMac.FormatHeader(MacType(type), mac);

    /// <inheritdoc />
    public string? ParseWantMac(string? value, string type)
    {
        try
        {
            return value is null ? null : SpaceMacRequest.Parse(value, MacType(type)).KeyShare;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public byte[]? ParseMac(string? value, string type)
    {
        try
        {
            return value is null ? null : global::ATProtoNet.Spaces.SpaceMac.ParseHeader(value, MacType(type));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public SpaceRepoVerdict? VerifySpaceRepo(byte[] archive, string repo, string space, byte[] sharedSecret, byte[] mac)
    {
        if (!Did.TryParse(repo, out var did) || !SpaceUri.TryParse(space, out var uri)) return null;
        using var secret = NewSecret([.. sharedSecret]);
        try
        {
            var verified = SpaceRepoExport.VerifyAsync(new MemoryStream(archive), did, uri, secret, mac).AsTask().GetAwaiter().GetResult();
            var commit = verified.Commit;
            return new(commit.Ver, commit.Repo.Value, commit.Space.Value, commit.Rev, commit.Data.ToArray(), [.. verified.Records.Select(r => (r.Path, r.Cid.Value))]);
        }
        catch (SpaceVerificationException)
        {
            return null;
        }
    }

    private static SpaceMacType MacType(string type) => type == "atproto-space-record" ? SpaceMacType.Record : SpaceMacType.Repo;

    private static KemPrivateKey KemKey(string scalarHex)
    {
        using var ecdh = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = Convert.FromHexString(scalarHex) });
        return KemPrivateKey.ImportPkcs8(ecdh.ExportPkcs8PrivateKey(), KemAlgorithm.Default);
    }

    // A shared secret never leaves its object (CRY-52); the vectors compare and supply its bytes.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_secret")]
    private static extern ref byte[] SecretOf(SharedSecret secret);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern SharedSecret NewSecret(byte[] secret);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Apply")]
    private static extern void ApplyElement(global::ATProtoNet.Spaces.LtHash hash, ReadOnlySpan<byte> element, bool add);
}
