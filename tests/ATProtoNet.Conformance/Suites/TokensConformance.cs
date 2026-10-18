using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance.Suites;

/// <summary>
/// Tokens: the service-auth verdict table (<c>auth/service-auth.json</c>), DPoP and PKCE
/// (<c>auth/dpop-pkce.json</c>) and authorization-server discovery (<c>auth/oauth-metadata.json</c>).
/// </summary>
public abstract class TokensConformance<TAdapter> : ConformanceSuite<TAdapter> where TAdapter : IProtocolAdapter, new()
{
    private const string ServiceAuthFile = "auth/service-auth.json";
    private const string DPoPFile = "auth/dpop-pkce.json";

    private static readonly JsonNode ServiceAuth = Vectors.Json(ServiceAuthFile);
    private static readonly IReadOnlyDictionary<string, JsonNode> ServiceAuthCases = Vectors.Cases(ServiceAuthFile);
    private static readonly JsonNode DPoPKeys = Vectors.Json(DPoPFile)["keys"]!;
    private static readonly IReadOnlyDictionary<string, JsonNode> ThumbprintCases = Vectors.Cases(DPoPFile, "thumbprints");
    private static readonly IReadOnlyDictionary<string, JsonNode> AthCases = Vectors.Cases(DPoPFile, "accessTokenHashes");
    private static readonly IReadOnlyDictionary<string, JsonNode> HtuCases = Vectors.Cases(DPoPFile, "htu");
    private static readonly IReadOnlyDictionary<string, JsonNode> ProofCases = Vectors.Cases(DPoPFile, "proofs");
    private static readonly IReadOnlyDictionary<string, JsonNode> PkceCases = Vectors.Cases(DPoPFile, "pkce");
    private static readonly IReadOnlyDictionary<string, JsonNode> MetadataCases = Vectors.Cases("auth/oauth-metadata.json");

    /// <summary>The service-auth vectors, by ID.</summary>
    public static TheoryData<string> ServiceAuthSequences() => new(ServiceAuthCases.Keys);

    /// <summary>The thumbprint vectors, by ID.</summary>
    public static TheoryData<string> Thumbprints() => new(ThumbprintCases.Keys);

    /// <summary>The <c>ath</c> vectors, by ID.</summary>
    public static TheoryData<string> AccessTokenHashes() => new(AthCases.Keys);

    /// <summary>The <c>htu</c> vectors, by ID.</summary>
    public static TheoryData<string> Htus() => new(HtuCases.Keys);

    /// <summary>The DPoP proof vectors, by ID.</summary>
    public static TheoryData<string> Proofs() => new(ProofCases.Keys);

    /// <summary>The PKCE vectors, by ID.</summary>
    public static TheoryData<string> Pkce() => new(PkceCases.Keys);

    /// <summary>The metadata vectors, by ID.</summary>
    public static TheoryData<string> Metadata() => new(MetadataCases.Keys);

    /// <summary>Each token of a sequence gets its verdict; a replay store spans the sequence.</summary>
    [Theory]
    [Trait("Spec", "AUT-66")]
    [MemberData(nameof(ServiceAuthSequences))]
    public void VerifyServiceAuth_Sequence_ReachesEachVerdict(string vectorId)
    {
        Vector(vectorId);
        var vector = ServiceAuthCases[vectorId];
        var now = DateTimeOffset.FromUnixTimeSeconds((long)ServiceAuth["now"]!);
        var steps = vector["steps"]!.AsArray();

        var verdicts = Adapter.VerifyServiceAuth(
            [.. steps.Select(s => ((string)s!["token"]!, now.AddSeconds((long?)s["at"] ?? 0)))], Expectations(vector["options"]));

        Assert.Equal(steps.Count, verdicts.Count);
        foreach (var (step, verdict) in steps.Zip(verdicts))
        {
            var expect = step!["expect"]!;
            Assert.True((bool)expect["ok"]! == verdict.Accepted, $"Expected {expect.ToJsonString()}, got {verdict}.");
            if (!verdict.Accepted)
            {
                Assert.Equal((string)expect["error"]!, verdict.Error);
                continue;
            }

            Assert.Equal((string)expect["issuer"]!, verdict.Issuer);
            Assert.Equal((string)expect["audience"]!, verdict.Audience);
            Assert.Equal((string)expect["method"]!, verdict.Method);
            Assert.Equal((string)expect["keyId"]!, verdict.KeyId);
            if (expect["tokenId"] is { } jti)
                Assert.Equal((string)jti!, verdict.TokenId);
            if (expect["expiresAt"] is { } exp)
                Assert.Equal((long)exp!, verdict.ExpiresAt);
        }
    }

    /// <summary>A JWK's RFC 7638 thumbprint covers exactly its required members.</summary>
    [Theory]
    [Trait("Spec", "AUT-54")]
    [MemberData(nameof(Thumbprints))]
    public void DPoPThumbprint_Vector_MatchesTheRfc(string vectorId)
    {
        Vector(vectorId);
        var vector = ThumbprintCases[vectorId];

        Assert.Equal((string?)vector["expect"], Adapter.DPoPThumbprint(vector["jwk"]!.ToJsonString()));
    }

    /// <summary>The <c>ath</c> is the base64url SHA-256 of the access token.</summary>
    [Theory]
    [Trait("Spec", "AUT-54")]
    [MemberData(nameof(AccessTokenHashes))]
    public void DPoPAccessTokenHash_Vector_MatchesTheRfc(string vectorId)
    {
        Vector(vectorId);
        var vector = AthCases[vectorId];

        Assert.Equal((string)vector["expect"]!, Adapter.DPoPAccessTokenHash((string)vector["token"]!));
    }

