using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Spaces;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// A DID document resolver backed by a dictionary, so a test can publish exactly the key
/// material and service entries it wants to verify against.
/// </summary>
public sealed class FakeDidDocumentResolver : ISpaceDidDocumentResolver
{
    private readonly Dictionary<string, DidDocument> _documents = new(StringComparer.Ordinal);

    /// <summary>How many times a document was resolved, for asserting on caching.</summary>
    public int ResolveCount { get; private set; }

    public FakeDidDocumentResolver Publish(string did, DidDocument document)
    {
        _documents[did] = document;
        return this;
    }

    /// <summary>Publishes an ordinary account: one <c>#atproto</c> Multikey and a PDS endpoint.</summary>
    public FakeDidDocumentResolver PublishAccount(string did, AtProtoKey key, string? pds = null)
    {
        var document = new DidDocument
        {
            Id = did,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = $"{did}#atproto",
                    Type = "Multikey",
                    Controller = did,
                    PublicKeyMultibase = key.ToMultikey(),
                },
            ],
        };

        if (pds is not null)
        {
            document.Service.Add(new ServiceEndpoint
            {
                Id = "#atproto_pds",
                Type = "AtprotoPersonalDataServer",
                Endpoint = pds,
            });
        }

        return Publish(did, document);
    }

    /// <summary>
    /// Publishes an account whose key sits under a legacy <c>Ecdsa...VerificationKey2019</c>
    /// entry — a bare uncompressed point rather than a multicodec-tagged compressed one, which
    /// is what older PLC releases and hand-written <c>did:web</c> documents serve.
    /// </summary>
    public FakeDidDocumentResolver PublishLegacyAccount(string did, string fragment, ECDsa key)
    {
        var q = key.ExportParameters(false).Q;

        return Publish(did, new DidDocument
        {
            Id = did,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = $"{did}{fragment}",
                    Type = "EcdsaSecp256r1VerificationKey2019",
                    Controller = did,
                    PublicKeyMultibase = "z" + AtProtoCrypto.Base58Encode([0x04, .. q.X!, .. q.Y!]),
                },
            ],
        });
    }

    public Task<DidDocument> ResolveAsync(string did, CancellationToken cancellationToken = default)
    {
        ResolveCount++;

        return _documents.TryGetValue(did, out var document)
            ? Task.FromResult(document)
            : throw new SpaceVerificationException("NotAuthorized", $"No fixture for '{did}'.");
    }
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
    public string Proof(
        string method,
        string url,
        string? accessToken = null,
        DateTimeOffset? issuedAt = null,
        string? jti = null,
        bool includePrivateKey = false,
        string algorithm = "ES256")
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

        var headerB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}");

        var signature = _key.SignData(
            signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{headerB64}.{payloadB64}.{Base64Url(signature)}";
    }

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
    public string SignJws(IDictionary<string, object> header, IDictionary<string, object> payload)
    {
        var headerB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}");

        var signature = _key.SignData(
            signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{headerB64}.{payloadB64}.{Base64Url(signature)}";
    }

    public void Dispose() => _key.Dispose();

    internal static string Base64Url(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
