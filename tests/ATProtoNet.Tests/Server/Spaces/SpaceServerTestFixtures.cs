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

/// <summary>
/// Mints DPoP proofs with claims a test chooses, including the ones the SDK's own generator
/// would never produce — a stale <c>iat</c>, a mismatched <c>htu</c>, a private key in the
/// header.
/// </summary>
public sealed class TestDPoPKey : IDisposable
{
    private readonly ECDsa _key;

    public TestDPoPKey()
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

    /// <summary>The RFC 7638 thumbprint a credential's <c>cnf.jkt</c> names.</summary>
    public string Thumbprint { get; }

    /// <summary>Mints a proof.</summary>
    /// <param name="method">The <c>htm</c>.</param>
    /// <param name="url">The <c>htu</c>, used verbatim.</param>
    /// <param name="accessToken">The credential to bind the proof to through <c>ath</c>.</param>
    /// <param name="issuedAt">The <c>iat</c>. Defaults to now.</param>
    /// <param name="jti">The <c>jti</c>. Defaults to a fresh one.</param>
    /// <param name="includePrivateKey">Leaks the private key into the embedded JWK as <c>d</c>.</param>
    /// <param name="algorithm">The <c>alg</c> header.</param>
    /// <param name="padded">Which segments carry base64 padding.</param>
    /// <param name="editJwk">Rewrites the embedded JWK before it is signed over.</param>
    public string Proof(
        string method,
        string url,
        string? accessToken = null,
        DateTimeOffset? issuedAt = null,
        string? jti = null,
        bool includePrivateKey = false,
        string algorithm = "ES256",
        JwsSegments padded = JwsSegments.None,
        Action<Dictionary<string, string>>? editJwk = null)
    {
        var jwk = new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = X,
            ["y"] = Y,
        };

        if (includePrivateKey)
            jwk["d"] = Base64Url(_key.ExportParameters(includePrivateParameters: true).D!);

        editJwk?.Invoke(jwk);

        var header = new Dictionary<string, object>
        {
            ["typ"] = "dpop+jwt",
            ["alg"] = algorithm,
            ["jwk"] = jwk,
        };

        var payload = new Dictionary<string, object>
        {
            ["jti"] = jti ?? Guid.NewGuid().ToString("N"),
            ["htm"] = method,
            ["htu"] = url,
            ["iat"] = (issuedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
        };

        if (accessToken is not null)
            payload["ath"] = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));

        return TestJws.Mint(header, payload, Sign, padded);
    }

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