    /// <summary>The <c>htu</c> is scheme, host, port and path, normalised; anything but an absolute URL with a host has none.</summary>
    [Theory]
    [Trait("Spec", "AUT-54")]
    [MemberData(nameof(Htus))]
    public void DPoPHtu_Vector_IsNormalised(string vectorId)
    {
        Vector(vectorId);
        var vector = HtuCases[vectorId];

        Assert.Equal((string?)vector["expect"], Adapter.DPoPHtu((string)vector["url"]!));
    }

    /// <summary>A proof is an ES256 <c>dpop+jwt</c> over exactly the public key, with the expected claims, or is refused.</summary>
    [Theory]
    [Trait("Spec", "AUT-54")]
    [MemberData(nameof(Proofs))]
    public void CreateDPoPProof_Vector_HasTheExpectedShape(string vectorId)
    {
        Vector(vectorId);
        var vector = ProofCases[vectorId];
        var key = DPoPKeys[(string)vector["key"]!]!;

        var proof = Adapter.CreateDPoPProof((string)key["curve"]!, (string)key["privateKey"]!, (string)vector["method"]!,
            (string)vector["url"]!, (string?)vector["nonce"], (string?)vector["accessToken"]);

        if (vector["expect"] is not { } expect)
        {
            Assert.Null(proof);
            return;
        }

        Assert.NotNull(proof);
        var parts = proof.Split('.');
        var header = JsonNode.Parse(Convert.FromBase64String(Padded(parts[0])))!.AsObject();
        var claims = JsonNode.Parse(Convert.FromBase64String(Padded(parts[1])))!.AsObject();
        Assert.Equal("dpop+jwt", (string?)header["typ"]);
        Assert.Equal("ES256", (string?)header["alg"]);
        Assert.True(JsonNode.DeepEquals(key["jwk"], header["jwk"]), $"The header's jwk is not exactly the public key: {header["jwk"]?.ToJsonString()}");
        Assert.Matches("^[0-9a-f]{32}$", (string?)claims["jti"]);
        Assert.NotNull(claims["iat"]);
        AssertMatches(expect.AsObject(), claims);
        Assert.True(VerifiesES256(key["jwk"]!, $"{parts[0]}.{parts[1]}", parts[2]), "The proof's signature does not verify.");
    }

    /// <summary>The S256 challenge matches the RFC's.</summary>
    [Theory]
    [Trait("Spec", "AUT-38")]
    [MemberData(nameof(Pkce))]
    public void PkceChallenge_Vector_MatchesTheRfc(string vectorId)
    {
        Vector(vectorId);
        var vector = PkceCases[vectorId];

        Assert.Equal((string)vector["expect"]!, Adapter.PkceChallenge((string)vector["verifier"]!));
    }

    /// <summary>A generated verifier and state are 32 random bytes in base64url.</summary>
    [Fact]
    [Trait("Spec", "AUT-38")]
    public void GeneratePkce_TwoCalls_AreFreshBase64UrlValues()
    {
        var first = Adapter.GeneratePkce();
        var second = Adapter.GeneratePkce();

        foreach (var value in new[] { first.Verifier, first.State, second.Verifier, second.State })
            Assert.Matches("^[A-Za-z0-9_-]{43}$", value);
        Assert.NotEqual(first.Verifier, second.Verifier);
        Assert.NotEqual(first.State, second.State);
    }

    /// <summary>Discovery accepts the issuer, or refuses with the OAuth error the vector names.</summary>
    [Theory]
    [Trait("Spec", "AUT-35")]
    [MemberData(nameof(Metadata))]
    public void ResolveAuthorizationServer_Vector_MatchesExpectation(string vectorId)
    {
        Vector(vectorId);
        var vector = MetadataCases[vectorId];
        var expect = vector["expect"]!;

        var verdict = Adapter.ResolveAuthorizationServer((string)vector["pds"]!, vector["resourceMetadata"]!.ToJsonString(),
            vector["serverMetadata"]!.ToJsonString(), (bool)vector["allowPrivateNetworks"]!);

        Assert.Equal(
            new MetadataVerdict((bool)expect["ok"]!, (string?)expect["issuer"], (string?)expect["error"]),
            verdict with { Error = verdict.Accepted ? null : verdict.Error, Issuer = verdict.Accepted ? verdict.Issuer : null });
    }

    private static ServiceAuthExpectations Expectations(JsonNode? options)
    {
        var defaults = ServiceAuth["defaults"]!;
        JsonNode? Option(string name) => options?.AsObject().TryGetPropertyValue(name, out var value) == true ? value : defaults[name];
        var documents = ServiceAuth["documents"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.ToJsonString());
        foreach (var (did, document) in options?["documents"]?.AsObject() ?? [])
            documents[did] = document!.ToJsonString();

        return new(
            [.. Option("audiences")!.AsArray().Select(a => (string)a!)],
            (string?)Option("method"),
            [.. Option("allowedKeyIds")!.AsArray().Select(k => (string)k!)],
            TimeSpan.FromSeconds((long)Option("maxLifetimeSeconds")!),
            TimeSpan.FromSeconds((long)Option("clockSkewSeconds")!),
            documents,
            (options?["freshDocuments"]?.AsObject() ?? []).ToDictionary(p => p.Key, p => p.Value!.ToJsonString()));
    }

    private static bool VerifiesES256(JsonNode jwk, string signingInput, string signature)
    {
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Convert.FromBase64String(Padded((string)jwk["x"]!)), Y = Convert.FromBase64String(Padded((string)jwk["y"]!)) },
        });
        return ecdsa.VerifyData(Encoding.ASCII.GetBytes(signingInput), Convert.FromBase64String(Padded(signature)), HashAlgorithmName.SHA256);
    }

    private static string Padded(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        return base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
    }
}
