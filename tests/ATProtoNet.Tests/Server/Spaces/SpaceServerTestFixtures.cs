using System.Security.Cryptography;
using System.Text;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Spaces;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// An <see cref="ISpaceAccountSigner"/> holding the keys a test hands it, standing in for a PDS's
/// actor store.
/// </summary>
public sealed class TestAccountSigner : ISpaceAccountSigner
{
    private readonly Dictionary<Did, ServiceAuthGenerator> _signers = [];

    /// <summary>Makes every lookup throw, as a key store that is down would.</summary>
    public bool Fail { get; init; }

    /// <summary>Answers every lookup with this generator, whichever account was asked for.</summary>
    public ServiceAuthGenerator? Override { get; init; }

    /// <summary>The accounts asked for, in order.</summary>
    public List<Did> Requests { get; } = [];

    public TestAccountSigner Add(Did account, AtProtoKey key)
    {
        _signers[account] = new ServiceAuthGenerator(account, key);
        return this;
    }

    public ValueTask<ServiceAuthGenerator?> GetSignerAsync(Did account, CancellationToken cancellationToken = default)
    {
        Requests.Add(account);

        if (Fail)
            throw new InvalidOperationException("The key store is unavailable.");

        return ValueTask.FromResult(Override ?? _signers.GetValueOrDefault(account));
    }
}

/// <summary>
/// Stands in for the authenticated session the <c>simplespace</c> administration endpoints read
/// their caller from, so a test can act as an account without an auth handler.
/// </summary>
public sealed class StubCallerResolver : ISpaceCallerResolver
{
    /// <summary>The DID the next request is made as, or <see langword="null"/> for anonymous.</summary>
    public string? Did { get; set; }

    public Did? GetCallerDid(Microsoft.AspNetCore.Http.HttpContext context) =>
        Did is null ? null : ATProtoNet.Identity.Did.Parse(Did);
}

/// <summary>Resolves a fixed set of JWKs for a client ID.</summary>
public sealed class FakeClientMetadataResolver : ISpaceClientMetadataResolver
{
    private readonly Dictionary<string, List<ATProtoNet.Auth.OAuth.JsonWebKey>> _keys = new(StringComparer.Ordinal);

    public FakeClientMetadataResolver Publish(string clientId, params ATProtoNet.Auth.OAuth.JsonWebKey[] keys)
    {
        _keys[clientId] = [.. keys];
        return this;
    }

    public Task<IReadOnlyList<ATProtoNet.Auth.OAuth.JsonWebKey>> ResolveKeysAsync(
        string clientId, CancellationToken cancellationToken = default) =>
        _keys.TryGetValue(clientId, out var keys)
            ? Task.FromResult<IReadOnlyList<ATProtoNet.Auth.OAuth.JsonWebKey>>(keys)
            : throw new SpaceVerificationException("InvalidClientAttestation", $"No fixture for '{clientId}'.");
}

/// <summary>The key a test credential is bound to, when the test never signs a request with it.</summary>
public static class TestHolder
{
    /// <summary>A P-256 <c>did:key</c> for a <c>cnf.kid</c>.</summary>
    public static string KeyId { get; } = AtProtoCrypto.GenerateP256Key().ToDidKey();
}

/// <summary>An ES256 key with the JWK and JWS plumbing a client attestation test needs.</summary>
public sealed class TestEcKey : IDisposable
{
    private readonly ECDsa _key;

    public TestEcKey()
    {
        _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = _key.ExportParameters(includePrivateParameters: false);

        X = Base64Url(parameters.Q.X!);
        Y = Base64Url(parameters.Q.Y!);

        var canonical = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{X}\",\"y\":\"{Y}\"}}";
        Thumbprint = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>The base64url x coordinate.</summary>
    public string X { get; }

    /// <summary>The base64url y coordinate.</summary>
    public string Y { get; }

    /// <summary>The RFC 7638 thumbprint.</summary>
    public string Thumbprint { get; }

    /// <summary>Signs raw bytes with this key, producing a JWS <c>r || s</c> signature.</summary>
    public byte[] Sign(byte[] signingInput) =>
        _key.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    /// <summary>This key as a published JWK, for a client's JWKS.</summary>
    public ATProtoNet.Auth.OAuth.JsonWebKey ToJsonWebKey(string? kid = null) => new()
    {
        Kty = "EC",
        Crv = "P-256",
        X = X,
        Y = Y,
        Kid = kid,
        Alg = "ES256",
    };

    /// <summary>Signs a JWS with this key, for a client attestation.</summary>
    public string SignJws(IDictionary<string, object> header, IDictionary<string, object> payload) =>
        TestJws.Mint(header, payload, Sign);

    public void Dispose() => _key.Dispose();

    internal static string Base64Url(ReadOnlySpan<byte> data) => TestJws.Encode(data);
}
