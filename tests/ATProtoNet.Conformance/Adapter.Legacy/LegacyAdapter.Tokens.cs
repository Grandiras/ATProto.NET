using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    private const string DPoPType = "ATProtoNet.Auth.OAuth.DPoP, ATProtoNet";
    private const string DiscoveryType = "ATProtoNet.Auth.OAuth.AuthorizationServerDiscovery, ATProtoNet";

    /// <inheritdoc />
    public IReadOnlyList<TokenVerdict> VerifyServiceAuth(IReadOnlyList<(string Token, DateTimeOffset Now)> steps, ServiceAuthExpectations expectations)
    {
        var resolver = new FakeDidResolver();
        foreach (var (did, document) in expectations.Documents)
            resolver.Publish(document, expectations.FreshDocuments.GetValueOrDefault(did));
        var clock = new SettableTime();
        var options = new ServiceAuthVerifierOptions { ClockSkew = expectations.ClockSkew, MaxTokenLifetime = expectations.MaxLifetime };
        options.AllowedKeyIds.Clear();
        foreach (var keyId in expectations.AllowedKeyIds)
            options.AllowedKeyIds.Add(keyId);
        var verifier = new ServiceAuthVerifier(resolver, new InMemoryJtiReplayStore(clock), options, clock);
        var method = expectations.Method is null ? null : Nsid.Parse(expectations.Method);

        return [.. steps.Select(step =>
        {
            clock.Now = step.Now;
            try
            {
                var verified = verifier.VerifyAsync(step.Token, [.. expectations.Audiences], method).GetAwaiter().GetResult();
                return new TokenVerdict(true, null, verified.Issuer.Value, verified.Audience, verified.Method?.Value, verified.KeyId,
                    verified.TokenId, verified.ExpiresAt.ToUnixTimeSeconds());
            }
            catch (ServiceAuthException ex)
            {
                return new TokenVerdict(false, ex.Error);
            }
        })];
    }

    /// <inheritdoc />
    public string? DPoPThumbprint(string jwkJson)
    {
        try
        {
            return Thumbprint(null, JsonSerializer.Deserialize<JsonWebKey>(jwkJson)!);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string DPoPAccessTokenHash(string accessToken) => AccessTokenHash(null, accessToken);

    /// <inheritdoc />
    public string? DPoPHtu(string url) => NormalizeHtu(null, url);

    /// <inheritdoc />
    public string? CreateDPoPProof(string curve, string privateKeyHex, string method, string url, string? nonce, string? accessToken)
    {
        var ecCurve = curve == "P-256" ? ECCurve.NamedCurves.nistP256 : ECCurve.CreateFromValue("1.3.132.0.10");
        using var key = ECDsa.Create(new ECParameters { Curve = ecCurve, D = Convert.FromHexString(privateKeyHex) });
        try
        {
            using var generator = new DPoPProofGenerator(key.ExportPkcs8PrivateKey());
            return accessToken is null
                ? generator.GenerateProof(method, url, nonce)
                : generator.GenerateProofWithAccessToken(method, url, nonce, accessToken);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string PkceChallenge(string verifier) => PkceGenerator.ComputeCodeChallenge(verifier);

    /// <inheritdoc />
    public (string Verifier, string State) GeneratePkce() => (PkceGenerator.GenerateCodeVerifier(), PkceGenerator.GenerateState());

    /// <inheritdoc />
    public MetadataVerdict ResolveAuthorizationServer(string pdsUrl, string resourceMetadataJson, string serverMetadataJson, bool allowPrivateNetworks)
    {
        using var http = new HttpClient(new MetadataHandler(resourceMetadataJson, serverMetadataJson));
        var discovery = NewDiscovery(http, NullLogger.Instance, null, allowPrivateNetworks);
        try
        {
            return new(true, ResolveAuthorizationServer(discovery, pdsUrl, default).GetAwaiter().GetResult().Issuer);
        }
        catch (OAuthException ex)
        {
            return new(false, Error: ex.Error);
        }
        finally
        {
            ((IDisposable)discovery).Dispose();
        }
    }

    private sealed class SettableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Serves the two metadata documents wherever their well-known paths are asked for, and 404 elsewhere.</summary>
    private sealed class MetadataHandler(string resource, string server) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                var path when path.Contains("/.well-known/oauth-protected-resource", StringComparison.Ordinal) => Json(resource),
                var path when path.Contains("/.well-known/oauth-authorization-server", StringComparison.Ordinal) => Json(server),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Thumbprint")]
    private static extern string Thumbprint([UnsafeAccessorType(DPoPType)] object? _, JsonWebKey jwk);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "AccessTokenHash")]
    private static extern string AccessTokenHash([UnsafeAccessorType(DPoPType)] object? _, string accessToken);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "NormalizeHtu")]
    private static extern string? NormalizeHtu([UnsafeAccessorType(DPoPType)] object? _, string url);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    [return: UnsafeAccessorType(DiscoveryType)]
    private static extern object NewDiscovery(HttpClient? httpClient, ILogger logger, IIdentityResolver? identityResolver, bool allowPrivateNetworks);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ResolveAuthorizationServerAsync")]
    private static extern Task<AuthorizationServerMetadata> ResolveAuthorizationServer(
        [UnsafeAccessorType(DiscoveryType)] object discovery, string pdsUrl, CancellationToken cancellationToken);
}
