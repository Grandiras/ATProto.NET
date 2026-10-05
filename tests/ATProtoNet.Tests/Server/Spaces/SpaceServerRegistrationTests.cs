using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using ATProtoNet.Tests.Identity;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ATProtoNet.Tests.Server.Spaces;

/// <summary>
/// The space server's verifiers are activated by the container rather than built by hand, so
/// they take the space server's own DID resolver and whatever <see cref="TimeProvider"/> is
/// registered.
/// </summary>
public class SpaceServerRegistrationTests
{
    [Fact]
    public async Task AddAtProtoSpaces_VerifiersUseARegisteredTimeProvider()
    {
        // An hour behind: a credential issued now is dated in the future by the registered clock.
        using var authorityKey = AtProtoCrypto.GenerateP256Key();
        using var holder = AtProtoCrypto.GenerateP256Key();
        var authority = Did.Parse("did:web:pds.example.com");
        var space = SpaceUri.Create(authority, Nsid.Parse("com.atmoboards.forum"), RecordKey.Parse("default"));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1)));
        services.AddKeyedSingleton<IDidResolver>(
            SpaceServerExtensions.DidResolverKey, new StubDidResolver().PublishAccount(authority.Value, authorityKey));
        services.AddAtProtoSpaces(o => o.ServiceDid = authority);
        using var provider = services.BuildServiceProvider();
        var credential = SpaceTokens.Create(
            SpaceTokenType.Credential, authority.Value, space.Value, authorityKey, confirmationKeyId: holder.ToDidKey());

        var ex = await Assert.ThrowsAsync<SpaceVerificationException>(
            () => provider.GetRequiredService<SpaceCredentialVerifier>().VerifyAsync(credential, space));

        Assert.Contains("future", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAtProtoSpaces_VerifiersResolveThroughTheSpaceServersOwnResolver()
    {
        var resolver = new StubDidResolver();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IDidResolver>(SpaceServerExtensions.DidResolverKey, resolver);
        services.AddAtProtoSpaces(o => o.ServiceDid = Did.Parse("did:web:pds.example.com"));
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<SpaceRequestAuthenticator>());
    }

    [Fact]
    public async Task AddSpaceAuthority_WithAnAccountSigner_HandsItToTheNotifier()
    {
        using var serviceKey = AtProtoCrypto.GenerateP256Key();
        var signer = new TestAccountSigner { Fail = true };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<IDidResolver>(SpaceServerExtensions.DidResolverKey, new StubDidResolver());
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
