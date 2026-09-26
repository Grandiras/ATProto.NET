using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Tests.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// The space server's verifiers are activated by the container rather than built by hand, so
/// they take the space server's own DID resolver and whatever <see cref="TimeProvider"/> is
/// registered.
/// </summary>
public class SpaceServerRegistrationTests
{
    private const string Url = "https://pds.example.com/xrpc/com.atproto.space.getRecord";

    [Fact]
    public async Task AddAtProtoSpaces_VerifiersUseARegisteredTimeProvider()
    {
        // An hour behind: a proof minted now is dated in the future by the registered clock.
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new ManualClock(DateTimeOffset.UtcNow.AddHours(-1)));
        services.AddAtProtoSpaces(o => o.ServiceDid = Did.Parse("did:web:pds.example.com"));
        using var provider = services.BuildServiceProvider();
        using var key = new TestDPoPKey();

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => provider.GetRequiredService<DPoPProofValidator>().ValidateAsync(key.Proof("GET", Url), "GET", Url));

        Assert.Contains("future", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAtProtoSpaces_VerifiersResolveThroughTheSpaceServersOwnResolver()
    {
        var resolver = new FakeDidDocumentResolver();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IDidResolver>(SpaceServerExtensions.DidResolverKey, resolver);
        services.AddAtProtoSpaces(o => o.ServiceDid = Did.Parse("did:web:pds.example.com"));
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<SpaceRequestAuthenticator>());
        Assert.NotNull(provider.GetRequiredService<SpaceServiceAuthVerifier>());
    }

    [Fact]
    public async Task AddSpaceAuthority_WithAnAccountSigner_HandsItToTheNotifier()
    {
        using var serviceKey = AtProtoCrypto.GenerateP256Key();
        var signer = new TestAccountSigner { Fail = true };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<IDidResolver>(SpaceServerExtensions.DidResolverKey, new FakeDidDocumentResolver());
        services.AddSingleton<ISpaceAccountSigner>(signer);
        services.AddAtProtoSpaces(o => o.ServiceDid = Did.Parse("did:web:pds.example.com"))
            .AddSpaceAuthority<InMemorySpaceAuthorityStore>(serviceKey);
        using var provider = services.BuildServiceProvider();

        var space = ATProtoNet.Spaces.SpaceUri.Parse("at://did:plc:bbbbbbbbbbbbbbbbbbbbbbbb/space/com.atmoboards.forum/default");
        var store = provider.GetRequiredService<ISpaceAuthorityStore>();
        await store.RegisterNotifyAsync(space, "did:web:syncer.example.com#atproto_space_syncer", DateTimeOffset.UtcNow.AddDays(1));

        // Delivery itself fails (nothing resolves), but the signer was asked for the writer first.
        await provider.GetRequiredService<SpaceWriteNotifier>().NotifyWriteAsync(
            space, Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa"), Tid.Parse("3l6oveex3ii2l"), [1]);

        Assert.Equal([Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa")], signer.Requests);
    }
}
