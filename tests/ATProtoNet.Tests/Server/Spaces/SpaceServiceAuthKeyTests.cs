using System.Net;
using ATProtoNet.Auth;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// Which key an authority's outbound service auth is signed with when its credentials use a
/// dedicated <c>#atproto_space</c> key. Service auth is only accepted from an <c>#atproto</c> key —
/// by the reference and by <see cref="ServiceAuthVerifier"/> — so the credential key must not sign it.
/// </summary>
public sealed class SpaceServiceAuthKeyTests : IDisposable
{
    private static readonly Did AuthorityDid = Did.Parse("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb");
    private static readonly Did AppDid = Did.Parse("did:web:app.example.com");
    private static readonly string ManagingApp = AppDid + "#forum";
    private static SpaceUri Space => SpaceUri.Parse($"at://{AuthorityDid}/space/com.atmoboards.forum/default");

    private readonly AtProtoKey _accountKey = AtProtoCrypto.GenerateP256Key();
    private readonly AtProtoKey _spaceKey = AtProtoCrypto.GenerateP256Key();
    private readonly FakeDidDocumentResolver _resolver = new();
    private readonly RecordingHandler _handler = new();

    public SpaceServiceAuthKeyTests()
    {
        // The authority publishes both keys; the managing app publishes its endpoint.
        _resolver.Publish(AuthorityDid, new DidDocument
        {
            Id = AuthorityDid,
            VerificationMethod =
            [
                new VerificationMethod { Id = $"{AuthorityDid}#atproto", Type = "Multikey", PublicKeyMultibase = _accountKey.ToMultikey() },
                new VerificationMethod { Id = $"{AuthorityDid}#atproto_space", Type = "Multikey", PublicKeyMultibase = _spaceKey.ToMultikey() },
            ],
        });
        _resolver.Publish(AppDid, new DidDocument
        {
            Id = AppDid,
            Service = [new DidDocumentService { Id = "#forum", Type = "BulletinManagingApp", Endpoint = "https://app.example.com" }],
        });
    }

    public void Dispose()
    {
        _accountKey.Dispose();
        _spaceKey.Dispose();
    }

    private ServiceProvider Build(AtProtoKey? serviceAuthKey = null, ISpaceAccountSigner? signer = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<IDidResolver>(SpaceServerExtensions.DidResolverKey, _resolver);
        if (signer is not null)
            services.AddSingleton(signer);

        services
            .AddAtProtoSpaces(o =>
            {
                o.ServiceDid = AuthorityDid;
                o.CredentialKeyId = SpaceAuthority.SigningKeyId;
            })
            .AddSpaceAuthority<InMemorySpaceAuthorityStore>(_spaceKey, serviceAuthKey)
            .AddSimpleSpace<InMemorySimpleSpaceStore>();

        services.AddHttpClient(SpaceServerExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _handler);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DedicatedCredentialKey_WithNeitherServiceKeyNorSigner_StopsTheHostStarting()
    {
        using var provider = Build();
        var check = provider.GetServices<IHostedService>().Single(s => s is SpaceAuthorityStartupCheck);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => check.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("serviceAuthKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DedicatedCredentialKey_WithAServiceAuthKey_SignsCheckUserAccessWithTheAccountKey()
    {
        using var provider = Build(serviceAuthKey: _accountKey);

        await provider.GetRequiredService<ISimpleSpaceManagingAppClient>().CheckUserAccessAsync(
            ManagingApp, Space, Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa"), SpaceAccessKind.Read, null);

        var verified = await Verify(_handler.LastToken!);
        Assert.Equal(AuthorityDid, verified.Issuer);
    }

    [Fact]
    public async Task DedicatedCredentialKey_WithASignerForTheServicesOwnDid_SignsWithTheSignersKey()
    {
        // The signer is asked for the service's own DID too, which is what lets it supply the
        // #atproto key the credential key cannot stand in for.
        var signer = new TestAccountSigner().Add(AuthorityDid, _accountKey);
        using var provider = Build(signer: signer);

        await provider.GetRequiredService<ISimpleSpaceManagingAppClient>().CheckUserAccessAsync(
            ManagingApp, Space, Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa"), SpaceAccessKind.Read, null);

        await Verify(_handler.LastToken!);
        Assert.Equal([AuthorityDid], signer.Requests);
    }

    [Fact]
    public async Task DefaultCredentialKey_SignsServiceAuthWithIt()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<IDidResolver>(SpaceServerExtensions.DidResolverKey, _resolver);
        services.AddAtProtoSpaces(o => o.ServiceDid = AuthorityDid)
            .AddSpaceAuthority<InMemorySpaceAuthorityStore>(_accountKey)
            .AddSimpleSpace<InMemorySimpleSpaceStore>();
        services.AddHttpClient(SpaceServerExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _handler);
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ISimpleSpaceManagingAppClient>().CheckUserAccessAsync(
            ManagingApp, Space, Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa"), SpaceAccessKind.Read, null);

        await Verify(_handler.LastToken!);
    }

    private Task<VerifiedServiceAuth> Verify(string token) =>
        new ServiceAuthVerifier(_resolver, new InMemoryJtiReplayStore())
            .VerifyAsync(token, [ManagingApp], Nsid.Parse(SpaceNsids.CheckUserAccess));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? LastToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastToken = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"authorized":true}""", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
